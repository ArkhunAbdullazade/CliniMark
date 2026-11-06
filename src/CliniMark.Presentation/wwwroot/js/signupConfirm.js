// No confirmation email exists, so a mistyped address would lock the person out. Show it back to them before the
// account is created. Without JavaScript the form simply submits, and the warnings on the page still apply.
(function () {
  "use strict";
  const form = document.querySelector("form[data-signup-form]");
  const dialog = document.getElementById("signup-confirm");
  if (!form || !dialog || typeof dialog.showModal !== "function") return;
  const email = form.querySelector('input[name="Email"]');
  let confirmed = false;

  form.addEventListener("submit", event => {
    if (confirmed || event.defaultPrevented) return;
    // Let the page's own validation report problems first; there is nothing to confirm until the form is valid.
    if (window.jQuery?.fn?.valid && !window.jQuery(form).valid()) return;
    event.preventDefault();
    dialog.querySelector("[data-signup-email]").textContent = email.value.trim();
    dialog.showModal();
  });
  dialog.querySelector("[data-signup-yes]").addEventListener("click", () => {
    confirmed = true;
    dialog.close();
    form.requestSubmit();
  });
  dialog.querySelector("[data-signup-back]").addEventListener("click", () => {
    dialog.close();
    email.focus();
    email.select();
  });
})();
