"""Check that every study image link loads before launch, and optionally copy the images to storage.

Input: a CSV with columns Id,ImageUrl, for example exported from the prepared database:
  psql "<connection>" -X -c "\\copy (SELECT \\"Id\\", \\"ImageUrl\\" FROM \\"MedDatas\\" ORDER BY \\"Id\\") TO 'image-urls.csv' CSV HEADER"
Usage:
  python -I deployment/check-image-urls.py image-urls.csv [report.csv] [--host <folder>]

Without --host it is read-only: it only downloads the images.
With --host each image is uploaded byte for byte to the blob container named by the environment variable
CLINIMARK_IMAGE_SAS_URL (a container URL with a write SAS; the container must allow anonymous blob reads), then read
back anonymously and compared. Unchanged bytes keep every image's size and EXIF data, so coordinates are unaffected.
Blobs are named by their content hash, so a rerun is safe and can never change an image behind an existing link.
The folder receives images/ (local copies) and image-map.csv (Id,SourceUrl,HostedUrl,Sha256,Bytes), which
is written only when every image succeeded.

A server-side check approximates a participant's browser; still open a few images in the study browsers.
With Pillow installed the report also lists each image's size and EXIF data. Browsers apply EXIF rotation and may
apply EXIF resolution, so saved coordinates for flagged images differ from the file's stored pixels.
"""
import argparse
import csv
import hashlib
import http.client
import io
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor

try:
    from PIL import Image
except ImportError:
    Image = None

HEADERS = {
    # Some publishers reject unknown clients; identify like a current desktop browser.
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36',
    'Accept': 'image/avif,image/webp,image/png,image/*,*/*;q=0.8',
}
ORIENTATION, X_RESOLUTION, EXIF_IFD, PIXEL_X = 0x0112, 0x011A, 0x8769, 0xA002
ATTEMPTS, RETRY_DELAY = 3, 2
SAS_VARIABLE = 'CLINIMARK_IMAGE_SAS_URL'


def image_kind(body):
    """Returns (extension, content type) from the file's own bytes, or None for anything a browser can't show as an image."""
    if body.startswith(b'\xff\xd8\xff'):
        return 'jpg', 'image/jpeg'
    if body.startswith(b'\x89PNG\r\n\x1a\n'):
        return 'png', 'image/png'
    if body.startswith((b'GIF87a', b'GIF89a')):
        return 'gif', 'image/gif'
    if body[:4] == b'RIFF' and body[8:12] == b'WEBP':
        return 'webp', 'image/webp'
    if body[4:8] == b'ftyp' and body[8:12] in (b'avif', b'avis'):
        return 'avif', 'image/avif'
    if body.startswith(b'BM'):
        return 'bmp', 'image/bmp'
    return None


def request(url, headers=HEADERS, data=None, method=None):
    """Returns (status, content type, body). With thousands of requests, a passing network or server error is expected."""
    for attempt in range(1, ATTEMPTS + 1):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=headers, data=data, method=method), timeout=30) as response:
                return response.status, response.headers.get('Content-Type', ''), response.read()
        except urllib.error.HTTPError as error:
            if attempt == ATTEMPTS or (error.code != 429 and error.code < 500):
                raise
        except (OSError, http.client.HTTPException):
            if attempt == ATTEMPTS:
                raise
        time.sleep(RETRY_DELAY * attempt)


def describe(body):
    """Returns (format, width, height, EXIF rotation, whether EXIF can change the browser's reported size)."""
    if Image is None:
        return '', '', '', '', ''
    try:
        with Image.open(io.BytesIO(body)) as image:
            exif = image.getexif()
            resized = bool(exif.get(X_RESOLUTION)) and bool(exif.get_ifd(EXIF_IFD).get(PIXEL_X))
            return image.format, image.width, image.height, exif.get(ORIENTATION, 1), resized
    except Exception as error:  # not decodable by Pillow; the browser may still show it
        return f'unreadable:{type(error).__name__}', '', '', '', ''


def upload(body, digest, kind, container, folder):
    """Stores the image and returns its public link once an anonymous read returns the same bytes."""
    extension, content_type = kind
    name = f'{digest}.{extension}'
    with open(os.path.join(folder, 'images', name), 'wb') as handle:
        handle.write(body)
    public = f'{container.scheme}://{container.netloc}{container.path}/{name}'
    request(f'{public}?{container.query}', method='PUT', data=body, headers={
        'x-ms-blob-type': 'BlockBlob',
        'x-ms-version': '2023-11-03',
        'x-ms-blob-content-type': content_type,
        # The name changes with the content, so browsers may keep a copy for good.
        'x-ms-blob-cache-control': 'public, max-age=31536000, immutable',
    })
    status, stored_type, stored = request(public, headers={})
    if status != 200 or stored_type != content_type or hashlib.sha256(stored).hexdigest() != digest:
        raise ValueError('readback differs')
    return public


