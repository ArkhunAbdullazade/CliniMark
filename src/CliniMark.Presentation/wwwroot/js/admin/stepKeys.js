// The left and right arrow keys step through a participant's images on the admin image page, like its Previous and
// Next buttons (_ParticipantSteps.cshtml). Keys are left alone where arrows already do something (text, lists, radio
// buttons such as "Colour by"), with a modifier held, and while a dialog is open. Checkboxes and buttons don't use them.
(function (root) {
  "use strict";
  const STEPS = { ArrowLeft: "previous", ArrowRight: "next" };
  const USES_ARROWS = "textarea, select, [contenteditable]:not([contenteditable='false']), "
    + "input:not([type=checkbox]):not([type=button]):not([type=submit]):not([type=reset])";
  const inControl = target => !!target?.closest?.(USES_ARROWS);

  // Returns the address to go to, or null when the key isn't a step.
  function stepFor(event, doc) {
    const step = STEPS[event.key];
    if (!step || event.defaultPrevented || event.repeat || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return null;
    if (inControl(event.target) || doc.querySelector(".modal.show, dialog[open]")) return null;
    return doc.querySelector(`a[data-step="${step}"]`)?.href || null;
  }

  const api = { stepFor };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  if (root.document) {
    root.document.addEventListener("keydown", event => {
      const href = stepFor(event, root.document);
      if (!href) return;
      event.preventDefault();
      root.location.assign(href);
    });
  }
})(typeof window !== "undefined" ? window : globalThis);
