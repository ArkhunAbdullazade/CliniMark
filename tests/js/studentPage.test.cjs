const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname,
  '../../src/MedAnnotateApp.Presentation/wwwroot/js/studentPage.js'), 'utf8');
const StudentCanvas = vm.runInNewContext(fs.readFileSync(path.join(__dirname,
  '../../src/MedAnnotateApp.Presentation/wwwroot/js/studentCanvas.js'), 'utf8') + '\nStudentCanvasManager');

class Element {
  constructor() { this.children = []; this.listeners = {}; this.dataset = {}; this.textContent = ''; }
  addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
  replaceChildren() { this.children = []; }
  append(...children) { this.children.push(...children); }
  appendChild(child) { this.append(child); }
  fire(name) { return Promise.all((this.listeners[name] || []).map(callback => callback())); }
  click() { return this.fire('click'); }
}
function page(response, options = {}) {
  const elements = Object.fromEntries(['annotation-status', 'submit-button', 'annotations-list',
    'image-container'].map(id => [id, new Element()]));
  const events = {};
  let onReady;
  class StudentCanvasManager {
    constructor() {
      this.groups = [];
      this.baseManager = {ready: true, annotations: [], currentAction: 'none',
        canvas: new Element(), onAnnotationsChange() {}, drawCanvas() {},
        resetAnnotation() { this.annotations = []; this.currentAction = 'none'; },
        stopInteraction() { this.stopped = true; this.readOnly = true; }};
    }
    createTools() { return {rectButton: new Element(), freehandButton: new Element(), zoomButton: new Element()}; }
    getAnnotationGroups() { return this.groups; }
    getAllGroupedAnnotationsJSON() { return this.groups.map(group => ({label: group.label, annotations: []})); }
    deleteGroup(index) { this.groups.splice(index, 1); this.onGroupedAnnotationsChange(); }
    hideTextBox() { this.textBoxHidden = true; }
    cancelCurrentAnnotation() { StudentCanvas.prototype.cancelCurrentAnnotation.call(this); }
  }
  const context = {
    window: {annotatedMedData: {id: 1}, addEventListener(name, callback) { events[name] = callback; }, watchAssignment: options.watch},
    document: {addEventListener(name, callback) { if (name === 'DOMContentLoaded') onReady = callback; },
      getElementById: id => elements[id], querySelector: () => ({value: 'synthetic-csrf'}),
      createElement: () => new Element()},
    StudentCanvasManager, confirm: () => true, location: {},
    fetch() {
      if (!response) throw new Error('This UI state test must not send requests.');
      const status = options.status || 200;
      return Promise.resolve(response).then(result => ({ok: status < 400, status, json: async () => result}));
    }
  };
  vm.runInNewContext(source, context, {filename: 'studentPage.js'});
  onReady();
  const warnsBeforeLeaving = () => {
    let warned = false;
    events.beforeunload({preventDefault() { warned = true; }});
    return warned;
  };
  return {elements, manager: context.window.canvasManager, warnsBeforeLeaving};
}

test('saving a local group then deleting the last group clears the warning and ready status', () => {
  const {elements, manager, warnsBeforeLeaving} = page();
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange();
  assert.equal(warnsBeforeLeaving(), true);
  assert.equal(elements['annotation-status'].dataset.state, 'unsaved');
  assert.equal(elements['submit-button'].disabled, false);
  const actions = elements['annotations-list'].children[0].children[1];
  actions.children[1].click();
  assert.equal(manager.groups.length, 0);
  assert.equal(warnsBeforeLeaving(), false);
  assert.equal(elements['submit-button'].disabled, true);
  assert.equal(elements['annotation-status'].dataset.state, 'ready');
  assert.match(elements['annotation-status'].textContent, /^Image ready/);
});

test('pending shapes keep warnings after either group or canvas changes and clear when removed', () => {
  const {elements, manager, warnsBeforeLeaving} = page();
  manager.baseManager.annotations = [{}];
  for (const change of [() => manager.onGroupedAnnotationsChange(), () => manager.baseManager.onAnnotationsChange()]) {
    change();
    assert.equal(warnsBeforeLeaving(), true);
    assert.equal(elements['annotation-status'].dataset.state, 'unsaved');
    assert.match(elements['annotation-status'].textContent, /Unsaved shape/);
  }
  manager.baseManager.annotations = [];
  manager.baseManager.onAnnotationsChange();
  assert.equal(warnsBeforeLeaving(), false);
  assert.equal(elements['annotation-status'].dataset.state, 'ready');
});

