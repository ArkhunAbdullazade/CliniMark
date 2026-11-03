"""Proves in a real browser that saved coordinates match what the annotator pointed at.

Draws through the real pages, saves through the real app, and reads the rows back from PostgreSQL. Creates accounts
and annotations, so run it only against a disposable medannotate_audit_* database. Needs playwright and Pillow, the app
on http://127.0.0.1:5210 in Development, and:
  MEDANNOTATE_SMOKE_GATE      the research access password
  MEDANNOTATE_AUDIT_PSQL      path to psql
  MEDANNOTATE_AUDIT_DB        libpq connection string whose dbname starts with medannotate_audit_ (PGPASSWORD for its password)
  MEDANNOTATE_ADMIN_EMAIL / MEDANNOTATE_ADMIN_PASSWORD   an account from create-admin
"""
import csv
import http.server
import io
import json
import math
import os
import subprocess
import sys
import tempfile
import threading
import time
import uuid
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw
from playwright.sync_api import sync_playwright

BASE = 'http://127.0.0.1:5210'
IMAGE_PORT = 5212
GATE = os.environ['MEDANNOTATE_SMOKE_GATE']
PSQL = os.environ['MEDANNOTATE_AUDIT_PSQL']
DATABASE = os.environ['MEDANNOTATE_AUDIT_DB']
PASSWORD = 'SyntheticOnly!2026'
assert 'dbname=medannotate_audit_' in DATABASE, 'Run this only on a disposable medannotate_audit_* database.'

KINDS = {'wide': (1600, 1000), 'small': (300, 200), 'tall': (800, 3000)}
WINDOWS = [(1920, 1080), (1366, 768), (1024, 768), (820, 1180)]
RATIOS = [1, 1.25, 2]
MARKERS = {(0.25, 0.25): (0, 200, 0), (0.75, 0.25): (0, 0, 220), (0.25, 0.75): (220, 0, 220), (0.75, 0.75): (230, 120, 0)}
failures, passed = [], 0
widest_marker_error = 0.0


def check(condition, message):
    global passed
    if condition:
        passed += 1
    else:
        failures.append(message)
        print('FAIL', message, flush=True)


def sql(statement):
    result = subprocess.run([PSQL, '-X', '-w', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-d', DATABASE], input=statement,
                            capture_output=True, text=True, encoding='utf-8', check=True)
    return result.stdout.strip()


def draw_markers(width, height):
    image = Image.new('RGB', (width, height), (235, 235, 235))
    canvas = ImageDraw.Draw(image)
    half = max(width, height) / 60
    for (fx, fy), colour in MARKERS.items():
        # Whole pixels, with one extra pixel past the far edge, so the marker's true centre is exactly fx*width, fy*height.
        canvas.rectangle([round(fx * width - half), round(fy * height - half), round(fx * width + half) - 1, round(fy * height + half) - 1], fill=colour)
    return image


def write_images(folder):
    for name, (width, height) in KINDS.items():
        draw_markers(width, height).save(folder / f'{name}.png')
    # Stored 600x400 with EXIF "rotate 90": the browser shows it upright as 400x600.
    upright = draw_markers(400, 600).rotate(90, expand=True)
    exif = Image.Exif()
    exif[0x0112] = 6
    upright.save(folder / 'rotated.jpg', quality=95, exif=exif)
    Image.new('RGB', (6000, 4000), (200, 200, 200)).save(folder / 'huge.png')


def serve(folder):
    class Quiet(http.server.SimpleHTTPRequestHandler):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, directory=str(folder), **kwargs)

        def log_message(self, *_):
            pass

    server = http.server.ThreadingHTTPServer(('127.0.0.1', IMAGE_PORT), Quiet)
    threading.Thread(target=server.serve_forever, daemon=True).start()


def seed(cases):
    rows = []
    for index, name in enumerate(cases, 1):
        url = f'http://127.0.0.1:{IMAGE_PORT}/{name}?n={index}'
        rows.append(f"({index}, '{url}', 'Synthetic image: lesion. Not patient data.', 'Cardiology', 'Chest and Thorax', 'CT', false, false)")
    keywords = ','.join(f"({index}, 'lesion')" for index in range(1, len(cases) + 1))
    sql('INSERT INTO "MedDatas" ("Id","ImageUrl","ImageDescription","Speciality","BodyRegion","Modality","IsAnnotated","IsAnnotatedByStudent") VALUES '
        + ','.join(rows) + f'; INSERT INTO "MedDataKeywords" ("MedDataId","Keyword") VALUES {keywords};')


