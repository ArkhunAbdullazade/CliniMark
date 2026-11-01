const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/MedAnnotateApp.Presentation/wwwroot/js/assignmentWatch.js'), 'utf8');
const flush = () => new Promise(setImmediate);
const answer = assigned => ({ok: true, json: async () => ({success: true, assigned})});

function element() {
  return {listeners: {}, attributes: {}, parts: {}, children: [], textContent: '', open: false,
    setAttribute(name, value) { this.attributes[name] = value; },
    addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); },
    querySelector(selector) { return this.parts[selector] ||= element(); },
    appendChild(child) { this.children.push(child); },
    showModal() { this.open = true; }};
}

function harness({busy = () => false} = {}) {
  let now = 1_000_000, reloaded = false;
  const timers = [], requests = [], responses = [], dialogs = [], released = [], listeners = {};
  const document = {hidden: false, body: element(),
    createElement() { const dialog = element(); dialogs.push(dialog); return dialog; },
    addEventListener(name, callback) { (listeners[name] ||= new Set()).add(callback); },
    removeEventListener(name, callback) { listeners[name]?.delete(callback); }};
  const context = {window: {}, document, encodeURIComponent, Date: {now: () => now},
    location: {reload() { reloaded = true; }},
    setInterval(callback) { timers.push(callback); return timers.length; },
    clearInterval(id) { timers[id - 1] = null; },
    fetch(url, options) {
      requests.push({url, options});
      const next = responses.shift() ?? answer(true);
      return next instanceof Error ? Promise.reject(next) : Promise.resolve(next);
    }};
  vm.runInNewContext(source, context, {filename: 'assignmentWatch.js'});
  const watch = context.window.watchAssignment({imageId: 7, busy,
    message: 'Nothing more can be saved on this page.', onReleased: () => released.push(true)});
  return {watch, document, requests, responses, dialogs, released, listeners,
    tick: async () => { timers[0]?.(); await flush(); },
    fire: async name => { for (const callback of [...(listeners[name] || [])]) callback(); await flush(); },
    advance: ms => { now += ms; },
    heartbeat: async () => { timers[1]?.(); await flush(); },
    stopped: () => timers[0] === null && timers[1] === null && !listeners.pointermove?.size && !listeners.visibilitychange?.size,
    reloaded: () => reloaded};
}

test('checks run while the person is active and the tab is visible, and right after a pause', async () => {
  const h = harness();
  await h.tick();
  assert.equal(h.requests.length, 1);
  assert.equal(h.requests[0].url, '/MedData/AssignmentStatus?medDataId=7');
  assert.equal(h.requests[0].options.cache, 'no-store');

  h.advance(6 * 60000);
  await h.tick();
  assert.equal(h.requests.length, 1, 'An unattended page stops checking');
  await h.fire('pointermove');
  assert.equal(h.requests.length, 2, 'The first input after a pause checks at once');
  await h.fire('pointermove');
  assert.equal(h.requests.length, 2, 'Continuous input waits for the timer');

  h.document.hidden = true;
  await h.tick();
  assert.equal(h.requests.length, 2, 'A hidden tab does not check');
  h.document.hidden = false;
  await h.fire('visibilitychange');
  assert.equal(h.requests.length, 3);
  assert.equal(h.released.length, 0);
  assert.equal(h.dialogs.length, 0);
});

test('a released image tells the page once, stops checking and shows a dialog that stays', async () => {
  const h = harness();
  h.responses.push(answer(false));
  await h.tick();
  assert.equal(h.released.length, 1);
  assert.equal(h.stopped(), true);
  const [dialog] = h.dialogs;
  assert.equal(dialog.open, true);
  assert.equal(dialog.querySelector('#released-dialog-message').textContent, 'Nothing more can be saved on this page.');
  let prevented = false;
  dialog.listeners.cancel[0]({preventDefault() { prevented = true; }});
  assert.equal(prevented, true, 'Escape does not dismiss it');
  dialog.querySelector('button').listeners.click[0]();
  assert.equal(h.reloaded(), true);
  await h.watch.check();
  assert.equal(h.requests.length, 1, 'Nothing is checked after the release');
});

test('busy pages, failures and signed-out answers never release the page', async () => {
  let busy = true;
  const h = harness({busy: () => busy});
  await h.tick();
  assert.equal(h.requests.length, 0, 'Saves and Next Image are never checked against');
  busy = false;
  h.responses.push(new Error('offline'), {ok: false, status: 401, json: async () => ({success: false})},
    {ok: true, json: async () => { throw new SyntaxError('sign-in page'); }});
  for (let i = 0; i < 3; i++) await h.tick();
  assert.equal(h.requests.length, 3);
  assert.equal(h.released.length, 0);

  // Next Image clears the lock itself, so an answer that lands while it runs is ignored.
  let finish;
  h.responses.push(new Promise(resolve => { finish = resolve; }).then(() => answer(false)));
  await h.tick();
  busy = true;
  finish();
  await flush(); await flush();
  assert.equal(h.released.length, 0);
  assert.equal(h.stopped(), false);
});

test('only one check is in flight at a time', async () => {
  const h = harness();
  let finish;
  h.responses.push(new Promise(resolve => { finish = resolve; }).then(() => answer(true)));
  h.watch.check(); h.watch.check(); h.watch.check();
  assert.equal(h.requests.length, 1);
  finish(); await flush(); await flush();
  await h.watch.check();
  assert.equal(h.requests.length, 2);
});

test('a slow heartbeat keeps checking an unattended or hidden page, so its image stays reserved without a time limit', async () => {
  const h = harness();
  h.advance(6 * 60000);
  h.document.hidden = true;
  await h.tick();
  assert.equal(h.requests.length, 0, 'The frequent check pauses while unattended');
  await h.heartbeat();
  assert.equal(h.requests.length, 1, 'The heartbeat still checks in, which refreshes the lock time');
  h.responses.push(answer(false));
  await h.heartbeat();
  assert.equal(h.released.length, 1, 'A release found by the heartbeat is still reported');
  assert.equal(h.stopped(), true);
});
