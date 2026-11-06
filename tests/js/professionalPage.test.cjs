const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/MedAnnotateApp.Presentation/wwwroot/js/professionalPage.js'), 'utf8');

// Browser events the page sends: failed requests go to problemReport.js, inputs refresh the note dots.
class FakeEvent { constructor(type, init = {}) { this.type = type; this.detail = init.detail; } }

class Element {
  constructor(text = '', keyword) {
    this.textContent = text;
    this.value = '';
    this.dataset = keyword ? {keyword} : {};
    this.listeners = {};
    this.attributes = {};
    const classes = new Set();
    this.classList = {
      add: name => classes.add(name),
      remove: (...names) => names.forEach(name => classes.delete(name)),
      contains: name => classes.has(name)
    };
  }
  addEventListener(name, callback) {
    (this.listeners[name] ||= []).push(callback);
  }
  setAttribute(name, value) { this.attributes[name] = value; }
  replaceChildren(...nodes) {
    this.children = nodes;
    this.textContent = nodes.map(node => typeof node === 'string' ? node : node.textContent).join('');
  }
  async fire(name, event = {}) {
    for (const callback of this.listeners[name] || []) await callback(event);
  }
  click() { return this.fire('click'); }
  dispatchEvent(event) { (this.dispatched ||= []).push(event); }
}

async function page(occurrences, expectedCount, options = {}) {
  const loadHistory = options.loadHistory;
  const saveAnnotation = options.saveAnnotation;
  const skipImage = options.skipImage;
  const events = {};
  const elements = Object.fromEntries(['annotation-status', 'next-button', 'skip-button', 'skip-image-button',
    'not-present-button', 'comments', 'technical-problem', 'image-container'].map(id => [id, new Element()]));
  const header = new Element();
  header.dataset.keywordCount = String(expectedCount);
  let onReady;
  const calls = [];
  class CanvasManager {
    constructor() {
      this.canvas = new Element();
      this.ready = options.imageReady ?? true;
      this.annotations = [];
      this.currentAction = 'none';
    }
    createTools() {
      return {rectButton: new Element(), freehandButton: new Element(), zoomButton: new Element()};
    }
    resetAnnotation() { this.annotations = []; }
    stopInteraction() { this.stopped = true; this.readOnly = true; }
    getAllAnnotationsJSON() { return 'synthetic image-coordinate envelope'; }
    loadCoordinates(coordinates) { this.loaded = coordinates; }
  }
  const requestErrors = [], confirms = [], reloads = [];
  const context = {
    window: {annotatedMedData: {id: 1}, addEventListener(name, callback) { events[name] = callback; }, watchAssignment: options.watch,
      setupMetadataCheck: options.labels},
    CustomEvent: FakeEvent, Event: FakeEvent,
    document: {
      dispatchEvent: event => { if (event.type === 'annotationrequesterror') requestErrors.push(event.detail); },
      addEventListener(name, callback) { if (name === 'DOMContentLoaded') onReady = callback; },
      createElement: tag => Object.assign(new Element(), {tagName: tag.toUpperCase()}),
      getElementById: id => elements[id],
      querySelectorAll: selector => selector === '.header-text .keyword' ? occurrences : [],
      querySelector: selector => selector === '.header-text' ? header : {value: 'synthetic-csrf'}
    },
    CanvasManager, confirm: message => { confirms.push(message); return options.confirm ?? true; },
    location: {reload() { reloads.push(true); }},
    fetch: async (url, options = {}) => {
      calls.push({url, ...options});
      if (url.includes('CurrentAnnotations') && loadHistory) return loadHistory();
      if (url === '/MedData/ProcessAnnotatedMedData' && saveAnnotation) return saveAnnotation();
      if (url.includes('SkipImage') && skipImage) return skipImage();
      return {ok: true, json: async () => url.includes('CurrentAnnotations') ? {annotations: []} : {success: true}};
    }
  };
  vm.runInNewContext(source, context, {filename: 'professionalPage.js'});
  onReady();
  await new Promise(setImmediate);
  const warnsBeforeLeaving = () => {
    let warned = false;
    events.beforeunload({preventDefault() { warned = true; }});
    return warned;
  };
  return {elements, calls, manager: context.window.canvasManager, warnsBeforeLeaving, requestErrors, confirms, reloads};
}

