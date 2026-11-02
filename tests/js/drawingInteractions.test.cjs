const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const geometry=require('../../src/MedAnnotateApp.Presentation/wwwroot/js/annotationGeometry.js');

function fixture(){
  const listeners={},lines=[];
  const ctx=new Proxy({lineTo:(x,y)=>lines.push([x,y])},{get:(target,key)=>target[key]??(()=>{})});
  const rect={left:80,top:140,width:600,height:400};
  const column={clientWidth:608};
  const imageContainer={style:{},closest:selector=>selector==='.left-column'?column:null};
  const canvas={width:0,height:0,style:{},setAttribute:()=>{},getContext:()=>ctx,
    getBoundingClientRect:()=>rect,addEventListener:(name,callback)=>{listeners[name]=callback;},dispatchEvent:()=>{}};
  const sourceImage={src:'fixture.svg',currentSrc:'fixture.svg',complete:true,naturalWidth:1200,naturalHeight:800,addEventListener:()=>{}};
  const container={style:{},parentElement:imageContainer,closest:()=>column};
  const elements={mainCanvas:canvas,sourceImage,canvasContainer:container,'image-container':imageContainer};
  const window={addEventListener:()=>{},innerWidth:1200};
  const document={getElementById:id=>elements[id],addEventListener:()=>{}};
  vm.runInNewContext(fs.readFileSync(path.resolve(__dirname,'../../src/MedAnnotateApp.Presentation/wwwroot/js/canvas.js'),'utf8'),
    {window,document,AnnotationGeometry:geometry,structuredClone,CustomEvent:function(){},console});
  const manager=new window.CanvasManager('mainCanvas','sourceImage','canvasContainer');
  const event=(x,y)=>({clientX:rect.left+x/2,clientY:rect.top+y/2});
  let changes=0;manager.onAnnotationsChange=()=>{changes++;};
  return{manager,canvas,event,listeners,lines,changes:()=>changes};
}

const rectangle=()=>({type:'rectangle',cx:300,cy:250,width:200,height:100,rotation:0});
const freehand=()=>({type:'freehand',points:[[300,200],[500,200],[500,400],[300,400],[300,200]].map(([x,y])=>({x,y}))});
const points=manager=>JSON.parse(manager.getAllAnnotationsJSON()).annotations.map(shape=>shape.points);
const gesture=(manager,event,start,end=start)=>{
  manager.handleMouseDown(event(...start));
  if(end!==start)manager.handleMouseMove(event(...end));
  manager.handleMouseUp(event(...end));
};

test('empty clicks and selecting existing boxes or freehand edges do not mark saved geometry dirty',()=>{
  const f=fixture();
  gesture(f.manager,f.event,[900,600]);
  assert.equal(f.manager.annotations.length,0);assert.equal(f.changes(),0);
  for(const [shape,hit] of [[rectangle(),[300,250]],[freehand(),[400,200]]]){
    f.manager.setAnnotations([shape]);
    const before=f.manager.getAllAnnotationsJSON();
    gesture(f.manager,f.event,hit);
    assert.equal(f.manager.selectedAnnotation,f.manager.annotations[0]);
    assert.equal(f.manager.getAllAnnotationsJSON(),before);assert.equal(f.changes(),0);
    if(shape.type==='rectangle'){
      const handles=f.manager.getAnnotationHandles(f.manager.annotations[0]);
      for(const handle of [handles.tl,handles.rotate]){
        gesture(f.manager,f.event,[handle.x,handle.y]);
        assert.equal(f.manager.getAllAnnotationsJSON(),before);assert.equal(f.changes(),0);
      }
    }
  }
});

test('creating a box or freehand outline notifies once and preserves original-image vertices',()=>{
  const f=fixture();
  gesture(f.manager,f.event,[200,150],[400,350]);
  assert.equal(f.changes(),1);
  assert.deepEqual(points(f.manager),[[[200,150],[400,150],[400,350],[200,350]]]);
  f.manager.switchMode('freehand');
  f.manager.handleMouseDown(f.event(600,200));
  for(const point of [[700,200],[725,260],[650,300]])f.manager.handleMouseMove(f.event(...point));
  f.manager.handleMouseUp(f.event(650,300));
  assert.equal(f.changes(),2);
  assert.deepEqual(points(f.manager)[1],[[600,200],[700,200],[725,260],[650,300],[600,200]]);
  const before=f.manager.getAllAnnotationsJSON();
  f.manager.handleMouseUp(f.event(650,300));
  assert.equal(f.changes(),2);assert.equal(f.manager.getAllAnnotationsJSON(),before);
  f.manager.loadCoordinates(before);assert.equal(f.manager.getAllAnnotationsJSON(),before);
});

