"use strict";

// Longer descriptions for the workspace buttons. Resting the pointer on a button with data-description for two
// seconds (or reaching it with the keyboard and staying there) slides out a red box explaining what it does, in the
// same style as the tool names. It goes away at once when the pointer leaves, the button is pressed or Escape is hit.
// The text is also announced to screen readers through aria-description.
(() => {
  const DELAY = 2000;
  let bubble = null, target = null, timer = 0;

  function hide() {
    clearTimeout(timer);
    timer = 0;
    target = null;
    bubble?.classList.remove("visible");
  }

  function show(element) {
    if (!element.isConnected) return;
    if (!bubble) {
      bubble = document.body.appendChild(document.createElement("div"));
      bubble.className = "hover-description";
      bubble.setAttribute("role", "tooltip");
    }
    bubble.textContent = element.dataset.description;
    bubble.classList.remove("visible");
    const rect = element.getBoundingClientRect(), width = bubble.offsetWidth, height = bubble.offsetHeight;
    // Below the button, or above it when the window ends first (the buttons under the image).
    const below = rect.bottom + 6 + height <= window.innerHeight - 6;
    bubble.style.left = `${Math.max(6, Math.min(rect.left - 1, window.innerWidth - width - 6))}px`;
    bubble.style.top = `${below ? rect.bottom + 6 : Math.max(6, rect.top - height - 6)}px`;
    requestAnimationFrame(() => { if (target === element) bubble.classList.add("visible"); });
  }

  function arm(element) {
    if (element === target) return;
    hide();
    target = element;
    timer = setTimeout(() => show(element), DELAY);
  }

  const describedFrom = node => node instanceof Element ? node.closest("[data-description]") : null;
  document.addEventListener("pointerover", event => {
    const element = describedFrom(event.target);
    if (element) arm(element);
  });
  document.addEventListener("pointerout", event => {
    if (target && !target.contains(event.relatedTarget)) hide();
  });
  document.addEventListener("focusin", event => {
    const element = describedFrom(event.target);
    if (element && element.matches(":focus-visible")) arm(element);
  });
  document.addEventListener("focusout", event => { if (event.target === target) hide(); });
  document.addEventListener("pointerdown", hide, true);
  document.addEventListener("keydown", event => { if (event.key === "Escape" || event.key === "Enter" || event.key === " ") hide(); }, true);
  window.addEventListener("scroll", hide, true);
  window.addEventListener("resize", hide);

  // Screen readers get the description at once; buttons added later (the drawing tools) set it themselves.
  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-description]").forEach(element => element.setAttribute("aria-description", element.dataset.description));
  });
})();
