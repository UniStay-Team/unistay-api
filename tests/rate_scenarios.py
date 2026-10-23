"""Default production-sized local rate policies, without test relaxation."""
import json, urllib.request, urllib.error
import http_scenarios as h
for _ in range(10): h.call('POST','auth/login/',{'email':'nobody@example.test','password':'not-valid-password'},expected=401)
for path,body in [('auth/login/',{'email':'nobody@example.test','password':'not-valid-password'}),('auth/password-reset/',{'email':'nobody@example.test'})]:
    if 'password-reset' in path:
        for _ in range(5): h.call('POST',path,body)
    req=urllib.request.Request(h.BASE+'/api/v1/'+path,data=json.dumps(body).encode(),headers={'Content-Type':'application/json'})
    try: urllib.request.urlopen(req); raise AssertionError('Expected 429')
    except urllib.error.HTTPError as e:
        assert e.code==429 and int(e.headers['Retry-After'])>0
        assert json.loads(e.read())['error']['status']==429
print('PASS: auth 10/min, mail 5/hour and Retry-After with default limits.')
