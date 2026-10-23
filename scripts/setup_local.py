"""Create local-only secrets once. Never prints credentials."""
from pathlib import Path
import os, secrets
os.umask(0o077)
root=Path(__file__).resolve().parents[1]
local=root/'.local'
local.mkdir(exist_ok=True)
for name in ('mail', 'media', 'init', 'backups'):
    (local/name).mkdir(exist_ok=True)
env=root/'.env.local'
if env.exists():
    print('Existing configuration preserved.')
else:
    admin=secrets.token_hex(32)
    app=secrets.token_hex(32)
    (local/'init/01-role.sql').write_text("CREATE ROLE unistay_api LOGIN PASSWORD '"+app+"' NOSUPERUSER NOCREATEDB NOCREATEROLE;\nALTER DATABASE unistay_csharp OWNER TO unistay_api;\nREVOKE CREATE ON SCHEMA public FROM PUBLIC;\nGRANT ALL ON SCHEMA public TO unistay_api;\n")
    (local/'init/01-role.sql').chmod(0o644)
    env.write_text('POSTGRES_ADMIN_PASSWORD='+admin+'\nPOSTGRES_APP_PASSWORD='+app+'\n')
    print('Created isolated C# configuration; no existing database changed.')
