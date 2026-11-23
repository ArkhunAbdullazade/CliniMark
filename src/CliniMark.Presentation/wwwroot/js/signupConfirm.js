// No confirmation email exists, so a mistyped address would lock the person out. Show it back to them before the
// account is created. Addresses at personal email services are refused here first, by the same rules as the server
// (InstitutionalEmail). Without JavaScript the form simply submits and the server checks everything.
(function () {
  "use strict";
  const form = document.querySelector("form[data-signup-form]");
  const dialog = document.getElementById("signup-confirm");
  if (!form || !dialog || typeof dialog.showModal !== "function") return;
  const email = form.querySelector('input[name="Email"]');
  const personalMessage = form.querySelector("[data-email-personal]");
  const words = name => (email.dataset[name] || "").split(" ").filter(Boolean);
  const domains = new Set(words("personalDomains")), families = new Set(words("personalFamilies"));
  const countrySecondLevels = new Set(["co", "com", "net", "org"]);
  function isPersonal(address) {
    const at = address.lastIndexOf("@");
    if (at < 0) return false;
    const domain = address.slice(at + 1).trim().replace(/\.+$/, "").toLowerCase();
    if (domains.has(domain)) return true;
    const labels = domain.split(".");
    return labels.length === 2 ? families.has(labels[0])
      : labels.length === 3 && families.has(labels[0]) && countrySecondLevels.has(labels[1]) && labels[2].length === 2;
  }
  const personal = () => {
    const refused = isPersonal(email.value);
    if (personalMessage) personalMessage.textContent = refused
      ? "This is a personal email address. Use your institutional or workplace email address." : "";
    email.setAttribute("aria-invalid", String(refused));
    return refused;
  };
  let confirmed = false, tried = false;
  email.addEventListener("input", () => { if (tried) personal(); });

  form.addEventListener("submit", event => {
    if (confirmed) return;
    tried = true;
    if (personal()) { event.preventDefault(); email.focus(); return; }
    if (event.defaultPrevented) return;
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
