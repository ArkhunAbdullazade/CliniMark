const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Arrays made inside the sandbox have their own prototype, so strict comparison needs plain copies.
const plain = value => JSON.parse(JSON.stringify(value));
const js = name => fs.readFileSync(path.join(__dirname, '../../src/CliniMark.Presentation/wwwroot/js/admin', name), 'utf8');

// Runs the Overview script with a stand-in Chart.js that records each chart's settings.
function overview() {
  const charts = [];
  function Chart(canvas, config) { Object.assign(this, {canvas, config, data: config.data, options: config.options}); charts.push(this); }
  Chart.prototype.update = Chart.prototype.resize = () => {};
  Chart.defaults = {font: {}, plugins: {legend: {labels: {}}, tooltip: {}}};
  const progress = (done, active, open) => ({done, active, open});
  const data = {activity: [], signups: [], mix: [], firstDraw: [], drawing: [],
    categories: {Modality: [{value: 'ct', specialist: progress(5, 1, 2), student: progress(1, 0, 3)},
      {value: 'mri', specialist: progress(1, 0, 9), student: progress(0, 0, 4)}]}};
  const elements = {'overview-data': {textContent: JSON.stringify(data)}, 'category-dimension': {value: 'Modality'}, 'category-box': {style: {}}};
  const element = id => (elements[id] ||= {id, style: {}});
  const document = {documentElement: {},
    getElementById(id) { const found = element(id); found.addEventListener ||= () => {}; return found; },
    querySelector: selector => selector === 'input[name=category-role]:checked' ? {value: 'specialist'} : null,
    querySelectorAll: () => []};
  const context = {document, Chart, getComputedStyle: () => ({getPropertyValue: () => ''}), Date, JSON, Math};
  context.window = context;
  vm.createContext(context);
  vm.runInContext(js('adminCharts.js'), context, {filename: 'adminCharts.js'});
  vm.runInContext(js('dashboard.js'), context, {filename: 'dashboard.js'});
  return {charts, chartOn: id => charts.find(chart => chart.canvas === elements[id])};
}

test('the progress by category tooltip follows the row under the pointer', () => {
  const category = overview().chartOn('category-chart');
  assert.equal(category.options.indexAxis, 'y', 'One horizontal bar per category');
  assert.equal(category.options.interaction.mode, 'index');
  assert.equal(category.options.interaction.axis, 'y',
    'Chart.js measures along x unless told otherwise, which shows another row\'s figures');
  assert.deepEqual(plain(category.data.labels), ['Mri', 'Ct'], 'Largest category first');
  assert.deepEqual(plain(category.data.datasets.map(set => set.data)), [[1, 5], [0, 1], [9, 2]]);
});

test('every chart that shows a whole row or column on hover measures along its category axis', () => {
  const {charts} = overview();
  assert.ok(charts.length >= 5);
  for (const chart of charts.filter(item => item.options.interaction?.mode === 'index')) {
    assert.equal(chart.options.interaction.axis || 'x', chart.options.indexAxis || 'x', chart.canvas.id);
  }
});