class Session:
    def __init__(self, browser, email, window, ratio):
        self.context = browser.new_context(viewport={'width': window[0], 'height': window[1]}, device_scale_factor=ratio)
        self.page = self.context.new_page()
        self.ratio = ratio
        self.errors = []
        self.page.on('console', lambda message: self.errors.append(message.text) if message.type == 'error' else None)
        self.page.on('pageerror', lambda error: self.errors.append(str(error)))
        self.page.goto(BASE + '/Identity/AuthorizationAccess')
        self.page.fill('input[name=Password]', GATE)
        self.page.click('button[type=submit]')
        self.page.wait_for_url('**/Identity/Login')
        self.email = email

    def login(self):
        self.page.fill('input[name=Email]', self.email)
        self.page.fill('input[name=Password]', PASSWORD)
        self.page.click('button[type=submit]')
        self.page.wait_for_load_state('load')

    def signup(self, position):
        self.page.goto(BASE + '/Identity/Signup')
        for name, value in (('Email', self.email), ('FullName', 'Synthetic Audit'), ('Password', PASSWORD), ('ConfirmPassword', PASSWORD),
                            ('University', 'Local test institute'), ('ClinicalExperience', '5')):
            self.page.fill(f'input[name={name}]', value)
        self.page.select_option('select[name=Position]', position)
        for name, value in (('Speciality', 'Cardiology'), ('BodyRegion', 'Chest and Thorax'), ('ImageModality', 'CT')):
            self.page.check(f'input[name={name}][value="{value}"]')
        self.page.click('form[data-signup-form] button[type=submit]')
        self.page.click('[data-signup-yes]')
        self.page.wait_for_url('**/Identity/Login')

    def ready(self):
        self.page.wait_for_function('() => window.canvasManager && (window.canvasManager.baseManager || window.canvasManager).ready === true', timeout=30000)
        return self.page.evaluate('window.annotatedMedData.id')

    def geometry(self):
        return self.page.evaluate('''() => { const c = document.getElementById("mainCanvas"), r = c.getBoundingClientRect();
            return {left: r.left, top: r.top, width: r.width, height: r.height, w: c.width, h: c.height}; }''')

    def client(self, x, y):
        g = self.geometry()
        return g['left'] + x * g['width'] / g['w'], g['top'] + y * g['height'] / g['h']

    def tolerance(self):
        g = self.geometry()
        return 1.01 * g['w'] / g['width'] + 0.01

    def on_canvas(self, x, y, label):
        hit = self.page.evaluate('([x, y]) => document.elementFromPoint(x, y)?.id', [x, y])
        check(hit == 'mainCanvas', f'{label}: the point {x:.1f},{y:.1f} is covered by {hit}')

    def drag(self, start, end, label):
        self.on_canvas(*start, label)
        self.page.mouse.move(*start)
        self.page.mouse.down()
        self.page.mouse.move(*end, steps=6)
        self.page.mouse.up()

    def rectangle(self, a, b, label):
        self.drag(self.client(*a), self.client(*b), label)
        return [[a[0], a[1]], [b[0], a[1]], [b[0], b[1]], [a[0], b[1]]]

    def freehand(self, points, label):
        self.on_canvas(*self.client(*points[0]), label)
        self.page.mouse.move(*self.client(*points[0]))
        self.page.mouse.down()
        for point in points[1:]:
            self.page.mouse.move(*self.client(*point), steps=3)
        self.page.mouse.up()

    def click_tool(self, name):
        self.page.click(f'#{name}ModeButton')

    def selected_handles(self):
        return self.page.evaluate('''() => { const m = window.canvasManager.baseManager || window.canvasManager;
            return m.getAnnotationHandles(m.selectedAnnotation); }''')

    def deselect(self, x, y):
        # A click on empty image clears the selection, so a handle cannot catch the next stroke.
        px, py = self.client(x, y)
        self.page.mouse.click(px, py)


