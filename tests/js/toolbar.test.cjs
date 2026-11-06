const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/CliniMark.Presentation/wwwroot/js/canvas.js'), 'utf8');
const drawingContext = new Proxy({}, {get: () => () => {}});

class Element {
  constructor(tagName) {
    this.tagName = tagName;
    this.children = [];
    this.attributes = {};
    this.style = {};
    this.className = '';
    this.offsetWidth = 30;
    this.offsetHeight = 100;
    this.classList = {
      contains: value => this.className.split(/\s+/).includes(value),
      add: value => { if (!this.classList.contains(value)) this.className += ` ${value}`; },
      remove: value => { this.className = this.className.split(/\s+/).filter(item => item !== value).join(' '); },
      toggle: (value, enabled) => enabled ? this.classList.add(value) : this.classList.remove(value)
    };
  }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  getAttribute(name) { return this.attributes[name]; }
  appendChild(child) { this.children.push(child); return child; }
  removeChild(child) { this.children.splice(this.children.indexOf(child), 1); }
  addEventListener() {}
  getContext() { return drawingContext; }
  querySelector(tagName) { return this.children.find(child => child.tagName === tagName); }
  getBoundingClientRect() {
    const left = parseFloat(this.style.left) || 100, top = parseFloat(this.style.top) || 120;
    const width = parseFloat(this.style.width) || 600, height = parseFloat(this.style.height) || 400;
    return {left, top, width, height, right: left + width, bottom: top + height};
  }
}

function toolbar() {
  const document = {body: new Element('body'), createElement: name => new Element(name),
    addEventListener() {}, removeEventListener() {}};
  const context = {window: {innerWidth: 1200}, document, console};
  vm.runInNewContext(source, context, {filename: 'canvas.js'});
  const manager = Object.create(context.window.CanvasManager.prototype);
  const canvas = new Element('canvas');
  canvas.width = 1200; canvas.height = 800;
  Object.assign(manager, {canvas, ctx: drawingContext, sourceImage: {},
    currentMode: 'rectangle', annotations: [], ready: true, customShapes: [], pendingCustom: null});
  return {manager, tools: manager.createTools(new Element('div'))};
}

function pressed(button, expected) {
  assert.equal(button.classList.contains('active'), expected);
  assert.equal(button.getAttribute('aria-pressed'), String(expected));
}

test('toolbar starts with rectangle selected and exposes every tool state', () => {
  const {tools} = toolbar();
  pressed(tools.rectButton, true);
  pressed(tools.freehandButton, false);
  pressed(tools.zoomButton, false);
  assert.deepEqual(Object.keys(tools), ['rectButton', 'freehandButton', 'zoomButton']);
  for (const button of Object.values(tools)) assert.ok(button.getAttribute('aria-label'));
});

test('rectangle and freehand selection keep visual and accessible state together', () => {
  const {manager, tools} = toolbar();
  manager.switchMode('freehand');
  assert.equal(manager.currentMode, 'freehand');
  pressed(tools.rectButton, false); pressed(tools.freehandButton, true);
  manager.switchMode('rectangle');
  assert.equal(manager.currentMode, 'rectangle');
  pressed(tools.rectButton, true); pressed(tools.freehandButton, false);
});

test('zoom to area is its own mode: it drops the selection and hands back to drawing', () => {
  const {manager, tools} = toolbar();
  const hints = [];
  manager.onInspectionChange = (active, kind) => hints.push([active, kind]);
  manager.selectedAnnotation = {type: 'rectangle', cx: 200, cy: 200, width: 100, height: 80, rotation: 0};
  manager.annotations = [manager.selectedAnnotation];
  manager.switchMode('zoom');
  assert.equal(manager.currentMode, 'zoom');
  assert.equal(manager.selectedAnnotation, null);
  assert.equal(manager.annotations.length, 1);
  pressed(tools.zoomButton, true);
  pressed(tools.rectButton, false); pressed(tools.freehandButton, false);
  assert.equal(manager.canvas.style.cursor, 'zoom-in');
  assert.deepEqual(hints.at(-1), [true, 'zoom']);
  manager.switchMode('freehand');
  pressed(tools.zoomButton, false); pressed(tools.freehandButton, true);
  assert.deepEqual(hints.at(-1), [false, 'zoom']);
});

test('the specialist toolbar adds Custom annotation between Freehand Contour and Zoom to Area, with its explanation', () => {
  const {manager} = toolbar();
  manager.toolsContainer = null;
  const tools = manager.createTools(new Element('div'), {custom: true});
  assert.deepEqual(Object.keys(tools), ['rectButton', 'freehandButton', 'customButton', 'zoomButton']);
  assert.deepEqual(manager.toolsContainer.children.map(button => button.getAttribute('data-label')),
    ['Boundary Box', 'Freehand Contour', 'Custom annotation', 'Zoom to Area']);
  assert.match(tools.customButton.getAttribute('data-description'), /^Additionally annotate and register any medical term/);
  manager.switchMode('custom');
  pressed(tools.customButton, true); pressed(tools.freehandButton, false); pressed(tools.rectButton, false);
  manager.switchMode('freehand');
  pressed(tools.customButton, false); pressed(tools.freehandButton, true);
});

