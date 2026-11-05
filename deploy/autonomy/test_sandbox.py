import base64, importlib.util, json, os, pathlib, socket, subprocess, tempfile, unittest, shutil

HERE = pathlib.Path(__file__).resolve().parent


class SandboxTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="manager-sandbox-")
        self.base = pathlib.Path(self.temp.name)
        self.root = self.base / "project"
        self.root.mkdir()
        runtime = self.base / "runtime"
        runtime.mkdir()
        self.runner = runtime / "sandbox.py"
        shutil.copy2(HERE / "sandbox.py", self.runner)
        shutil.copy2(HERE / "workspace.py", runtime / "workspace.py")
        (runtime / "settings.json").write_text(
            json.dumps({"roots": [str(self.root)], "readOnlyCaches": []})
        )

    def tearDown(self):
        self.temp.cleanup()

    def run_command(self, command):
        payload = (
            base64.urlsafe_b64encode(
                json.dumps(
                    {"root": str(self.root), "cwd": str(self.root), "command": command}
                ).encode()
            )
            .decode()
            .rstrip("=")
        )
        return subprocess.run(
            ["/usr/bin/python3", str(self.runner), payload],
            capture_output=True,
            text=True,
            timeout=60,
            env={**os.environ, "MANAGER_TEST_SECRET": "never-inherit"},
        )

    def test_project_write_and_host_secrets_isolation(self):
        (self.root / ".env").write_text("REAL_SECRET=do-not-read")
        outside = self.base / "outside"
        outside.write_text("secret")
        result = self.run_command(
            "printf okay > result.txt; test ! -e '"
            + str(outside)
            + '\' && test ! -s .env && test ! -e /etc/shadow && test ! -e /home/hatikk/.ssh && test -z "$MANAGER_TEST_SECRET"'
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.root / "result.txt").read_text(), "okay")

    def test_host_network_unavailable(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            port = listener.getsockname()[1]
            code = f"import socket; s=socket.socket(); s.settimeout(1); assert s.connect_ex(('127.0.0.1',{port})) != 0"
            result = self.run_command("python3 -c " + __import__("shlex").quote(code))
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_existing_database_read_only(self):
        (self.root / "application.db").write_text("preserve")
        result = self.run_command("printf changed > application.db")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.root / "application.db").read_text(), "preserve")

    def test_destructive_command_rejected(self):
        for command in ["sudo id", "git push origin main", "git reset --hard HEAD"]:
            self.assertEqual(self.run_command(command).returncode, 126)

    def test_cannot_publish_opencode_plugin(self):
        result = self.run_command(
            'mkdir -p .opencode/plugins/escape; printf unsafe > .opencode/plugins/escape/index.ts; printf "{}" > opencode.json'
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.root / ".opencode").exists())
        self.assertFalse((self.root / "opencode.json").exists())

    def test_dotnet_build(self):
        (self.root / "Example.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        )
        (self.root / "Example.cs").write_text(
            "public sealed class Example { public int Value => 42; }"
        )
        result = self.run_command("dotnet build --ignore-failed-sources")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue((self.root / "bin/Debug/net10.0/Example.dll").is_file())


if __name__ == "__main__":
    unittest.main()
