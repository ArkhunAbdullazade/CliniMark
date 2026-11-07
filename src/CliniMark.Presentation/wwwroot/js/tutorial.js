"use strict";

// The tutorial video. A new participant gets it before their first annotation and watches it to the end: it can't be
// closed or skipped ahead until then. If the video can't load they are never trapped: they carry on, and it opens again
// next time because nothing was recorded. The Tutorial button replays it at any time, with free control.
(() => {
  const SKIP_TOLERANCE = 1, NORMAL_STEP = 5;
  const dialog = document.getElementById("tutorial-dialog");
  if (!dialog) return;
  const video = dialog.querySelector("video");
  const closeButton = dialog.querySelector("[data-tutorial-close]");
  const doneButton = dialog.querySelector("[data-tutorial-done]");
  const note = dialog.querySelector("[data-tutorial-note]");
  let required = dialog.dataset.required === "true", furthest = 0;

  async function recordWatched() {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const send = window.CliniMarkRetry?.fetchWithRetry || ((url, options) => fetch(url, options));
    // A failed save only means the video opens once more next time, so it never holds the participant back.
    try { await send("/MedData/TutorialWatched", {method: "POST", headers: {"X-CSRF-TOKEN": token}}); } catch { }
  }

  function release(message) {
    required = false;
    note.textContent = message;
    closeButton.hidden = false;
    doneButton.hidden = false;
    doneButton.focus();
  }

  function open() {
    closeButton.hidden = required;
    doneButton.hidden = true;
    note.textContent = required ? "Please watch this short video to the end. You can then start annotating." : "";
    if (!dialog.open) dialog.showModal();
  }

  video.addEventListener("timeupdate", () => {
    const step = video.currentTime - furthest;
    if (!video.seeking && step > 0 && step < NORMAL_STEP) furthest = video.currentTime;
  });
  video.addEventListener("seeking", () => {
    if (required && video.currentTime > furthest + SKIP_TOLERANCE) video.currentTime = furthest;
  });
  video.addEventListener("ended", () => {
    if (!required) return;
    release("You're ready to start annotating.");
    recordWatched();
  });
  video.addEventListener("error", () => {
    if (required) release("The video could not load. You can start annotating now, and it will open again next time.");
  });

  // Escape asks to cancel; browsers let a second press through, so a closed dialog reopens while it is still required.
  dialog.addEventListener("cancel", event => { if (required) event.preventDefault(); });
  dialog.addEventListener("close", () => {
    if (required) { dialog.showModal(); return; }
    video.pause();
    video.currentTime = 0;
  });
  closeButton.addEventListener("click", () => dialog.close());
  doneButton.addEventListener("click", () => dialog.close());
  document.getElementById("tutorial-button")?.addEventListener("click", () => {
    open();
    video.play()?.catch?.(() => { });
  });

  if (required) open();
})();
