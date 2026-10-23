"""Exact administrative contract, PostgreSQL audit and public-client integration."""
import json, os, secrets, subprocess, urllib.request
import http_scenarios as h
email='admin-'+secrets.token_hex(6)+'@example.test'
password='Admin-fixture!'+secrets.token_hex(12)
container=os.environ['UNISTAY_TEST_CONTAINER']
command=['docker','exec','-i',container,'dotnet','UniStay.Api/bin/Debug/net10.0/UniStay.Api.dll','--create-admin']
subprocess.run(command,cwd=h.ROOT,input=json.dumps(dict(email=email,name='Admin fixture',password=password,role='student')).encode(),check=True,stdout=subprocess.DEVNULL)
admin=h.call('POST','auth/login/',dict(email=email,password=password)); token=admin['token']
me=h.call('GET','admin/me/',token=token)
assert set(me)=={'id','email','name','permissions'} and 'users.update' in me['permissions']
h.call('POST','auth/register/',dict(email='forbidden@example.test',password=password,name='Forbidden',role='student',is_admin=True),expected=400)
student=h.account('student','Managed'); owner=h.account('landlord','Owner')
uid=student['user']['id']
collections=('users','listings','photos','applications','application-events')
for path in collections:
    assert set(h.call('GET',f'admin/{path}/',token=token))=={'count','next','previous','results'}
    for actor in (student,owner):
        h.call('GET',f'admin/{path}/',token=actor['token'],expected=403)
        h.call('DELETE',f'admin/{path}/1/',token=actor['token'],expected=403)
    h.call('POST',f'admin/{path}/',{},token,405)
    h.call('GET',f'admin/{path}/?page=0',token=token,expected=400)
    h.call('GET',f'admin/{path}/?page=unknown',token=token,expected=400)
    h.call('GET',f'admin/{path}/?page=999',token=token,expected=404)
    h.call('GET',f'admin/{path}/?page_size=10',token=token,expected=400)
h.call('GET','admin/users/',expected=401)
h.call('DELETE',f'admin/users/{uid}/',token=token,expected=405)
h.call('POST',f'admin/users/{uid}/password/',{'password':password},token,404)
h.call('GET','admin/events/',token=token,expected=404)
h.call('PATCH',f"admin/users/{admin['user']['id']}/",{'is_active':False},token,409)
for field,value in [('email','another@example.test'),('role','landlord'),('is_admin',True),('permissions',['users.update']),('email_verified',False)]:
    h.call('PATCH',f'admin/users/{uid}/',{'name':'must not persist',field:value},token,400)
for data in ({},{'name':None},{'name':' '},{'is_active':None}): h.call('PATCH',f'admin/users/{uid}/',data,token,400)
user=h.call('GET',f'admin/users/{uid}/',token=token)
assert user['name']=='Managed'
assert set(user)==set('id email name role email_verified is_active is_admin date_joined'.split())
assert user['date_joined'].endswith('Z')
user=h.call('PATCH',f'admin/users/{uid}/',{'name':' Changed '},token)
assert user['name']=='Changed'
h.call('GET','me/',token=student['token']) # name change must not revoke session
h.call('PATCH',f'admin/users/{uid}/',{'name':'Changed'},token)
profile_path=f'admin/users/{uid}/profile/'
for data in ({},{'share_with_group':True},{'user_id':uid},{'bio':None},{'schedule':'bad'},{'university':'x'*161}):
    h.call('PATCH',profile_path,data,token,400)
profile=h.call('PATCH',profile_path,{'university':'AITU','schedule':'early','cleanliness':'tidy','smoking':False},token)
assert profile['smoking'] is False and profile['share_with_group'] is False and profile['user_id']==uid
h.call('PATCH',profile_path,{'smoking':False},token)
listing,photo=h.listing(owner)
app=h.apply(student,listing)
admin_app=h.call('GET',f"admin/applications/{app['id']}/",token=token)
assert admin_app['id']==app['id'] and admin_app['student_id']==uid and admin_app['listing_id']==listing['id']
assert set(admin_app)==set('id student_id student_name listing_id listing_title status message created_at updated_at'.split())
h.transition(owner,app,'accept')
admin_listing=h.call('GET',f"admin/listings/{listing['id']}/",token=token)
assert admin_listing['address']=='Synthetic address' and admin_listing['occupied']==1
assert set(admin_listing)==set('id owner_id owner_name title description city district address university price deposit utilities_included capacity move_in_date status occupied available group_ready created_at updated_at'.split())
admin_photo=h.call('GET',f"admin/photos/{photo['id']}/",token=token)
assert set(admin_photo)==set('id listing_id image caption created_at'.split())
events=h.call('GET',f"admin/application-events/?application_id={app['id']}",token=token)['results']
assert len(events)==2 and set(events[0])==set('id application_id actor_id from_status to_status created_at'.split())
assert h.call('GET',f"admin/application-events/{events[0]['id']}/",token=token)==events[0]
for path in (f"listings/{listing['id']}",f"photos/{photo['id']}",f"applications/{app['id']}",f"application-events/{events[0]['id']}"):
    h.call('PATCH',f'admin/{path}/',{},token,405)
    h.call('DELETE',f'admin/{path}/',token=token,expected=405)
