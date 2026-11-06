const {test}=require('node:test');
const assert=require('node:assert/strict');
const geometry=require('../../src/CliniMark.Presentation/wwwroot/js/annotationGeometry.js');
const charts=require('../../src/CliniMark.Presentation/wwwroot/js/admin/adminCharts.js');
const overlay=require('../../src/CliniMark.Presentation/wwwroot/js/admin/adminOverlay.js');

const local=(y,m,d,h=0)=>new Date(y,m-1,d,h);
const bucket=(date,specialist,student)=>({hour:date.toISOString(),specialist,student});

test('daily totals follow the local calendar and fill empty days up to today',()=>{
  const shaped=charts.daily([bucket(local(2026,10,29,9),3,1),bucket(local(2026,10,29,23),2,0),bucket(local(2026,10,31,8),0,4)],local(2026,11,1,12));
  assert.deepEqual(shaped.keys,['2026-10-29','2026-10-30','2026-10-31','2026-11-01']);
  assert.deepEqual(shaped.specialist,[5,0,0,0]);
  assert.deepEqual(shaped.student,[1,0,4,0]);
  assert.deepEqual(charts.daily([],local(2026,11,1)).specialist,[0]);
});

test('the last 48 hours always has 48 slots ending at the current hour',()=>{
  const now=local(2026,10,30,14);
  const shaped=charts.lastHours([bucket(local(2026,10,30,14),2,0),bucket(local(2026,10,28,13),9,9)],now,48);
  assert.equal(shaped.labels.length,48);
  assert.equal(shaped.specialist.at(-1),2);
  assert.equal(shaped.specialist.reduce((a,b)=>a+b,0),2,'buckets older than the window are left out');
});

test('sign-ups accumulate per role',()=>{
  const shaped=charts.cumulative([bucket(local(2026,10,29,9),2,1),bucket(local(2026,10,30,9),1,0)],local(2026,10,31));
  assert.deepEqual(shaped.specialist,[2,3,3]);
  assert.deepEqual(shaped.student,[1,1,1]);
});

test('category series sorts by total and drops values with nothing for the role',()=>{
  const items=[
    {value:'ct',specialist:{done:1,active:0,open:1},student:{done:0,active:0,open:0}},
    {value:'mri',specialist:{done:2,active:1,open:3},student:{done:1,active:0,open:0}}];
  assert.deepEqual(charts.categorySeries(items,'specialist').labels,['mri','ct']);
  assert.deepEqual(charts.categorySeries(items,'student'),{labels:['mri'],done:[1],active:[0],open:[0]});
});

const shape=JSON.stringify({version:1,coordinateSystem:'image-pixels',imageWidth:1000,imageHeight:500,
  annotations:[{type:'rectangle',points:[[10,20],[100,20],[100,80],[10,80]]},{type:'freehand',points:[[200,200],[260,210],[230,260]]}]});

test('overlay parses saved shapes and reports legacy coordinates instead of guessing',()=>{
  const parsed=overlay.parse([
    {id:'a1',term:'lesion',annotator:'Dr A',role:'Specialist',coordinates:shape},
    {id:'a2',term:'margin',annotator:'Dr A',role:'Specialist',coordinates:'[[1,2,3,4]]'}],geometry);
  assert.equal(parsed.shapes.length,2);
  assert.deepEqual(parsed.shapes[0].points,[[10,20],[100,20],[100,80],[10,80]]);
  assert.equal(parsed.problems.length,1);
  assert.equal(parsed.problems[0].id,'a2');
  assert.deepEqual(overlay.dimensions(parsed.shapes),[[1000,500]]);
});

test('layers take fixed palette slots in first-seen order and never cycle past eight',()=>{
  const items=Array.from({length:10},(_,index)=>({id:`a${index}`,term:`term ${index}`,annotator:index%2?'Dr B':'Dr A',role:'Specialist',coordinates:shape}));
  const {shapes}=overlay.parse(items,geometry);
  const byTerm=overlay.layers(shapes,'term');
  assert.equal(byTerm.length,10);
  assert.deepEqual(byTerm.slice(0,8).map(layer=>layer.colour),overlay.PALETTE);
  assert.ok(byTerm.slice(8).every(layer=>layer.colour===overlay.OTHER));
  const byAnnotator=overlay.layers(shapes,'annotator');
  assert.deepEqual(byAnnotator.map(layer=>[layer.name,layer.count]),[['Specialist: Dr A',10],['Specialist: Dr B',10]]);
});
