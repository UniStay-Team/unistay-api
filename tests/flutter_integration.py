"""Run the team's unchanged Dart API test against an isolated C# / PostgreSQL instance.
Usage: backend/.venv/bin/python api/tests/flutter_integration.py PATH_TO_MOBILE PATH_TO_FLUTTER
"""
import base64, io, json, os, pathlib, shutil, subprocess, sys, tempfile, time, urllib.request
import http_scenarios as h
root=h.ROOT
mobile=pathlib.Path(sys.argv[1]).resolve()
flutter=str(pathlib.Path(sys.argv[2]).resolve())
compose=['docker','compose','--env-file','.env.local']
name='unistay-csharp-flutter-'+h.suffix
folder=root/'.local'/('flutter-'+h.suffix)
(folder/'mail').mkdir(parents=True); (folder/'media').mkdir()
database='unistay_csharp_flutter_'+h.suffix
settings=dict(line.split('=',1) for line in (root/'.env.local').read_text().splitlines() if '=' in line)
env=os.environ.copy()
env['ConnectionStrings__UniStay']=f"Host=db;Database={database};Username=unistay_api;Password={settings['POSTGRES_APP_PASSWORD']}"
env['TestRelaxRateLimits']='true'
env['LocalMailDirectory']='/src/.local/'+folder.name+'/mail'
env['MediaDirectory']='/src/.local/'+folder.name+'/media'

def run(args, **kwargs):
    return subprocess.run(args,cwd=root,check=True,**kwargs)
run(compose+['exec','-T','db','psql','-U','unistay_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c',f'CREATE DATABASE {database} OWNER unistay_api;'],stdout=subprocess.DEVNULL)
try:
    run(compose+['run','-d','--name',name,'-p','127.0.0.1:8002:8080','-e','TestRelaxRateLimits','-e','ConnectionStrings__UniStay','-e','LocalMailDirectory','-e','MediaDirectory','api'],env=env,stdout=subprocess.DEVNULL)
    h.BASE='http://127.0.0.1:8002'
    for attempt in range(60):
        try:
            with urllib.request.urlopen(h.BASE+'/health/',timeout=2) as r:
                if r.status==200: break
        except Exception: time.sleep(1)
    else: raise RuntimeError('C# test server did not start.')
    for role,email,name_ in [('student','mobile-student@example.test','Mobile Student'),('landlord','mobile-owner@example.test','Mobile Owner')]:
        h.call('POST','auth/register/',dict(email=email,name=name_,password='Local-smoke-only-382!',role=role),expected=201)
        for attempt in range(30):
            messages=[json.loads(p.read_text()) for p in (folder/'mail').glob('*.json')]
            match=next((m for m in reversed(messages) if m['email']==email),None)
            if match: break
            time.sleep(.1)
        h.call('POST','auth/verify-email/',{'token':match['token']})
    with tempfile.TemporaryDirectory(prefix='mobile-check-',dir=root/'.local') as tmp:
        copy=pathlib.Path(tmp)/'mobile'
        shutil.copytree(mobile,copy,ignore=shutil.ignore_patterns('.dart_tool','build','.git'))
        (copy/'tool').mkdir(exist_ok=True)
        shutil.copy2(root/'tests/flutter_live_test.dart',copy/'tool/live_api_test.dart')
        subprocess.run([flutter,'pub','get'],cwd=copy,check=True)
        from PIL import Image
        photo=io.BytesIO(); Image.new('RGB',(8,8),'green').save(photo,'PNG')
        subprocess.run([flutter,'test','tool/live_api_test.dart',f'--dart-define=LIVE_API_URL={h.BASE}','--dart-define=LIVE_PHOTO='+base64.b64encode(photo.getvalue()).decode()],cwd=copy,check=True)
        print('PASS: current team Flutter ApiClient completed the C# integration scenario.')
finally:
    subprocess.run(['docker','rm','-f',name],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    run(compose+['exec','-T','db','psql','-U','unistay_admin','-d','postgres','-c',f'DROP DATABASE IF EXISTS {database} WITH (FORCE);'],stdout=subprocess.DEVNULL)

    shutil.rmtree(folder)