for query in ('role=unknown','is_active=1','email_verified=null','page=1&page=2'):
    h.call('GET','admin/users/?'+query,token=token,expected=400)
for path in ('photos/?listing_id=-1','applications/?status=bad','listings/?owner_id=abc','application-events/?actor_id=0'):
    h.call('GET','admin/'+path,token=token,expected=400)
assert h.call('GET',f'admin/users/?search={student["email"]}&role=student&is_active=true&email_verified=true',token=token)['count']==1
assert h.call('GET',f"admin/listings/?owner_id={owner['user']['id']}&city=астана&status=published",token=token)['count']==1
assert h.call('GET',f'admin/applications/?student_id={uid}&status=accepted',token=token)['count']==1
h.call('PATCH',f'admin/users/{uid}/',{'is_active':False},token)
h.call('GET','me/',token=student['token'],expected=401)
h.call('POST','auth/login/',dict(email=student['email'],password=h.password),expected=401)
assert h.call('GET',f"admin/listings/{listing['id']}/",token=token)['occupied']==1
h.call('PATCH',f'admin/users/{uid}/',{'is_active':True},token)
h.call('GET','me/',token=student['token'],expected=401)
h.call('POST','auth/login/',dict(email=student['email'],password=h.password))
# Query only this disposable test database; do not expose secrets or audit personal data.
def sql(query):
    return subprocess.check_output(['docker','compose','--env-file','.env.local','exec','-T','db','psql','-U','unistay_admin','-d',os.environ['UNISTAY_TEST_DATABASE'],'-At','-c',query],cwd=h.ROOT,text=True).strip()
assert sql('SELECT count(*) FROM admin_audit_events')=='4'
assert sql('SELECT count(*) FROM admin_audit_events WHERE "RequestId" = \'\'')=='0'
assert sql('SELECT "ChangedFields"::text FROM admin_audit_events ORDER BY "Id" LIMIT 1')=='{name}'
# More than one page, stable ordering and filter-preserving links.
sql("""INSERT INTO accounts ("Email","Name","Role","PasswordHash","EmailVerified","IsActive","IsStaff","IsSuperuser","Permissions","DateJoined") SELECT 'page-'||n||'@example.test','Pagination fixture','student','unusable',true,true,false,false,ARRAY[]::text[],CURRENT_TIMESTAMP FROM generate_series(1,25) n""")
page=h.call('GET','admin/users/?search=Pagination&page=1',token=token)
assert page['count']==25 and len(page['results'])==20 and 'search=Pagination' in page['next']
second=h.call('GET','admin/users/?search=Pagination&page=2',token=token)
assert len(second['results'])==5 and second['previous'] and second['next'] is None
assert max(x['id'] for x in page['results']) < min(x['id'] for x in second['results'])
with urllib.request.urlopen(h.BASE+'/api/schema/') as response: schema=json.load(response)
assert '/api/v1/admin/application-events/{id}/' in schema['paths']
assert 'delete' not in schema['paths']['/api/v1/admin/users/{id}/']
assert {p['name'] for p in schema['paths']['/api/v1/admin/users/']['get']['parameters']}=={'page','search','role','is_active','email_verified'}
# D0 restricted account can read only its three sections.
sql(f'''UPDATE accounts SET "IsStaff"=true,"Permissions"=ARRAY['users.read','listings.read','applications.read'] WHERE "Id"={owner['user']['id']}''')
limited=h.call('POST','auth/login/',dict(email=owner['email'],password=h.password))['token']
for path in ('users','listings','applications'): h.call('GET',f'admin/{path}/',token=limited)
h.call('GET','admin/photos/',token=limited,expected=403)
h.call('PATCH',f'admin/users/{uid}/',{'name':'forbidden'},limited,403)
request=urllib.request.Request(h.BASE+'/api/v1/admin/users/',method='OPTIONS',headers={'Origin':'http://localhost:4200','Access-Control-Request-Method':'GET','Access-Control-Request-Headers':'authorization'})
with urllib.request.urlopen(request) as response: assert response.headers['Access-Control-Allow-Origin']=='http://localhost:4200'
print(f'PASS: {h.checks} administrative HTTP checks; exact DTOs, access, filters, atomic audit, revocation and shared application IDs.')