test('inline repeated phrases advance once per canonical term in description order', async () => {
  const firstBoundary = new Element('Boundary', 'boundary');
  const lesion = new Element('LESION', 'lesion');
  const repeatedBoundary = new Element('boundary', 'boundary');
  const {elements, calls, manager} = await page([firstBoundary, lesion, repeatedBoundary], 2);
  assert.equal(firstBoundary.classList.contains('current_keyword'), true);
  assert.equal(repeatedBoundary.classList.contains('current_keyword'), true);
  assert.equal(lesion.classList.contains('not_annotated_keyword'), true);

  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  await elements['next-button'].click();
  assert.equal(firstBoundary.classList.contains('annotated_keyword'), true);
  assert.equal(repeatedBoundary.classList.contains('annotated_keyword'), true);
  assert.equal(lesion.classList.contains('current_keyword'), true);

  await repeatedBoundary.click();
  assert.equal(manager.readOnly, true, 'Repeated saved phrase opens the same saved response');
  assert.equal(manager.loaded, 'synthetic image-coordinate envelope');
  await lesion.click();
  assert.equal(manager.readOnly, false);
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  await elements['next-button'].click();

  const saves = calls.filter(call => call.url === '/MedData/ProcessAnnotatedMedData');
  assert.deepEqual(saves.map(call => JSON.parse(call.body).ExtractedKeyword), ['boundary', 'lesion']);
  assert.equal(elements['next-button'].textContent, 'Next Image');
  assert.equal(elements['skip-button'].disabled, true);
});

function fakeLabels() {
  const labels = {done: false, enabled: null, loaded: null, attention: false, complete: () => labels.done,
    setEnabled(value) { labels.enabled = value; }, setAttention(value) { labels.attention = value; }, load(records) { labels.loaded = records; }};
  return {labels, setup: settings => { labels.settings = settings; return labels; }};
}

async function finishTerm(elements, manager) {
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  await elements['next-button'].click();
}

test('Next Image waits for the label check and the status says what is missing', async () => {
  const {labels, setup} = fakeLabels();
  const {elements, manager, calls} = await page([new Element('lesion', 'lesion')], 1, {labels: setup});
  assert.equal(labels.enabled, true);
  await finishTerm(elements, manager);
  assert.equal(elements['next-button'].textContent, 'Next Image');
  assert.equal(elements['next-button'].disabled, true);
  assert.match(elements['annotation-status'].textContent, /Confirm the image modality, speciality and body region/);

  labels.done = true;
  labels.settings.onChange();
  assert.equal(elements['next-button'].disabled, false);
  assert.match(elements['annotation-status'].textContent, /Select Next Image/);
  await elements['next-button'].click();
  assert.equal(calls.filter(call => call.url.includes('/MedData/NextImage')).length, 1);
});

test('what is still missing is shown in bold red and the label box is tinted, only once every term is saved', async () => {
  const {labels, setup} = fakeLabels();
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1, {labels: setup});
  assert.equal(labels.attention, false);
  await finishTerm(elements, manager);
  assert.equal(labels.attention, true);
  const emphasised = elements['annotation-status'].children.find(node => typeof node !== 'string');
  assert.equal(emphasised.className, 'status-term');
  assert.equal(emphasised.textContent, 'Confirm the image modality, speciality and body region');

  labels.done = true;
  labels.settings.onChange();
  assert.equal(labels.attention, false);
  assert.doesNotMatch(elements['annotation-status'].textContent, /Confirm the image modality/);
});

test('reviewing a saved term keeps saying that the labels are missing, until they are answered', async () => {
  const {labels, setup} = fakeLabels();
  const term = new Element('lesion', 'lesion');
  const {elements, manager} = await page([term], 1, {labels: setup});
  await finishTerm(elements, manager);
  await term.click();
  assert.match(elements['annotation-status'].textContent, /^Saved annotation for lesion \(read only\)\. .*Confirm the image modality/);

  labels.done = true;
  labels.settings.onChange();
  assert.match(elements['annotation-status'].textContent, /^Saved annotation for lesion \(read only\)\. All terms are saved\. Select Next Image/);
});

