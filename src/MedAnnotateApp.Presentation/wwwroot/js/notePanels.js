"use strict";

// Comments and Report a technical problem open as panels that slide down under their buttons, so the image and
// terms stay in view. Hovering a button previews its panel; clicking pins it open and puts the cursor in it.
// A panel stays open while its text box has focus, and only one panel is open at a time. Closed panels are inert,
// so their text boxes cannot be reached by Tab. Whatever is typed stays in the box when the panel closes.
document.addEventListener("DOMContentLoaded", () => {
  const area = document.querySelector("[data-note-panels]");
  if (!area) return;
  const toggles = [...area.querySelectorAll("[data-note-toggle]")];
  const panelOf = toggle => document.getElementById(toggle.dataset.noteToggle);
  let pinned = null;

  function show(toggle) {
    for (const other of toggles) {
      const open = other === toggle, panel = panelOf(other);
      other.setAttribute("aria-expanded", String(open));
      other.classList.toggle("open", open);
      panel.classList.toggle("open", open);
      panel.inert = !open;
    }
  }
  const typingIn = () => toggles.find(toggle => panelOf(toggle).contains(document.activeElement));
  const settle = () => show(pinned || typingIn() || null);

  toggles.forEach(toggle => {
    panelOf(toggle).inert = true;
    toggle.addEventListener("mouseenter", () => { if (!pinned && !typingIn()) show(toggle); });
    toggle.addEventListener("click", () => {
      pinned = pinned === toggle ? null : toggle;
      show(pinned);
      if (pinned) panelOf(toggle).querySelector("textarea")?.focus();
      else toggle.focus();
    });
  });
  area.addEventListener("mouseleave", settle);
  area.addEventListener("focusout", () => setTimeout(() => { if (!area.matches(":hover")) settle(); }));
  area.addEventListener("keydown", event => {
    const toggle = typingIn();
    if (event.key !== "Escape" || !toggle) return;
    event.preventDefault();
    pinned = null;
    toggle.focus();
    show(null);
  });
});
