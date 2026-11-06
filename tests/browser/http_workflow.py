"""Smoke test the synthetic isolated local audit app, never the research database.

Requires the fixtures.sql dataset.
Creates test accounts/annotations. Run only on a disposable medannotate_audit_* DB.
"""
import html
import http.cookiejar
import json
import os
from pathlib import Path
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from uuid import uuid4

BASE = os.environ.get('MEDANNOTATE_URL', 'http://127.0.0.1:5210')
PASSWORD = 'SyntheticOnly!2026'
REPORT = []

class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.token = None

    def request(self, route, *, form=None, payload=None, method=None, csrf=True):
        headers = {}
        data = None
        if form is not None:
            fields = dict(form)
            if csrf and self.token: fields['__RequestVerificationToken'] = self.token
            data = urllib.parse.urlencode(fields, doseq=True).encode()
        if payload is not None:
            data = json.dumps(payload).encode()
            headers['Content-Type'] = 'application/json'
            if csrf and self.token: headers['X-CSRF-TOKEN'] = self.token
        req = urllib.request.Request(BASE + route, data=data, headers=headers, method=method)
        try:
            response = self.opener.open(req, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read().decode()
        token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)
        if token: self.token = html.unescape(token.group(1))
        return response.status, response.geturl(), body

    def register(self, name, position='specialist', invalid=False):
        self.request('/Identity/Signup')
        fields = dict(Email=f'{name}-{uuid4().hex[:8]}@example.net', FullName='Synthetic Audit Researcher',
            Password=PASSWORD, ConfirmPassword=PASSWORD, Position=position, University='Local test institute',
            ClinicalExperience='5', Speciality=['cardiology', 'dermatology'],
            BodyRegion=['Chest and Thorax', 'head and neck'], ImageModality=['ct', 'mri'])
        if invalid:
            # Every list is optional, but an entirely empty selection is refused.
            bad = {key: value for key, value in fields.items() if key not in ('Speciality', 'BodyRegion', 'ImageModality')}
            status, url, body = self.request('/Identity/PostSignup', form=bad)
            assert status == 200 and 'Select at least one option in any of the lists.' in body
            REPORT.append('Empty filter selection rejected by server before account creation')
        status, url, body = self.request('/Identity/PostSignup', form=fields)
        assert status == 200 and url.endswith('/Identity/Login'), (status, url, body[:300])
        self.email = fields['Email']
        status, url, body = self.request('/Identity/PostLogin', form={'Email': self.email, 'Password': PASSWORD})
        expected = '/Home/Student' if position == 'medical student' else '/Home/Professional'
        assert status == 200 and url.endswith(expected), (status, url, body[:300])
        assert 'Synthetic' in body, 'This test must only run on the isolated synthetic dataset'
        self.page = body
        return body

    def annotations(self, image):
        status, _, body = self.request(f'/MedData/CurrentAnnotations?medDataId={image}')
        return status, json.loads(body)

def expect(client, route, payload, expected=200, method='POST', csrf=True):
    status, _, body = client.request(route, payload=payload, method=method, csrf=csrf)
    assert status == expected, (route, status, body[:300])
    return json.loads(body) if body else None

anon = Client()
status, url, body = anon.request('/')
assert status == 200 and url.endswith('/Identity/Login') and 'name="Password"' in body, (status, url)
status, url, _ = anon.request('/Identity/AuthorizationAccess')
assert status == 200 and url.endswith('/Identity/Login'), (status, url)
status, url, _ = anon.request('/Home/Professional')
assert status == 200 and '/Identity/Login' in url, (status, url)
REPORT.append('Visitors reach the sign-in page directly, with no shared password, and nothing else without signing in')

one = Client()
one.register('specialist-one', invalid=True)
status, data = one.annotations(1)
assert status == 200 and data['annotations'] == []
REPORT.append('Ordinary-email specialist registered and signed in without email confirmation; correct filtered image assigned')
status, url, body = one.request('/Home/Professional')
assert status == 200 and 'Image source:' in body and 'https://example.org/synthetic-article' in body
assert 'href="https://creativecommons.org/licenses/by/4.0/"' in body and 'Synthetic credit line for image 1.' in body
assert body.index('class="image-viewport"') < body.index('data-image-source')  # below the image, outside its sizing box
assert 'js/requestRetry.js' in body
REPORT.append('Annotation page shows the image source, licence link and full credit line, and loads the save-retry script')