test('Skip entire image moves on at once when nothing has been entered', async () => {
  const {elements, calls, confirms, reloads, warnsBeforeLeaving} = await page([new Element('lesion', 'lesion')], 1);
  assert.equal(elements['skip-image-button'].disabled, false);
  await elements['skip-image-button'].click();
  const skip = calls.find(call => call.url.includes('/MedData/SkipImage'));
  assert.equal(skip.url, '/MedData/SkipImage?medDataId=1');
  assert.equal(skip.method, 'PUT');
  assert.equal(confirms.length, 0);
  assert.equal(reloads.length, 1);
  assert.equal(warnsBeforeLeaving(), false);
});

test('Skip entire image asks first when it would remove saved work, and declining changes nothing', async () => {
  const {elements, calls, confirms, reloads} = await page([new Element('lesion', 'lesion')], 1, {confirm: false,
    loadHistory: () => ({ok: true, json: async () => ({annotations: [{extractedKeyword: 'lesion', pressedButton: 'Save and Next Term', boxCoordinates: '{}'}]})})});
  await elements['skip-image-button'].click();
  assert.equal(confirms.length, 1);
  assert.equal(calls.filter(call => call.url.includes('SkipImage')).length, 0);
  assert.equal(reloads.length, 0);
});

test('Skip entire image also asks when a shape is drawn but not saved', async () => {
  const {elements, manager, confirms, calls} = await page([new Element('lesion', 'lesion')], 1);
  manager.annotations = [{type: 'rectangle'}];
  manager.onAnnotationsChange();
  await elements['skip-image-button'].click();
  assert.equal(confirms.length, 1);
  assert.equal(calls.filter(call => call.url.includes('SkipImage')).length, 1);
});

test('a broken image can still be skipped, so it never traps the specialist', async () => {
  const {elements, calls} = await page([new Element('lesion', 'lesion')], 1, {imageReady: false});
  assert.equal(elements['next-button'].disabled, true);
  assert.equal(elements['skip-image-button'].disabled, false);
  await elements['skip-image-button'].click();
  assert.equal(calls.filter(call => call.url.includes('SkipImage')).length, 1);
});

test('a failed skip stays on the image and says why', async () => {
  const {elements, reloads, requestErrors} = await page([new Element('lesion', 'lesion')], 1,
    {skipImage: () => ({ok: false, status: 409, json: async () => ({success: false, message: 'This image is no longer assigned to you.'})})});
  await elements['skip-image-button'].click();
  assert.equal(reloads.length, 0);
  assert.equal(elements['skip-image-button'].disabled, false);
  assert.match(elements['annotation-status'].textContent, /no longer assigned/);
  assert.equal(requestErrors.length, 1);
});

test('the label check does not hold back saving the terms', async () => {
  const {setup} = fakeLabels();
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1, {labels: setup});
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  assert.equal(elements['next-button'].textContent, 'Save and Next Term');
  assert.equal(elements['next-button'].disabled, false);
});

test('saved label answers are restored with the term answers', async () => {
  const {labels, setup} = fakeLabels();
  const records = [{field: 'modality', verdict: 'correct'}];
  await page([new Element('lesion', 'lesion')], 1, {labels: setup, loadHistory: () => ({ok: true,
    json: async () => ({annotations: [], metadataChecks: records})})});
  assert.deepEqual(labels.loaded, records);
});

test('a released image closes the label check', async () => {
  const {labels, setup} = fakeLabels();
  let onReleased;
  await page([new Element('lesion', 'lesion')], 1, {labels: setup,
    watch: settings => { onReleased = settings.onReleased; return {check() {}}; }});
  assert.equal(labels.enabled, true);
  onReleased();
  assert.equal(labels.enabled, false);
});

