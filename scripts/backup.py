"""Local encrypted, authenticated backups. Never prints keys or database contents."""
import hashlib, hmac, os, pathlib, subprocess, sys, tarfile, tempfile, datetime
os.umask(0o077)
root = pathlib.Path(__file__).resolve().parents[1]
os.chdir(root)
compose = ['docker', 'compose', '--env-file', '.env.local']
def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)
keyfile = root / '.local/backup.key'
if not keyfile.exists():
    if sys.argv[1] != 'backup':
        raise SystemExit('Missing backup.key; restore the original key.')
    keyfile.write_bytes(os.urandom(64).hex().encode())
key = keyfile.read_bytes()
mac_key = hashlib.sha256(b'UniStay backup authentication\0' + key).digest()
def tag(path):
    digest = hmac.new(mac_key, digestmod=hashlib.sha256)
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()
def crypt(source, target, decrypt=False):
    run(['openssl', 'enc', '-aes-256-cbc', '-pbkdf2', '-iter', '200000',
         *(['-d'] if decrypt else ['-salt']), '-pass', 'file:' + str(keyfile),
         '-in', str(source), '-out', str(target)])
with tempfile.TemporaryDirectory(dir=root / '.local') as tmp:
    tmp = pathlib.Path(tmp)
    archive = tmp / 'backup.tar'
    if sys.argv[1] == 'backup':
        # Stop writes so database and photos describe the same point in time.
        run(compose + ['stop', 'api'])
        try:
            with open(tmp / 'database.dump', 'wb') as f:
                run(compose + ['exec', '-T', 'db', 'pg_dump', '-U', 'unistay_admin', '-d', 'unistay_csharp', '-Fc'], stdout=f)
            run(compose + ['cp', 'api:/src/.local/media', str(tmp / 'media')])
        finally:
            run(compose + ['up', '-d', '--wait', 'api'])
        with tarfile.open(archive, 'w') as tf:
            tf.add(tmp / 'database.dump', arcname='database.dump')
            tf.add(tmp / 'media', arcname='media')
        out = root / '.local/backups' / (datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f') + '.enc')
        crypt(archive, out)
        out.with_suffix('.hmac').write_text(tag(out))
        print('Encrypted backup:', out)
    elif sys.argv[1] in ('verify', 'restore-check') and len(sys.argv) == 3:
        source = pathlib.Path(sys.argv[2]).resolve()
        if not hmac.compare_digest(tag(source), source.with_suffix('.hmac').read_text().strip()):
            raise SystemExit('Authentication failed. Backup not decrypted.')
        crypt(source, archive, True)
        with tarfile.open(archive) as tf:
            dump = tf.extractfile('database.dump')
            with open(tmp / 'database.dump', 'wb') as f:
                import shutil
                shutil.copyfileobj(dump, f)
            assert any(n.startswith('media') for n in tf.getnames())
        with open(tmp / 'database.dump', 'rb') as f:
            run(compose + ['exec', '-T', 'db', 'pg_restore', '--list'], stdin=f, stdout=subprocess.DEVNULL)
        if sys.argv[1] == 'restore-check':
            import secrets
            database='unistay_csharp_restore_'+secrets.token_hex(6)
            sql=compose + ['exec', '-T', 'db', 'psql', '-U', 'unistay_admin', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c']
            run(sql + [f'CREATE DATABASE {database} OWNER unistay_api;'], stdout=subprocess.DEVNULL)
            try:
                with open(tmp / 'database.dump', 'rb') as f:
                    run(compose + ['exec','-T','db','pg_restore','-U','unistay_admin','--role=unistay_api','--no-owner','--no-acl','--exit-on-error','-d',database], stdin=f)
                run(compose + ['exec','-T','db','psql','-U','unistay_admin','-d',database,'-v','ON_ERROR_STOP=1','-c','SELECT count(*) AS restored_accounts FROM accounts; SELECT count(*) AS restored_listings FROM listings;'])
                print('Full PostgreSQL restore succeeded in a separate temporary database.')
            finally:
                run(sql + [f'DROP DATABASE {database} WITH (FORCE);'], stdout=subprocess.DEVNULL)
        print('Authentication, decryption and dump structure verified; working database unchanged.')
    else:
        raise SystemExit('Usage: backup.py backup | verify FILE.enc')