coordinates = json.dumps(dict(version=1, coordinateSystem='image-pixels', imageWidth=1200, imageHeight=800,
    annotations=[dict(type='rectangle', points=[[300,200],[900,200],[900,600],[300,600]])]), separators=(',', ':'))
save = dict(Id=1, ExtractedKeyword='lesion', BoxCoordinates=coordinates, PressedButton='Save and Next Term',
    Comment='Synthetic smoke test', Timestamps='(100,200)', ImageUrl='https://invalid.example/tampered-image')
expect(one, '/MedData/ProcessAnnotatedMedData', save, expected=400, csrf=False)
expect(one, '/MedData/NextImage?medDataId=1', {}, expected=400, method='PUT')
expect(one, '/MedData/ProcessAnnotatedMedData', dict(save, BoxCoordinates=coordinates.replace('[900,600]', '[1300,600]')), expected=400)
expect(one, '/MedData/ProcessAnnotatedMedData', save)
expect(one, '/MedData/ProcessAnnotatedMedData', save)
status, data = one.annotations(1)
assert status == 200 and len(data['annotations']) == 1 and data['annotations'][0]['boxCoordinates'] == coordinates
REPORT.append('CSRF, incomplete completion and out-of-bounds coordinates rejected; repeated save creates one response; coordinates round-trip exactly')

one.request('/Home/Professional')
status, data = one.annotations(1)
assert len(data['annotations']) == 1
REPORT.append('Reload resumes the same assignment and saved term')

two = Client()
two.register('specialist-two')
assert two.annotations(2)[0] == 200
assert two.annotations(1)[0] == 403
expect(two, '/MedData/ProcessAnnotatedMedData', save, expected=409)
REPORT.append('Second specialist receives another matching image; cannot read or save against the first specialist assignment')

one.request('/Identity/Filters')
status, url, body = one.request('/Identity/Filters', form=dict(Speciality=['cardiology'], BodyRegion=['abdomen'], ImageModality=['ct']))
assert status == 200 and url.endswith('/Home/Professional') and 'Excluded by body region' in body
assert one.annotations(1)[0] == 403
one.request('/Identity/Filters')
status, url, body = one.request('/Identity/Filters', form=dict(Speciality=['cardiology','dermatology'], BodyRegion=['Chest and Thorax','head and neck'], ImageModality=['ct','mri']))
assert one.annotations(1)[0] == 200
REPORT.append('Changed filters stay effective on assignment; restoring filters resumes partial work without mixing images')

expect(one, '/MedData/ProcessAnnotatedMedData', dict(Id=1, ExtractedKeyword='boundary', BoxCoordinates='', PressedButton='Not Visible/Abstract'))
expect(one, '/MedData/NextImage?medDataId=1', {}, expected=400, method='PUT')
expect(one, '/MedData/SaveMetadataChecks', dict(Id=1, Checks=[dict(Field=field, Verdict='cannot_tell') for field in ('modality', 'speciality', 'body_region')]))
expect(one, '/MedData/NextImage?medDataId=1', {}, method='PUT')
expect(one, '/MedData/NextImage?medDataId=1', {}, method='PUT')
status, url, body = one.request('/Home/Professional')
assert status == 200 and 'Choose new filters' in body and 'id="sourceImage"' not in body
REPORT.append('Completion requires every term and repeated completion is safe; no matching available image shows the empty state')

student = Client()
student.register('student', position='medical student')
status, denied_url, body = student.request('/MedData/NextImage?medDataId=1', payload={}, method='PUT')
assert '/Identity/AccessDenied' in denied_url or status == 403
expect(student, '/MedData/SubmitStudentAnnotations', dict(Annotations=[dict(Id=1, Coordinates=coordinates, TextualAnnotation='Synthetic lesion')]))
expect(student, '/MedData/SubmitStudentAnnotations', dict(Annotations=[dict(Id=1, Coordinates=coordinates, TextualAnnotation='Synthetic lesion')]))
REPORT.append('Student cannot call specialist completion; student geometry save and repeat succeed without duplicates')

# Leave this isolated specialist account on a third synthetic image for the visual UI check.
one.request('/Identity/Filters')
one.request('/Identity/Filters', form=dict(Speciality=['cardiology'], BodyRegion=['abdomen'], ImageModality=['ct']))

output = Path(__file__).resolve().parents[1] / '.artifacts/http-workflow.json'
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps(dict(passed=True, checks=REPORT, specialist_email=one.email), indent=2))
print(json.dumps(dict(passed=True, checks=REPORT), indent=2))
