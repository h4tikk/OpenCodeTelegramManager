#!/usr/bin/python3
"""Execute one command inside a project-only, networkless bubblewrap namespace."""

import base64, json, os, pathlib, re, resource, stat, sys, subprocess
from workspace import ProjectWorkspace
from xml.sax.saxutils import escape

DENIED = re.compile(
    r"\b(?:sudo|su|doas|pkexec|mkfs\w*|wipefs|shutdown|reboot|mount|umount)\b|\bgit\b[^\n;&|]*(?:\bpush\b|\breset\s+--hard\b|\bclean\b)|\b(?:drop|truncate)\s+(?:database|schema|table)\b|\b(?:rails|rake)\s+db:(?:drop|reset|purge)|\bdotnet\s+ef\s+database\b",
    re.I,
)
SECRET = re.compile(
    r"^(?:\.env(?:\.|$)|\.ssh$|\.gnupg$|\.aws$|\.codex$|secrets?$|credentials?|auth\.json$|service\.json$|id_(?:rsa|ed25519)|.*\.(?:key|pem|pfx)$)",
    re.I,
)
DATABASE = re.compile(r"\.(?:db|sqlite|sqlite3)(?:-(?:wal|shm))?$", re.I)


def checked(payload, roots):
    if set(payload) != {"root", "cwd", "command"} or not all(
        isinstance(v, str) for v in payload.values()
    ):
        raise ValueError("Invalid sandbox request")
    root = pathlib.Path(payload["root"]).resolve(strict=True)
    cwd = pathlib.Path(payload["cwd"]).resolve(strict=True)
    if not root.is_dir() or not cwd.is_dir() or not cwd.is_relative_to(root):
        raise ValueError("Invalid project directory")
    if not any(
        root.is_relative_to(pathlib.Path(x).resolve(strict=True)) for x in roots
    ):
        raise ValueError("Outside trusted roots")
    if (
        len(payload["command"]) > 100000
        or "\x00" in payload["command"]
        or DENIED.search(payload["command"])
    ):
        raise ValueError("Privileged/destructive command denied")
    return root, cwd


