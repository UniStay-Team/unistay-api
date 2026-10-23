"""Start/reset only the explicitly separate synthetic demo database at localhost:8003."""
import json, os, pathlib, secrets, subprocess, sys
root=pathlib.Path(__file__).resolve().parents[1]
compose=['docker','compose','--env-file','.env.local']
def run(cmd,**kwargs): return subprocess.run(cmd,cwd=root,check=True,**kwargs)
settings=dict(line.split('=',1) for line in (root/'.env.local').read_text().splitlines() if '=' in line)
folder=root/'.local/demo';folder.mkdir(parents=True,exist_ok=True);folder.chmod(0o700)
credentials=folder/'credentials.json'
if credentials.exists(): password=json.loads(credentials.read_text())['password']
else:
    password='Demo!'+secrets.token_urlsafe(24)
    fd=os.open(credentials,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'w') as f: json.dump({'accounts':['demo-student@example.test','demo-owner@example.test','demo-admin@example.test','demo-neighbor@example.test'],'password':password},f,indent=2)
env=os.environ.copy(); env.update(ConnectionStrings__UniStay=f"Host=db;Database=unistay_csharp_demo;Username=unistay_api;Password={settings['POSTGRES_APP_PASSWORD']}",DemoMode='true',LocalMailDirectory='/src/.local/demo/mail',MediaDirectory='/src/.local/demo/media')
run(compose+['up','-d','--wait','db'],stdout=subprocess.DEVNULL)
sql=compose+['exec','-T','db','psql','-U','unistay_admin','-d','postgres','-At','-c']
if not subprocess.check_output(sql+["SELECT 1 FROM pg_database WHERE datname='unistay_csharp_demo'"],cwd=root,text=True).strip():
    run(sql+['CREATE DATABASE unistay_csharp_demo OWNER unistay_api'],stdout=subprocess.DEVNULL)
container='unistay-csharp-demo'
existing=subprocess.run(['docker','inspect','-f','{{ index .Config.Labels "com.docker.compose.project" }}',container],capture_output=True,text=True)
if existing.returncode==0:
    if existing.stdout.strip()!='unistay-csharp': raise RuntimeError('Container name is owned by another project.')
    run(['docker','rm','-f',container],stdout=subprocess.DEVNULL)
run(compose+['run','-d','--name',container,'-p','127.0.0.1:8003:8080','-e','ConnectionStrings__UniStay','-e','DemoMode','-e','LocalMailDirectory','-e','MediaDirectory','api'],env=env,stdout=subprocess.DEVNULL)
import time,urllib.request
for _ in range(60):
    try:
        urllib.request.urlopen('http://127.0.0.1:8003/health/',timeout=2).close();break
    except Exception: time.sleep(1)
else: raise RuntimeError('Demo API startup failed.')
run(['docker','exec','-i',container,'dotnet','UniStay.Api/bin/Debug/net10.0/UniStay.Api.dll','--seed-demo'],input=password.encode(),stdout=subprocess.DEVNULL)
print('Demo ready: http://127.0.0.1:8003/api/docs/ . Credentials are in .local/demo/credentials.json (not printed or committed).')
