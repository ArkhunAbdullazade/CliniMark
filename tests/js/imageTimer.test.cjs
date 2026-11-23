const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/CliniMark.Presentation/wwwroot/js/imageTimer.js'), 'utf8');
const flush = () => new Promise(setImmediate);
const ok = () => ({ok: true, json: async () => ({success: true, recorded: true})});

function harness() {
  let now = 1_000_000;
  const timers = [], timeouts = [], requests = [], responses = [], listeners = {}, windowListeners = {};
  const tutorial = {open: false};
  const document = {hidden: false,
    getElementById(id) { return id === 'tutorial-dialog' ? tutorial : null; },
    addEventListener(name, callback) { (listeners[name] ||= new Set()).add(callback); },
    removeEventListener(name, callback) { listeners[name]?.delete(callback); }};
  const window = {
    addEventListener(name, callback) { (windowListeners[name] ||= new Set()).add(callback); },
    removeEventListener(name, callback) { windowListeners[name]?.delete(callback); }};
  const context = {window, document, Date: {now: () => now}, JSON, Math, Promise,
    crypto: {randomUUID: () => '6f1c2d3e-4a5b-4c6d-8e7f-901234567890'},
    setInterval(callback) { timers.push(callback); return timers.length; },
    clearInterval(id) { timers[id - 1] = null; },
    setTimeout(callback) { timeouts.push(callback); return timeouts.length; },
    fetch(url, options) {
      requests.push({url, options, body: JSON.parse(options.body)});
      const next = responses.shift() ?? ok();
      return next instanceof Error ? Promise.reject(next) : next === 'hang' ? new Promise(() => {}) : Promise.resolve(next);
    }};
  vm.runInNewContext(source, context, {filename: 'imageTimer.js'});
  const timer = window.trackImageTime({imageId: 42, token: 'csrf-token'});
  // Runs the one-second timer the given number of times, a second apart.
  const seconds = async count => { for (let i = 0; i < count; i++) { now += 1000; timers[0]?.(); await flush(); } };
  return {timer, window, document, tutorial, requests, responses, timeouts, seconds,
    advance: ms => { now += ms; },
    input: async name => { for (const callback of [...(listeners[name] || [])]) callback(); await flush(); },
    fire: async name => { for (const callback of [...(listeners[name] || [])]) callback(); await flush(); },
    fireWindow: async name => { for (const callback of [...(windowListeners[name] || [])]) callback(); await flush(); },
    listening: () => (listeners.pointermove?.size || 0) + (listeners.visibilitychange?.size || 0) + (windowListeners.pagehide?.size || 0)};
}

test('counts seconds while the page is visible and in use, and reports the running total', async () => {
  const h = harness();
  assert.equal(h.window.imageTimer, h.timer);
  await h.seconds(1);
  assert.equal(h.requests.length, 1, 'The first second is reported at once');
  const first = h.requests[0];
  assert.equal(first.url, '/MedData/RecordTime');
  assert.equal(first.options.method, 'POST');
  assert.equal(first.options.headers['X-CSRF-TOKEN'], 'csrf-token');
  assert.deepEqual(first.body, {Id: 42, View: '6f1c2d3e-4a5b-4c6d-8e7f-901234567890', Seconds: 1});

  await h.seconds(29);
  assert.equal(h.requests.length, 1, 'Then every half minute');
  await h.seconds(1);
  assert.equal(h.requests.length, 2);
  assert.equal(h.requests[1].body.Seconds, 31);
  assert.equal(h.requests[1].body.View, first.body.View, 'One page keeps one id');
  assert.equal(h.timer.seconds(), 31);
});

test('stops counting after three minutes without input, in a hidden tab and while the tutorial plays', async () => {
  const h = harness();
  await h.seconds(200);
  assert.equal(h.timer.seconds(), 180, 'Nothing counts after three idle minutes');
  await h.input('pointermove');
  await h.seconds(10);
  assert.equal(h.timer.seconds(), 190, 'Input starts it again, without counting the idle stretch');

  h.document.hidden = true;
  await h.fire('visibilitychange');
  await h.seconds(60);
  assert.equal(h.timer.seconds(), 190, 'A hidden tab never counts');
  h.document.hidden = false;
  await h.fire('visibilitychange');
  await h.input('keydown');
  await h.seconds(5);
  assert.equal(h.timer.seconds(), 195);

  h.tutorial.open = true;
  await h.seconds(30);
  assert.equal(h.timer.seconds(), 195, 'The tutorial video is not time on the image');
  h.tutorial.open = false;
  await h.seconds(2);
  assert.equal(h.timer.seconds(), 197);
});

test('a sleeping computer or a throttled tab adds at most two seconds at once', async () => {
  const h = harness();
  await h.seconds(3);
  h.advance(10 * 60000);
  await h.input('pointerdown');
  await h.seconds(1);
  assert.equal(h.timer.seconds(), 5);
});

test('hiding or leaving the page sends the total with keepalive, and failed reports are carried by the next one', async () => {
  const h = harness();
  h.responses.push(new Error('offline'));
  await h.seconds(1);
  await h.seconds(30);
  assert.equal(h.requests.length, 2, 'The total is sent again after a failure');
  assert.equal(h.requests[1].body.Seconds, 31);

  await h.seconds(4);
  h.document.hidden = true;
  await h.fire('visibilitychange');
  assert.equal(h.requests.length, 3);
  assert.equal(h.requests[2].options.keepalive, true);
  assert.equal(h.requests[2].body.Seconds, 35);
  await h.fire('visibilitychange');
  assert.equal(h.requests.length, 3, 'Nothing new to send');

  h.document.hidden = false;
  await h.fire('visibilitychange');
  await h.input('wheel');
  await h.seconds(3);
  await h.fireWindow('pagehide');
  assert.equal(h.requests.length, 4);
  assert.equal(h.requests[3].options.keepalive, true);
  assert.equal(h.requests[3].body.Seconds, 38);
});

test('flush sends before moving on but never holds the page up', async () => {
  const h = harness();
  await h.seconds(1);
  await h.seconds(10);
  h.responses.push('hang');
  let done = false;
  h.timer.flush().then(() => { done = true; });
  await flush();
  assert.equal(h.requests.at(-1).body.Seconds, 11);
  assert.equal(done, false);
  h.timeouts.forEach(callback => callback());
  await flush();
  assert.equal(done, true, 'A slow server only delays the next step briefly');
});

test('stop ends counting, sends the total and removes every listener', async () => {
  const h = harness();
  await h.seconds(1);
  await h.seconds(6);
  h.timer.stop();
  await flush();
  assert.equal(h.requests.at(-1).body.Seconds, 7);
  assert.equal(h.requests.at(-1).options.keepalive, true);
  assert.equal(h.listening(), 0);
  const sent = h.requests.length;
  await h.seconds(30);
  await h.input('pointermove');
  assert.equal(h.timer.seconds(), 7);
  assert.equal(h.requests.length, sent);
  h.timer.stop();
  assert.equal(h.requests.length, sent, 'Stopping twice sends nothing more');
});
