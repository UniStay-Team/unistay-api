"""Interactive local administrator creation; credentials are sent through stdin."""
import getpass, json, pathlib, subprocess
root=pathlib.Path(__file__).resolve().parents[1]
email=input('Email: ').strip()
name=input('Name: ').strip()
password=getpass.getpass('Password (10–128 characters): ')
if password!=getpass.getpass('Repeat password: '):
    raise SystemExit('Passwords do not match.')
result=subprocess.run(['docker','compose','--env-file','.env.local','exec','-T','api','dotnet','UniStay.Api/bin/Debug/net10.0/UniStay.Api.dll','--create-admin'],cwd=root,input=json.dumps(dict(email=email,name=name,password=password,role='student')).encode())
raise SystemExit(result.returncode)
