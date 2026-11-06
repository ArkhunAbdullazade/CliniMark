const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const geometry=require('../../src/CliniMark.Presentation/wwwroot/js/annotationGeometry.js');

const plain=value=>JSON.parse(JSON.stringify(value));
const near=(actual,expected)=>assert.ok(Math.abs(actual-expected)<1e-6,`${actual} differs from ${expected}`);

// The page shows the 1200x800 image at 600x400 from (80,120), so one screen pixel is two image pixels.
function fixture(){
  const drawn=[];
  const ctx=new Proxy({drawImage:(...args)=>drawn.push(args)},{get:(target,key)=>target[key]??(()=>{})});
  function element(tag){
    const listeners={},captures=new Set();
    return{tag,style:{},children:[],listeners,width:0,height:0,parentElement:null,attributes:{},className:'',
      classList:{toggle(){},add(){},remove(){}},
      appendChild(child){this.children.push(child);child.parentElement=this;return child;},
      removeChild(child){this.children.splice(this.children.indexOf(child),1);child.parentElement=null;},
      remove(){this.parentElement?.removeChild(this);},
      addEventListener(name,callback){(listeners[name]??=[]).push(callback);},
      emit(name,event={}){for(const callback of listeners[name]??[])callback(event);},
      setAttribute(name,value){this.attributes[name]=String(value);},getContext:()=>ctx,focus(){},
      get offsetWidth(){return 30;},get offsetHeight(){return 100;},
      setPointerCapture:id=>captures.add(id),hasPointerCapture:id=>captures.has(id),releasePointerCapture:id=>captures.delete(id)};
  }
  const rect={left:80,top:120,width:600,height:400,right:680,bottom:520};
  const canvas=element('canvas');canvas.getBoundingClientRect=()=>rect;canvas.dispatchEvent=()=>{};
  const sourceImage={src:'fixture.svg',currentSrc:'fixture.svg',complete:true,naturalWidth:1200,naturalHeight:800,addEventListener:()=>{}};
  const column={clientWidth:608};
  const viewport={clientWidth:608,clientHeight:408};
  const imageContainer={style:{},closest:selector=>selector==='.image-viewport'?viewport:column,
    getBoundingClientRect:()=>rect};
  const container=element('div');container.parentElement=imageContainer;container.closest=()=>column;
  const elements={mainCanvas:canvas,sourceImage,canvasContainer:container,'image-container':imageContainer};
  const document={getElementById:id=>elements[id],createElement:element,body:element('body'),addEventListener:()=>{},removeEventListener:()=>{}};
  const window={addEventListener:()=>{},innerWidth:1400,innerHeight:900,devicePixelRatio:1};
  vm.runInNewContext(fs.readFileSync(path.resolve(__dirname,'../../src/CliniMark.Presentation/wwwroot/js/canvas.js'),'utf8'),
    {window,document,AnnotationGeometry:geometry,structuredClone,CustomEvent:function(){},console});
  const manager=new window.CanvasManager('mainCanvas','sourceImage','canvasContainer');
  manager.setAnnotations([{type:'rectangle',cx:300,cy:250,width:200,height:100,rotation:0}]);
  const stored=manager.getAllAnnotationsJSON();let changes=0;manager.onAnnotationsChange=()=>{changes++;};
  const opened=[];
  const openZoomView=manager.openZoomView.bind(manager);
  manager.openZoomView=(region,box)=>opened.push({region,box});
  manager.switchMode('zoom');
  const emit=(name,x,y,pointerId=3)=>canvas.emit(name,{clientX:rect.left+x,clientY:rect.top+y,pointerId,button:0,preventDefault(){}});
  const drag=(from,to)=>{emit('pointerdown',...from);emit('pointermove',...to);emit('pointerup',...to);};
  return{manager,canvas,container,imageContainer,element,emit,drag,opened,drawn,openZoomView,sourceImage,changes:()=>changes,stored};
}

function untouched(f){
  assert.equal(f.manager.getAllAnnotationsJSON(),f.stored);
  assert.equal(f.changes(),0);
  assert.equal(f.manager.selectedAnnotation,null);
  assert.equal(f.manager.currentAction,'none');
}

test('dragging a box opens exactly that area in image pixels and leaves the shapes alone',()=>{
  const f=fixture();
  f.emit('pointerdown',250,150);
  f.emit('pointermove',100,50);
  const box=f.container.children[0];
  assert.equal(box.className,'zoom-selection');
  assert.deepEqual(box.style,{left:'100px',top:'50px',width:'150px',height:'100px'});
  f.emit('pointerup',100,50);
  assert.equal(f.opened.length,1);
  assert.deepEqual(plain(f.opened[0].region),{x:200,y:100,width:300,height:200});
  assert.equal(f.opened[0].box,box);
  assert.equal(f.manager.zoomSelection,null);
  untouched(f);
});

