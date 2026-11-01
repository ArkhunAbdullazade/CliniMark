"use strict";

// Text size for the specialist description, and a dialog for reading all of it at a comfortable size.
// Sizes are remembered in this browser only.
document.addEventListener("DOMContentLoaded", () => {
  const panel = document.querySelector(".description-panel");
  const text = panel?.querySelector(".header-text");
  const dialog = document.getElementById("description-dialog");
  if (!panel || !text || !dialog) return;
  const reader = dialog.querySelector(".description-dialog-text");
  const termNote = document.getElementById("description-dialog-term");
  const scales = {
    panel: {element: panel, sizes: [0.8, 0.95, 1.1, 1.25, 1.4, 1.6], step: 1},
    dialog: {element: dialog, sizes: [1, 1.125, 1.25, 1.4, 1.6, 1.8], step: 1}
  };
  const storageKey = name => `medannotate.description-size.${name}`;

  function apply(name) {
    const scale = scales[name];
    scale.element.style.setProperty("--description-size", `${scale.sizes[scale.step]}rem`);
    scale.element.querySelectorAll("[data-size-step]").forEach(button => {
      const next = scale.step + Number(button.dataset.sizeStep);
      button.disabled = next < 0 || next >= scale.sizes.length;
    });
  }
  function resize(name, change) {
    const scale = scales[name];
    scale.step = Math.min(scale.sizes.length - 1, Math.max(0, scale.step + change));
    apply(name);
    try { localStorage.setItem(storageKey(name), String(scale.step)); } catch { /* Private windows may block storage. */ }
    if (name === "panel") showCurrentTerm();
  }
  // Larger text pushes the current term out of the box, so bring it back into view.
  function showCurrentTerm() {
    const current = text.querySelector(".current_keyword");
    if (!current) return;
    const phrase = current.getBoundingClientRect(), box = text.getBoundingClientRect();
    if (phrase.top < box.top + 4) text.scrollTop -= box.top + 4 - phrase.top;
    else if (phrase.bottom > box.bottom - 4) text.scrollTop += phrase.bottom - box.bottom + 4;
  }
  for (const [name, scale] of Object.entries(scales)) {
    try {
      const saved = localStorage.getItem(storageKey(name));
      if (saved !== null && Number.isInteger(Number(saved))) scale.step = Math.min(scale.sizes.length - 1, Math.max(0, Number(saved)));
    } catch { /* Fall back to the default size. */ }
    apply(name);
    scale.element.querySelectorAll("[data-size-step]").forEach(button =>
      button.addEventListener("click", () => resize(name, Number(button.dataset.sizeStep))));
  }

  function open() {
    // A read-only copy: terms are still chosen in the description box.
    reader.replaceChildren(...[...text.querySelector("p").childNodes].map(node => node.cloneNode(true)));
    reader.querySelectorAll(".keyword").forEach(term => ["id", "role", "tabindex", "aria-pressed"].forEach(name => term.removeAttribute(name)));
    const current = text.querySelector(".current_keyword");
    termNote.textContent = current ? `Current term: ${current.dataset.keyword || current.textContent.trim()}` : "";
    dialog.showModal();
    reader.scrollTop = 0;
  }
  panel.querySelector("[data-description-open]").addEventListener("click", open);
  text.addEventListener("click", event => {
    if (event.target.closest(".keyword") || String(window.getSelection?.() || "").trim()) return;
    // Clicks on the box's own scrollbar only scroll it.
    if (event.clientX > text.getBoundingClientRect().left + text.clientLeft + text.clientWidth) return;
    open();
  });
  dialog.querySelector(".btn-close").addEventListener("click", () => dialog.close());
  // Only a press that starts and ends on the dark backdrop closes it, so selecting text past the edge does not.
  let fromBackdrop = false;
  dialog.addEventListener("pointerdown", event => { fromBackdrop = event.target === dialog; });
  dialog.addEventListener("click", event => { if (fromBackdrop && event.target === dialog) dialog.close(); });
});
