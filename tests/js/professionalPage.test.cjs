const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/CliniMark.Presentation/wwwroot/js/professionalPage.js'), 'utf8');

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
      toggle: (name, force = !classes.has(name)) => { if (force) classes.add(name); else classes.delete(name); return force; },
      contains: name => classes.has(name)
    };
  }
  addEventListener(name, callback) {
    (this.listeners[name] ||= []).push(callback);
  }
  setAttribute(name, value) { this.attributes[name] = value; }
  append(...nodes) { (this.children ||= []).push(...nodes); }
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
    'not-present-button', 'comments', 'technical-problem', 'image-container', 'cancel-change-button',
    'saved-terms', 'saved-count', 'saved-items', 'comment-save', 'comment-status'].map(id => [id, new Element()]));
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
    loadCoordinates(coordinates) { this.loaded = coordinates; this.annotations = [{loadedShape: true}]; }
  }
  const requestErrors = [], confirms = [], reloads = [];
  const context = {
    window: {annotatedMedData: options.noImage ? undefined : {id: 1}, addEventListener(name, callback) { events[name] = callback; }, watchAssignment: options.watch,
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
    fetch: async (url, request = {}) => {
      calls.push({url, ...request});
      if (url.includes('CurrentAnnotations') && loadHistory) return loadHistory();
      if (url === '/MedData/ProcessAnnotatedMedData' && saveAnnotation) return saveAnnotation();
      if (url.includes('SkipImage') && skipImage) return skipImage();
      if (url === '/MedData/CorrectAnnotatedMedData' && options.correct) return options.correct();
      if (url === '/MedData/SaveImageComment' && options.imageComment) return options.imageComment();
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

test('leaving the page without an image (end of series, nothing free) neither fails nor asks to stay', async () => {
  // Live problem reports 1-7 (2026-11-23): the leave-page check read the custom annotations before they existed.
  const {warnsBeforeLeaving, manager} = await page([], 0, {noImage: true});
  assert.equal(manager, undefined, 'No image, so no drawing tools are set up');
  assert.doesNotThrow(warnsBeforeLeaving);
  assert.equal(warnsBeforeLeaving(), false);
});

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
  assert.equal(manager.loaded, 'synthetic image-coordinate envelope', 'Repeated saved phrase opens the same saved response');
  assert.equal(manager.readOnly, false, 'A saved term opens to be changed');
  assert.equal(firstBoundary.classList.contains('changing_keyword'), true);
  assert.equal(elements['next-button'].textContent, 'Save changes');
  assert.equal(elements['next-button'].disabled, true, 'Nothing to save until something changes');
  await lesion.click();
  assert.equal(manager.readOnly, false);
  assert.equal(firstBoundary.classList.contains('changing_keyword'), false);
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
  assert.match(elements['annotation-status'].textContent, /^Changing your saved answer for lesion\. Its saved shapes are shown/);
  labels.settings.onChange();
  assert.match(elements['annotation-status'].textContent, /^Changing your saved answer for lesion\./, 'A label answer does not hide the open term');
  assert.equal(elements['next-button'].textContent, 'Save changes');

  await elements['cancel-change-button'].click();
  assert.match(elements['annotation-status'].textContent, /Confirm the image modality/);
  labels.done = true;
  labels.settings.onChange();
  assert.match(elements['annotation-status'].textContent, /^All terms saved\. Select Next Image/);
  assert.equal(elements['next-button'].textContent, 'Next Image');
});

test('Skip the case moves on at once when nothing has been entered', async () => {
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

test('Skip the case asks first when it would remove saved work, and declining changes nothing', async () => {
  const {elements, calls, confirms, reloads} = await page([new Element('lesion', 'lesion')], 1, {confirm: false,
    loadHistory: () => ({ok: true, json: async () => ({annotations: [{extractedKeyword: 'lesion', pressedButton: 'Save and Next Term', boxCoordinates: '{}'}]})})});
  await elements['skip-image-button'].click();
  assert.equal(confirms.length, 1);
  assert.equal(calls.filter(call => call.url.includes('SkipImage')).length, 0);
  assert.equal(reloads.length, 0);
});

test('Skip the case also asks when a shape is drawn but not saved', async () => {
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
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  assert.equal(status.dataset.state, 'inspection');
  manager.onInspectionChange(false);
  assert.equal(status.textContent, 'Unsaved changes. Select Save and Next Term to save this term.');
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
  assert.equal(manager.readOnly, false);
  assert.equal(manager.loaded, 'saved coordinate envelope');
  assert.equal(elements.comments.value, '', 'The comment box is the image comment; opening a term leaves it alone');
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

test('removing every shape returns the term to an untouched state, and the comment is saved on its own', async () => {
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
  assert.equal(status.dataset.state, 'ready', 'A comment is not part of the term');
  assert.equal(warnsBeforeLeaving(), true, 'but leaving with an unsaved comment asks first');
  await elements['comment-save'].click();
  await new Promise(setImmediate);
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

test('an unsent technical problem report is saved with the term, and the comment box is left alone', async () => {
  const {elements, calls, manager} = await page([new Element('lesion', 'lesion')], 1);
  const status = elements['annotation-status'], problem = elements['technical-problem'];
  problem.value = '  The image loaded blurred  ';
  await problem.fire('input');
  assert.equal(status.dataset.state, 'ready', 'The report has its own status line and Send now button');
  elements.comments.value = 'Clinical note';
  await elements['skip-button'].click();
  const body = JSON.parse(calls.find(call => call.url === '/MedData/ProcessAnnotatedMedData').body);
  assert.equal(body.TechnicalProblem, 'The image loaded blurred');
  assert.equal(body.Comment, '', 'The comment is saved on its own, not with the term');
  assert.equal(problem.value, '');
  assert.equal(elements.comments.value, 'Clinical note', 'Moving to the next term keeps the comment box as it is');
  assert.equal(manager.annotations.length, 0);
});

test('the problem box stays usable when the image never loads, and failed requests are logged automatically', async () => {
  const {elements, requestErrors} = await page([new Element('lesion', 'lesion')], 1,
    {imageReady: false, loadHistory: () => ({ok: false, status: 503, json: async () => ({success: false, message: 'Database unavailable.'})})});
  assert.equal(elements['technical-problem'].disabled, false);
  assert.equal(elements.comments.disabled, true);
  assert.deepEqual(requestErrors.map(error => [error.action, error.status]), [['Loading saved answers', 503]]);
});

const drawnHistory = () => historyResponse([{extractedKeyword: 'lesion', pressedButton: 'Save and Next Term',
  boxCoordinates: 'saved coordinate envelope', comment: 'first note'}]);
const corrections = calls => calls.filter(call => call.url === '/MedData/CorrectAnnotatedMedData').map(call => JSON.parse(call.body));

test('a saved term opens with its shapes, and a changed term keeps the comment it was saved with', async () => {
  const lesion = new Element('lesion', 'lesion'), margin = new Element('margin', 'margin');
  const {elements, calls, manager} = await page([lesion, margin], 2, {loadHistory: drawnHistory});
  assert.equal(margin.classList.contains('current_keyword'), true);
  assert.equal(lesion.dataset.description !== undefined, true, 'Saved terms say they can be opened');

  await lesion.click();
  assert.equal(elements.comments.value, '', 'The comment box is not the term');
  assert.equal(elements['cancel-change-button'].hidden, false);
  assert.equal(elements['skip-button'].disabled, true, 'Its shapes must go before it can be Uncertain');
  const status = elements['annotation-status'];
  assert.equal(status.children[1].textContent, 'lesion', 'The term is emphasised in the status');
  assert.equal(elements['next-button'].disabled, true);
  await elements['next-button'].click();
  assert.equal(corrections(calls).length, 0, 'Nothing is sent while nothing changed');
  assert.equal(margin.classList.contains('current_keyword'), true, 'An unchanged term just closes');

  await lesion.click();
  manager.annotations = [{syntheticShape: true}, {syntheticShape: true}];
  manager.onAnnotationsChange();
  assert.equal(elements['next-button'].disabled, false);
  await elements['next-button'].click();
  assert.deepEqual(corrections(calls), [{Id: 1, BoxCoordinates: 'synthetic image-coordinate envelope', ExtractedKeyword: 'lesion',
    PressedButton: 'Save and Next Term', Comment: 'first note'}]);
  assert.match(status.textContent, /^Your changes to lesion are saved\. Annotate the current term: margin/);
  assert.equal(status.children[1].textContent, 'margin');
  assert.equal(margin.classList.contains('current_keyword'), true, 'Back on the term they were on');
  assert.equal(elements['cancel-change-button'].hidden, true);
});

test('a drawn term becomes Uncertain once its shapes are deleted, and new shapes are sent when they change', async () => {
  const lesion = new Element('lesion', 'lesion'), margin = new Element('margin', 'margin');
  const {elements, calls, manager} = await page([lesion, margin], 2, {loadHistory: drawnHistory});
  await lesion.click();
  manager.annotations = [];
  manager.onAnnotationsChange();
  assert.equal(elements['skip-button'].disabled, false);
  assert.equal(elements['next-button'].disabled, true, 'Save changes needs shapes; the other answers are the buttons beside it');
  await elements['skip-button'].click();
  assert.deepEqual(corrections(calls).at(-1), {Id: 1, BoxCoordinates: '', ExtractedKeyword: 'lesion', PressedButton: 'Uncertain/Skip', Comment: 'first note'});
  assert.equal(lesion.classList.contains('skipped_keyword'), true);

  await lesion.click();
  assert.equal(elements['skip-button'].disabled, false, 'An Uncertain term opens without shapes');
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  await elements['next-button'].click();
  assert.deepEqual(corrections(calls).at(-1), {Id: 1, BoxCoordinates: 'synthetic image-coordinate envelope', ExtractedKeyword: 'lesion',
    PressedButton: 'Save and Next Term', Comment: 'first note'});
  assert.equal(lesion.classList.contains('annotated_keyword'), true);
});

test('Cancel leaves the saved answer as it was, asking first when something changed', async () => {
  const options = {loadHistory: drawnHistory, confirm: false};
  const lesion = new Element('lesion', 'lesion'), margin = new Element('margin', 'margin');
  const {elements, calls, manager, confirms, warnsBeforeLeaving} = await page([lesion, margin], 2, options);
  await lesion.click();
  manager.annotations = [];
  manager.onAnnotationsChange();
  assert.equal(warnsBeforeLeaving(), true, 'Unsaved changes to a saved term are protected');
  await elements['cancel-change-button'].click();
  assert.equal(confirms.length, 1);
  assert.equal(elements['next-button'].textContent, 'Save changes', 'Declining keeps the term open');
  options.confirm = true;
  await margin.click();
  assert.equal(confirms.length, 2, 'Opening another term asks too');
  assert.equal(elements['next-button'].textContent, 'Save and Next Term');
  assert.equal(corrections(calls).length, 0);
  assert.equal(warnsBeforeLeaving(), false);
  await lesion.click();
  assert.equal(manager.loaded, 'saved coordinate envelope', 'Still the saved shapes');
});

test('a refused change keeps the term open with the reason, and the next change can still be saved', async () => {
  let refuse = true;
  const lesion = new Element('lesion', 'lesion'), margin = new Element('margin', 'margin');
  const {elements, calls, requestErrors, manager} = await page([lesion, margin], 2, {loadHistory: drawnHistory,
    correct: async () => refuse
      ? {ok: false, status: 400, json: async () => ({success: false, message: 'Choose a valid annotation response.'})}
      : {ok: true, json: async () => ({success: true})}});
  await lesion.click();
  manager.annotations = [{syntheticShape: true}, {syntheticShape: true}];
  manager.onAnnotationsChange();
  await elements['next-button'].click();
  assert.equal(elements['annotation-status'].textContent, 'Choose a valid annotation response.');
  assert.equal(elements['next-button'].textContent, 'Save changes');
  assert.equal(requestErrors.at(-1).action, 'Changing lesion');
  refuse = false;
  await elements['next-button'].click();
  assert.equal(corrections(calls).length, 2);
  assert.match(elements['annotation-status'].textContent, /^Your changes to lesion are saved/);
});

test('after the last term, saved terms can still be changed before Next Image', async () => {
  const lesion = new Element('lesion', 'lesion');
  const {elements, calls, manager} = await page([lesion], 1, {loadHistory: drawnHistory});
  assert.equal(elements['next-button'].textContent, 'Next Image');
  assert.equal(manager.readOnly, true, 'Nothing to draw once every term is saved');
  await lesion.click();
  assert.equal(manager.readOnly, false);
  assert.equal(elements.comments.disabled, false);
  manager.annotations = [];
  manager.onAnnotationsChange();
  await elements['not-present-button'].click();
  assert.equal(corrections(calls).at(-1).PressedButton, 'Not Visible/Abstract');
  assert.equal(elements['next-button'].textContent, 'Next Image');
  assert.equal(manager.readOnly, true);
  assert.equal(calls.filter(call => call.url.includes('NextImage')).length, 0, 'Saving a change never moves to the next image');
});

// The saved-terms list: each row is li > button > [icon, name, state, edit > [pencil, label]].
const savedList = elements => (elements['saved-items'].children || []).map(item => {
  const [icon, name, state, edit] = item.children[0].children;
  return {visible: !item.hidden, button: item.children[0], name: name.textContent, state: state.textContent, edit: edit.children[1].textContent,
    open: item.children[0].attributes['aria-current'] === 'true', icon: icon.className};
});

test('saved terms are listed under the notes in the order of the text, and open again from the list', async () => {
  const lesion = new Element('Lesion', 'lesion'), margin = new Element('margin', 'margin');
  const {elements, manager} = await page([lesion, margin], 2);
  assert.equal(elements['saved-terms'].hidden, true, 'Nothing to list before the first save');
  assert.deepEqual(savedList(elements).map(row => row.name), ['Lesion', 'margin'], 'One row per term, as written in the text');

  await finishTerm(elements, manager);
  assert.equal(elements['saved-terms'].hidden, false);
  assert.equal(elements['saved-count'].textContent, '1 of 2');
  let rows = savedList(elements);
  assert.deepEqual(rows.map(row => row.visible), [true, false], 'Only saved terms show');
  assert.deepEqual([rows[0].state, rows[0].edit, rows[0].open, rows[0].icon], ['Outlined', 'Edit', false, 'bi bi-check-circle-fill']);
  assert.match(rows[0].button.dataset.description, /Select to see your answer and change it/);

  await rows[0].button.click();
  assert.equal(elements['next-button'].textContent, 'Save changes', 'The row opens the term like the text does');
  assert.equal(lesion.classList.contains('changing_keyword'), true);
  rows = savedList(elements);
  assert.deepEqual([rows[0].open, rows[0].edit], [true, 'Editing']);

  manager.annotations = [];
  manager.onAnnotationsChange();
  await elements['skip-button'].click();
  rows = savedList(elements);
  assert.deepEqual([rows[0].state, rows[0].open, rows[0].icon], ['Uncertain', false, 'bi bi-question-circle-fill'], 'The row follows the changed answer');
  assert.equal(elements['next-button'].textContent, 'Save and Next Term');
});

test('only terms that do something when selected lift on hover', async () => {
  const lesion = new Element('lesion', 'lesion'), margin = new Element('margin', 'margin'), later = new Element('effusion', 'effusion');
  const {elements, manager} = await page([lesion, margin, later], 3);
  const clickable = () => [lesion, margin, later].map(term => term.classList.contains('keyword-clickable'));
  assert.deepEqual(clickable(), [false, false, false], 'The current term is already in hand; later terms wait their turn');
  await finishTerm(elements, manager);
  assert.deepEqual(clickable(), [true, false, false], 'A saved term can be opened');
  await lesion.click();
  assert.deepEqual(clickable(), [false, true, false], 'While it is open, the current term leads back');
  await margin.click();
  assert.deepEqual(clickable(), [true, false, false]);
});

test('selecting the term already being drawn keeps the drawing and asks nothing', async () => {
  const lesion = new Element('lesion', 'lesion');
  const {elements, manager, confirms} = await page([lesion], 1);
  manager.annotations = [{syntheticShape: true}];
  manager.onAnnotationsChange();
  await lesion.click();
  assert.equal(confirms.length, 0);
  assert.equal(manager.annotations.length, 1);
  assert.equal(elements['next-button'].disabled, false);
});

const imageComments = calls => calls.filter(call => call.url === '/MedData/SaveImageComment').map(call => JSON.parse(call.body));
const savedOnce = (extra = {}) => () => ({ok: true, json: async () => ({annotations: [{extractedKeyword: 'lesion', pressedButton: 'Save and Next Term',
  boxCoordinates: 'saved coordinate envelope', comment: ''}], ...extra})});

test('the comment is typed in one box and saved with its Save button, also after every term is saved', async () => {
  const lesion = new Element('lesion', 'lesion');
  const {elements, calls} = await page([lesion], 1, {loadHistory: savedOnce()});
  assert.equal(elements['next-button'].textContent, 'Next Image');
  const box = elements.comments, save = elements['comment-save'], note = elements['comment-status'];
  assert.equal(box.disabled, false, 'The comment box is open after the last term');
  assert.equal(save.disabled, true, 'Nothing to save yet');

  box.value = '  Synthetic note  ';
  await box.fire('input');
  assert.equal(note.textContent, 'Not saved yet.');
  assert.equal(save.disabled, false);
  await save.click();
  await new Promise(setImmediate);
  assert.deepEqual(imageComments(calls), [{Id: 1, Comment: 'Synthetic note'}]);
  assert.deepEqual([note.textContent, note.dataset.state, save.disabled], ['Saved.', 'sent', true]);
  await save.click();
  assert.equal(imageComments(calls).length, 1, 'Saving again without a change sends nothing');

  box.value = '';
  await box.fire('input');
  await save.click();
  await new Promise(setImmediate);
  assert.deepEqual(imageComments(calls).at(-1), {Id: 1, Comment: ''});
  assert.equal(note.textContent, 'Comment removed.');
});

test('Next Image saves a comment that was typed but not saved, and stays if it cannot be saved', async () => {
  let fail = true;
  const lesion = new Element('lesion', 'lesion');
  const {elements, calls, warnsBeforeLeaving} = await page([lesion], 1, {loadHistory: savedOnce(), imageComment: async () => fail
    ? {ok: false, status: 503, json: async () => ({success: false, message: 'The comment could not be saved. Keep this page open and retry.'})}
    : {ok: true, json: async () => ({success: true})}});
  elements.comments.value = 'Typed, Save not pressed';
  await elements.comments.fire('input');
  assert.equal(warnsBeforeLeaving(), true, 'Leaving the page with an unsaved comment asks first');
  await elements['next-button'].click();
  assert.equal(calls.filter(call => call.url.includes('NextImage')).length, 0, 'No next image while the comment is unsaved');
  assert.match(elements['annotation-status'].textContent, /Your comment could not be saved yet/);
  assert.equal(elements['comment-status'].dataset.state, 'error');

  fail = false;
  await elements['next-button'].click();
  const order = calls.map(call => call.url).filter(url => url.includes('SaveImageComment') || url.includes('NextImage'));
  assert.deepEqual(order.slice(-2), ['/MedData/SaveImageComment', '/MedData/NextImage?medDataId=1']);
  assert.equal(warnsBeforeLeaving(), false);
});

test('a saved comment comes back when the page opens again, also while terms are still to do', async () => {
  const lesion = new Element('lesion', 'lesion'), margin = new Element('margin', 'margin');
  const {elements, calls, warnsBeforeLeaving} = await page([lesion, margin], 2, {loadHistory: savedOnce({imageComment: 'Saved before the reload'})});
  assert.equal(elements.comments.value, 'Saved before the reload');
  assert.equal(elements.comments.disabled, false);
  assert.equal(elements['comment-save'].disabled, true, 'Already saved');
  assert.equal(warnsBeforeLeaving(), false, 'Nothing unsaved');
  await elements['comment-save'].click();
  assert.equal(imageComments(calls).length, 0);
});
