#!/usr/bin/env python3
"""Install the managed-session-only OpenCode v2 sandbox adapter. Does not restart OpenCode."""

import json, os, pathlib, shutil, datetime

source = pathlib.Path(__file__).resolve().parent.parent / "deploy/autonomy"
home = pathlib.Path.home()
runtime = home / ".local/lib/opencode-manager-autonomy"
plugin = home / ".config/opencode/plugins/opencode-manager-autonomy"
backup = (
    home
    / ".local/state/opencode-manager/autonomy-backups"
    / datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
)
if not shutil.which("bwrap"):
    raise SystemExit("bubblewrap is required; no unsafe fallback is available")
for folder in [runtime, plugin]:
    if folder.exists():
        backup.mkdir(parents=True, exist_ok=True, mode=0o700)
        shutil.copytree(
            folder,
            backup / ("runtime" if folder == runtime else "plugin"),
            dirs_exist_ok=True,
        )
    folder.mkdir(parents=True, exist_ok=True, mode=0o700)
    folder.chmod(0o700)
for name in ["sandbox.py", "workspace.py", "policy.mjs", "integration.mjs"]:

    if (runtime / name).exists():
        (runtime / name).chmod(0o600)
    shutil.copy2(source / name, runtime / name)
    (runtime / name).chmod(0o400)
roots = ["/mnt/data/Projects"]
caches = [str(home / ".nuget/packages"), str(home / ".cargo/registry")]
settings = {
    "roots": roots,
    "readOnlyCaches": [p for p in caches if pathlib.Path(p).is_dir()],
    "nugetPackages": str(home / ".nuget/packages"),
}
(runtime / "settings.json").write_text(json.dumps(settings, indent=2) + "\n")
(runtime / "settings.json").chmod(0o600)
# Promise SDK is provided by the installed OpenCode loader.
entry = (
    'import { Plugin } from "@opencode/plugin";\nimport { install } from '
    + json.dumps(str(runtime / "integration.mjs"))
    + ';\nexport default Plugin.define({id:"opencode.manager.autonomy.v1",setup(ctx){return install(ctx,'
    + json.dumps({"roots": roots, "runner": str(runtime / "sandbox.py")})
    + ");}});\n"
)
if (plugin / "index.ts").exists():
    (plugin / "index.ts").chmod(0o600)
(plugin / "index.ts").write_text(entry)
(plugin / "index.ts").chmod(0o400)
print(
    "Managed-session sandbox installed. OpenCode locations must reload to activate it."
)
