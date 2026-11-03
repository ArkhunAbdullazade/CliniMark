"use strict";

document.addEventListener("DOMContentLoaded", () => {
  let dirty = false, saving = false, navigating = false, released = false, manager;
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
  const headers = {"Content-Type":"application/json","X-CSRF-TOKEN":token};
  const status = document.getElementById("annotation-status");
  let baseStatus = {message: status?.textContent || "", state: status?.dataset.state || "ready"};
  let inspecting = null;
  const inspectionHints = {
    zoom: "Zoom to Area: drag a box over the image, or click a point, to see it enlarged. Select Boundary Box or Freehand Contour to draw again."
  };
  const renderStatus = () => {
    if (!status) return;
    const hint = inspecting && !["error", "saving"].includes(baseStatus.state);
    status.textContent = hint ? inspectionHints[inspecting] : baseStatus.message;
    status.dataset.state = hint ? "inspection" : baseStatus.state;
  };
  const showStatus = (message, state="ready") => {
    baseStatus = {message, state};
    renderStatus();
  };
  const inspectionChanged = (active, kind = "zoom") => {
    if (active && !inspecting && status) baseStatus = {message: status.textContent, state: status.dataset.state || "ready"};
    inspecting = active ? kind : null;
    renderStatus();
  };
  const hasUnsavedWork=()=>manager ? manager.getAnnotationGroups().length>0 || manager.baseManager.annotations.length>0 || manager.baseManager.currentAction!=="none" : dirty;
  window.addEventListener("beforeunload", e => {if(!navigating && !released && (hasUnsavedWork() || saving)){e.preventDefault();e.returnValue="";}});
  for (const id of ["end-button","end-button-center"]) document.getElementById(id)?.addEventListener("click", async () => {
    if (saving || (hasUnsavedWork() && !confirm("Discard all unsubmitted annotations and log out?"))) return;
    try {
      const response = await fetch("/Identity/Logout",{method:"POST",headers,body:JSON.stringify({isAnnotationStarted:false,medDataId:window.annotatedMedData?.id})});
      if (!response.ok) throw new Error("Logout failed. Your work is still on this page. Please try again.");
      dirty=false;navigating=true; location.href="/Identity/Login";
    } catch (error) { showStatus(error.message,"error"); }
  });
  if (!window.annotatedMedData) return;
  const imageId=window.annotatedMedData.id;
  const submit=document.getElementById("submit-button");
  const list=document.getElementById("annotations-list");
  manager=window.canvasManager=new StudentCanvasManager("mainCanvas","sourceImage","canvasContainer");
  manager.baseManager.onInspectionChange=inspectionChanged;
  if(status?.dataset.state==="error")showStatus(status.textContent,"error");
  const tools=manager.createTools(document.getElementById("image-container"));
  tools.rectButton.addEventListener("click",()=>manager.switchMode("rectangle"));
  tools.freehandButton.addEventListener("click",()=>manager.switchMode("freehand"));
  tools.zoomButton.addEventListener("click",()=>manager.switchMode("zoom"));
  function updateList() {
    list.replaceChildren();
    const groups=manager.getAnnotationGroups();
    submit.disabled=released || saving || !manager.baseManager.ready || groups.length===0;
    if (!groups.length) {
      const empty=document.createElement("p");empty.className="empty-annotations-message";empty.textContent="Draw a shape, enter a label, then save the group. Submit all groups when finished.";list.appendChild(empty);
    }
    groups.forEach((group,index)=>{
      const item=document.createElement("div");item.className="annotation-item";
      const label=document.createElement("span");label.className="annotation-label";label.textContent=`${group.label} (${group.annotations.length} shape${group.annotations.length===1?"":"s"})`;
      const actions=document.createElement("div");actions.className="annotation-actions";
      const edit=document.createElement("button");edit.type="button";edit.textContent="Edit";edit.disabled=saving || released;
      edit.addEventListener("click",()=>{if(!saving && !released)manager.editGroup(index);});
      const remove=document.createElement("button");remove.type="button";remove.textContent="Delete";remove.disabled=saving || released;
      remove.addEventListener("click",()=>{if(!saving && !released && confirm("Delete this annotation group?"))manager.deleteGroup(index);});
      actions.append(edit,remove);item.append(label,actions);list.appendChild(item);
    });
  }
  function updateDraftState() {
    const pending=manager.baseManager.annotations.length>0 || manager.baseManager.currentAction!=="none";
    dirty=hasUnsavedWork();updateList();
    showStatus(pending ? "Unsaved shape changes. Enter a label and save this group before submitting." : dirty ?
      "Groups are held on this page. Select Submit Annotations to save them to the database." :
      "Image ready. Draw a shape, label it and save the group.",dirty ? "unsaved" : "ready");
  }
  manager.onGroupedAnnotationsChange=updateDraftState;
  const original=manager.baseManager.onAnnotationsChange;
  manager.baseManager.onAnnotationsChange=()=>{original();updateDraftState();};
  manager.baseManager.canvas.addEventListener("annotationimageready",()=>{updateList();showStatus("Image ready. Draw a shape, label it and save the group.");});
  manager.baseManager.canvas.addEventListener("annotationimageerror",()=>{
    showStatus(status?.textContent || "This image could not be loaded.","error");
    updateList();
  });
  if(manager.baseManager.ready)showStatus("Image ready. Draw a shape, label it and save the group.");
  const watch=window.watchAssignment?.({imageId,busy:()=>saving || navigating,
    message:"It is no longer assigned to you, so the groups on this page can't be submitted.",
    onReleased:()=>{
      released=true;dirty=false;
      manager.baseManager.stopInteraction();manager.hideTextBox();updateList();
      showStatus("This image was released, so these groups can't be submitted. Reload to continue.","error");
    }});
  submit.addEventListener("click",async()=>{
    if(released || saving || !manager.baseManager.ready || imageId!==window.annotatedMedData.id)return;
    if(manager.baseManager.currentAction!=="none" || manager.baseManager.annotations.length){showStatus("Finish and save or cancel the current group before submitting.","error");return;}
    const groups=manager.getAnnotationGroups();
    if(!groups.length || groups.some(g=>!g.label?.trim())){showStatus("Every group needs a label and at least one complete shape.","error");return;}
    let payload;
    try {
      payload={Annotations:manager.getAllGroupedAnnotationsJSON().map(group=>({Id:imageId,Coordinates:group.annotations,TextualAnnotation:group.label}))};
    } catch(error){showStatus(error.message,"error");return;}
    saving=true;manager.baseManager.readOnly=true;submit.textContent="Submitting…";updateList();showStatus("Saving all groups… keep this page open.","saving");
    try {
      // Repeats the request while the app restarts (requestRetry.js); plain fetch where that script is absent.
      const send=window.MedAnnotateRetry?.fetchWithRetry || ((u,o)=>fetch(u,o));
      const response=await send("/MedData/SubmitStudentAnnotations",{method:"POST",headers,body:JSON.stringify(payload)},
        {onRetry:()=>showStatus("The connection to the server was lost. Retrying automatically; keep this page open.","saving")});
      let result;try{result=await response.json();}catch{throw new Error("Your session may have expired. Sign in in another tab and retry; keep this page open.");}
      if(!response.ok || !result.success)throw Object.assign(new Error(result.message || "Save failed. All groups are still on this page."),{status:response.status});
      dirty=false;saving=false;navigating=true;location.href=result.redirectUrl || "/Home/Student";
    } catch(error){
      // Logged for the study team automatically (problemReport.js).
      document.dispatchEvent(new CustomEvent("annotationrequesterror",{detail:{action:"Submitting annotations",message:error.message,status:error.status}}));
      saving=false;manager.baseManager.readOnly=false;submit.textContent="Submit Annotations";updateList();showStatus(error.message,"error");
      // A conflict may mean the image was released, so confirm it now rather than at the next check.
      if(error.status===409)watch?.check();
    }
  });
  updateList();
});