def arguments(payload, settings, workspace=None):
    root, cwd = checked(payload, settings["roots"])
    args = [
        "/usr/bin/bwrap",
        "--clearenv",
        "--unshare-user",
        "--unshare-pid",
        "--unshare-net",
        "--unshare-ipc",
        "--unshare-uts",
        "--die-with-parent",
        "--new-session",
        "--cap-drop",
        "ALL",
        "--ro-bind",
        "/usr",
        "/usr",
        "--symlink",
        "usr/bin",
        "/bin",
        "--symlink",
        "usr/lib",
        "/lib",
        "--symlink",
        "usr/lib",
        "/lib64",
        "--proc",
        "/proc",
        "--dev",
        "/dev",
        "--tmpfs",
        "/tmp",
        "--tmpfs",
        "/home",
        "--dir",
        "/home/runner",
        "--bind",
        str(workspace or root),
        str(root),
        "--chdir",
        str(cwd),
    ]
    for filename in ["/etc/ssl", "/etc/ca-certificates", "/etc/localtime"]:
        if pathlib.Path(filename).exists():
            args += ["--ro-bind", filename, filename]
    # Public dependency caches only. Credentials/configuration and the user's home are not mounted.
    for alias in settings.get("readOnlyCaches", []):
        p = pathlib.Path(alias)
        if p.is_dir():
            real = p.resolve(strict=True)
            args += ["--ro-bind", str(real), str(real)]
            if str(real) != str(p):
                args += ["--ro-bind", str(real), str(p)]
    # Keep restore offline and deterministic. No inherited NuGet credentials or network retry loops.
    config = (
        "<configuration><packageSources><clear/>"
        + "".join(
            '<add key="cache' + str(i) + '" value="' + escape(str(p)) + '"/>'
            for i, p in enumerate(settings.get("readOnlyCaches", []))
        )
        + "</packageSources></configuration>"
    )
    descriptor = os.memfd_create("manager-offline-nuget", 0)
    os.write(descriptor, config.encode())
    os.lseek(descriptor, 0, 0)
    args += [
        "--ro-bind-data",
        str(descriptor),
        "/home/runner/.nuget/NuGet/NuGet.Config",
    ]
    for name, data in [
        ("passwd", f"runner:x:{os.getuid()}:{os.getgid()}::/home/runner:/bin/bash\n"),
        ("group", f"runner:x:{os.getgid()}:\n"),
    ]:
        fd = os.memfd_create("manager-" + name, 0)
        os.write(fd, data.encode())
        os.lseek(fd, 0, 0)
        args += ["--ro-bind-data", str(fd), "/etc/" + name]
    count = 0
    masked = set()
    for base, dirs, files in os.walk(root, followlinks=False):
        count += len(dirs) + len(files)
        if count > 200000:
            raise ValueError("Project tree exceeds sandbox inspection limit")
        for name in list(dirs):
            if name == ".git":
                p = (pathlib.Path(base) / name).resolve()
                if p.is_relative_to(root):
                    args += ["--ro-bind", str(p), str(p)]
                    if (p / "config").is_file():
                        args += ["--ro-bind", "/dev/null", str(p / "config")]
                dirs.remove(name)
                continue
            if SECRET.search(name):
                p = (pathlib.Path(base) / name).resolve()
                if p.is_relative_to(root) and p not in masked:
                    args += ["--tmpfs", str(p)]
                    masked.add(p)
                dirs.remove(name)
        for name in files:
            if not (SECRET.search(name) or DATABASE.search(name)):
                continue
            p = (pathlib.Path(base) / name).resolve()
            if not p.is_relative_to(root) or p in masked or not p.is_file():
                continue
            args += [
                "--ro-bind",
                str(p) if DATABASE.search(name) else "/dev/null",
                str(p),
            ]
            masked.add(p)
    for k, v in {
        "HOME": "/home/runner",
        "PATH": "/usr/bin:/bin",
        "LANG": "C.UTF-8",
        "TMPDIR": "/tmp",
        "DOTNET_CLI_HOME": "/home/runner",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_NOLOGO": "1",
        "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
        "NuGetAudit": "false",
        "DOTNET_GCHeapHardLimit": "40000000",
        "GIT_CONFIG_NOSYSTEM": "1",
        "GIT_CONFIG_GLOBAL": "/dev/null",
        "GIT_TERMINAL_PROMPT": "0",
        "GIT_CONFIG_COUNT": "2",
        "GIT_CONFIG_KEY_0": "core.fsmonitor",
        "GIT_CONFIG_VALUE_0": "false",
        "GIT_CONFIG_KEY_1": "core.hooksPath",
        "GIT_CONFIG_VALUE_1": "/dev/null",
    }.items():
        args += ["--setenv", k, v]
    if settings.get("nugetPackages"):
        args += ["--setenv", "NUGET_PACKAGES", settings["nugetPackages"]]
    return args + ["/usr/bin/bash", "--noprofile", "--norc", "-c", payload["command"]]


def main():
    if len(sys.argv) != 2:
        raise ValueError("Expected one encoded request")
    settings = json.loads(pathlib.Path(__file__).with_name("settings.json").read_text())
    encoded = sys.argv[1]
    payload = json.loads(base64.urlsafe_b64decode(encoded + "=" * (-len(encoded) % 4)))
    root, _ = checked(payload, settings["roots"])
    stage = ProjectWorkspace(root, SECRET, DATABASE)
    stage.prepare()
    args = arguments(payload, settings, stage.path)
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    resource.setrlimit(resource.RLIMIT_NOFILE, (1024, 1024))
    resource.setrlimit(resource.RLIMIT_FSIZE, (512 * 1024 * 1024, 512 * 1024 * 1024))
    resource.setrlimit(resource.RLIMIT_CPU, (600, 600))
    descriptors = [
        int(args[i + 1]) for i, x in enumerate(args) if x == "--ro-bind-data"
    ]
    try:
        result = subprocess.run(args, pass_fds=descriptors, timeout=600)
        stage.merge()
        return result.returncode
    finally:
        stage.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print("Sandbox refused request: " + type(error).__name__, file=sys.stderr)
        sys.exit(126)
