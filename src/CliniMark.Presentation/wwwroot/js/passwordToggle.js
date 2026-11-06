// Lets people check what they typed. Every field is masked again on submit, so password managers see a normal password field.
(function () {
  "use strict";
  document.querySelectorAll("[data-password-toggle]").forEach(button => {
    const input = button.parentElement.querySelector("input");
    const icon = button.querySelector("i");
    const show = visible => {
      input.type = visible ? "text" : "password";
      icon.className = visible ? "bi bi-eye-slash" : "bi bi-eye";
      button.setAttribute("aria-pressed", String(visible));
      button.setAttribute("aria-label", visible ? "Hide password" : "Show password");
    };
    button.addEventListener("click", () => show(input.type === "password"));
    input.form?.addEventListener("submit", () => show(false));
  });
})();
