"use strict";

// Notices when the open image stops being assigned to this account (released by the study team, or by the idle
// lock sweeper), so nobody keeps annotating work that can no longer be saved. Checks run often while the person is
// using the page. A slow heartbeat also runs while the page is open, even unattended or in a background tab: each
// check refreshes the image's lock time, so there is no time limit on annotating, and only images whose page was
// closed are ever released by the sweeper.
(() => {
  const CHECK_EVERY = 20000, IDLE_AFTER = 5 * 60000, HEARTBEAT_EVERY = 10 * 60000;
  const INPUT_EVENTS = ["pointerdown", "pointermove", "keydown", "wheel"];

  function watchAssignment({imageId, busy, onReleased, message}) {
    let lastInput = Date.now(), inFlight = false, stopped = false;
    const check = async (heartbeat = false) => {
      if (stopped || inFlight || busy() || (document.hidden && !heartbeat)) return;
      inFlight = true;
      try {
        const response = await fetch(`/MedData/AssignmentStatus?medDataId=${encodeURIComponent(imageId)}`,
          {cache: "no-store", headers: {Accept: "application/json"}});
        const result = response.ok ? await response.json() : null;
        // Next Image and student submits clear the lock themselves, so answers arriving meanwhile are ignored.
        if (result?.assigned === false && !stopped && !busy()) release();
      } catch { /* A dropped connection or an ended session is reported by the next save. */ }
      finally { inFlight = false; }
    };
    // The first input after a quiet spell checks at once, before any new drawing starts.
    const input = () => {
      const now = Date.now(), resumed = now - lastInput > CHECK_EVERY;
      lastInput = now;
      if (resumed) check();
    };
    const visible = () => { if (!document.hidden) check(); };
    const timer = setInterval(() => { if (Date.now() - lastInput < IDLE_AFTER) check(); }, CHECK_EVERY);
    const heartbeat = setInterval(() => check(true), HEARTBEAT_EVERY);
    INPUT_EVENTS.forEach(name => document.addEventListener(name, input, {capture: true, passive: true}));
    document.addEventListener("visibilitychange", visible);

    function stop() {
      stopped = true;
      clearInterval(timer);
      clearInterval(heartbeat);
      INPUT_EVENTS.forEach(name => document.removeEventListener(name, input, {capture: true}));
      document.removeEventListener("visibilitychange", visible);
    }
    function release() {
      stop();
      onReleased();
      showReleased(message);
    }
    return {check, stop};
  }

  function showReleased(message) {
    const dialog = document.createElement("dialog");
    dialog.className = "annotation-dialog released-dialog";
    dialog.setAttribute("aria-labelledby", "released-dialog-title");
    dialog.setAttribute("aria-describedby", "released-dialog-message");
    dialog.innerHTML = '<div class="released-dialog-body"><span class="released-dialog-icon" aria-hidden="true"><i class="bi bi-lock"></i></span>' +
      '<h2 class="annotation-dialog-title" id="released-dialog-title">This image was released</h2>' +
      '<p id="released-dialog-message"></p><p class="annotation-dialog-note">The study team may have released it, or it was open a long time without saved work.</p>' +
      '<button type="button" class="annotation-dialog-action">Reload and continue</button></div>';
    dialog.querySelector("#released-dialog-message").textContent = message;
    dialog.querySelector("button").addEventListener("click", () => location.reload());
    // Nothing on the page can be saved any more, so the dialog stays until the page reloads.
    dialog.addEventListener("cancel", event => event.preventDefault());
    document.body.appendChild(dialog);
    dialog.showModal();
  }

  window.watchAssignment = watchAssignment;
})();
