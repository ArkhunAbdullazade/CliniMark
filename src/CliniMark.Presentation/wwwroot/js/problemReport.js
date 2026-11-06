"use strict";

// Technical problems always reach the study team, whether typed by the participant or logged automatically when the
// image will not load, a save fails or a script breaks the page. This script depends on nothing else on the page and
// loads before the other scripts, so it keeps working when the image or the drawing tools do not. Reports that cannot
// be sent right away (no connection, signed out) stay in this browser and are sent as soon as that is possible.
(() => {
  const QUEUE_KEY = "clinimark.pending-problem-reports";
  // Automatic reports per page load, one per distinct problem, so an error loop cannot flood the study team.
  const MAX_AUTOMATIC = 8;
  const logged = new Set();

  const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
  const imageId = () => window.annotatedMedData?.id ?? null;
  const currentTerm = () => document.querySelector(".header-text .current_keyword")?.dataset.keyword ?? null;
  const details = extra => JSON.stringify({page: location.pathname, browser: navigator.userAgent, ...extra});

  async function send(report) {
    let response;
    try {
      response = await fetch("/MedData/ReportProblem", {method: "POST", body: JSON.stringify(report),
        headers: {"Content-Type": "application/json", "X-CSRF-TOKEN": token()}});
    } catch {
      throw Object.assign(new Error("No connection."), {keep: true});
    }
    let result = null;
    try { result = await response.json(); } catch { /* A sign-in page instead of JSON: kept and retried below. */ }
    if (response.ok && result?.success) return;
    // Server trouble or a lost sign-in may pass; a rejected report (too long, too many) would not.
    throw Object.assign(new Error(result?.message || "The report could not be sent."), {keep: response.status >= 500 || response.status === 401 || !result});
  }

  const readQueue = () => { try { return JSON.parse(localStorage.getItem(QUEUE_KEY) || "[]"); } catch { return []; } };
  const writeQueue = items => { try { localStorage.setItem(QUEUE_KEY, JSON.stringify(items.slice(-20))); } catch { /* Storage blocked. */ } };
  let flushing = false;
  async function flush() {
    if (flushing) return;
    const pending = readQueue();
    if (!pending.length) return;
    flushing = true;
    const left = [];
    for (const report of pending) {
      try { await send(report); } catch (error) { if (error.keep) left.push(report); }
    }
    writeQueue([...left, ...readQueue().slice(pending.length)]);
    flushing = false;
  }
  async function sendOrKeep(report) {
    try { await send(report); return "sent"; }
    catch (error) {
      if (!error.keep) throw error;
      writeQueue([...readQueue(), report]);
      return "kept";
    }
  }

  function logAutomatically(kind, message, extra = {}, key = `${kind}|${message}`) {
    if (logged.has(key) || logged.size >= MAX_AUTOMATIC) return;
    logged.add(key);
    sendOrKeep({kind, message: String(message || "").slice(0, 4000), medDataId: imageId(), term: currentTerm(), details: details(extra)})
      .catch(() => { /* Nothing more can be done for an automatic report. */ });
  }

  // Script errors, and files that fail to load. Registered before the page's own scripts run.
  window.addEventListener("error", event => {
    const target = event.target;
    if (target instanceof HTMLImageElement && target.id === "sourceImage")
      logAutomatically("image-load-error", "The study image did not load.", {imageUrl: target.currentSrc || target.src}, "image-load-error");
    else if (target instanceof HTMLScriptElement || target instanceof HTMLLinkElement)
      logAutomatically("script-error", `A page file did not load: ${target.src || target.href}`);
    else if (target === window || !(target instanceof Element))
      logAutomatically("script-error", `${event.message} (${event.filename}:${event.lineno}:${event.colno})`, {stack: String(event.error?.stack || "").slice(0, 2000)});
  }, true);
  window.addEventListener("unhandledrejection", event =>
    logAutomatically("script-error", `Unhandled: ${event.reason?.message || event.reason}`, {stack: String(event.reason?.stack || "").slice(0, 2000)}));
  // Failed saves and loads, announced by the annotation pages.
  document.addEventListener("annotationrequesterror", event => {
    const {action, message, status} = event.detail || {};
    logAutomatically("save-error", `${action}: ${message}`, {status});
  });

  document.addEventListener("DOMContentLoaded", () => {
    // The canvas also reports images that load but cannot be used (no size, changed source).
    document.getElementById("mainCanvas")?.addEventListener("annotationimageerror", event =>
      logAutomatically("image-load-error", event.detail?.message || "The study image could not be used.",
        {imageUrl: document.getElementById("sourceImage")?.src}, "image-load-error"));

    // The participant's own report: sent at once with the image and the term on screen.
    const forms = [...document.querySelectorAll("[data-problem-report]")];
    forms.forEach(form => {
      const text = form.querySelector("textarea"), button = form.querySelector("[data-problem-send]");
      const status = form.querySelector("[data-problem-status]"), hint = status?.textContent || "";
      const say = (message, state = "") => { if (status) { status.textContent = message; status.dataset.state = state; } };
      text.addEventListener("input", () => { if (status?.dataset.state) say(hint); });
      button.addEventListener("click", async () => {
        const message = text.value.trim();
        if (!message) { say("Describe the problem first.", "error"); text.focus(); return; }
        button.disabled = true;
        say("Sending…");
        try {
          const outcome = await sendOrKeep({kind: "user-report", message, medDataId: imageId(), term: currentTerm(), details: details({})});
          text.value = "";
          text.dispatchEvent(new Event("input", {bubbles: true}));
          say(outcome === "sent" ? "Sent to the study team. Thank you." : "No connection right now. The report is kept in this browser and sent automatically.", "sent");
        } catch (error) {
          say(error.message, "error");
        } finally {
          button.disabled = false;
        }
      });
    });
    // An unsent report is easy to lose when leaving the page.
    window.addEventListener("beforeunload", event => {
      if (forms.some(form => form.querySelector("textarea").value.trim())) { event.preventDefault(); event.returnValue = ""; }
    });

    flush();
    window.addEventListener("online", flush);
    setInterval(() => { if (readQueue().length) flush(); }, 60000);
  });
})();
