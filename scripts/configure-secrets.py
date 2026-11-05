#!/usr/bin/env python3
"""Hidden credential input. No token appears in arguments, repository, stdout, or logs."""
import getpass,json,os,pathlib,subprocess,urllib.request,urllib.error
root=pathlib.Path(__file__).resolve().parent.parent
config=pathlib.Path.home()/'.config/opencode-manager/manager.env'
config.parent.mkdir(parents=True,exist_ok=True,mode=0o700);config.parent.chmod(0o700)
values={}
if config.exists():
    for line in config.read_text().splitlines():
        if '=' in line and not line.lstrip().startswith('#'):
            key,value=line.split('=',1);values[key]=value
bot_token=getpass.getpass('Telegram Bot Token (hidden): ').strip()
owner=input('Your Telegram User ID: ').strip()
if not owner.isdecimal() or int(owner)<=0:raise SystemExit('Positive numeric OwnerUserId required')
if not bot_token or any(c.isspace() for c in bot_token):raise SystemExit('Invalid token format')
try:
    with urllib.request.urlopen('https://api.telegram.org/bot'+bot_token+'/getMe',timeout=20) as response:
        result=json.load(response)
    if not result.get('ok'):raise ValueError()
except Exception:raise SystemExit('Telegram token validation failed; credentials were not written') from None
values.update({'Telegram__Token':bot_token,'BotSecurity__OwnerUserId':owner,'OpenCode__BaseUrl':values.get('OpenCode__BaseUrl') or 'http://127.0.0.1:49374','OpenCode__Username':'opencode','TaskExecution__MaxConcurrentTasks':values.get('TaskExecution__MaxConcurrentTasks') or '2'})
if not values.get('OpenCode__Password'):
    try:values['OpenCode__Password']=json.loads((pathlib.Path.home()/'.config/opencode/service.json').read_text())['password']
    except Exception:values['OpenCode__Password']=getpass.getpass('OpenCode password (hidden): ')
# Keep the existing inode so Docker file-secret bind mounts also see the updated values.
fd=os.open(config,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
with os.fdopen(fd,'w') as f:
    f.write(''.join(k+'='+v+'\n' for k,v in values.items()));f.flush();os.fsync(f.fileno())
config.chmod(0o600)
print('Protected configuration saved. Telegram bot: @'+result['result'].get('username','unknown'))
subprocess.run(['docker','compose','up','-d','--force-recreate'],cwd=root,check=True)