test('an unfinished gesture warns before leaving even before a shape-change callback', () => {
  const {elements, manager, warnsBeforeLeaving} = page();
  manager.baseManager.currentAction = 'draw';
  assert.equal(warnsBeforeLeaving(), true);
  manager.onGroupedAnnotationsChange();
  assert.equal(elements['annotation-status'].dataset.state, 'unsaved');
  manager.baseManager.currentAction = 'none';
  manager.onGroupedAnnotationsChange();
  assert.equal(warnsBeforeLeaving(), false);
  assert.equal(elements['annotation-status'].dataset.state, 'ready');
});

test('successful submission redirects without warning about the now-persisted groups', async () => {
  const {elements, manager, warnsBeforeLeaving} = page({success: true, redirectUrl: '/Home/Student'});
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange();
  assert.equal(warnsBeforeLeaving(), true);
  await elements['submit-button'].click();
  assert.equal(warnsBeforeLeaving(), false);
});

test('the real student cancel method restores ready status when the draft is empty', () => {
  const {elements, manager, warnsBeforeLeaving} = page();
  manager.baseManager.annotations = [{}];
  manager.currentGroup = {label: 'draft'};
  manager.baseManager.onAnnotationsChange();
  manager.cancelCurrentAnnotation();
  assert.equal(warnsBeforeLeaving(), false);
  assert.equal(elements['annotation-status'].dataset.state, 'ready');
  assert.equal(manager.currentGroup, null);
  assert.equal(manager.selectedGroupIndex, -1);
});

test('cancelling edits preserves the original local group and its submit warning', () => {
  const {elements, manager, warnsBeforeLeaving} = page();
  const group = {label: 'original label', annotations: [{}]};
  manager.groups.push(group);
  manager.currentGroup = {label: 'unsaved replacement'};
  manager.selectedGroupIndex = 0;
  manager.baseManager.annotations = [{}];
  manager.cancelCurrentAnnotation();
  assert.equal(manager.groups[0], group);
  assert.equal(manager.groups[0].label, 'original label');
  assert.equal(elements['submit-button'].disabled, false);
  assert.equal(elements['annotation-status'].dataset.state, 'unsaved');
  assert.match(elements['annotation-status'].textContent, /Groups are held/);
  assert.equal(warnsBeforeLeaving(), true);
});

test('inspection restores student status without changing local groups', () => {
  const {elements, manager} = page();
  const group = {label: 'test finding', annotations: [{}]};
  manager.groups.push(group); manager.onGroupedAnnotationsChange();
  const status = elements['annotation-status'], before = status.textContent;
  manager.baseManager.onInspectionChange(true);
  assert.equal(status.dataset.state, 'inspection');
  assert.match(status.textContent, /^Zoom to Area/);
  assert.equal(manager.groups[0], group);
  manager.baseManager.onInspectionChange(false);
  assert.equal(status.textContent, before);
  assert.equal(status.dataset.state, 'unsaved');
});

test('student status updates during inspection are restored when it ends', () => {
  const {elements, manager} = page();
  manager.baseManager.onInspectionChange(true);
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange();
  const status = elements['annotation-status'];
  assert.equal(status.dataset.state, 'inspection');
  manager.baseManager.onInspectionChange(false);
  assert.equal(status.dataset.state, 'unsaved');
  assert.match(status.textContent, /Groups are held/);
});

test('inspection never covers student errors, including an image error arriving while inspecting', async () => {
  const {elements, manager} = page();
  const status = elements['annotation-status'];
  status.textContent = 'The outline must enclose an area.'; status.dataset.state = 'error';
  manager.baseManager.onInspectionChange(true);
  assert.equal(status.textContent, 'The outline must enclose an area.');
  assert.equal(status.dataset.state, 'error');
  status.textContent = 'This image could not be loaded.'; status.dataset.state = 'error';
  manager.baseManager.ready = false;
  await manager.baseManager.canvas.fire('annotationimageerror');
  manager.baseManager.onInspectionChange(false);
  assert.equal(status.textContent, 'This image could not be loaded.');
  assert.equal(status.dataset.state, 'error');
});

