"""Stage shell work and merge ordinary files without publishing OpenCode configuration or overwriting concurrent edits."""

import hashlib, os, pathlib, shutil, stat, tempfile

CONTROL = {
    ".git",
    ".opencode",
    ".agents",
    ".codex",
    ".claude",
    "opencode.json",
    "opencode.jsonc",
    ".opencode.json",
    ".opencode.jsonc",
}


def fingerprint(p):
    h = hashlib.sha256()
    with p.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.digest()


class ProjectWorkspace:
    def __init__(self, root, secret, database):
        self.root = root
        self.secret = secret
        self.database = database
        self.temp = tempfile.TemporaryDirectory(prefix="manager-workspace-")
        self.path = pathlib.Path(self.temp.name) / "project"
        self.path.mkdir()
        self.before = {}

    def allowed(self, relative):
        return not any(
            p in CONTROL or self.secret.search(p) for p in relative.parts
        ) and not self.database.search(relative.name)

    def files(self, base):
        count = 0
        size = 0
        for directory, dirs, files in os.walk(base, followlinks=False):
            dirs[:] = [
                x
                for x in dirs
                if not (pathlib.Path(directory) / x).is_symlink()
                and self.allowed((pathlib.Path(directory) / x).relative_to(base))
            ]
            for name in files:
                p = pathlib.Path(directory) / name
                relative = p.relative_to(base)
                if not self.allowed(relative) or p.is_symlink() or not p.is_file():
                    continue
                count += 1
                size += p.stat().st_size
                if count > 100000 or size > 512 * 1024 * 1024:
                    raise ValueError("Workspace exceeds file/size limits")
                yield relative, p

    def prepare(self):
        for relative, source in self.files(self.root):
            target = self.path / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            self.before[relative] = fingerprint(source)
        return self.path

    def merge(self):
        after = dict(self.files(self.path))
        changes = []
        for relative, source in after.items():
            result = fingerprint(source)
            if self.before.get(relative) == result:
                continue
            target = self.root / relative
            if target.is_symlink() or not target.resolve().is_relative_to(self.root):
                raise ValueError("Unsafe output path")
            if relative in self.before:
                if not target.is_file() or fingerprint(target) != self.before[relative]:
                    raise ValueError(
                        "Concurrent project change; output was not applied"
                    )
            elif target.exists():
                raise ValueError("Concurrent new file; output was not applied")
            changes.append((source, target))
        deleted = []
        for relative, original in self.before.items():
            if relative in after:
                continue
            target = self.root / relative
            if (
                target.is_symlink()
                or not target.is_file()
                or fingerprint(target) != original
            ):
                raise ValueError("Concurrent deletion conflict")
            deleted.append(target)
        # Validate every output before changing the working tree.
        for source, target in changes:
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.parent.resolve().is_relative_to(self.root):
                raise ValueError("Output parent changed")
            with tempfile.NamedTemporaryFile(
                dir=target.parent, prefix=".manager-result-", delete=False
            ) as out:
                name = out.name
                with source.open("rb") as incoming:
                    shutil.copyfileobj(incoming, out)
            os.chmod(name, stat.S_IMODE(source.stat().st_mode) & 0o777)
            os.replace(name, target)
        for target in deleted:
            target.unlink()

    def close(self):
        self.temp.cleanup()