def assert_markers_where_mapping_says(session, width, height, label):
    """Clicks and saves share one mapping, so a shifted image would cancel out in the saved numbers. Only comparing the
    mapping with where the image's pixels really landed on screen can reveal it, so measure each marker's true centre."""
    global widest_marker_error
    shot = Image.open(io.BytesIO(session.page.screenshot())).convert('RGB')
    pixels = shot.load()
    scale = session.geometry()['width'] / width
    reach = max(width, height) / 60 * scale * session.ratio * 2 + 8
    for (fx, fy), colour in MARKERS.items():
        x, y = session.client(fx * width, fy * height)
        x, y = x * session.ratio, y * session.ratio
        found = [(px + .5, py + .5) for py in range(max(0, int(y - reach)), min(shot.height, int(y + reach) + 1))
                 for px in range(max(0, int(x - reach)), min(shot.width, int(x + reach) + 1))
                 if all(abs(a - b) < 40 for a, b in zip(pixels[px, py], colour))]
        check(bool(found), f'{label}: marker {fx},{fy} is not near its mapped screen position')
        if found:
            error = max(abs(sum(p[0] for p in found) / len(found) - x), abs(sum(p[1] for p in found) / len(found) - y)) / session.ratio
            widest_marker_error = max(widest_marker_error, error)
            check(error <= 1, f'{label}: marker {fx},{fy} is {error:.2f} screen pixels from where the mapping puts it')


def close(actual, expected, tolerance):
    return all(abs(a - b) <= tolerance for a, b in zip(actual, expected))


def compare(points, expected, tolerance, label):
    check(len(points) == len(expected), f'{label}: {len(points)} points, expected {len(expected)}')
    for actual, wanted in zip(points, expected):
        check(close(actual, wanted, tolerance), f'{label}: point {actual} is not within {tolerance:.2f} of {wanted}')


def rotated(corners, centre, angle):
    cos, sin = math.cos(angle), math.sin(angle)
    return [[centre[0] + (x - centre[0]) * cos - (y - centre[1]) * sin, centre[1] + (x - centre[0]) * sin + (y - centre[1]) * cos] for x, y in corners]


