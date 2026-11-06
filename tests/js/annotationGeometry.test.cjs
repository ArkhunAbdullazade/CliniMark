const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const geometry=require('../../src/CliniMark.Presentation/wwwroot/js/annotationGeometry.js');
const close=(a,b,tolerance=1e-9)=>assert.ok(Math.abs(a-b)<=tolerance,`${a} differs from ${b}`);

test('known 1200x800 image maps CSS, scroll offsets, browser zoom and DPR independently',()=>{
  for(const width of [1200,780,600,360,177.25])for(const offset of [{left:32,top:160},{left:-40,top:-300},{left:88.125,top:17.5}])for(const dpr of [1,1.25,2,3]){
    const rect={...offset,width,height:width*2/3};
    // CSS client coordinates do not use backing pixels or devicePixelRatio.
    global.devicePixelRatio=dpr;
    const expected={x:300,y:200};
    const client=geometry.imageToClient(expected,rect,1200,800);
    const actual=geometry.clientToImage(client.x,client.y,rect,1200,800);
    close(actual.x,expected.x);close(actual.y,expected.y);
  }
});
test('edges clamp to continuous image bounds including width and height',()=>{
  const rect={left:100,top:50,width:600,height:400};
  assert.deepEqual(geometry.clientToImage(-5,-5,rect,1200,800),{x:0,y:0});
  assert.deepEqual(geometry.clientToImage(900,900,rect,1200,800),{x:1200,y:800});
  assert.throws(()=>geometry.clientToImage(1,1,{...rect,width:0},1200,800));
});
test('known rectangle persists in original image coordinates with declared dimensions',()=>{
  const json=geometry.serialize([{type:'rectangle',cx:600,cy:400,width:600,height:400,rotation:0}],1200,800);
  const actual=JSON.parse(json);
  assert.equal(actual.coordinateSystem,'image-pixels');assert.equal(actual.version,1);
  assert.deepEqual(actual.annotations[0].points,[[300,200],[900,200],[900,600],[300,600]]);
  assert.equal(geometry.serialize(geometry.deserialize(json,1200,800),1200,800),json);
});
test('precision measured over 1000 rotated rectangles and save/reload round trips',()=>{
  let quantization=0,roundtrip=0;
  for(let i=0;i<1000;i++){
    const shape={type:'rectangle',cx:600+Math.sin(i)*40,cy:400+Math.cos(i)*30,width:77.12345+i/100,height:99.76543+i/200,rotation:i/163};
    const expected=geometry.corners(shape);
    const json=geometry.serialize([shape],1200,800),points=JSON.parse(json).annotations[0].points;
    const restored=geometry.corners(geometry.deserialize(json,1200,800)[0]);
    points.forEach((point,index)=>point.forEach((value,axis)=>{
      quantization=Math.max(quantization,Math.abs(value-expected[index][axis]));
      roundtrip=Math.max(roundtrip,Math.abs(restored[index][axis]-expected[index][axis]));
    }));
  }
  console.log(`Measured maximum per-axis serialization error: ${quantization.toFixed(9)} original px; rectangle reload error: ${roundtrip.toFixed(9)} original px`);
  assert.ok(quantization<=0.000500001);assert.ok(roundtrip<=0.0015);
});
test('freehand vertices survive without adaptive simplification',()=>{
  const points=Array.from({length:1000},(_,i)=>({x:600+100*Math.cos(i*Math.PI/500),y:400+100*Math.sin(i*Math.PI/500)}));
  const json=geometry.serialize([{type:'freehand',points}],1200,800);
  const restored=geometry.deserialize(json,1200,800)[0];
  assert.equal(restored.points.length,1000);
  restored.points.forEach((point,i)=>{close(point.x,points[i].x,.000501);close(point.y,points[i].y,.000501);});
});
test('one keyword round trip preserves two boxes including rotation and one freehand boundary',()=>{
  const shapes=[
    {type:'rectangle',cx:55.125,cy:50.25,width:90,height:60,rotation:0},
    {type:'rectangle',cx:315,cy:145,width:60*Math.SQRT2,height:30*Math.SQRT2,rotation:Math.PI/4},
    {type:'freehand',points:[[500,100],[550,110],[570,150],[525,175],[490,140],[500,100]].map(([x,y])=>({x,y}))}
  ];
  const json=geometry.serialize(shapes,1000,500),stored=JSON.parse(json);
  assert.deepEqual(stored.annotations.map(shape=>shape.type),['rectangle','rectangle','freehand']);
  assert.deepEqual(stored.annotations[1].points,[[300,100],[360,160],[330,190],[270,130]]);
  assert.equal(stored.annotations[2].points.length,6);
  const restored=geometry.deserialize(json,1000,500);
  assert.equal(restored.length,3);close(restored[1].rotation,Math.PI/4);
  assert.equal(geometry.serialize(restored,1000,500),json);
});
test('incomplete, nonfinite, out-of-bounds, legacy and wrong-image dimensions are rejected',()=>{
  const good={type:'rectangle',cx:600,cy:400,width:600,height:400,rotation:0};
  assert.throws(()=>geometry.serialize([],1200,800));
  assert.throws(()=>geometry.serialize([{...good,width:0}],1200,800));
  assert.throws(()=>geometry.serialize([{...good,cx:-1}],1200,800));
  assert.throws(()=>geometry.serialize([{...good,rotation:NaN}],1200,800));
  assert.throws(()=>geometry.deserialize('[[[0,0],[1,1]]]',1200,800));
  assert.throws(()=>geometry.deserialize(geometry.serialize([good],1200,800),2400,1600));
});