test('a shaky click or tiny stroke under three screen pixels never becomes a shape',()=>{
  const f=fixture();
  // The fixture shows the image at half size, so three screen pixels are six image pixels.
  gesture(f.manager,f.event,[200,200],[205,205]);
  assert.equal(f.manager.annotations.length,0);assert.equal(f.changes(),0);
  gesture(f.manager,f.event,[200,200],[300,204]);
  assert.equal(f.manager.annotations.length,0);
  gesture(f.manager,f.event,[200,200],[206,206]);
  assert.equal(f.manager.annotations.length,1);assert.equal(f.changes(),1);
  f.manager.setAnnotations([]);f.manager.switchMode('freehand');
  f.manager.handleMouseDown(f.event(400,400));
  for(const point of [[403,400],[403,403]])f.manager.handleMouseMove(f.event(...point));
  f.manager.handleMouseUp(f.event(403,403));
  assert.equal(f.manager.annotations.length,0);assert.equal(f.manager.currentAction,'none');
});

test('a corner beside the image edge resizes instead of deleting the shape',()=>{
  const f=fixture();f.manager.setAnnotations([{type:'rectangle',cx:1100,cy:100,width:200,height:200,rotation:0}]);
  gesture(f.manager,f.event,[1100,100]);
  const handles=f.manager.getAnnotationHandles(f.manager.annotations[0]);
  // In the corner of the image the delete handle is clamped ten screen pixels from each edge, so its 11px click
  // area overlaps the top-right corner handle.
  assert.ok(f.manager.isPointNearHandle({x:1195,y:5},handles.tr)&&f.manager.isPointNearHandle({x:1195,y:5},handles.delete,11));
  f.manager.handleMouseDown(f.event(1195,5));
  assert.equal(f.manager.currentAction,'resize');assert.equal(f.manager.annotations.length,1);
  f.manager.handleMouseUp(f.event(1195,5));
  const moved=f.manager.annotations[0];f.manager.selectedAnnotation=moved;
  const after=f.manager.getAnnotationHandles(moved);
  gesture(f.manager,f.event,[after.delete.x,after.delete.y]);
  assert.equal(f.manager.annotations.length,0);
});

test('box movement, fixed-corner resize, rotation and deletion each notify once with known coordinates',()=>{
  const f=fixture();f.manager.setAnnotations([rectangle()]);
  gesture(f.manager,f.event,[300,250],[400,300]);
  assert.equal(f.changes(),1);
  assert.deepEqual(points(f.manager)[0],[[300,250],[500,250],[500,350],[300,350]]);
  const corner=f.manager.getAnnotationHandles(f.manager.annotations[0]).tl;
  gesture(f.manager,f.event,[corner.x,corner.y],[350,275]);
  assert.equal(f.changes(),2);
  assert.deepEqual(points(f.manager)[0],[[350,275],[500,275],[500,350],[350,350]]);
  const rotate=f.manager.getAnnotationHandles(f.manager.annotations[0]).rotate;
  gesture(f.manager,f.event,[rotate.x,rotate.y],[625,312.5]);
  assert.equal(f.changes(),3);
  assert.deepEqual(points(f.manager)[0],[[462.5,237.5],[462.5,387.5],[387.5,387.5],[387.5,237.5]]);
  geometry.validate(JSON.parse(f.manager.getAllAnnotationsJSON()),1200,800);
  f.listeners.keydown({key:'Delete',preventDefault:()=>{}});
  assert.equal(f.changes(),4);assert.equal(f.manager.annotations.length,0);
});

test('freehand selection stays on its edge and movement/deletion retain every sampled point',()=>{
  const f=fixture();f.manager.setAnnotations([freehand()]);
  gesture(f.manager,f.event,[400,300]);
  assert.equal(f.manager.selectedAnnotation,null);assert.equal(f.changes(),0);
  assert.equal(f.manager.annotations.length,1);
  gesture(f.manager,f.event,[400,200],[450,300]);
  assert.equal(f.changes(),1);
  assert.deepEqual(points(f.manager)[0],[[350,300],[550,300],[550,500],[350,500],[350,300]]);
  const handle=f.manager.getFreehandDeleteHandle(f.manager.annotations[0]);
  gesture(f.manager,f.event,[handle.x+1,handle.y+1]);
  assert.equal(f.changes(),2);assert.equal(f.manager.annotations.length,0);
});

test('live freehand closing-edge feedback renders without adding or moving research vertices',()=>{
  const f=fixture();f.manager.switchMode('freehand');
  f.manager.handleMouseDown(f.event(200,200));
  f.manager.handleMouseMove(f.event(400,200));
  f.manager.handleMouseMove(f.event(400,350));
  const before=JSON.stringify(f.manager.freehandPoints);
  f.lines.length=0;f.manager.drawCanvas();
  assert.equal(JSON.stringify(f.manager.freehandPoints),before);
  assert.equal(f.manager.freehandPoints.length,3);
  assert.ok(f.lines.some(([x,y])=>x===200&&y===200),'the closing edge should be visible before pointer release');
  assert.equal(f.manager.annotations.length,0);assert.equal(f.changes(),0);
  f.manager.handleMouseUp(f.event(400,350));
  assert.deepEqual(points(f.manager)[0],[[200,200],[400,200],[400,350],[200,200]]);
  assert.equal(f.changes(),1);
});

