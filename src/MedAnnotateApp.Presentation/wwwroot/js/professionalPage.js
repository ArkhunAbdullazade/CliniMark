"use strict";

document.addEventListener("DOMContentLoaded", () => {
  let dirty = false, saving = false, initialized = false, navigating = false, released = false;
  const status = document.getElementById("annotation-status");
  let baseStatus = {message: status?.textContent || "", state: status?.dataset.state || "ready"};
  let inspecting = null;
  const inspectionHints = {
    zoom: "Zoom to Area: drag a box over the image, or click a point, to see it enlarged. Select Boundary Box or Freehand Contour to draw again.",
    custom: "Custom annotation: draw a closed contour around any finding that is not highlighted in the text, then name and save it. Select Boundary Box or Freehand Contour to return to the current term."
  };
  // The term in the current-term prompt is shown in bold red, so the specialist sees at once what to outline.
  const currentTermPrompt = /^(Annotate the current term: )(.+)(\. Draw a rectangular boundary box or a free contour\. Zoom, move, resize, rotate, and delete as needed\.)$/s;
  const renderStatus = () => {
    if (!status) return;
    const hint = inspecting && !["error", "saving"].includes(baseStatus.state);
    const message = hint ? inspectionHints[inspecting] : baseStatus.message;
    const parts = hint ? null : currentTermPrompt.exec(message);
    if (parts) {
      const term = document.createElement("strong");
      term.className = "status-term";
      term.textContent = parts[2];
      status.replaceChildren(parts[1], term, parts[3]);
    } else status.textContent = message;
    status.dataset.state = hint ? "inspection" : baseStatus.state;
  };
  const showStatus = (message, state = "ready") => {
    baseStatus = {message, state};
    renderStatus();
  };
  const inspectionChanged = (active, kind = "zoom") => {
    if (active && !inspecting && status) baseStatus = {message: status.textContent, state: status.dataset.state || "ready"};
    inspecting = active ? kind : null;
    renderStatus();
  };
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const headers = { "Content-Type": "application/json", "X-CSRF-TOKEN": token || "" };
  const request = async (url, options = {}) => {
    // Repeats the request while the app restarts (requestRetry.js); plain fetch where that script is absent.
    const send = window.MedAnnotateRetry?.fetchWithRetry || ((u, o) => fetch(u, o));
    const response = await send(url, { ...options, headers: { ...headers, ...options.headers } },
      { onRetry: () => showStatus("The connection to the server was lost. Retrying automatically; keep this page open.", "saving") });
    let result;
    try { result = await response.json(); } catch { throw new Error("Your session may have expired. Keep this page open and sign in again in another tab before retrying."); }
    if (!response.ok || result.success === false)
      throw Object.assign(new Error(result.message || "The request could not be saved. Your work is still on this page."), {status: response.status});
    return result;
  };
  window.addEventListener("beforeunload", e => {
    if (!navigating && (dirty || saving || custom?.hasPending())) { e.preventDefault(); e.returnValue = ""; }
  });
  for (const id of ["end-button", "end-button-center"]) {
    document.getElementById(id)?.addEventListener("click", async () => {
      if (saving) return;
      if (dirty && !confirm("This term has unsaved annotations or comments. Discard them and log out?")) return;
      try {
        await request("/Identity/Logout", {method: "POST", body: JSON.stringify({isAnnotationStarted:window.hasAnnotatedCurrentImage === true,medDataId:window.annotatedMedData?.id})});
        dirty = false; navigating = true;
        location.href = "/Identity/Login";
      } catch (error) { showStatus(error.message, "error"); }
    });
  }
  if (!window.annotatedMedData) return;
  const medData = window.annotatedMedData;
  const assignmentId = medData.id;
  const next = document.getElementById("next-button");
  const skip = document.getElementById("skip-button");
  const absent = document.getElementById("not-present-button");
  const comments = document.getElementById("comments");
  // Saved with the term like the comment, but kept separate so technical issues never mix with clinical notes. It can
  // also be sent at once (problemReport.js), so it never depends on the image or the tools working.
  const problem = document.getElementById("technical-problem");
  // Failed saves and loads are logged for the study team automatically (problemReport.js).
  const requestFailed = (action, error) => document.dispatchEvent(new CustomEvent("annotationrequesterror",
    {detail: {action, message: error.message, status: error.status}}));
  const occurrences = [...document.querySelectorAll(".header-text .keyword")];
  // Repeated phrases share one response; progress follows their first appearance in the text.
  const terms = occurrences.filter((term, index) => occurrences.findIndex(other => other.dataset.keyword === term.dataset.keyword) === index);
  const expectedTermCount = Number(document.querySelector(".header-text")?.dataset.keywordCount || 0);
  const manager = new CanvasManager("mainCanvas", "sourceImage", "canvasContainer");
  window.canvasManager = manager;
  manager.onInspectionChange = inspectionChanged;
  if (status?.dataset.state === "error") showStatus(status.textContent, "error");
  const tools = manager.createTools(document.getElementById("image-container"), {custom: true});
  tools.rectButton.addEventListener("click", () => manager.switchMode("rectangle"));
  tools.freehandButton.addEventListener("click", () => manager.switchMode("freehand"));
  tools.customButton?.addEventListener("click", () => manager.switchMode("custom"));
  tools.zoomButton.addEventListener("click", () => manager.switchMode("zoom"));
  // Findings beyond the term list, saved on their own (customAnnotations.js); absent in some tests.
  const custom = window.setupCustomAnnotations?.({manager, imageId: assignmentId, request, showStatus, requestFailed});
  // The specialist confirms or corrects the image's labels (metadataCheck.js); required before Next Image.
  const labels = window.setupMetadataCheck?.({imageId: assignmentId, request, requestFailed,
    onConflict: error => lockLost(error),
    onChange: () => {
      buttons();
      if (initialized && currentIndex === -1 && reviewIndex === -1 && !saving) showStatus(prompt());
    }});
  let customRecords = [], labelRecords = [];
  let history = [], currentIndex = -1, reviewIndex = -1;
  let historyLoaded = false, historyLoading = false;
  let started = Date.now(), firstDraw = 0;
  window.setAnnotationStartTime = () => { if (!firstDraw) firstDraw = Date.now(); };
  const termName = i => terms[i]?.dataset.keyword || terms[i]?.textContent.trim();
  const saved = i => history.find(row => row.extractedKeyword === termName(i));
  const pendingIndex = () => terms.findIndex((_, i) => !saved(i));
  function buttons() {
    const complete = initialized && currentIndex === -1;
    next.textContent = complete ? "Next Image" : "Save and Next Term";
    next.disabled = released || saving || !initialized || !manager.ready || (complete ? labels?.complete() === false : reviewIndex !== -1 || !manager.annotations.length || manager.currentAction !== "none");
    skip.disabled = absent.disabled = released || saving || !initialized || !manager.ready || complete || reviewIndex !== -1 || manager.annotations.length > 0;
    comments.disabled = released || !initialized || !manager.ready || saving || reviewIndex !== -1 || complete;
    problem.disabled = saving;
    manager.readOnly = released || !initialized || !manager.ready || saving || reviewIndex !== -1 || complete;
    // Custom annotations can be added whenever the image is theirs, including while reviewing or after the last term.
    manager.customAllowed = !released && initialized && manager.ready && !saving;
    labels?.setEnabled(!released && initialized && manager.ready && !saving);
    occurrences.forEach(term => {
      const i = terms.findIndex(first => first.dataset.keyword === term.dataset.keyword);
      term.classList.remove("current_keyword", "annotated_keyword", "not_annotated_keyword", "skipped_keyword");
      const row = saved(i);
      term.classList.add(row ? row.pressedButton === "Save and Next Term" ? "annotated_keyword" : "skipped_keyword" : i === currentIndex ? "current_keyword" : "not_annotated_keyword");
      term.setAttribute("aria-pressed", String(i === (reviewIndex === -1 ? currentIndex : reviewIndex)));
    });
  }
  const prompt = () => currentIndex === -1 ? (labels?.complete() === false ? "All terms saved. Confirm the image modality, speciality and body region, then select Next Image." : "All terms saved. Select Next Image to continue. You can select a saved term to review it.") : `Annotate the current term: ${termName(currentIndex)}. Draw a rectangular boundary box or a free contour. Zoom, move, resize, rotate, and delete as needed.`;
  function showCurrent() {
    reviewIndex = -1;
    manager.resetAnnotation(); comments.value = ""; dirty = false;
    currentIndex = pendingIndex(); started = Date.now(); firstDraw = 0;
    buttons();
    showStatus(prompt());
    const current = terms[currentIndex], textBox = current?.closest?.(".header-text");
    if (textBox) {
      const phrase = current.getBoundingClientRect(), box = textBox.getBoundingClientRect();
      if (phrase.top < box.top + 4) textBox.scrollTop -= box.top + 4 - phrase.top;
      else if (phrase.bottom > box.bottom - 4) textBox.scrollTop += phrase.bottom - box.bottom + 4;
    }
  }
  async function initialize() {
    if (initialized || historyLoading) return;
    if (!terms.length || terms.length !== expectedTermCount) {
      showStatus(`Some annotation terms could not be found separately in the description, so image #${assignmentId} can't be annotated. No responses have been saved.`, "error");
      buttons();
      return;
    }
    if (!historyLoaded) {
      historyLoading = true;
      try {
        const result = await request(`/MedData/CurrentAnnotations?medDataId=${encodeURIComponent(assignmentId)}`);
        history = result.annotations || [];
        customRecords = result.customAnnotations || [];
        labelRecords = result.metadataChecks || [];
        historyLoaded = true;
        window.hasAnnotatedCurrentImage = history.length > 0;
      } catch (error) { requestFailed("Loading saved answers", error); showStatus(error.message, "error"); buttons(); return; }
      finally { historyLoading = false; }
    }
    if (!manager.ready) return;
    initialized = true;
    custom?.load(customRecords);
    labels?.load(labelRecords);
    showCurrent();
  }
  manager.canvas.addEventListener("annotationimageready", initialize);
  manager.canvas.addEventListener("annotationimageerror", () => {
    showStatus(status?.textContent || "This image could not be loaded.", "error");
    buttons();
  });
  // Removing every shape and the comment returns the term to its untouched state.
  function draftChanged(message) {
    dirty = manager.annotations.length > 0 || comments.value.trim() !== "";
    showStatus(dirty ? message : prompt(), dirty ? "unsaved" : "ready");
  }
  manager.onAnnotationsChange = () => {
    if (!manager.readOnly && initialized) draftChanged("Unsaved changes. Select Save and Next Term to save this term.");
    buttons();
  };
  comments.addEventListener("input", () => draftChanged(comments.value.trim() ? "Unsaved comment." : "Unsaved changes. Select Save and Next Term to save this term."));
  const watch = window.watchAssignment?.({imageId: assignmentId, busy: () => saving || navigating,
    message: "It is no longer assigned to you, so nothing more can be saved on this page. Answers you already saved are kept.",
    onReleased: () => {
      released = true; dirty = false;
      manager.stopInteraction();
      buttons();
      showStatus("This image was released, so nothing more can be saved on this page. Reload to continue.", "error");
    }});
  occurrences.forEach(term => {
    const i = terms.findIndex(first => first.dataset.keyword === term.dataset.keyword);
    term.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") { event.preventDefault(); term.click(); }
    });
    term.addEventListener("click", () => {
    if (released || saving || !initialized || !manager.ready) return;
    const record = saved(i);
    if (!record && i !== currentIndex) return;
    if (dirty && !confirm("Discard unsaved changes to review another term?")) return;
    if (!record) { showCurrent(); return; }
    manager.resetAnnotation(); dirty = false; reviewIndex = i; comments.value = record.comment || "";
    const reviewNext = currentIndex < 0 ? "All terms are saved. Select Next Image to continue." : "Select the current term to continue.";
    if (record.boxCoordinates) {
      try { manager.loadCoordinates(record.boxCoordinates); showStatus(`Saved annotation for ${termName(i)} (read only). ${reviewNext}`); }
      catch (error) { showStatus(`This saved response has legacy or changed image coordinates. It is preserved and has not been converted. ${error.message}`, "error"); }
    } else showStatus(`Saved response for ${termName(i)}: ${record.pressedButton}. ${reviewNext}`);
    buttons();
    });
  });
  async function save(action) {
    if (released || saving || !initialized || !manager.ready || currentIndex < 0 || reviewIndex !== -1 || assignmentId !== window.annotatedMedData.id) return;
    let coordinates = "";
    try { if (action === "Save and Next Term") coordinates = manager.getAllAnnotationsJSON(); }
    catch (error) { showStatus(error.message, "error"); return; }
    const elapsed = firstDraw ? `(${Math.max(0,firstDraw-started)},${Math.max(0,Date.now()-firstDraw)})` : "(0,0)";
    const payload = {Id:assignmentId,BoxCoordinates:coordinates,ExtractedKeyword:termName(currentIndex),PressedButton:action,Comment:comments.value.trim(),TechnicalProblem:problem.value.trim(),Timestamps:elapsed};
    saving = true; buttons(); showStatus("Saving… keep this page open.", "saving");
    try {
      await request("/MedData/ProcessAnnotatedMedData", {method:"POST",body:JSON.stringify(payload)});
      history.push({extractedKeyword:payload.ExtractedKeyword,boxCoordinates:coordinates,pressedButton:action,comment:payload.Comment,technicalProblem:payload.TechnicalProblem});
      window.hasAnnotatedCurrentImage = true;
      // The problem report went with this answer, so the box starts empty for the next term.
      problem.value = ""; problem.dispatchEvent(new Event("input", {bubbles: true}));
      saving = false; showCurrent();
    } catch (error) { requestFailed(`Saving ${payload.ExtractedKeyword}`, error); saving = false; buttons(); showStatus(error.message, "error"); lockLost(error); }
  }
  // A conflict may mean the image was released, so confirm it now rather than at the next check.
  const lockLost = error => { if (error.status === 409) watch?.check(); };
  next.addEventListener("click", async () => {
    if (released || saving || !initialized || !manager.ready) return;
    if (custom?.hasPending()) { showStatus("Save or cancel the custom annotation before moving on.", "error"); return; }
    if (currentIndex !== -1) { await save("Save and Next Term"); return; }
    saving = true; buttons(); showStatus("Loading the next image…", "saving");
    try {
      await request(`/MedData/NextImage?medDataId=${encodeURIComponent(assignmentId)}`,{method:"PUT"});
      saving = false; dirty = false; navigating = true; location.reload();
    } catch (error) { requestFailed("Moving to the next image", error); saving = false; buttons(); showStatus(error.message,"error"); lockLost(error); }
  });
  skip.addEventListener("click", () => save("Uncertain/Skip"));
  absent.addEventListener("click", () => save("Not Visible/Abstract"));
  buttons();
  initialize();
});