def specialist_case(session, image_id, kind, resize_to=None, scroll=False):
    width, height = KINDS[kind]
    label = f'specialist image {image_id} {kind} {session.page.viewport_size} ratio {session.ratio}'
    print(label, flush=True)
    assert_markers_where_mapping_says(session, width, height, label)
    W, H = width, height
    if scroll:
        session.page.evaluate('''() => { const t = document.querySelector(".header-text"); if (t) t.scrollTop = t.scrollHeight;
            window.scrollTo(0, document.documentElement.scrollHeight); }''')
        check(session.geometry()['top'] < 100000, f'{label}: page scrolled')
    expected = []

    # 1. A plain rectangle.
    expected.append(('rectangle', session.rectangle((.08 * W, .08 * H), (.3 * W, .3 * H), label), 1))
    if resize_to:
        session.page.set_viewport_size({'width': resize_to[0], 'height': resize_to[1]})
        session.page.wait_for_timeout(150)
    session.deselect(.5 * W, .02 * H)

    # 2. A freehand outline.
    session.click_tool('freehand')
    outline = [(.6 * W, .08 * H), (.9 * W, .08 * H), (.9 * W, .3 * H), (.75 * W, .36 * H), (.6 * W, .3 * H)]
    session.freehand(outline, label)
    expected.append(('freehand', [list(point) for point in outline], 1))
    session.click_tool('rectangle')
    session.deselect(.5 * W, .02 * H)

    # 3. Drawn, then moved.
    tol = session.tolerance()
    start = ((.08 * W, .62 * H), (.28 * W, .78 * H))
    session.rectangle(*start, label)
    shift = (.04 * W, .05 * H)
    centre = ((start[0][0] + start[1][0]) / 2, (start[0][1] + start[1][1]) / 2)
    session.drag(session.client(*centre), session.client(centre[0] + shift[0], centre[1] + shift[1]), label + ' move')
    a, b = (start[0][0] + shift[0], start[0][1] + shift[1]), (start[1][0] + shift[0], start[1][1] + shift[1])
    expected.append(('rectangle', [[a[0], a[1]], [b[0], a[1]], [b[0], b[1]], [a[0], b[1]]], 2))
    session.deselect(.5 * W, .02 * H)

    # 4. Drawn, then resized by its bottom-right corner; the top-left corner must stay put.
    a = (.62 * W, .62 * H)
    session.rectangle(a, (.74 * W, .74 * H), label)
    handle = session.selected_handles()['br']
    target = (.84 * W, .86 * H)
    session.drag(session.client(handle['x'], handle['y']), session.client(*target), label + ' resize')
    expected.append(('rectangle', [[a[0], a[1]], [target[0], a[1]], [target[0], target[1]], [a[0], target[1]]], 2))
    session.deselect(.5 * W, .02 * H)

    # 5. Dragged past the image's bottom-right corner: it must stop exactly on the image edge.
    a = (.93 * W, .93 * H)
    g = session.geometry()
    session.drag(session.client(*a), (g['left'] + g['width'] + 40, g['top'] + g['height'] + 40), label + ' edge')
    expected.append(('rectangle', [[a[0], a[1]], [W, a[1]], [W, H], [a[0], H]], 1))
    session.deselect(.5 * W, .02 * H)

    # 6. Dragged past the top-left corner.
    a = (.05 * W, .05 * H)
    session.drag(session.client(*a), (g['left'] - 40, g['top'] - 40), label + ' corner')
    expected.append(('rectangle', [[0, 0], [a[0], 0], [a[0], a[1]], [0, a[1]]], 1))
    session.deselect(.5 * W, .02 * H)

    # 7. Drawn, then rotated by its round handle.
    a, b = (.38 * W, .42 * H), (.56 * W, .58 * H)
    corners = session.rectangle(a, b, label)
    centre = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
    handle = session.selected_handles()['rotate']
    start_angle = math.atan2(handle['y'] - centre[1], handle['x'] - centre[0])
    radius = math.hypot(handle['x'] - centre[0], handle['y'] - centre[1])
    turn = math.radians(30)
    target = (centre[0] + radius * math.cos(start_angle + turn), centre[1] + radius * math.sin(start_angle + turn))
    session.drag(session.client(handle['x'], handle['y']), session.client(*target), label + ' rotate')
    reach = max(math.hypot(x - centre[0], y - centre[1]) for x, y in corners)
    expected.append(('rectangle', rotated(corners, centre, turn), 2 + reach / radius))

    session.page.wait_for_function('() => !document.getElementById("next-button").disabled')
    session.page.click('#next-button')
    deadline = time.time() + 15
    saved = ''
    while time.time() < deadline and not saved:
        saved = sql(f'''SELECT "BoxCoordinates" FROM "AnnotatedMedDatas" WHERE "MedDataId" = {image_id} AND "Email" = '{session.email}';''')
        time.sleep(0.1)
    check(bool(saved), f'{label}: nothing was saved')
    document = json.loads(saved)
    check((document['imageWidth'], document['imageHeight']) == (width, height), f"{label}: saved size {document['imageWidth']}x{document['imageHeight']}")
    shapes = document['annotations']
    check(len(shapes) == len(expected), f'{label}: {len(shapes)} shapes saved, expected {len(expected)}')
    tolerance = session.tolerance()
    # Some tolerance scales with the page size, which may have changed mid-way: use the coarser of both.
    for index, ((shape_type, points, factor), shape) in enumerate(zip(expected, shapes)):
        tol_here = max(tol, tolerance) * factor
        check(shape['type'] == shape_type, f'{label}: shape {index} is {shape["type"]}')
        if shape_type == 'freehand':
            compare(shape['points'][:1], points[:1], tol_here, f'{label} freehand start')
            check(shape['points'][0] == shape['points'][-1], f'{label}: freehand not closed')
            for wanted in points:
                check(any(close(actual, wanted, tol_here) for actual in shape['points']), f'{label}: freehand misses {wanted}')
        else:
            compare(shape['points'], points, tol_here, f'{label} shape {index}')
        for x, y in shape['points']:
            check(0 <= x <= width and 0 <= y <= height, f'{label}: point {x},{y} is outside the image')

    # Reopening the saved term must bring back the same corners.
    session.page.wait_for_function('() => document.querySelector(".keyword.annotated_keyword")')
    session.page.click('.keyword.annotated_keyword')
    session.page.wait_for_function('() => window.canvasManager.annotations.length > 0')
    reopened = session.page.evaluate('window.canvasManager.annotations.map(s => AnnotationGeometry.corners(s))')
    for index, (points, shape) in enumerate(zip(reopened, shapes)):
        compare(points, shape['points'], 0.0011, f'{label} reopened shape {index}')
    session.page.wait_for_function('() => !document.getElementById("next-button").disabled')
    session.page.click('#next-button')
    session.page.wait_for_load_state('load')
    return document


