"""Synthetic HTTP + PostgreSQL concurrency checks. Local C# development stack only."""
import base64, concurrent.futures, io, json, os, pathlib, secrets, sys, time, urllib.error, urllib.request
ROOT=pathlib.Path(__file__).resolve().parents[1]
BASE=os.environ.get('UNISTAY_TEST_URL','http://127.0.0.1:8001')
MAIL=pathlib.Path(os.environ.get('UNISTAY_MAIL_DIRECTORY',str(ROOT/'.local/mail')))
checks=0

def call(method, path, body=None, token=None, expected=200):
    global checks
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    data=json.dumps(body).encode() if body is not None else None
    request=urllib.request.Request(BASE+'/api/v1/'+urllib.parse.quote(path,safe='/?=&%'), data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=25) as r: status, raw=r.status,r.read()
    except urllib.error.HTTPError as r: status,raw=r.code,r.read()
    result=json.loads(raw) if raw else None
    if expected is not None:
        assert status==expected, (method,path,status,expected,result)
        checks+=1
    return (status,result) if expected is None else result

def mail(email, kind):
    for attempt in range(30):
        for p in sorted(MAIL.glob('*.json'), key=lambda x:x.stat().st_mtime, reverse=True):
            value=json.loads(p.read_text())
            if value['email']==email and value['kind']==kind: return value
        time.sleep(0.1)
    raise AssertionError('Local mail not found')

suffix=secrets.token_hex(5)
password='Fixture-only!'+secrets.token_hex(10)
def account(role, label):
    email=f'{label}-{suffix}@example.test'.lower()
    user=call('POST','auth/register/',dict(email=email,password=password,name=label,role=role),expected=201)
    call('POST','auth/verify-email/',{'token':mail(email,'verify')['token']})
    login=call('POST','auth/login/',dict(email=email,password=password))
    assert set(login)=={'token','expires_at','user'}
    assert 'password' not in json.dumps(user) and 'is_staff' not in user
    return dict(email=email, token=login['token'], user=login['user'])