function managerFixture(){
  const listeners={};let rect={left:100,top:150,width:600,height:400}, viewport=null;
  const ctx=new Proxy({},{get:()=>()=>{}});
  const column={clientWidth:608};
  const imageContainer={style:{},closest:selector=>selector==='.image-viewport'?viewport:column};
  const canvas={width:0,height:0,style:{},setAttribute:()=>{},focus:()=>{},getContext:()=>ctx,getBoundingClientRect:()=>rect,addEventListener:(name,callback)=>{listeners[name]=callback;},dispatchEvent:()=>{},setPointerCapture:()=>{},hasPointerCapture:()=>false};
  const sourceImage={src:'fixture.svg',currentSrc:'fixture.svg',complete:true,naturalWidth:1200,naturalHeight:800,addEventListener:()=>{}};
  const container={style:{},parentElement:imageContainer,closest:()=>column};
  const elements={mainCanvas:canvas,sourceImage,canvasContainer:container,'image-container':imageContainer};
  const document={getElementById:id=>elements[id],addEventListener:()=>{}};
  const window={addEventListener:()=>{},innerWidth:1200};
  const context={window,document,AnnotationGeometry:geometry,structuredClone,CustomEvent:function(){},console};
  vm.runInNewContext(fs.readFileSync(path.resolve(__dirname,'../../src/CliniMark.Presentation/wwwroot/js/canvas.js'),'utf8'),context);
  const manager=new window.CanvasManager('mainCanvas','sourceImage','canvasContainer');
  const event=(x,y)=>({clientX:rect.left+x*rect.width/1200,clientY:rect.top+y*rect.height/800});
  return{manager,canvas,event,setRect:value=>{rect=value;},setViewport:value=>{viewport=value;},column,listeners,sourceImage};
}
test('actual canvas draws identical rectangles in four drag directions',()=>{
  for(const [start,end]of [[[300,200],[900,600]],[[900,600],[300,200]],[[300,600],[900,200]],[[900,200],[300,600]]]){
    const{manager,event}=managerFixture();manager.handleMouseDown(event(...start));manager.handleMouseMove(event(...end));manager.handleMouseUp(event(...end));
    assert.deepEqual(JSON.parse(manager.getAllAnnotationsJSON()).annotations[0].points,[[300,200],[900,200],[900,600],[300,600]]);
  }
});
test('actual canvas resize leaves backing dimensions and persisted shapes unchanged',()=>{
  const{manager,event,setRect,column,canvas}=managerFixture();manager.handleMouseDown(event(300,200));manager.handleMouseUp(event(900,600));
  const before=manager.getAllAnnotationsJSON();
  column.clientWidth=308;setRect({left:20,top:-190,width:300,height:200});manager.resizeDisplay();
  assert.equal(canvas.width,1200);assert.equal(canvas.height,800);assert.equal(manager.getAllAnnotationsJSON(),before);
  manager.loadCoordinates(before);assert.equal(manager.getAllAnnotationsJSON(),before);
});