test('inspection restores the specialist status and keeps the current shapes and term', async () => {
  const term = new Element('lesion', 'lesion');
  const {elements, manager} = await page([term], 1);
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  const status = elements['annotation-status'], before = status.textContent;
  manager.onInspectionChange(true);
  assert.equal(status.dataset.state, 'inspection');
  assert.match(status.textContent, /^Zoom to Area/);
  assert.equal(term.classList.contains('current_keyword'), true);
  assert.equal(manager.annotations.length, 1);
  manager.onInspectionChange(false);
  assert.equal(status.textContent, before);
  assert.equal(status.dataset.state, 'unsaved');
});

test('specialist status updates during inspection are restored when it ends', async () => {
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1);
  const status = elements['annotation-status'];
  manager.onInspectionChange(true);
  elements.comments.value = 'A comment entered while inspecting';
  await elements.comments.fire('input');
  assert.equal(status.dataset.state, 'inspection');
  manager.onInspectionChange(false);
  assert.equal(status.textContent, 'Unsaved comment.');
  assert.equal(status.dataset.state, 'unsaved');
});

test('inspection never covers specialist errors, including an image error arriving while inspecting', async () => {
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1);
  const status = elements['annotation-status'];
  status.textContent = 'The outline must enclose an area.';
  status.dataset.state = 'error';
  manager.onInspectionChange(true);
  assert.equal(status.textContent, 'The outline must enclose an area.');
  assert.equal(status.dataset.state, 'error');
  status.textContent = 'This image could not be loaded.';
  status.dataset.state = 'error'; manager.ready = false;
  await manager.canvas.fire('annotationimageerror');
  manager.onInspectionChange(false);
  assert.equal(status.textContent, 'This image could not be loaded.');
  assert.equal(status.dataset.state, 'error');
});

test('specialist saving status takes priority over the inspection hint', async () => {
  const response = deferred();
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1,
    {saveAnnotation: () => response.promise});
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange(); manager.onInspectionChange(true);
  const saving = elements['next-button'].click();
  await new Promise(setImmediate);
  const status = elements['annotation-status'];
  assert.equal(status.dataset.state, 'saving');
  assert.match(status.textContent, /Saving/);
  manager.onInspectionChange(false);
  assert.equal(status.dataset.state, 'saving');
  response.resolve({ok: true, json: async () => ({success: true})});
  await saving;
});

test('a missing description keyword blocks annotation and completion controls', async () => {
  const {elements, calls, manager} = await page([new Element('lesion', 'lesion')], 2);
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.match(elements['annotation-status'].textContent, /image #\d+ can't be annotated/);
  assert.equal(elements['next-button'].disabled, true);
  assert.equal(elements['skip-button'].disabled, true);
  assert.equal(elements['not-present-button'].disabled, true);
  assert.equal(manager.readOnly, true);
  assert.equal(calls.length, 0, 'Incomplete terms must not trigger annotation API calls');
});

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return {promise, resolve};
}
const historyResponse = (annotations = []) => ({ok: true, json: async () => ({annotations})});

test('saved responses begin loading before the image but controls and comments wait for both', async () => {
  const response = deferred(), term = new Element('lesion', 'lesion');
  const {elements, calls, manager} = await page([term], 1,
    {imageReady: false, loadHistory: () => response.promise});
  assert.equal(calls.length, 1, 'History fetch starts without waiting for image readiness');
  assert.equal(manager.readOnly, true);
  assert.equal(elements.comments.disabled, true, 'Loading must not accept comments that initialization would clear');
  response.resolve(historyResponse());
  await new Promise(setImmediate);
  assert.equal(manager.readOnly, true);
  assert.equal(elements.comments.disabled, true);
  assert.equal(elements['skip-button'].disabled, true);
  await elements['next-button'].click();
  assert.equal(calls.length, 1, 'No completion request is allowed before initialization');
  manager.ready = true;
  await manager.canvas.fire('annotationimageready');
  assert.equal(manager.readOnly, false);
  assert.equal(elements.comments.disabled, false);
  assert.equal(term.classList.contains('current_keyword'), true);
  assert.equal(calls.length, 1);
});

