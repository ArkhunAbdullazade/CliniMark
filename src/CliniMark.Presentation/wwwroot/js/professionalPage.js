"use strict";

document.addEventListener("DOMContentLoaded", () => {
  let dirty = false, saving = false, initialized = false, navigating = false, released = false;
  // Custom annotations (customAnnotations.js), set up only once there is an image. Declared here because the
  // leave-page check below runs on every page, including the end-of-series page that has no image.
  let custom = null;
  const status = document.getElementById("annotation-status");
  let baseStatus = {message: status?.textContent || "", state: status?.dataset.state || "ready"};
  let inspecting = null;
  const inspectionHints = {
    zoom: "Zoom to Area: drag a box over the image, or click a point, to see it enlarged. Select Boundary Box or Freehand Contour to draw again.",
    custom: "Custom annotation: draw a closed contour around any finding that is not highlighted in the text, then name and save it. Select Boundary Box or Freehand Contour to return to the current term."
  };
  // The term in the current-term prompt is shown in bold red, so the specialist sees at once what to outline.
  const currentTermPrompt = /^(.*?Annotate the current term: )(.+)(\. Draw a rectangular boundary box or a free contour\. Zoom, move, resize, rotate, and delete as needed\.)$/s;
  // A saved term opened to change it (selected in the description).
  const changeTermPrompt = /^(Changing your saved answer for )(.+?)(\. (?:Its saved shapes are shown|It was saved as ).*)$/s;
  // So is the reason Next Image is still locked once every term is saved.
  const confirmLabels = "Confirm the image modality, speciality and body region";
  const emphasised = text => {
    const element = document.createElement("strong");
    element.className = "status-term";
    element.textContent = text;
    return element;
  };
  const renderStatus = () => {
    if (!status) return;
    const hint = inspecting && !["error", "saving"].includes(baseStatus.state);
    const message = hint ? inspectionHints[inspecting] : baseStatus.message;
    const parts = hint ? null : currentTermPrompt.exec(message) || changeTermPrompt.exec(message);
    if (parts) status.replaceChildren(parts[1], emphasised(parts[2]), parts[3]);
    else if (!hint && message.includes(confirmLabels)) {
      const [before, after] = message.split(confirmLabels);
      status.replaceChildren(before, emphasised(confirmLabels), after);
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
    const send = window.CliniMarkRetry?.fetchWithRetry || ((u, o) => fetch(u, o));
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
        // The time on the open image is sent while the session still exists (imageTimer.js).
        await window.imageTimer?.flush();
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
  const skipImage = document.getElementById("skip-image-button");
  // Closes a saved term opened to change it, keeping it as it was.
  const cancel = document.getElementById("cancel-change-button");
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
  custom = window.setupCustomAnnotations?.({manager, imageId: assignmentId, request, showStatus, requestFailed});
  // The specialist confirms or corrects the image's labels (metadataCheck.js); required before Next Image.
  const labels = window.setupMetadataCheck?.({imageId: assignmentId, request, requestFailed,
    onConflict: error => lockLost(error),
    onChange: () => {
      buttons();
      if (!initialized || currentIndex !== -1 || saving) return;
      if (reviewIndex === -1) showStatus(prompt());
      else if (!dirty) showStatus(changePrompt());
    }});
  let customRecords = [], labelRecords = [];
  // reviewIndex is the saved term opened to change it (-1: none). Its saved comment, and whether its shapes were touched.
  let history = [], currentIndex = -1, reviewIndex = -1;
  let opened = null, shapesTouched = false;
  let historyLoaded = false, historyLoading = false;
  let started = Date.now(), firstDraw = 0;
  window.setAnnotationStartTime = () => { if (!firstDraw) firstDraw = Date.now(); };
  const termName = i => terms[i]?.dataset.keyword || terms[i]?.textContent.trim();
  const saved = i => history.find(row => row.extractedKeyword === termName(i));
  const pendingIndex = () => terms.findIndex((_, i) => !saved(i));
  // Saved terms, listed under the note buttons in the order of the text. Each row opens its term again to change it,
  // like selecting it in the text. Rows are made once and only shown, hidden and relabelled, so focus stays put.
  const savedSection = document.getElementById("saved-terms");
  const savedCount = document.getElementById("saved-count");
  const savedItems = document.getElementById("saved-items");
  const ANSWERS = {
    "Save and Next Term": {kind: "drawn", label: "Outlined", icon: "bi-check-circle-fill"},
    "Uncertain/Skip": {kind: "uncertain", label: "Uncertain", icon: "bi-question-circle-fill"},
    "Not Visible/Abstract": {kind: "absent", label: "Not visible", icon: "bi-eye-slash-fill"}
  };
  const savedRows = !savedItems ? [] : terms.map((_, i) => {
    const item = document.createElement("li");
    const button = document.createElement("button");
    const icon = document.createElement("i"), name = document.createElement("span"), state = document.createElement("span");
    const edit = document.createElement("span"), pencil = document.createElement("i"), editLabel = document.createElement("span");
    button.type = "button"; button.className = "saved-term";
    icon.setAttribute("aria-hidden", "true");
    name.className = "saved-term-name"; name.textContent = terms[i].textContent.trim() || termName(i);
    state.className = "saved-term-state";
    edit.className = "saved-term-edit"; pencil.className = "bi bi-pencil"; pencil.setAttribute("aria-hidden", "true");
    edit.append(pencil, editLabel);
    button.append(icon, name, state, edit);
    button.addEventListener("click", () => selectTerm(i));
    item.hidden = true;
    item.append(button);
    savedItems.append(item);
    return {item, button, icon, state, editLabel};
  });
  function showSavedList() {
    let count = 0;
    savedRows.forEach((row, i) => {
      const record = saved(i);
      row.item.hidden = !record;
      if (!record) return;
      count++;
      const answer = ANSWERS[record.pressedButton] || {kind: "uncertain", label: record.pressedButton, icon: "bi-circle"};
      const open = i === reviewIndex;
      row.button.dataset.kind = answer.kind;
      row.button.classList.toggle("open", open);
      row.button.setAttribute("aria-current", String(open));
      row.button.disabled = released || saving || !initialized || !manager.ready;
      row.button.dataset.description = open
        ? "This term is open: change it, then select Save changes, or Cancel to leave it as it was."
        : `Saved as ${answer.label}. Select to see your answer and change it if needed.`;
      row.icon.className = `bi ${answer.icon}`;
      row.state.textContent = answer.label;
      row.editLabel.textContent = open ? "Editing" : "Edit";
    });
    if (savedSection) savedSection.hidden = count === 0;
    if (savedCount) savedCount.textContent = `${count} of ${terms.length}`;
  }
  // A saved term that is open has changes when its shapes were touched or its comment differs from the saved one.
  const changed = () => reviewIndex !== -1 && opened !== null && (shapesTouched || comments.value.trim() !== opened.comment);
  function buttons() {
    const changing = initialized && reviewIndex !== -1;
    const complete = initialized && currentIndex === -1 && !changing;
    next.textContent = changing ? "Save changes" : complete ? "Next Image" : "Save and Next Term";
    next.disabled = released || saving || !initialized || !manager.ready || (complete ? labels?.complete() === false
      : !manager.annotations.length || manager.currentAction !== "none" || (changing && !changed()));
    skip.disabled = absent.disabled = released || saving || !initialized || !manager.ready || complete || manager.annotations.length > 0;
    if (cancel) { cancel.hidden = !changing; cancel.disabled = saving; }
    // Also while the image is failing to load: a broken case must not trap the specialist.
    skipImage.disabled = released || saving;
    comments.disabled = released || !initialized || !manager.ready || saving || complete;
    problem.disabled = saving;
    manager.readOnly = released || !initialized || !manager.ready || saving || complete;
    // Custom annotations can be added whenever the image is theirs, including while reviewing or after the last term.
    manager.customAllowed = !released && initialized && manager.ready && !saving;
    labels?.setEnabled(!released && initialized && manager.ready && !saving);
    labels?.setAttention(complete && labels.complete() === false);
    occurrences.forEach(term => {
      const i = terms.findIndex(first => first.dataset.keyword === term.dataset.keyword);
      term.classList.remove("current_keyword", "annotated_keyword", "not_annotated_keyword", "skipped_keyword");
      const row = saved(i);
      term.classList.add(row ? row.pressedButton === "Save and Next Term" ? "annotated_keyword" : "skipped_keyword" : i === currentIndex ? "current_keyword" : "not_annotated_keyword");
      term.classList.toggle("changing_keyword", i === reviewIndex);
      // Lifts on hover only where selecting does something: a saved term not already open, or the current term while
      // a saved one is open (it goes back).
      term.classList.toggle("keyword-clickable", !released && initialized && manager.ready && !saving
        && (row ? i !== reviewIndex : i === currentIndex && reviewIndex !== -1));
      term.setAttribute("aria-pressed", String(i === (reviewIndex === -1 ? currentIndex : reviewIndex)));
      // Saved terms open again to be changed (hoverDescriptions.js shows this after a pause).
      if (row) term.dataset.description = "Select to see your saved answer for this term and change it if needed.";
      else delete term.dataset.description;
    });
    showSavedList();
  }
  const prompt = () => currentIndex === -1 ? (labels?.complete() === false ? `All terms saved. ${confirmLabels}, then select Next Image.` : "All terms saved. Select Next Image to continue. You can still select a saved term in the text to change it.") : `Annotate the current term: ${termName(currentIndex)}. Draw a rectangular boundary box or a free contour. Zoom, move, resize, rotate, and delete as needed.`;
  const reviewNext = () => "Select Save changes to keep your changes, or Cancel to leave it as it was.";
  const changePrompt = () => {
    const record = saved(reviewIndex);
    const drawn = record?.pressedButton === "Save and Next Term";
    return `Changing your saved answer for ${termName(reviewIndex)}. ${drawn
      ? "Its saved shapes are shown: move, resize, delete or add shapes, or edit the comment. To answer Uncertain/Skip or Not Visible/Abstract instead, delete its shapes first."
      : `It was saved as ${record?.pressedButton}. Draw shapes to annotate it, choose the other answer, or edit the comment.`} ${reviewNext()}`;
  };
  function showCurrent() {
    reviewIndex = -1; opened = null; shapesTouched = false;
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
    if (reviewIndex !== -1) {
      // A comment edit alone can make Save changes available.
      dirty = changed();
      showStatus(dirty ? `Unsaved changes to ${termName(reviewIndex)}. ${reviewNext()}` : changePrompt(), dirty ? "unsaved" : "ready");
      buttons();
      return;
    }
    dirty = manager.annotations.length > 0 || comments.value.trim() !== "";
    showStatus(dirty ? message : prompt(), dirty ? "unsaved" : "ready");
  }
  manager.onAnnotationsChange = () => {
    // Loading saved shapes doesn't come through here, so on an opened saved term this is always the specialist's change.
    if (reviewIndex !== -1 && !manager.readOnly) shapesTouched = true;
    if (!manager.readOnly && initialized) draftChanged("Unsaved changes. Select Save and Next Term to save this term.");
    buttons();
  };
  comments.addEventListener("input", () => draftChanged(comments.value.trim() ? "Unsaved comment." : "Unsaved changes. Select Save and Next Term to save this term."));
  // Active time on this image, for the study team (imageTimer.js).
  const timer = window.trackImageTime?.({imageId: assignmentId, token});
  const watch = window.watchAssignment?.({imageId: assignmentId, busy: () => saving || navigating,
    message: "It is no longer assigned to you, so nothing more can be saved on this page. Answers you already saved are kept.",
    onReleased: () => {
      released = true; dirty = false;
      timer?.stop();
      manager.stopInteraction();
      buttons();
      showStatus("This image was released, so nothing more can be saved on this page. Reload to continue.", "error");
    }});
  occurrences.forEach(term => {
    const i = terms.findIndex(first => first.dataset.keyword === term.dataset.keyword);
    term.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") { event.preventDefault(); term.click(); }
    });
    term.addEventListener("click", () => selectTerm(i));
  });
  // Selecting a term, in the text or in the saved list: a saved term opens to be changed, and the current term closes
  // an open one. Selecting the term already in hand does nothing, so it never offers to throw away a drawing.
  function selectTerm(i) {
    if (released || saving || !initialized || !manager.ready) return;
    const record = saved(i);
    if (!record && i !== currentIndex) return;
    if (i === reviewIndex || (!record && reviewIndex === -1)) return;
    if (dirty && !confirm(reviewIndex !== -1 ? `Discard your unsaved changes to ${termName(reviewIndex)}?` : "Discard unsaved changes to open another term?")) return;
    if (!record) { showCurrent(); return; }
    openSaved(i, record);
  }

  // Opens a saved term to change it: its shapes and comment come back, editable, until Save changes or Cancel.
  function openSaved(i, record) {
    manager.resetAnnotation(); dirty = false; shapesTouched = false; opened = null;
    if (record.boxCoordinates) {
      try { manager.loadCoordinates(record.boxCoordinates); }
      catch (error) {
        showCurrent();
        showStatus(`Your saved answer for ${termName(i)} uses older image coordinates, so it can't be changed here. It is kept as saved. ${error.message}`, "error");
        return;
      }
    }
    reviewIndex = i; comments.value = record.comment || "";
    opened = {comment: (record.comment || "").trim()};
    buttons();
    showStatus(changePrompt());
  }

  // Saves the open saved term with its new shapes, answer and comment (the server keeps the version it replaces).
  async function saveChange(action) {
    if (released || saving || !initialized || !manager.ready || reviewIndex === -1 || assignmentId !== window.annotatedMedData.id) return;
    const i = reviewIndex, record = saved(i), comment = comments.value.trim();
    let coordinates = "";
    try {
      // Untouched shapes are sent exactly as saved, so a comment change doesn't rewrite them.
      if (action === "Save and Next Term") coordinates = !shapesTouched && record.pressedButton === action ? record.boxCoordinates : manager.getAllAnnotationsJSON();
    } catch (error) { showStatus(error.message, "error"); return; }
    if (action === record.pressedButton && coordinates === (record.boxCoordinates || "") && comment === opened.comment) {
      showCurrent(); return;
    }
    const payload = {Id: assignmentId, BoxCoordinates: coordinates, ExtractedKeyword: termName(i), PressedButton: action, Comment: comment};
    saving = true; buttons(); showStatus("Saving your changes… keep this page open.", "saving");
    try {
      await request("/MedData/CorrectAnnotatedMedData", {method: "POST", body: JSON.stringify(payload)});
      Object.assign(record, {boxCoordinates: coordinates, pressedButton: action, comment});
      saving = false; showCurrent();
      showStatus(`Your changes to ${termName(i)} are saved. ${prompt()}`);
    } catch (error) { requestFailed(`Changing ${payload.ExtractedKeyword}`, error); saving = false; buttons(); showStatus(error.message, "error"); lockLost(error); }
  }

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
    if (reviewIndex !== -1) { await saveChange("Save and Next Term"); return; }
    if (currentIndex !== -1) { await save("Save and Next Term"); return; }
    saving = true; buttons(); showStatus("Loading the next image…", "saving");
    try {
      await timer?.flush();
      await request(`/MedData/NextImage?medDataId=${encodeURIComponent(assignmentId)}`,{method:"PUT"});
      saving = false; dirty = false; navigating = true; location.reload();
    } catch (error) { requestFailed("Moving to the next image", error); saving = false; buttons(); showStatus(error.message,"error"); lockLost(error); }
  });
  // Saved answers can't be seen until they have loaded, so a failed load is treated as if there were some.
  const hasWork = () => !historyLoaded || history.length > 0 || dirty || problem.value.trim() !== "" || custom?.hasSaved() || custom?.hasPending();
  skipImage.addEventListener("click", async () => {
    if (released || saving) return;
    if (hasWork() && !confirm("Skip this case? What you have entered on it will be removed, and another specialist will start it fresh.")) return;
    saving = true; buttons(); showStatus("Moving to the next image…", "saving");
    try {
      await timer?.flush();
      await request(`/MedData/SkipImage?medDataId=${encodeURIComponent(assignmentId)}`, {method: "PUT"});
      saving = false; dirty = false; navigating = true; location.reload();
    } catch (error) { requestFailed("Skipping the image", error); saving = false; buttons(); showStatus(error.message, "error"); lockLost(error); }
  });
  skip.addEventListener("click", () => reviewIndex !== -1 ? saveChange("Uncertain/Skip") : save("Uncertain/Skip"));
  absent.addEventListener("click", () => reviewIndex !== -1 ? saveChange("Not Visible/Abstract") : save("Not Visible/Abstract"));
  cancel?.addEventListener("click", () => {
    if (saving || reviewIndex === -1) return;
    if (dirty && !confirm(`Discard your unsaved changes to ${termName(reviewIndex)}?`)) return;
    showCurrent();
  });
  buttons();
  initialize();
});