def student_case(session, image_id, kind):
    width, height = KINDS[kind]
    label = f'student image {image_id} {kind} {session.page.viewport_size} ratio {session.ratio}'
    print(label, flush=True)
    assert_markers_where_mapping_says(session, width, height, label)
    W, H = width, height
    tol = session.tolerance()
    expected = session.rectangle((.1 * W, .1 * H), (.4 * W, .4 * H), label)
    session.page.fill('.floating-input', 'first group')
    session.page.click('.floating-save-btn')
    session.click_tool('freehand')
    outline = [(.55 * W, .55 * H), (.9 * W, .55 * H), (.9 * W, .9 * H), (.55 * W, .9 * H)]
    session.freehand(outline, label)
    session.page.fill('.floating-input', 'second group')
    session.page.click('.floating-save-btn')
    session.page.click('#submit-button')
    session.page.wait_for_load_state('load')
    deadline = time.time() + 15
    rows = ''
    while time.time() < deadline and not rows:
        rows = sql(f'''SELECT json_agg(json_build_object('label', "TextualAnnotation", 'c', "Coordinates") ORDER BY "Id") FROM "AnnotatedByStudentsMedDatas" WHERE "MedDataId" = {image_id} AND "Email" = '{session.email}';''')
        time.sleep(0.1)
    groups = json.loads(rows)
    check([group['label'] for group in groups] == ['first group', 'second group'], f'{label}: labels {groups}')
    first, second = json.loads(groups[0]['c']), json.loads(groups[1]['c'])
    check((first['imageWidth'], first['imageHeight']) == (width, height), f'{label}: saved size')
    compare(first['annotations'][0]['points'], expected, tol, f'{label} rectangle')
    for wanted in outline:
        check(any(close(actual, wanted, tol) for actual in second['annotations'][0]['points']), f'{label}: freehand misses {wanted}')


def exif_case(browser, email, image_id):
    """A rotated JPEG must be shown upright, with coordinates and size in that upright image."""
    session = Session(browser, email, (1366, 768), 1)
    session.login()
    check(session.ready() == image_id, 'the rotated image was not assigned next')
    size = session.page.evaluate('[window.canvasManager.canvas.width, window.canvasManager.canvas.height]')
    check(size == [400, 600], f'EXIF rotation: browser reports {size}, expected the upright 400x600')
    assert_markers_where_mapping_says(session, 400, 600, 'EXIF rotated image')
    corners = session.rectangle((40, 60), (160, 240), 'EXIF')
    session.page.click('#next-button')
    deadline, saved = time.time() + 15, ''
    while time.time() < deadline and not saved:
        saved = sql(f'''SELECT "BoxCoordinates" FROM "AnnotatedMedDatas" WHERE "MedDataId" = {image_id} AND "Email" = '{email}';''')
        time.sleep(0.1)
    document = json.loads(saved)
    check((document['imageWidth'], document['imageHeight']) == (400, 600), 'EXIF rotation: saved size is not the upright one')
    compare(document['annotations'][0]['points'], corners, session.tolerance(), 'EXIF rectangle')
    session.page.wait_for_function('() => !document.getElementById("next-button").disabled')
    session.page.click('#next-button')
    session.page.wait_for_load_state('load')
    session.context.close()


def draw_speed(browser, email, image_id):
    session = Session(browser, email, (1366, 768), 1)
    session.login()
    check(session.ready() == image_id, 'the large image was not assigned next')
    # Time whole frames, so the browser's own painting of the redraw counts too.
    timings = session.page.evaluate('''async () => { const m = window.canvasManager, times = [];
        for (let i = 0; i < 40; i++) { const start = performance.now(); m.drawCanvas(); await new Promise(requestAnimationFrame); times.push(performance.now() - start); }
        times.sort((a, b) => a - b); return {median: times[20], worst: times[39]}; }''')
    print(f"6000x4000 redraw: median {timings['median']:.1f} ms, worst {timings['worst']:.1f} ms")
    session.context.close()
    return timings


