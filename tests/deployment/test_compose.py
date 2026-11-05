#!/usr/bin/env python3
import json,subprocess
config=json.loads(subprocess.check_output(['docker','compose','config','--format','json'],text=True))
s=config['services']['opencode-manager']
assert s['network_mode']=='host'
assert s['restart']=='unless-stopped'
assert s['user'].split(':')[0]!='0'
assert not s.get('privileged',False)
assert not s.get('ports')
assert 'ALL' in s['cap_drop']
assert any('no-new-privileges' in x for x in s['security_opt'])
assert s['read_only']
for v in s['volumes']:
    assert v['type']=='bind'
    assert v['source']==v['target']
    assert v['source'] not in ['/','/home','/etc','/var/run/docker.sock']
    assert not v.get('bind',{}).get('create_host_path',False)
assert not any('Token' in k or 'Password' in k for k in s['environment'])
print('Compose security, host networking, persistence and path identity: PASS')
