#!/usr/bin/env python3
"""Real OpenCode permission hooks + sandbox shell; disposable session, no model requests."""

import base64, json, pathlib, tempfile, urllib.request, urllib.error, uuid, shutil, time

project = pathlib.Path(__file__).resolve().parent.parent
root = pathlib.Path(tempfile.mkdtemp(prefix="autonomy-", dir=project / "tests"))
password = json.loads(
    (pathlib.Path.home() / ".config/opencode/service.json").read_text()
)["password"]
base = "http://127.0.0.1:49374"
headers = {
    "Authorization": "Basic "
    + base64.b64encode(("opencode:" + password).encode()).decode(),
    "Content-Type": "application/json",
}
client = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def request(method, path, body=None):
    req = urllib.request.Request(
        base + path,
        method=method,
        headers=headers,
        data=json.dumps(body).encode() if body is not None else None,
    )
    with client.open(req, timeout=25) as response:
        raw = response.read()
        return response.status, json.loads(raw) if raw else None


sid = None
try:
    for attempt in range(30):
        _, plugins = request("GET", "/api/plugin?location%5Bdirectory%5D=" + str(root))
        if any(
            x["id"] == "opencode.manager.autonomy.v1"
            and x["state"]["status"] == "active"
            for x in plugins["data"]
        ):
            break
        time.sleep(0.1)
    else:
        raise AssertionError("Autonomy plugin did not activate")
    _, created = request(
        "POST",
        "/api/session",
        {
            "title": "Manager autonomous permission verification (no AI)",
            "location": {"directory": str(root)},
            "permissions": [{"action": "*", "resource": "*", "effect": "ask"}],
            "metadata": {
                "opencodeManagerAutonomy": {
                    "version": 1,
                    "enabled": True,
                    "directory": str(root),
                }
            },
        },
    )
    sid = created["data"]["id"]
    runner = str(
        pathlib.Path.home() / ".local/lib/opencode-manager-autonomy/sandbox.py"
    )
    payload = (
        base64.urlsafe_b64encode(
            json.dumps(
                {
                    "root": str(root),
                    "cwd": str(root),
                    "command": "printf AUTONOMY_OK > output.txt; test ! -e /etc/shadow",
                }
            ).encode()
        )
        .decode()
        .rstrip("=")
    )
    command = "/usr/bin/python3 " + repr(runner) + " " + repr(payload)
    for expected, action, resources in [
        ("allow", "read", [str(root / "Program.cs")]),
        ("allow", "shell", [command]),
        ("deny", "read", ["/etc/passwd"]),
        ("deny", "shell", ["sudo id"]),
    ]:
        status, result = request(
            "POST",
            f"/api/session/{sid}/permission",
            {"action": action, "resources": resources},
        )
        assert status == 200 and result["data"]["effect"] == expected, (action, result)
    request("POST", f"/api/session/{sid}/shell", {"command": command})
    assert (root / "output.txt").read_text() == "AUTONOMY_OK"
    _, messages = request("GET", f"/api/session/{sid}/message")
    assert not any(x["type"] == "assistant" for x in messages["data"])
    _, pending = request("GET", f"/api/session/{sid}/permission")
    assert not pending["data"]
    print(
        json.dumps(
            {
                "plugin": "active",
                "safe_file_permission": "ALLOW",
                "isolated_shell_permission": "ALLOW",
                "outside_and_sudo": "DENY",
                "actual_isolated_shell": "PASS",
                "owner_prompts": 0,
                "model_generations": 0,
            }
        )
    )
finally:
    if sid:
        request("PATCH", "/api/session/" + sid, {"archived": True})
    shutil.rmtree(root)
