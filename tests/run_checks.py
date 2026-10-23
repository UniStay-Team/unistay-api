"""Run HTTP suites in disposable PostgreSQL databases, never in the working database."""
import os, pathlib, secrets, shutil, subprocess, sys, time, urllib.request
root=pathlib.Path(__file__).resolve().parents[1]
compose=['docker','compose','--env-file','.env.local']
settings=dict(line.split('=',1) for line in (root/'.env.local').read_text().splitlines() if '=' in line)
def run(args,**kw): return subprocess.run(args,cwd=root,check=True,**kw)
for scenario in (sys.argv[1:] or ['http_scenarios.py','admin_scenarios.py','rate_scenarios.py']):
    if scenario not in ('http_scenarios.py','admin_scenarios.py','rate_scenarios.py'): raise ValueError('Unknown test suite')
    suffix=secrets.token_hex(6)
    database='unistay_csharp_test_'+suffix
    container='unistay-csharp-test-'+suffix
    folder=root/'.local'/('test-'+suffix)
    (folder/'mail').mkdir(parents=True); (folder/'media').mkdir()
    env=os.environ.copy()
    env.update(TestRelaxRateLimits='false' if scenario=='rate_scenarios.py' else 'true', ConnectionStrings__UniStay=f"Host=db;Database={database};Username=unistay_api;Password={settings['POSTGRES_APP_PASSWORD']}",
        LocalMailDirectory='/src/.local/'+folder.name+'/mail', MediaDirectory='/src/.local/'+folder.name+'/media',
        UNISTAY_TEST_URL='http://127.0.0.1:8002', UNISTAY_MAIL_DIRECTORY=str(folder/'mail'), UNISTAY_TEST_CONTAINER=container, UNISTAY_TEST_DATABASE=database)
    sql=compose+['exec','-T','db','psql','-U','unistay_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c']
    run(sql+[f'CREATE DATABASE {database} OWNER unistay_api;'],stdout=subprocess.DEVNULL)
    try:
        run(compose+['run','-d','--name',container,'-p','127.0.0.1:8002:8080','-e','TestRelaxRateLimits','-e','ConnectionStrings__UniStay','-e','LocalMailDirectory','-e','MediaDirectory','api'],env=env,stdout=subprocess.DEVNULL)
        for attempt in range(60):
            try:
                with urllib.request.urlopen('http://127.0.0.1:8002/health/',timeout=2) as response:
                    if response.status==200: break
            except Exception: time.sleep(1)
        else: raise RuntimeError('Temporary API did not start.')
        run([sys.executable,str(root/'tests'/scenario)],env=env)
    finally:
        subprocess.run(['docker','rm','-f',container],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        run(sql+[f'DROP DATABASE {database} WITH (FORCE);'],stdout=subprocess.DEVNULL)
        shutil.rmtree(folder)
print('All HTTP scenarios passed on isolated databases; working data unchanged.')