test('a click enlarges a square around the point, kept inside the image',()=>{
  const f=fixture();
  f.drag([300,200],[302,201]);
  const side=400/3*2;
  near(f.opened[0].region.width,side);near(f.opened[0].region.height,side);
  near(f.opened[0].region.x,(302-400/6)*2);near(f.opened[0].region.y,(201-400/6)*2);
  f.drag([3,4],[3,4]);
  assert.deepEqual({x:f.opened[1].region.x,y:f.opened[1].region.y},{x:0,y:0});
  untouched(f);
});

test('the box stops at the image edges and works while saved terms are only being reviewed',()=>{
  const f=fixture();
  f.manager.readOnly=true;
  f.drag([500,300],[900,700]);
  assert.deepEqual(plain(f.opened[0].region),{x:1000,y:600,width:200,height:200});
  untouched(f);
});

test('cancelled gestures and mode changes remove the box without opening anything',()=>{
  const f=fixture();
  f.emit('pointerdown',100,100);f.emit('pointermove',200,200);
  f.canvas.emit('pointercancel',{});
  assert.equal(f.container.children.length,0);
  f.emit('pointerdown',100,100);f.emit('pointermove',200,200);
  f.manager.switchMode('rectangle');
  assert.equal(f.container.children.length,0);
  assert.equal(f.manager.zoomSelection,null);
  assert.equal(f.opened.length,0);
  f.emit('pointerdown',350,250);f.emit('pointermove',450,300);f.emit('pointerup',450,300);
  assert.equal(f.manager.annotations.length,2,'Rectangle mode draws again after leaving zoom');
});

test('a second finger cannot start or end another box mid-drag',()=>{
  const f=fixture();
  f.emit('pointerdown',100,100,3);
  f.emit('pointerdown',300,300,4);
  f.emit('pointerup',300,300,4);
  assert.equal(f.opened.length,0);
  assert.equal(f.container.children.length,1);
  f.emit('pointerup',200,150,3);
  assert.deepEqual(plain(f.opened[0].region),{x:200,y:200,width:200,height:100});
});

function stubDialog(f){
  const view=f.element('canvas');
  // Only the canvas exists: the dialog shows no title or size note.
  const dialog={open:false,showModal(){this.open=true;},querySelector:selector=>selector==='canvas'?view:null};
  f.manager.zoomDialog=dialog;
  return{dialog,view};
}

test('the enlarged view fills the dialog from the full-resolution image',()=>{
  const f=fixture(),{dialog,view}=stubDialog(f);
  f.openZoomView({x:200,y:100,width:300,height:200},f.element('div'));
  // Height is the limit: (900 * 0.9 - 88) / 200 = 3.61 CSS pixels per image pixel, 7.22x the page's 0.5.
  assert.equal(view.style.width,'1083px');assert.equal(view.style.height,'722px');
  assert.deepEqual([view.width,view.height],[1083,722]);
  assert.deepEqual(f.drawn.at(-1),[f.sourceImage,200,100,300,200,0,0,1083,722]);
  assert.equal(dialog.open,true);
});

test('small areas stop at eight times their size on the page',()=>{
  const f=fixture(),{view}=stubDialog(f);
  f.openZoomView({x:10,y:10,width:20,height:20},f.element('div'));
  assert.equal(view.style.width,'80px');assert.equal(view.style.height,'80px');
});

test('stopping interaction cancels a box, closes the view and disables every tool',()=>{
  const f=fixture();
  const tools=f.manager.createTools(f.imageContainer);
  f.manager.toolsContainer.querySelectorAll=()=>Object.values(tools);
  f.manager.switchMode('zoom');
  let closed=false;
  f.manager.zoomDialog={open:true,close(){closed=true;this.open=false;}};
  f.emit('pointerdown',100,100);f.emit('pointermove',200,200);
  f.manager.stopInteraction();
  assert.equal(closed,true);
  assert.equal(f.container.children.length,0);
  assert.equal(f.manager.currentMode,'rectangle');
  assert.equal(f.manager.readOnly,true);
  assert.ok(Object.values(tools).every(button=>button.disabled));
  f.emit('pointerdown',100,100);f.emit('pointermove',200,200);f.emit('pointerup',200,200);
  untouched(f);
});