test('an image arriving first shares the in-flight fetch and restores history without resetting later drafts', async () => {
  const response = deferred(), savedTerm = new Element('lesion', 'lesion'), current = new Element('boundary', 'boundary');
  const {elements, calls, manager} = await page([savedTerm, current], 2,
    {imageReady: false, loadHistory: () => response.promise});
  manager.ready = true;
  await manager.canvas.fire('annotationimageready');
  await manager.canvas.fire('annotationimageready');
  assert.equal(calls.length, 1);
  assert.equal(manager.readOnly, true);
  assert.equal(elements.comments.disabled, true);
  response.resolve(historyResponse([{extractedKeyword: 'lesion', pressedButton: 'Save and Next Term',
    boxCoordinates: 'saved coordinate envelope', comment: 'saved comment'}]));
  await new Promise(setImmediate);
  assert.equal(current.classList.contains('current_keyword'), true);
  await savedTerm.click();
  assert.equal(manager.readOnly, true);
  assert.equal(manager.loaded, 'saved coordinate envelope');
  assert.equal(elements.comments.value, 'saved comment');
  await current.click();
  manager.annotations = [{syntheticShape: true}]; elements.comments.value = 'current draft';
  manager.onAnnotationsChange();
  await manager.canvas.fire('annotationimageready');
  assert.equal(calls.length, 1);
  assert.equal(manager.annotations.length, 1);
  assert.equal(elements.comments.value, 'current draft');
});

test('failed history loading stays locked and a later image event can retry without duplicate requests', async () => {
  const retry = deferred(); let attempts = 0;
  const {elements, calls, manager} = await page([new Element('lesion', 'lesion')], 1,
    {imageReady: false, loadHistory: () => ++attempts === 1 ?
      {ok: false, json: async () => ({success: false, message: 'This image is not assigned to you.'})} : retry.promise});
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.match(elements['annotation-status'].textContent, /not assigned/);
  assert.equal(manager.readOnly, true);
  manager.ready = true;
  const retryReady = manager.canvas.fire('annotationimageready');
  await manager.canvas.fire('annotationimageready');
  assert.equal(calls.length, 2, 'Only one retry may be in flight');
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.equal(elements.comments.disabled, true);
  assert.equal(manager.readOnly, true);
  await elements['next-button'].click();
  assert.equal(calls.length, 2);
  retry.resolve(historyResponse());
  await retryReady;
  assert.equal(manager.readOnly, false);
  assert.equal(elements['annotation-status'].dataset.state, 'ready');
});

test('finishing the history prefetch does not hide an image loading error', async () => {
  const response = deferred();
  const {elements, calls, manager} = await page([new Element('lesion', 'lesion')], 1,
    {imageReady: false, loadHistory: () => response.promise});
  elements['annotation-status'].textContent = 'This image could not be loaded.';
  elements['annotation-status'].dataset.state = 'error';
  await manager.canvas.fire('annotationimageerror');
  response.resolve(historyResponse());
  await new Promise(setImmediate);
  assert.equal(calls.length, 1);
  assert.equal(manager.readOnly, true);
  assert.equal(elements.comments.disabled, true);
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.match(elements['annotation-status'].textContent, /image could not be loaded/);
});

test('removing every shape and the comment returns the term to an untouched state', async () => {
  const {elements, manager, warnsBeforeLeaving} = await page([new Element('lesion', 'lesion')], 1);
  const status = elements['annotation-status'];
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  assert.equal(status.dataset.state, 'unsaved');
  assert.equal(warnsBeforeLeaving(), true);
  manager.annotations = [];
  manager.onAnnotationsChange();
  assert.equal(status.dataset.state, 'ready');
  assert.match(status.textContent, /^Annotate the current term: lesion\./);
  // The term itself is a separate bold red element; the rest of the prompt stays plain text.
  const term = status.children[1];
  assert.equal(term.tagName, 'STRONG');assert.equal(term.className, 'status-term');assert.equal(term.textContent, 'lesion');
  assert.equal(warnsBeforeLeaving(), false);

  elements.comments.value = 'A note';
  await elements.comments.fire('input');
  assert.equal(status.textContent, 'Unsaved comment.');
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  manager.annotations = [];
  manager.onAnnotationsChange();
  assert.equal(status.dataset.state, 'unsaved', 'The comment still needs saving');
  assert.equal(warnsBeforeLeaving(), true);
  elements.comments.value = '  ';
  await elements.comments.fire('input');
  assert.equal(status.dataset.state, 'ready');
  assert.equal(warnsBeforeLeaving(), false);
});

