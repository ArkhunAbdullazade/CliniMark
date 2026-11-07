// tutorial.js: the first viewing can't be closed or skipped, it is recorded once at the end, and replays are free.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../src/CliniMark.Presentation/wwwroot/js/tutorial.js'), 'utf8');

class Node_ {
  constructor() { this.listeners = {}; this.hidden = false; this.textContent = ''; }
  addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
  fire(name, event = {}) { for (const callback of this.listeners[name] || []) callback(event); return event; }
  click() { this.fire('click'); }
  focus() { this.focused = true; }
}

function page({required}) {
  const video = Object.assign(new Node_(), {currentTime: 0, seeking: false, paused: true,
    pause() { this.paused = true; }, play() { this.paused = false; return Promise.resolve(); }});
  const close = new Node_(), done = new Node_(), note = new Node_(), button = new Node_();
  const dialog = Object.assign(new Node_(), {dataset: {required: String(required)}, open: false, opened: 0,
    showModal() { this.open = true; this.opened++; }, close() { this.open = false; this.fire('close'); },
    querySelector: selector => ({video, '[data-tutorial-close]': close, '[data-tutorial-done]': done, '[data-tutorial-note]': note})[selector]});
  const calls = [];
  const context = {
    window: {}, fetch: async (url, options) => { calls.push({url, ...options}); return {ok: true}; },
    document: {
      getElementById: id => ({'tutorial-dialog': dialog, 'tutorial-button': button})[id],
      querySelector: () => ({value: 'synthetic-csrf'})
    }
  };
  vm.runInNewContext(source, context, {filename: 'tutorial.js'});
  return {dialog, video, close, done, note, button, calls};
}

const watch = video => { for (let t = 1; t <= 10; t++) { video.currentTime = t; video.fire('timeupdate'); } };

test('a new participant gets the video at once, with no way to close it', () => {
  const {dialog, close, done, note} = page({required: true});
  assert.equal(dialog.open, true);
  assert.equal(close.hidden, true);
  assert.equal(done.hidden, true);
  assert.match(note.textContent, /watch this short video to the end/);
});

test('someone who has already watched it is not interrupted', () => {
  assert.equal(page({required: false}).dialog.open, false);
});

test('Escape does not close the first viewing, and neither does a second press', () => {
  const {dialog} = page({required: true});
  let prevented = false;
  dialog.fire('cancel', {preventDefault() { prevented = true; }});
  assert.equal(prevented, true);
  dialog.close();
  assert.equal(dialog.open, true);
});

test('skipping ahead during the first viewing goes back to the furthest point watched', () => {
  const {video} = page({required: true});
  watch(video);
  video.currentTime = 120; video.seeking = true; video.fire('seeking');
  assert.equal(video.currentTime, 10);
});

test('going back during the first viewing is allowed', () => {
  const {video} = page({required: true});
  watch(video);
  video.currentTime = 3; video.seeking = true; video.fire('seeking');
  assert.equal(video.currentTime, 3);
});

test('reaching the end records it once and lets the participant start', async () => {
  const {dialog, video, done, close, calls} = page({required: true});
  video.fire('ended'); video.fire('ended');
  await new Promise(setImmediate);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, '/MedData/TutorialWatched');
  assert.equal(calls[0].method, 'POST');
  assert.equal(calls[0].headers['X-CSRF-TOKEN'], 'synthetic-csrf');
  assert.equal(done.hidden, false);
  assert.equal(close.hidden, false);
  done.click();
  assert.equal(dialog.open, false);
});

test('a video that will not load never traps the participant, and nothing is recorded', async () => {
  const {dialog, video, done, calls, note} = page({required: true});
  video.fire('error');
  await new Promise(setImmediate);
  assert.equal(calls.length, 0);
  assert.match(note.textContent, /could not load/);
  done.click();
  assert.equal(dialog.open, false);
});

test('the Tutorial button replays it with free control, and closing stops it', async () => {
  const {dialog, video, close, button, calls} = page({required: false});
  button.click();
  assert.equal(dialog.open, true);
  assert.equal(close.hidden, false);
  assert.equal(video.paused, false);
  video.currentTime = 150; video.seeking = true; video.fire('seeking');
  assert.equal(video.currentTime, 150);
  video.fire('ended');
  await new Promise(setImmediate);
  assert.equal(calls.length, 0);
  close.click();
  assert.equal(dialog.open, false);
  assert.equal(video.paused, true);
  assert.equal(video.currentTime, 0);
});