def upload(owner, listing, valid=True, expected=201):
    global checks
    boundary='fixture'+secrets.token_hex(8)
    # A synthetic RGB image generated using the already installed development Pillow.
    from PIL import Image
    output=io.BytesIO(); Image.new('RGB',(8,8),'green').save(output,'PNG')
    photo=output.getvalue() if valid else b'not-an-image'
    data=(f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="fixture.png"\r\nContent-Type: image/png\r\n\r\n'.encode()+photo+f'\r\n--{boundary}--\r\n'.encode())
    req=urllib.request.Request(BASE+f'/api/v1/listings/{listing}/photos/', data=data, headers={'Authorization':'Bearer '+owner['token'],'Content-Type':'multipart/form-data; boundary='+boundary},method='POST')
    try:
        with urllib.request.urlopen(req,timeout=25) as r: status,raw=r.status,r.read()
    except urllib.error.HTTPError as r: status,raw=r.code,r.read()
    result=json.loads(raw)
    assert status==expected,(status, result)
    checks+=1
    return result

def listing(owner, capacity=1):
    value=call('POST','listings/',dict(title='Fixture '+suffix,description='Synthetic test listing',city='Астана',district='Есиль',address='Synthetic address',university='AITU',price='85000.00',capacity=capacity),owner['token'],201)
    assert value['price']=='85000.00' and value['available']==capacity
    call('PATCH',f"listings/{value['id']}/",{'status':'published'},owner['token'],400)
    photo=upload(owner,value['id'])
    value=call('PATCH',f"listings/{value['id']}/",{'status':'published'},owner['token'])
    return value,photo

def apply(student, housing):
    return call('POST','applications/',{'listing_id':housing['id']},student['token'],201)
def transition(owner, app, target, expected=200):
    return call('POST',f"applications/{app['id']}/{target}/",token=owner['token'],expected=expected)

if __name__ == '__main__':
    call('GET','me/',expected=401)
    call('POST','auth/register/',{'email':'bad','password':'short','name':'x','role':'admin'},expected=400)
    owner=account('landlord','Owner')
    other=account('landlord','Other')
    student=account('student','Student')
    second=account('student','Second')
    for user in (student,second):
        call('PATCH','me/profile/',{'university':'AITU','schedule':'early','cleanliness':'tidy','smoking':False,'share_with_group':True},user['token'])
        assert call('GET','me/profile/',token=user['token'])['smoking'] is False
        call('PATCH','me/profile/',{'university':None},user['token'],400)
        call('GET','admin/users/',token=user['token'],expected=403)
    call('GET','admin/users/',token=owner['token'],expected=403)
    a,photo=listing(owner)
    b,_=listing(other)
    call('PATCH',f"listings/{a['id']}/",{'title':'attack'},other['token'],404)
    call('GET','listings/?min_price=NaN',expected=400)
    call('GET','listings/?available=invalid',expected=400)
    assert call('GET',f"listings/{a['id']}/")['address'] is None
    upload(owner,a['id'],False,400)
    call('DELETE',f"photos/{photo['id']}/",token=other['token'],expected=404)
    call('DELETE',f"photos/{photo['id']}/",token=owner['token'],expected=409)
    one=apply(student,a); two=apply(second,a)
    call('POST','applications/',{'listing_id':a['id']},student['token'],409)
    call('GET',f"applications/{one['id']}/",token=other['token'],expected=404)
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        results=list(pool.map(lambda x:transition(owner,x,'accept',None),(one,two)))
    assert sorted(r[0] for r in results)==[200,409],results
    winner_index=next(i for i,r in enumerate(results) if r[0]==200)
    winner=(student,second)[winner_index]; winning=(one,two)[winner_index]
    assert transition(owner,winning,'accept')['events'] == results[winner_index][1]['events']
    assert call('GET',f"listings/{a['id']}/",token=winner['token'])['address']=='Synthetic address'
    call('PATCH',f"listings/{a['id']}/",{'price':'1.00'},owner['token'],409)
    transition(winner,winning,'cancel')
    assert call('GET',f"listings/{a['id']}/",token=winner['token'])['address'] is None
    loser=(second,student)[winner_index]; losing=(two,one)[winner_index]
    transition(loser,losing,'cancel')
    first=apply(student,a); another=apply(student,b)
    with concurrent.futures.ThreadPoolExecutor(2) as pool:
        fs=[pool.submit(transition,owner,first,'accept',None),pool.submit(transition,other,another,'accept',None)]
        race=[f.result() for f in fs]
    assert sorted(r[0] for r in race)==[200,409],race
    winner_owner=owner if race[0][0]==200 else other
    winner_listing=a if race[0][0]==200 else b
    winner_app=first if race[0][0]==200 else another
    assert len(call('GET',f"listings/{winner_listing['id']}/group/",token=second['token']))==1
    call('PATCH','me/profile/',{'share_with_group':False},student['token'])
    assert call('GET',f"listings/{winner_listing['id']}/group/",token=second['token'])==[]
    assert len(call('GET',f"listings/{winner_listing['id']}/group/",token=winner_owner['token']))==1
    call('DELETE',f"listings/{winner_listing['id']}/",token=winner_owner['token'],expected=204)
    call('GET',f"listings/{winner_listing['id']}/",expected=404)
    assert call('GET',f"listings/{winner_listing['id']}/",token=student['token'])['address']=='Synthetic address'
    transition(student,winner_app,'cancel')
    call('POST','auth/password-reset/',{'email':student['email']})
    reset=mail(student['email'],'reset'); new_password='Changed!'+secrets.token_hex(12)
    call('POST','auth/password-reset/confirm/',dict(uid=reset['uid'],token=reset['token'],password=new_password))
    call('GET','me/',token=student['token'],expected=401)
    call('POST','auth/password-reset/confirm/',dict(uid=reset['uid'],token=reset['token'],password=new_password),expected=400)
    call('POST','auth/login/',dict(email=student['email'],password=password),expected=401)
    call('POST','auth/logout/',token=owner['token'],expected=204)
    call('GET','me/',token=owner['token'],expected=401)
    print(f'PASS: {checks} HTTP assertions; last-place and same-student PostgreSQL races; synthetic suffix {suffix}.')