def check(row, container=None, folder=None):
    image_id, url = row['Id'], (row.get('ImageUrl') or '').strip()
    empty = ('', '', '', '', '', '', '')
    if not url.lower().startswith(('https://', 'http://')):
        return (image_id, url, 'invalid-url', '', 0) + empty
    try:
        status, content_type, body = request(url)
    except urllib.error.HTTPError as error:
        return (image_id, url, f'http-{error.code}', '', 0) + empty
    except Exception as error:  # timeouts, DNS, TLS
        return (image_id, url, type(error).__name__, '', 0) + empty
    kind = image_kind(body)
    digest = hashlib.sha256(body).hexdigest()
    facts = (content_type, len(body)) + describe(body) + (digest,)
    if status != 200:
        return (image_id, url, f'http-{status}') + facts + ('',)
    if kind is None:  # such as an HTML error or challenge page sent with status 200
        return (image_id, url, 'not-an-image') + facts + ('',)
    if container is None:
        # A link is shown as it is, so the server's own type matters; a hosted copy gets its type from the bytes.
        return (image_id, url, 'ok' if content_type.startswith('image/') else 'not-an-image') + facts + ('',)
    try:
        return (image_id, url, 'ok') + facts + (upload(body, digest, kind, container, folder),)
    except urllib.error.HTTPError as error:  # never print the URL here: the upload URL holds the SAS
        return (image_id, url, f'upload-http-{error.code}') + facts + ('',)
    except Exception as error:
        return (image_id, url, f'upload-{type(error).__name__}') + facts + ('',)


def main():
    parser = argparse.ArgumentParser(description='Check study image links and optionally copy the images to storage.')
    parser.add_argument('source')
    parser.add_argument('report', nargs='?', default='image-url-report.csv')
    parser.add_argument('--host', metavar='FOLDER', help=f'copy the images to the container in {SAS_VARIABLE}')
    arguments = parser.parse_args()

    container = None
    if arguments.host:
        container = urllib.parse.urlsplit(os.environ.get(SAS_VARIABLE, ''))
        if not container.scheme or not container.netloc or not container.path.strip('/') or not container.query:
            sys.exit(f'{SAS_VARIABLE} must hold a container URL with a write SAS.')
        container = container._replace(path=container.path.rstrip('/'))
        os.makedirs(os.path.join(arguments.host, 'images'), exist_ok=True)

    with open(arguments.source, newline='', encoding='utf-8-sig') as handle:
        rows = list(csv.DictReader(handle))
    if not rows or 'ImageUrl' not in rows[0]:
        sys.exit(f'No Id,ImageUrl rows found in {arguments.source}; nothing was checked.')
    with ThreadPoolExecutor(max_workers=6) as pool:
        results = list(pool.map(lambda row: check(row, container, arguments.host), rows))
    with open(arguments.report, 'w', newline='', encoding='utf-8') as handle:
        writer = csv.writer(handle)
        writer.writerow(['Id', 'ImageUrl', 'Result', 'ContentType', 'Bytes', 'Format', 'Width', 'Height', 'ExifRotation', 'ExifResolution', 'Sha256', 'HostedUrl'])
        writer.writerows(results)
    failures = [result for result in results if result[2] != 'ok']
    insecure = [result for result in results if result[1].lower().startswith('http://')]
    print(f'{len(results) - len(failures)}/{len(results)} images loaded; report: {arguments.report}')
    if insecure and not container:
        print(f'{len(insecure)} URLs use http:// and will be blocked or warned about on the HTTPS site.')
    if Image is None:
        print('Pillow is not installed, so image sizes and EXIF data were not checked.')
    rotated = [result[0] for result in results if result[8] not in ('', 1)]
    resized = [result[0] for result in results if result[9] is True]
    unreadable = [result[0] for result in results if str(result[5]).startswith('unreadable')]
    if rotated:
        print(f'{len(rotated)} images have EXIF rotation; coordinates use the upright image: {", ".join(rotated[:20])}')
    if resized:
        print(f'{len(resized)} images have EXIF resolution data; a browser may report another size: {", ".join(resized[:20])}')
    if unreadable:
        print(f'{len(unreadable)} images could not be decoded here; open them in a browser: {", ".join(unreadable[:20])}')
    for result in failures[:20]:
        print(f'  FAIL {result[0]}: {result[2]} {result[1]}')
    if failures:
        sys.exit(1)
    if container:
        with open(os.path.join(arguments.host, 'image-map.csv'), 'w', newline='', encoding='utf-8') as handle:
            writer = csv.writer(handle)
            writer.writerow(['Id', 'SourceUrl', 'HostedUrl', 'Sha256', 'Bytes'])
            writer.writerows((result[0], result[1], result[11], result[10], result[4]) for result in results)
        print(f'Copied {len(results)} images ({len({result[10] for result in results})} distinct files); verified by anonymous read-back.')


if __name__ == '__main__':
    main()