test('student saving status takes priority over the inspection hint', async () => {
  let finish;
  const response = new Promise(resolve => { finish = resolve; });
  const {elements, manager} = page(response);
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange(); manager.baseManager.onInspectionChange(true);
  const saving = elements['submit-button'].click();
  const status = elements['annotation-status'];
  assert.equal(status.dataset.state, 'saving');
  assert.match(status.textContent, /Saving all groups/);
  manager.baseManager.onInspectionChange(false);
  assert.equal(status.dataset.state, 'saving');
  finish({success: true, redirectUrl: '/Home/Student'});
  await saving;
});

test('real student canvas draws grouped outlines first and the selected outline last', () => {
  let baseDraws = 0;
  const outlined = [];
  class CanvasManager {
    constructor() {
      this.ready = true;
      this.drawCanvas = () => { baseDraws++; };
      this.drawAnnotation = shape => outlined.push(shape);
    }
  }
  const ActualStudentCanvas = vm.runInNewContext(fs.readFileSync(path.join(__dirname,
    '../../src/MedAnnotateApp.Presentation/wwwroot/js/studentCanvas.js'), 'utf8') + '\nStudentCanvasManager',
    {CanvasManager, document: {addEventListener() {}}});
  ActualStudentCanvas.prototype.createFloatingTextBox = () => {};
  ActualStudentCanvas.prototype.setupEventListeners = () => {};
  const manager = new ActualStudentCanvas('canvas', 'image', 'container');
  const groupedShape = {type: 'freehand'}, selectedShape = {type: 'rectangle'};
  manager.groupedAnnotations = [{annotations: [groupedShape]}];
  manager.baseManager.selectedAnnotation = selectedShape;
  manager.baseManager.drawCanvas();
  assert.equal(baseDraws, 1, 'The base image is redrawn before the outlines');
  assert.deepEqual(outlined, [groupedShape, selectedShape]);
  assert.equal(manager.groupedAnnotations[0].annotations[0], groupedShape);
  assert.equal(manager.baseManager.selectedAnnotation, selectedShape);
});

test('a released student image locks submit, group actions and the canvas without a leave warning', async () => {
  let watched;
  const {elements, manager, warnsBeforeLeaving} = page(undefined, {watch: options => { watched = options; return {check() {}}; }});
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange();
  assert.equal(watched.busy(), false);
  assert.equal(warnsBeforeLeaving(), true);
  watched.onReleased();
  assert.equal(manager.baseManager.stopped, true);
  assert.equal(manager.textBoxHidden, true);
  assert.equal(elements['submit-button'].disabled, true);
  const actions = elements['annotations-list'].children[0].children[1];
  assert.equal(actions.children[0].disabled, true);
  assert.equal(actions.children[1].disabled, true);
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.equal(warnsBeforeLeaving(), false);
  await elements['submit-button'].click();
  assert.match(elements['annotation-status'].textContent, /released/, 'Submitting after a release sends nothing');
});

test('a student submit conflict asks the lock watcher to check at once', async () => {
  let checks = 0;
  const {elements, manager} = page({success: false, message: 'This image is no longer assigned to you.'},
    {status: 409, watch: () => ({check() { checks++; }})});
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange();
  await elements['submit-button'].click();
  assert.equal(checks, 1);
  assert.equal(elements['annotation-status'].dataset.state, 'error');
  assert.match(elements['annotation-status'].textContent, /no longer assigned/);
});

test('student zoom to area hint replaces and then restores the group status', () => {
  const {elements, manager} = page();
  manager.groups.push({label: 'test finding', annotations: [{}]});
  manager.onGroupedAnnotationsChange();
  const status = elements['annotation-status'], before = status.textContent;
  manager.baseManager.onInspectionChange(true, 'zoom');
  assert.match(status.textContent, /^Zoom to Area/);
  manager.baseManager.onInspectionChange(false, 'zoom');
  assert.equal(status.textContent, before);
  assert.equal(status.dataset.state, 'unsaved');
});