test('a released image locks every control, drops the leave warning and sends nothing more', async () => {
  let watched;
  const term = new Element('lesion', 'lesion');
  const {elements, calls, manager, warnsBeforeLeaving} = await page([term], 1,
    {watch: options => { watched = options; return {check() {}}; }});
  assert.equal(watched.busy(), false);
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  watched.onReleased();
  assert.equal(manager.stopped, true);
  assert.equal(manager.readOnly, true);
  for (const id of ['next-button', 'skip-button', 'not-present-button', 'comments']) assert.equal(elements[id].disabled, true, id);
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.match(elements['annotation-status'].textContent, /released/);
  assert.equal(warnsBeforeLeaving(), false);
  manager.onAnnotationsChange();
  assert.equal(elements['next-button'].disabled, true);
  const before = calls.length;
  await elements['next-button'].click();
  await elements['skip-button'].click();
  await term.click();
  assert.equal(calls.length, before);
  assert.match(elements['annotation-status'].textContent, /released/);
});

test('saving counts as busy for the lock check and a conflict asks for an immediate check', async () => {
  let watched, checks = 0;
  const response = deferred();
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1, {
    watch: options => { watched = options; return {check() { checks++; }}; },
    saveAnnotation: () => response.promise});
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  const saving = elements['next-button'].click();
  await new Promise(setImmediate);
  assert.equal(watched.busy(), true);
  response.resolve({ok: false, status: 409, json: async () => ({success: false, message: 'This image is no longer assigned to you.'})});
  await saving;
  assert.equal(watched.busy(), false);
  assert.equal(checks, 1);
  assert.equal(elements['annotation-status'].dataset.state, 'error');
});

test('zoom to area shows its own hint and restores the specialist status afterwards', async () => {
  const {elements, manager} = await page([new Element('lesion', 'lesion')], 1);
  const status = elements['annotation-status'], before = status.textContent;
  manager.onInspectionChange(true, 'zoom');
  assert.equal(status.dataset.state, 'inspection');
  assert.match(status.textContent, /^Zoom to Area/);
  manager.onInspectionChange(false, 'zoom');
  assert.equal(status.textContent, before);
  assert.equal(status.dataset.state, 'ready');
});

test('an unsent technical problem report is saved with the term, separately from the comment, and never blocks the term', async () => {
  const {elements, calls, manager} = await page([new Element('lesion', 'lesion')], 1);
  const status = elements['annotation-status'], problem = elements['technical-problem'];
  problem.value = '  The image loaded blurred  ';
  await problem.fire('input');
  assert.equal(status.dataset.state, 'ready', 'The report has its own status line and Send now button');
  elements.comments.value = 'Clinical note';
  await elements['skip-button'].click();
  const body = JSON.parse(calls.find(call => call.url === '/MedData/ProcessAnnotatedMedData').body);
  assert.equal(body.TechnicalProblem, 'The image loaded blurred');
  assert.equal(body.Comment, 'Clinical note');
  assert.equal(problem.value, '');
  assert.equal(elements.comments.value, '');
  assert.equal(manager.annotations.length, 0);
});

test('the problem box stays usable when the image never loads, and failed requests are logged automatically', async () => {
  const {elements, requestErrors} = await page([new Element('lesion', 'lesion')], 1,
    {imageReady: false, loadHistory: () => ({ok: false, status: 503, json: async () => ({success: false, message: 'Database unavailable.'})})});
  assert.equal(elements['technical-problem'].disabled, false);
  assert.equal(elements.comments.disabled, true);
  assert.deepEqual(requestErrors.map(error => [error.action, error.status]), [['Loading saved answers', 503]]);
});
