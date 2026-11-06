const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/CliniMark.Presentation/wwwroot/js/signupConfirm.js'), 'utf8');

function fixture({valid = true, withJquery = true} = {}) {
  const listeners = {};
  const shown = {textContent: ''};
  const buttons = {
    '[data-signup-yes]': {click: null, addEventListener(_, callback) { this.click = callback; }},
    '[data-signup-back]': {click: null, addEventListener(_, callback) { this.click = callback; }}
  };
  const dialog = {
    open: false,
    showModal() { this.open = true; },
    close() { this.open = false; },
    querySelector: selector => selector === '[data-signup-email]' ? shown : buttons[selector]
  };
  const email = {value: '  person@example.org ', focused: false, selected: false,
    focus() { this.focused = true; }, select() { this.selected = true; }};
  let submitted = 0;
  const form = {
    querySelector: () => email,
    addEventListener(name, callback) { listeners[name] = callback; },
    requestSubmit() { const event = submit(); listeners.submit(event); if (!event.defaultPrevented) submitted++; }
  };
  const submit = (defaultPrevented = false) => ({defaultPrevented, preventDefault() { this.defaultPrevented = true; }});
  const document = {
    querySelector: () => form,
    getElementById: () => dialog
  };
  const window = withJquery ? {jQuery: Object.assign(() => ({valid: () => valid}), {fn: {valid() {}}})} : {};
  vm.runInNewContext(source, {window, document});
  return {listeners, dialog, shown, email, buttons, submit, submitted: () => submitted};
}

test('a valid form shows the typed email and is held until the person confirms it', () => {
  const f = fixture();
  const event = f.submit();
  f.listeners.submit(event);
  assert.equal(event.defaultPrevented, true);
  assert.equal(f.dialog.open, true);
  assert.equal(f.shown.textContent, 'person@example.org');
  f.buttons['[data-signup-yes]'].click();
  assert.equal(f.dialog.open, false);
  assert.equal(f.submitted(), 1);
});

test('going back keeps the form unsent and selects the email for correction', () => {
  const f = fixture();
  f.listeners.submit(f.submit());
  f.buttons['[data-signup-back]'].click();
  assert.equal(f.dialog.open, false);
  assert.equal(f.submitted(), 0);
  assert.equal(f.email.focused, true);
  assert.equal(f.email.selected, true);
});

test('an invalid form reports its own errors without the email check', () => {
  const invalid = fixture({valid: false});
  const event = invalid.submit();
  invalid.listeners.submit(event);
  assert.equal(invalid.dialog.open, false);
  assert.equal(event.defaultPrevented, false);

  const blocked = fixture();
  blocked.listeners.submit(blocked.submit(true));
  assert.equal(blocked.dialog.open, false);
});

test('the check still works when validation scripts did not load', () => {
  const f = fixture({withJquery: false});
  f.listeners.submit(f.submit());
  assert.equal(f.dialog.open, true);
});