test('hover feedback distinguishes editing targets without changing saved coordinates',()=>{
  const f=fixture();f.manager.setAnnotations([rectangle()]);
  const before=f.manager.getAllAnnotationsJSON();
  f.manager.handleMouseMove(f.event(900,600));
  const drawingCursor=f.canvas.style.cursor;
  f.manager.handleMouseMove(f.event(300,250));
  const moveCursor=f.canvas.style.cursor;
  gesture(f.manager,f.event,[300,250]);
  const handle=f.manager.getAnnotationHandles(f.manager.annotations[0]).tl;
  f.manager.handleMouseMove(f.event(handle.x,handle.y));
  const resizeCursor=f.canvas.style.cursor;
  const rotate=f.manager.getAnnotationHandles(f.manager.annotations[0]).rotate;
  f.manager.handleMouseMove(f.event(rotate.x,rotate.y));
  const rotateCursor=f.canvas.style.cursor,rotateHint=f.canvas.title;
  assert.equal(typeof drawingCursor,'string');assert.notEqual(drawingCursor,moveCursor);
  assert.match(resizeCursor,/resize/);
  // The rotation handle shows a clockwise-arrow cursor, falling back to grab.
  assert.match(rotateCursor,/^url\("data:image\/svg\+xml,.+"\) 12 12, grab$/);assert.equal(rotateHint,'Drag to rotate.');
  assert.equal(f.manager.currentAction,'none');assert.equal(f.changes(),0);
  assert.equal(f.manager.getAllAnnotationsJSON(),before);
});

test('pointer cancellation restores exact box and freehand geometry without reporting a saved change',()=>{
  for(const kind of ['move','resize','rotate','freehand']){
    const f=fixture();f.manager.setAnnotations([kind==='freehand'?freehand():rectangle()]);
    const before=f.manager.getAllAnnotationsJSON();
    let start=kind==='freehand'?[400,200]:[300,250];
    if(kind==='resize'||kind==='rotate'){
      gesture(f.manager,f.event,[300,250]);
      const handle=f.manager.getAnnotationHandles(f.manager.annotations[0])[kind==='resize'?'tl':'rotate'];
      start=[handle.x,handle.y];
    }
    f.manager.handleMouseDown(f.event(...start));
    f.manager.handleMouseMove(f.event(450,350));
    assert.notEqual(JSON.stringify(f.manager.annotations),JSON.stringify([kind==='freehand'?freehand():rectangle()]));
    f.listeners.pointercancel();
    assert.equal(f.manager.currentAction,'none');assert.equal(f.changes(),0);
    assert.equal(f.manager.getAllAnnotationsJSON(),before,`${kind} cancellation must preserve stored vertices`);
    assert.equal(f.manager.originalAnnotation,null);
  }
});

test('custom annotation contours are held apart from the term, work while term tools are locked and are dropped on a tool change',()=>{
  const f=fixture();f.manager.setAnnotations([rectangle()]);
  const offered=[];f.manager.onCustomShape=shape=>offered.push(shape);
  const before=f.manager.getAllAnnotationsJSON();
  const trace=(m,from)=>{m.handleMouseDown(f.event(...from));for(const p of [[700,150],[700,350],[600,350]])m.handleMouseMove(f.event(...p));m.handleMouseUp(f.event(600,350));};
  f.manager.switchMode('custom');
  // Locked term tools (review, last term saved) still allow custom annotation when the page permits it.
  f.manager.readOnly=true;f.manager.customAllowed=true;
  trace(f.manager,[600,150]);
  assert.equal(offered.length,1);assert.equal(offered[0].type,'freehand');
  assert.equal(f.manager.pendingCustom,offered[0]);
  assert.equal(f.manager.getAllAnnotationsJSON(),before,'The term answer is unchanged');assert.equal(f.changes(),0);
  const saved=JSON.parse(f.manager.getCustomCoordinatesJSON(f.manager.pendingCustom));
  assert.equal(saved.annotations.length,1);assert.equal(saved.annotations[0].type,'freehand');
  geometry.validate(saved,1200,800);
  // A second contour waits until the first is named or cancelled.
  trace(f.manager,[100,100]);assert.equal(offered.length,1);
  f.manager.switchMode('rectangle');
  assert.equal(f.manager.pendingCustom,null);assert.equal(offered.at(-1),null);
  // Without the page's permission, custom drawing is locked like the term tools.
  f.manager.switchMode('custom');f.manager.customAllowed=false;
  trace(f.manager,[600,150]);assert.equal(offered.length,2);assert.equal(f.manager.pendingCustom,null);
});

