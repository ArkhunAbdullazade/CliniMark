// Shared behaviour for every admin page: local times, confirm dialogs, bulk selection and auto-submitting filters.
(function () {
  "use strict";

  const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  function ago(date) {
    const seconds = Math.round((date.getTime() - Date.now()) / 1000);
    const steps = [[60, "second"], [60, "minute"], [24, "hour"], [30, "day"]];
    let value = seconds;
    for (const [size, unit] of steps) {
      if (Math.abs(value) < size) return relative.format(value, unit);
      value = Math.round(value / size);
    }
    return relative.format(value, "month");
  }
  function renderTimes() {
    document.querySelectorAll("time[data-local]").forEach(element => {
      const value = element.getAttribute("datetime");
      if (!value) { element.textContent = "—"; return; }
      const date = new Date(value);
      const exact = date.toLocaleString(undefined, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
      const mode = element.dataset.local;
      element.title = date.toLocaleString();
      element.textContent = mode === "relative" ? ago(date)
        : mode === "date" ? date.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })
        : exact;
    });
  }
  renderTimes();
  setInterval(renderTimes, 30000);

  // Every state-changing form asks first; the modal states exactly what will happen.
  const modalElement = document.getElementById("confirm-modal");
  if (modalElement && window.bootstrap) {
    const modal = new bootstrap.Modal(modalElement);
    let pending = null, pendingSubmitter = null;
    document.addEventListener("submit", event => {
      const form = event.target;
      const submitter = event.submitter || null;
      // A row button can carry its own message; otherwise the form's message applies.
      const message = (submitter && submitter.dataset.confirm) || form.dataset.confirm;
      if (!(form instanceof HTMLFormElement) || !message || form.dataset.confirmed === "true") return;
      event.preventDefault();
      pending = form;
      pendingSubmitter = submitter;
      // A form can show what was typed into it, so the admin checks the exact value before it is applied.
      const typed = form.dataset.confirmInput ? form.elements[form.dataset.confirmInput].value.trim() : "";
      document.getElementById("confirm-body").textContent = message.replace("{typed}", typed);
      document.getElementById("confirm-ok").textContent = (submitter && submitter.dataset.confirmButton) || form.dataset.confirmButton || "Release";
      modal.show();
    });
    document.getElementById("confirm-ok").addEventListener("click", () => {
      if (!pending) return;
      pending.dataset.confirmed = "true";
      modal.hide();
      if (pendingSubmitter) pending.requestSubmit(pendingSubmitter);
      else pending.requestSubmit();
    });
  }

  // Bulk selection on the locks table.
  document.querySelectorAll("[data-select-all]").forEach(master => {
    const scope = document.querySelector(master.dataset.selectAll);
    if (!scope) return;
    const boxes = () => Array.from(scope.querySelectorAll("input[type=checkbox][name=selected]"));
    const button = document.querySelector(master.dataset.bulkButton);
    const sync = () => {
      const checked = boxes().filter(box => box.checked).length;
      master.checked = checked > 0 && checked === boxes().length;
      master.indeterminate = checked > 0 && checked < boxes().length;
      if (button) {
        button.disabled = checked === 0;
        button.querySelector("[data-count]").textContent = checked;
      }
    };
    master.addEventListener("change", () => { boxes().forEach(box => { box.checked = master.checked; }); sync(); });
    scope.addEventListener("change", sync);
    sync();
  });

  document.querySelectorAll("[data-autosubmit]").forEach(control =>
    control.addEventListener("change", () => control.form && control.form.requestSubmit()));
})();
