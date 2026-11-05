#!/usr/bin/env python3
"""Adopt availability of an existing shared server; start the normal launcher only after it exits."""
import base64,json,os,pathlib,time,urllib.request,urllib.error
root=pathlib.Path.home()/'.config/opencode'
client=urllib.request.build_opener(urllib.request.ProxyHandler({}))
def available():
    try:
        password=json.loads((root/'service.json').read_text())['password']
        request=urllib.request.Request('http://127.0.0.1:49374/api/info',headers={'Authorization':'Basic '+base64.b64encode(('opencode:'+password).encode()).decode()})
        with client.open(request,timeout=3) as response:return response.status==200
    except urllib.error.HTTPError:return True # A listening server must not be duplicated on auth failures.
    except (OSError,ValueError,KeyError):return False
if available():
    print('Monitoring existing shared OpenCode server; active sessions are preserved.',flush=True)
    while available():time.sleep(5)
launcher=str(root/'runtime/launch-service.sh')
os.execv(launcher,[launcher])
