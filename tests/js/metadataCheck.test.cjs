const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../src/CliniMark.Presentation/wwwroot/js/metadataCheck.js'), 'utf8');
const context = {window: {}};
vm.runInNewContext(source, context, {filename: 'metadataCheck.js'});
const {searchLabels} = context.window.MetadataCheckSearch;

const options = ['Cardiology', 'Dermatology', 'Hematology', 'Pulmonology', 'Urology/Nephrology'];
const values = result => Array.from(result.matches, match => match.value);

test('an empty search lists every label except the one shown', () => {
  const result = searchLabels({options, shown: 'Pulmonology'}, '');
  assert.deepEqual(values(result), ['Cardiology', 'Dermatology', 'Hematology', 'Urology/Nephrology']);
  assert.equal(result.addable, null);
});

test('labels starting with the text come before labels containing it', () => {
  assert.deepEqual(values(searchLabels({options}, 'uro')), ['Urology/Nephrology']);
  assert.deepEqual(values(searchLabels({options: ['Neurology', 'Urology'], shown: ''}, 'urol')), ['Urology', 'Neurology']);
});

test('searching ignores capitals and extra spaces', () => {
  assert.deepEqual(values(searchLabels({options}, '  DERMA  ')), ['Dermatology']);
});

test('a value with no exact match can be added, trimmed', () => {
  const result = searchLabels({options}, '  rheu   matology ');
  assert.deepEqual(values(result), []);
  assert.equal(result.addable, 'rheu matology');
});

test('a partial match can still be added as something new', () => {
  const result = searchLabels({options}, 'Derma');
  assert.deepEqual(values(result), ['Dermatology']);
  assert.equal(result.addable, 'Derma');
});

test('an exact match, the label shown and Unknown cannot be added', () => {
  assert.equal(searchLabels({options}, 'cardiology').addable, null);
  assert.equal(searchLabels({options, shown: 'Rheumatology'}, 'rheumatology').addable, null);
  assert.equal(searchLabels({options}, 'Unknown').addable, null);
});

test('values added by clinicians are found and marked, and are not added twice', () => {
  const result = searchLabels({options, added: ['Rheumatology']}, 'rheum');
  assert.deepEqual(values(result), ['Rheumatology']);
  assert.equal(result.matches[0].added, true);
  assert.equal(searchLabels({options, added: ['Rheumatology']}, 'RHEUMATOLOGY').addable, null);
});

test('a value over the length limit cannot be added', () => {
  assert.equal(searchLabels({options}, 'a'.repeat(101)).addable, null);
  assert.equal(searchLabels({options}, 'a'.repeat(100)).addable, 'a'.repeat(100));
});
