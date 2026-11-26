"use strict";

// Measures how long the participant actively works on the open image, for the time figures in the study team's admin
// panel. Time counts only while this page is the visible tab of the active window (another program in front stops it,
// even with CliniMark still on screen), and the person has used the mouse, keyboard or touch screen on it in the last
// minute, so a page left open while they see a patient stops counting within a minute; it also pauses while the
// tutorial video is open. Coming back to the window counts as using it. The page reports its running total after the first second, every half minute, before moving on and when it is
// hidden or closed. Each report repeats the whole total, so a lost or repeated report never loses or doubles time.
(() => {
  const TICK = 1000, REPORT_EVERY = 30000, IDLE_AFTER = 60000, FLUSH_WAIT = 1500;
  const INPUT_EVENTS = ["pointerdown", "pointermove", "keydown", "wheel", "scroll", "touchstart"];

  function viewId() {
    if (typeof crypto !== "undefined" && crypto.randomUUID) return crypto.randomUUID();
    const hex = Array.from({length: 32}, (_, i) => (i === 12 ? 4 : i === 16 ? 8 + Math.floor(Math.random() * 4) : Math.floor(Math.random() * 16)).toString(16)).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  function trackImageTime({imageId, token}) {
    const view = viewId();
    let activeMs = 0, sent = 0, early = false, lastInput = Date.now(), lastTick = Date.now(), lastReport = Date.now(), stopped = false;
    const tutorialOpen = () => document.getElementById("tutorial-dialog")?.open === true;
    // The browser window is the one in use (not merely visible behind or beside another program).
    const focused = () => typeof document.hasFocus !== "function" || document.hasFocus();
    // Adds the time since the last tick if the person was working. A step is at most two ticks, so a sleeping laptop
    // or a throttled background tab never adds a long stretch at once.
    const tick = () => {
      const now = Date.now();
      if (!stopped && !document.hidden && focused() && !tutorialOpen() && now - lastInput <= IDLE_AFTER)
        activeMs += Math.min(Math.max(0, now - lastTick), 2 * TICK);
      lastTick = now;
    };
    const send = (keepalive = false) => {
      tick();
      const total = Math.floor(activeMs / 1000);
      if (total <= sent) return Promise.resolve();
      return fetch("/MedData/RecordTime", {method: "POST", keepalive, cache: "no-store",
        headers: {"Content-Type": "application/json", Accept: "application/json", "X-CSRF-TOKEN": token || ""},
        body: JSON.stringify({Id: imageId, View: view, Seconds: total})})
        .then(response => response.ok ? response.json() : null)
        .then(result => { if (result?.success) sent = Math.max(sent, total); })
        .catch(() => { /* The next report carries the same total. */ });
    };
    const input = () => { lastInput = Date.now(); };
    const visibility = () => {
      if (document.hidden) send(true);
      else lastTick = Date.now();
    };
    const pagehide = () => send(true);
    // Switching back to the window is a deliberate act, so it counts as using the page.
    const windowFocus = () => { input(); lastTick = Date.now(); };
    const timer = setInterval(() => {
      tick();
      const now = Date.now();
      // The first report comes after the first second, so even a quick Skip the case is recorded.
      if ((!early && activeMs >= 1000) || now - lastReport >= REPORT_EVERY) {
        early = true;
        lastReport = now;
        send();
      }
    }, TICK);
    INPUT_EVENTS.forEach(name => document.addEventListener(name, input, {capture: true, passive: true}));
    document.addEventListener("visibilitychange", visibility);
    window.addEventListener("pagehide", pagehide);
    window.addEventListener("focus", windowFocus);

    const tracker = {
      seconds: () => { tick(); return Math.floor(activeMs / 1000); },
      // Before leaving the image (Next Image, Skip the case, submitting, logging out): sends the total while the image
      // is still assigned and the session still open, but never holds the page up for more than a moment.
      flush: () => Promise.race([send(), new Promise(resolve => setTimeout(resolve, FLUSH_WAIT))]),
      // The image was released: stops counting and sends what was counted.
      stop() {
        if (stopped) return;
        tick();
        stopped = true;
        clearInterval(timer);
        INPUT_EVENTS.forEach(name => document.removeEventListener(name, input, {capture: true}));
        document.removeEventListener("visibilitychange", visibility);
        window.removeEventListener("pagehide", pagehide);
        window.removeEventListener("focus", windowFocus);
        send(true);
      }
    };
    window.imageTimer = tracker;
    return tracker;
  }

  window.trackImageTime = trackImageTime;
})();