test('viewport height and width constrain the display without changing image coordinates',()=>{
  const{manager,event,canvas,setViewport}=managerFixture();
  manager.handleMouseDown(event(300,200));manager.handleMouseUp(event(900,600));
  const before=manager.getAllAnnotationsJSON();
  setViewport({clientWidth:908,clientHeight:208});manager.resizeDisplay();
  close(parseFloat(canvas.style.width),300);close(parseFloat(canvas.style.height),200);
  setViewport({clientWidth:108,clientHeight:708});manager.resizeDisplay();
  close(parseFloat(canvas.style.width),100);close(parseFloat(canvas.style.height),100*2/3);
  setViewport({clientWidth:1508,clientHeight:1008});manager.resizeDisplay();
  close(parseFloat(canvas.style.width),1200);close(parseFloat(canvas.style.height),800);
  assert.equal(canvas.width,1200);assert.equal(canvas.height,800);
  assert.equal(manager.getAllAnnotationsJSON(),before);
});
test('actual canvas editing moves bounded freehand shapes, deleting and source identity protection',()=>{
  const{manager,event,sourceImage}=managerFixture();manager.switchMode('freehand');
  for(const[p,i]of [[[100,100],0],[[200,100],1],[[200,200],2],[[100,200],3]])i===0?manager.handleMouseDown(event(...p)):manager.handleMouseMove(event(...p));
  manager.handleMouseUp(event(100,200));assert.equal(manager.annotations.length,1);
  manager.handleMouseDown(event(150,100));manager.handleMouseMove(event(250,200));manager.handleMouseUp(event(250,200));
  close(manager.annotations[0].points[0].x,200);close(manager.annotations[0].points[0].y,200);
  const handle=manager.getFreehandDeleteHandle(manager.annotations[0]);
  manager.handleMouseDown(event(handle.x+1,handle.y+1));assert.equal(manager.annotations.length,0);
  sourceImage.currentSrc='other.svg';assert.throws(()=>manager.getAllAnnotationsJSON(),/assigned image/);
});
test('incomplete canvas gesture cannot be serialized and pointer cancellation clears draft',()=>{
  const{manager,event,listeners}=managerFixture();manager.handleMouseDown(event(300,200));manager.handleMouseMove(event(900,600));
  assert.throws(()=>manager.getAllAnnotationsJSON(),/Finish drawing/);listeners.pointercancel();assert.equal(manager.annotations.length,0);assert.equal(manager.currentAction,'none');
});
test('actual rectangle resize uses opposite fixed corner, movement and rotation preserve bounds',()=>{
  const{manager,event}=managerFixture();manager.handleMouseDown(event(300,200));manager.handleMouseUp(event(900,600));
  const handle=manager.getAnnotationHandles(manager.annotations[0]).tl;
  manager.handleMouseDown(event(handle.x,handle.y));manager.handleMouseMove(event(400,300));manager.handleMouseUp(event(400,300));
  assert.deepEqual(JSON.parse(manager.getAllAnnotationsJSON()).annotations[0].points,[[400,300],[900,300],[900,600],[400,600]]);
  const rotate=manager.getAnnotationHandles(manager.annotations[0]).rotate;
  manager.handleMouseDown(event(rotate.x,rotate.y));manager.handleMouseMove(event(1000,450));manager.handleMouseUp(event(1000,450));
  close(manager.annotations[0].rotation,Math.PI/2);geometry.validate(JSON.parse(manager.getAllAnnotationsJSON()),1200,800);
  manager.handleMouseDown(event(650,450));manager.handleMouseMove(event(1199,799));manager.handleMouseUp(event(1199,799));
  geometry.validate(JSON.parse(manager.getAllAnnotationsJSON()),1200,800);
});