def admin_case(browser, image_id, saved):
    context = browser.new_context(viewport={'width': 1440, 'height': 900})
    page = context.new_page()
    page.goto(BASE + '/Identity/AuthorizationAccess')
    page.fill('input[name=Password]', GATE)
    page.click('button[type=submit]')
    page.wait_for_url('**/Identity/Login')
    page.fill('input[name=Email]', os.environ['MEDANNOTATE_ADMIN_EMAIL'])
    page.fill('input[name=Password]', os.environ['MEDANNOTATE_ADMIN_PASSWORD'])
    page.click('button[type=submit]')
    page.wait_for_url('**/Admin**')
    page.goto(f'{BASE}/Admin/Images/Details/{image_id}')
    page.wait_for_function('() => document.querySelector("#image-stage svg") && document.getElementById("admin-image").naturalWidth > 0')
    drawn = page.evaluate('''() => { const s = document.querySelector("#image-stage svg").getBoundingClientRect(), i = document.getElementById("admin-image").getBoundingClientRect();
        return {box: [s.left - i.left, s.top - i.top, s.width - i.width, s.height - i.height],
                polygons: [...document.querySelectorAll('#image-stage svg polygon[fill]:not([fill="none"])')].map(p => [...p.points].map(q => [q.x, q.y]))}; }''')
    check(all(abs(value) <= 1 for value in drawn['box']), f'admin overlay is offset from the image: {drawn["box"]}')
    for index, shape in enumerate(saved['annotations']):
        compare(drawn['polygons'][index], shape['points'], 0.01, f'admin overlay shape {index}')

    # Both export formats must carry the database value unchanged.
    stored = sql(f'''SELECT "BoxCoordinates" FROM "AnnotatedMedDatas" WHERE "MedDataId" = {image_id} LIMIT 1;''')
    for form, name in (('Csv', 'specialist_responses.csv'), ('Json', 'specialist_responses.json')):
        response = context.request.get(f'{BASE}/Admin/Export/Download?format={form}&identity=Pseudonymous&completeness=All')
        check(response.ok, f'{form} export failed: {response.status}')
        with zipfile.ZipFile(io.BytesIO(response.body())) as archive:
            text = archive.read(name).decode('utf-8-sig')
        if form == 'Csv':
            rows = [row for row in csv.DictReader(io.StringIO(text, newline='')) if row['ImageId'] == str(image_id)]
        else:
            rows = [row for row in json.loads(text) if str(row['ImageId']) == str(image_id)]
        check(any(row['Coordinates'] == stored for row in rows), f'{form} export changed the coordinates of image {image_id}')
    context.close()


def main():
    specialist_cases = [(kind, window, ratio) for kind in KINDS for window in WINDOWS for ratio in RATIOS]
    extras = [('wide', (1366, 768), 1, (1024, 600), False), ('wide', (1024, 500), 1, None, True), ('tall', (1366, 768), 1, (1920, 1080), False)]
    names = [f'{kind}.png' for kind, _, _ in specialist_cases] + [f'{kind}.png' for kind, *_ in extras] + ['rotated.jpg', 'huge.png']
    with tempfile.TemporaryDirectory() as temporary:
        folder = Path(temporary)
        write_images(folder)
        serve(folder)
        seed(names)
        specialist, student = f'coord-{uuid.uuid4().hex[:8]}@example.net', f'coord-student-{uuid.uuid4().hex[:8]}@example.net'
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(channel=os.environ.get('MEDANNOTATE_BROWSER', 'msedge'))
            first = Session(browser, specialist, (1366, 768), 1)
            first.signup('specialist')
            first.context.close()
            first = Session(browser, student, (1366, 768), 1)
            first.signup('medical student')
            first.context.close()

            documents, errors = {}, []
            for image_id, (kind, window, ratio) in enumerate(specialist_cases, 1):
                session = Session(browser, specialist, window, ratio)
                session.login()
                check(session.ready() == image_id, f'image {image_id} was not assigned next')
                documents[image_id] = specialist_case(session, image_id, kind)
                errors += session.errors
                session.context.close()
            for offset, (kind, window, ratio, resize_to, scroll) in enumerate(extras, len(specialist_cases) + 1):
                session = Session(browser, specialist, window, ratio)
                session.login()
                check(session.ready() == offset, f'image {offset} was not assigned next')
                documents[offset] = specialist_case(session, offset, kind, resize_to, scroll)
                errors += session.errors
                session.context.close()
            exif_case(browser, specialist, len(specialist_cases) + len(extras) + 1)
            timings = draw_speed(browser, specialist, len(specialist_cases) + len(extras) + 2)

            for image_id, (kind, window, ratio) in enumerate(specialist_cases[:12], 1):
                session = Session(browser, student, window, ratio)
                session.login()
                check(session.ready() == image_id, f'student image {image_id} was not assigned next')
                student_case(session, image_id, kind)
                errors += session.errors
                session.context.close()

            admin_case(browser, 1, documents[1])
            browser.close()
    # An image the browser could not fetch would already have failed; only unexpected errors remain.
    unexpected = [error for error in errors if 'favicon' not in error]
    check(not unexpected, f'browser errors: {unexpected[:5]}')
    print(json.dumps({'passed': passed, 'failed': len(failures), 'widest_marker_error_px': round(widest_marker_error, 3), 'redraw_ms': timings, 'failures': failures[:30]}, indent=2))
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
