const test = require('node:test');
const assert = require('node:assert/strict');
const {stepFor} = require('../../src/CliniMark.Presentation/wwwroot/js/admin/stepKeys.js');

// A stand-in page: the step links, an optional open dialog, and focus targets matched by selector.
function page({previous = '/Admin/Images/Details/7?participant=p1', next = '/Admin/Images/Details/5?participant=p1', dialog = false} = {}) {
  const links = {previous, next};
  return {querySelector(selector) {
    if (selector.startsWith('.modal.show')) return dialog ? {} : null;
    const step = /data-step="(\w+)"/.exec(selector)?.[1];
    return links[step] ? {href: links[step]} : null;
  }};
}
const focus = tag => ({closest: selector => {
  // Mirrors the browser: the selector lists the controls that use arrow keys themselves.
  const usesArrows = ['textarea', 'select', 'input[type=text]', 'input[type=radio]', 'input[type=search]'].includes(tag);
  return selector.includes('textarea') && usesArrows ? {} : null;
}});
const key = (name, extra = {}) => ({key: name, target: focus('body'), ...extra});

test('the arrow keys go to the previous and next image of the participant', () => {
  assert.equal(stepFor(key('ArrowLeft'), page()), '/Admin/Images/Details/7?participant=p1');
  assert.equal(stepFor(key('ArrowRight'), page()), '/Admin/Images/Details/5?participant=p1');
  assert.equal(stepFor(key('ArrowUp'), page()), null);
  assert.equal(stepFor(key('Enter'), page()), null);
});

test('the first and last image have nowhere further to go', () => {
  assert.equal(stepFor(key('ArrowLeft'), page({previous: null})), null);
  assert.equal(stepFor(key('ArrowRight'), page({next: null})), null);
});

test('keys are left alone while typing, choosing, with a modifier, held down or under a dialog', () => {
  for (const tag of ['textarea', 'select', 'input[type=text]', 'input[type=radio]'])
    assert.equal(stepFor(key('ArrowRight', {target: focus(tag)}), page()), null, tag);
  for (const tag of ['input[type=checkbox]', 'button', 'a'])
    assert.ok(stepFor(key('ArrowRight', {target: focus(tag)}), page()), `${tag} has no use for arrow keys`);
  for (const modifier of ['altKey', 'ctrlKey', 'metaKey', 'shiftKey'])
    assert.equal(stepFor(key('ArrowRight', {[modifier]: true}), page()), null, modifier);
  assert.equal(stepFor(key('ArrowRight', {repeat: true}), page()), null, 'Holding the key down does not race through images');
  assert.equal(stepFor(key('ArrowRight', {defaultPrevented: true}), page()), null);
  assert.equal(stepFor(key('ArrowRight'), page({dialog: true})), null);
});
