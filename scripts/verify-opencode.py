#!/usr/bin/env python3
"""Live v2 contract smoke test. Disposable session only; resume=false, no model generation."""
import base64,json,os,pathlib,tempfile,urllib.request,urllib.parse,urllib.error,uuid
base=os.environ.get('OpenCode__BaseUrl','http://127.0.0.1:49374')
password=os.environ.get('OpenCode__Password')
if not password:
    password=json.loads((pathlib.Path.home()/'.config/opencode/service.json').read_text())['password']
headers={'Authorization':'Basic '+base64.b64encode(('opencode:'+password).encode()).decode(),'Content-Type':'application/json'}
client=urllib.request.build_opener(urllib.request.ProxyHandler({}))
def request(method,path,data=None):
    req=urllib.request.Request(base+path,headers=headers,method=method,data=json.dumps(data).encode() if data is not None else None)
    try:
        with client.open(req,timeout=20) as response:
            body=response.read();return json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        print("API contract error",path,error.code,error.read(2500).decode(errors="replace"));raise

def frame(stream):
    lines=[]
    while True:
        line=stream.readline().decode().rstrip('\r\n')
        if not line and lines:return json.loads('\n'.join(lines))
        if line.startswith('data:'):lines.append(line[5:].strip())

def sessions():
    out=[];cursor=None
    while True:
        page=request('GET','/api/session?limit=100'+('&cursor='+urllib.parse.quote(cursor,safe='') if cursor else ''));out.extend(page['data']);cursor=page['cursor'].get('next')
        if not cursor:return out

before={s['id'] for s in sessions()}
info=request('GET','/api/info');assert info['version'].startswith('2.')
root=pathlib.Path(tempfile.mkdtemp(prefix='opencode-manager-contract-'))
sid='ses_manager_smoke_'+uuid.uuid4().hex
result={'version':info['version'],'models':len(request('GET','/api/model')['data']),'providers':len(request('GET','/api/provider')['data']),'agents':len(request('GET','/api/agent')['data'])}
try:
    with client.open(urllib.request.Request(base+'/api/event?types=session.created',headers=headers),timeout=15) as events:
        assert frame(events)['type']=='server.connected';result['sse_connected']='PASS'
        s=request('POST','/api/session',{'id':sid,'title':'Manager contract verification (no generation)','location':{'directory':str(root)},'permissions':[{'action':'*','resource':'*','effect':'deny'}]})['data']
        event=frame(events);assert event['type']=='session.created';result['sse_created']='PASS'
    assert s['location']['directory']==str(root)
    assert any(s['id']==sid for s in request('GET','/api/session?directory='+urllib.parse.quote(str(root),safe=''))['data']);result['directory_sessions']='PASS'
    request('PATCH','/api/session/'+sid,{'title':'Manager verified (no generation)'})
    request('POST','/api/session/'+sid+'/agent',{'agent':s.get('agent','build')})
    if s.get('model'):request('POST','/api/session/'+sid+'/model',{'model':s['model']})
    assert isinstance(request('GET','/api/session/'+sid+'/message')['data'],list);result['history_rename_model_agent']='PASS'
    mid='msg_manager_smoke_'+uuid.uuid4().hex
    png='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC'
    response=request('POST','/api/session/'+sid+'/prompt',{'id':mid,'text':'Contract verification only; do not run.','resume':False,'files':[{'uri':'data:image/png;base64,'+png,'name':'fixture.png'}]})['data']
    assert response['id']==mid
    assert any(i['id']==mid for i in request('GET','/api/session/'+sid+'/inbox')['data'])
    request('DELETE','/api/session/'+sid+'/inbox/'+mid);result['image_admission_resume_false']='PASS'
    assert isinstance(request('GET','/api/session/'+sid+'/permission')['data'],list);result['permission_list']='PASS'
    request('POST','/api/session/'+sid+'/interrupt');result['interrupt_idle']='PASS'
    assert before.issubset({s['id'] for s in sessions()});result['existing_sessions_preserved']='PASS'
    result['paid_generations']=0
finally:
    request('PATCH','/api/session/'+sid,{'archived':True})
print(json.dumps(result,indent=2))
