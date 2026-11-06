"""Tests deployment/check-image-urls.py against a local stand-in for the publisher and blob storage.

  python -I tests/deployment/test_image_hosting.py
"""
import csv
import hashlib
import importlib.util
import os
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'deployment', 'check-image-urls.py')
SECRET = 'sv=2023&sig=TOP-SECRET'
# Not a decodable picture, but enough for the type check; the script must keep these exact bytes.
JPEG = b'\xff\xd8\xff\xe0' + bytes(range(256)) * 4
PNG = b'\x89PNG\r\n\x1a\n' + bytes(range(255, -1, -1)) * 4


def load_script():
    spec = importlib.util.spec_from_file_location('check_image_urls', SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class Server(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send(self, status, content_type='', body=b''):
        self.send_response(status)
        if content_type:
            self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        state = self.server.state
        if self.path.startswith('/images/'):
            blob = state['blobs'].get(self.path)
            if state['corrupt_reads'] and blob:
                return self.send(200, blob[0], blob[1] + b'x')
            return self.send(200, *blob) if blob else self.send(404)
        if self.path == '/flaky.jpg':
            state['flaky'] += 1
            return self.send(503) if state['flaky'] == 1 else self.send(200, 'image/jpeg', JPEG)
        pages = {
            '/a.jpg': ('image/jpeg', JPEG),
            '/b.png': ('image/png', PNG),
            '/untyped.jpg': ('application/octet-stream', JPEG),
            '/challenge.jpg': ('text/html', b'<html>Verify you are human</html>'),
            '/mislabelled.jpg': ('image/jpeg', b'<html>Verify you are human</html>'),
        }
        return self.send(200, *pages[self.path]) if self.path in pages else self.send(404)

    def do_PUT(self):
        state = self.server.state
        body = self.rfile.read(int(self.headers['Content-Length']))
        if state['deny_uploads'] or self.path.split('?')[1] != SECRET or self.headers['x-ms-blob-type'] != 'BlockBlob':
            return self.send(403)
        path = self.path.split('?')[0]
        state['blobs'][path] = (self.headers['x-ms-blob-content-type'], body)
        state['cache'][path] = self.headers['x-ms-blob-cache-control']
        self.send(201)


class ImageHostingTests(unittest.TestCase):
    def setUp(self):
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), Server)
        self.server.state = {'blobs': {}, 'cache': {}, 'flaky': 0, 'deny_uploads': False, 'corrupt_reads': False}
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.base = f'http://127.0.0.1:{self.server.server_port}'
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.folder = folder.name

    def run_script(self, paths, host=True):
        source, report = os.path.join(self.folder, 'urls.csv'), os.path.join(self.folder, 'report.csv')
        with open(source, 'w', newline='', encoding='utf-8') as handle:
            writer = csv.writer(handle)
            writer.writerow(['Id', 'ImageUrl'])
            writer.writerows((index, self.base + path) for index, path in enumerate(paths, 1))
        command = [sys.executable, '-I', SCRIPT, source, report] + (['--host', self.folder] if host else [])
        environment = dict(os.environ, CLINIMARK_IMAGE_SAS_URL=f'{self.base}/images?{SECRET}')
        done = subprocess.run(command, capture_output=True, text=True, env=environment)
        with open(report, newline='', encoding='utf-8') as handle:
            return done, list(csv.DictReader(handle))

    def read_map(self):
        with open(os.path.join(self.folder, 'image-map.csv'), newline='', encoding='utf-8') as handle:
            return list(csv.DictReader(handle))

    def has_map(self):
        return os.path.exists(os.path.join(self.folder, 'image-map.csv'))

    def test_copies_are_byte_identical_with_the_type_from_the_bytes_and_a_lasting_cache(self):
        done, _ = self.run_script(['/a.jpg', '/b.png', '/untyped.jpg'])
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        rows = self.read_map()
        self.assertEqual([row['Id'] for row in rows], ['1', '2', '3'])
        self.assertEqual(rows[0]['SourceUrl'], self.base + '/a.jpg')
        digest = hashlib.sha256(JPEG).hexdigest()
        self.assertEqual(rows[0]['HostedUrl'], f'{self.base}/images/{digest}.jpg')
        self.assertEqual(rows[0]['HostedUrl'], rows[2]['HostedUrl'], 'identical files share one blob')
        self.assertEqual(self.server.state['blobs'][f'/images/{digest}.jpg'], ('image/jpeg', JPEG))
        self.assertIn('immutable', self.server.state['cache'][f'/images/{digest}.jpg'])
        with open(os.path.join(self.folder, 'images', f'{digest}.jpg'), 'rb') as handle:
            self.assertEqual(handle.read(), JPEG)

    def test_rerun_keeps_the_same_links(self):
        self.run_script(['/a.jpg'])
        first = self.read_map()[0]['HostedUrl']
        done, _ = self.run_script(['/a.jpg'])
        self.assertEqual(done.returncode, 0)
        self.assertEqual(self.read_map()[0]['HostedUrl'], first)

    def test_non_images_stop_the_copy_even_with_status_200_or_an_image_type(self):
        for path in ('/challenge.jpg', '/mislabelled.jpg', '/missing.jpg'):
            done, rows = self.run_script(['/a.jpg', path])
            self.assertEqual(done.returncode, 1, path)
            self.assertNotEqual(rows[1]['Result'], 'ok', path)
            self.assertFalse(self.has_map(), path)

    def test_a_passing_server_error_is_retried(self):
        done, rows = self.run_script(['/flaky.jpg'])
        self.assertEqual(done.returncode, 0, done.stdout)
        self.assertEqual(rows[0]['Result'], 'ok')

    def test_a_refused_upload_stops_the_launch_without_leaking_the_sas(self):
        self.server.state['deny_uploads'] = True
        done, rows = self.run_script(['/a.jpg'])
        self.assertEqual(done.returncode, 1)
        self.assertEqual(rows[0]['Result'], 'upload-http-403')
        self.assertFalse(self.has_map())
        with open(os.path.join(self.folder, 'report.csv'), encoding='utf-8') as handle:
            self.assertNotIn('TOP-SECRET', handle.read())
        self.assertNotIn('TOP-SECRET', done.stdout + done.stderr)

    def test_changed_bytes_on_read_back_stop_the_launch(self):
        self.server.state['corrupt_reads'] = True
        done, rows = self.run_script(['/a.jpg'])
        self.assertEqual(done.returncode, 1)
        self.assertEqual(rows[0]['Result'], 'upload-ValueError')
        self.assertFalse(self.has_map())

    def test_saved_files_and_links_contain_no_sas(self):
        self.run_script(['/a.jpg'])
        with open(os.path.join(self.folder, 'image-map.csv'), encoding='utf-8') as handle:
            self.assertNotIn('TOP-SECRET', handle.read())

    def test_without_host_nothing_is_uploaded_and_links_must_declare_an_image_type(self):
        done, rows = self.run_script(['/a.jpg'], host=False)
        self.assertEqual(done.returncode, 0, done.stdout)
        self.assertEqual(self.server.state['blobs'], {})
        self.assertFalse(self.has_map())
        self.assertEqual(rows[0]['HostedUrl'], '')
        done, rows = self.run_script(['/untyped.jpg'], host=False)
        self.assertEqual(done.returncode, 1)
        self.assertEqual(rows[0]['Result'], 'not-an-image')

    def test_host_without_a_usable_sas_refuses_to_start(self):
        source = os.path.join(self.folder, 'urls.csv')
        with open(source, 'w', encoding='utf-8') as handle:
            handle.write('Id,ImageUrl\n1,http://127.0.0.1:9/a.jpg\n')
        environment = {key: value for key, value in os.environ.items() if key != 'CLINIMARK_IMAGE_SAS_URL'}
        done = subprocess.run([sys.executable, '-I', SCRIPT, source, os.path.join(self.folder, 'r.csv'), '--host', self.folder],
                              capture_output=True, text=True, env=environment)
        self.assertNotEqual(done.returncode, 0)
        self.assertIn('CLINIMARK_IMAGE_SAS_URL', done.stderr)

    def test_image_kinds(self):
        kind = load_script().image_kind
        self.assertEqual(kind(JPEG), ('jpg', 'image/jpeg'))
        self.assertEqual(kind(PNG), ('png', 'image/png'))
        self.assertEqual(kind(b'GIF89a...'), ('gif', 'image/gif'))
        self.assertEqual(kind(b'RIFF\x00\x00\x00\x00WEBPVP8 '), ('webp', 'image/webp'))
        self.assertEqual(kind(b'\x00\x00\x00\x1cftypavif'), ('avif', 'image/avif'))
        self.assertEqual(kind(b'BM\x00\x00'), ('bmp', 'image/bmp'))
        for other in (b'', b'<svg xmlns="http://www.w3.org/2000/svg"/>', b'<html></html>', b'%PDF-1.7'):
            self.assertIsNone(kind(other))


if __name__ == '__main__':
    unittest.main()
