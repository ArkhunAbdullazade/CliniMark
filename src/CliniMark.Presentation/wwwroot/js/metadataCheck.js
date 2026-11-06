"use strict";

// The label check on the specialist page: Yes, No or Can't tell for the image's modality, speciality and body region.
// No opens a search of the study's labels; a value that is missing can be added, always as a deliberate choice, so a
// typing slip is never saved by accident. Every answer is saved at once and can be changed until Next Image.
(() => {
  const MAX_LENGTH = 100;
  const clean = value => String(value ?? "").trim().replace(/\s+/g, " ");
  const same = (first, second) => clean(first).toLowerCase() === clean(second).toLowerCase();

  // What the search list shows for a query: labels containing it, those starting with it first, and whether the query
  // can be added as a new value. The label already shown is never offered, and Unknown is what Can't tell is for.
  function searchLabels({options = [], added = [], shown = ""}, query) {
    const wanted = clean(query).toLowerCase();
    const entries = [...options.map(value => ({value, added: false})), ...added.map(value => ({value, added: true}))]
      .filter(entry => !same(entry.value, shown));
    const matches = entries.filter(entry => entry.value.toLowerCase().includes(wanted));
    const starts = matches.filter(entry => entry.value.toLowerCase().startsWith(wanted));
    const known = entries.some(entry => same(entry.value, query)) || same(query, shown) || wanted === "unknown";
    return {
      matches: [...starts, ...matches.filter(entry => !starts.includes(entry))],
      addable: wanted && !known && clean(query).length <= MAX_LENGTH ? clean(query) : null
    };
  }

  function setupMetadataCheck({imageId, request, requestFailed, onChange, onConflict}) {
    const root = document.getElementById("label-check");
    if (!root) return null;
    const choices = JSON.parse(root.dataset.options || "{}");
    const all = root.querySelector("[data-label-all]");
    const error = root.querySelector("[data-label-error]");
    const rows = [...root.querySelectorAll("[data-label-row]")].map(element => ({
      element, field: element.dataset.field, name: element.dataset.name, shown: element.dataset.shown,
      missing: element.dataset.missing === "true", buttons: [...element.querySelectorAll("[data-answer]")],
      result: element.querySelector("[data-label-result]"), verdict: null, value: null, picker: null
    }));
    let enabled = false, busy = false;

    const complete = () => rows.every(row => row.verdict && !row.picker);

    function render() {
      for (const row of rows) {
        const selected = row.picker ? "corrected" : row.verdict;
        for (const button of row.buttons) {
          button.disabled = !enabled || busy;
          button.setAttribute("aria-pressed", String(button.dataset.answer === selected));
        }
        const corrected = row.verdict === "corrected" && !row.picker;
        row.result.hidden = !corrected;
        row.result.textContent = corrected ? `Corrected to: ${row.value}` : "";
        if (row.picker) row.picker.input.disabled = !enabled || busy;
      }
      if (all) {
        const allYes = rows.every(row => row.verdict === "correct" && !row.picker);
        const otherAnswer = rows.some(row => row.picker || (row.verdict && row.verdict !== "correct"));
        all.disabled = !enabled || busy || allYes || otherAnswer;
        all.setAttribute("aria-pressed", String(allYes));
      }
    }

    function showError(message) {
      error.textContent = message;
      error.hidden = !message;
    }

    async function save(checks, apply) {
      if (busy || !enabled) return;
      busy = true;
      showError("");
      render();
      try {
        await request("/MedData/SaveMetadataChecks", {method: "POST", body: JSON.stringify({Id: imageId, Checks: checks})});
        apply();
      } catch (failure) {
        requestFailed("Saving the label check", failure);
        showError(failure.message);
        if (failure.status === 409) onConflict?.(failure);
      } finally {
        busy = false;
        render();
        onChange?.();
      }
    }

    function closePicker(row) {
      row.picker?.element.remove();
      row.picker = null;
    }

    function answer(row, verdict, value = null) {
      return save([{Field: row.field, Verdict: verdict, Value: value}], () => {
        closePicker(row);
        row.verdict = verdict;
        row.value = value;
        const list = choices[row.field] ||= {options: [], added: []};
        if (value && ![...list.options, ...list.added].some(entry => same(entry, value))) list.added.push(value);
      });
    }

    function openPicker(row) {
      const element = Object.assign(document.createElement("div"), {className: "label-picker"});
      const input = Object.assign(document.createElement("input"), {
        className: "label-search", type: "text", maxLength: MAX_LENGTH, autocomplete: "off", placeholder: `Search or type the ${row.name}`});
      const list = Object.assign(document.createElement("ul"), {className: "label-options", id: `label-options-${row.field}`});
      list.setAttribute("role", "listbox");
      list.setAttribute("aria-label", `Correct ${row.name}`);
      input.setAttribute("role", "combobox");
      input.setAttribute("aria-label", `Correct ${row.name}`);
      input.setAttribute("aria-autocomplete", "list");
      input.setAttribute("aria-expanded", "true");
      input.setAttribute("aria-controls", list.id);
      element.append(input, list);
      row.element.append(element);
      row.picker = {element, input};

      let items = [], active = -1;
      function highlight() {
        items.forEach((item, index) => {
          item.element.classList.toggle("active", index === active);
          item.element.setAttribute("aria-selected", String(index === active));
        });
        if (active >= 0) {
          input.setAttribute("aria-activedescendant", items[active].element.id);
          items[active].element.scrollIntoView?.({block: "nearest"});
        } else input.removeAttribute("aria-activedescendant");
      }
      function option(text, value, className = "") {
        const item = Object.assign(document.createElement("li"), {className: `label-option ${className}`.trim(), textContent: text});
        item.id = `label-option-${row.field}-${items.length}`;
        item.setAttribute("role", "option");
        item.addEventListener("mousedown", event => event.preventDefault());
        item.addEventListener("click", () => answer(row, "corrected", value));
        items.push({element: item, value});
        return item;
      }
      function refresh() {
        const {options = [], added = []} = choices[row.field] || {};
        const {matches, addable} = searchLabels({options, added, shown: row.shown}, input.value);
        items = [];
        const nodes = matches.map(match => {
          const item = option(match.value, match.value);
          if (match.added) item.append(Object.assign(document.createElement("span"), {className: "label-added", textContent: "added by a clinician"}));
          return item;
        });
        if (!matches.length) nodes.push(Object.assign(document.createElement("li"), {className: "label-none", textContent: "No match"}));
        if (addable) nodes.push(option(`+ Add "${addable}" as a new ${row.name}`, addable, "label-add"));
        list.replaceChildren(...nodes);
        // Enter picks the best match, but never adds a value: that needs the arrow keys or a click.
        active = clean(input.value) && matches.length ? 0 : -1;
        highlight();
      }
      input.addEventListener("input", refresh);
      input.addEventListener("keydown", event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault();
          if (!items.length) return;
          active = event.key === "ArrowDown" ? (active + 1) % items.length : (active <= 0 ? items.length : active) - 1;
          highlight();
        } else if (event.key === "Enter") {
          event.preventDefault();
          if (active >= 0) answer(row, "corrected", items[active].value);
        } else if (event.key === "Escape" && !row.missing) {
          event.preventDefault();
          closePicker(row);
          render();
          onChange?.();
          row.buttons.find(button => button.dataset.answer === "corrected")?.focus();
        }
      });
      refresh();
      render();
      onChange?.();
      input.focus();
    }

    for (const row of rows) {
      for (const button of row.buttons) {
        button.addEventListener("click", () => {
          const verdict = button.dataset.answer;
          if (verdict !== "corrected") { answer(row, verdict); return; }
          if (!row.picker) { openPicker(row); return; }
          if (row.missing) { row.picker.input.focus(); return; }
          closePicker(row);
          render();
          onChange?.();
        });
      }
    }
    all?.addEventListener("click", () => save(rows.map(row => ({Field: row.field, Verdict: "correct"})), () => {
      for (const row of rows) { closePicker(row); row.verdict = "correct"; row.value = null; }
    }));

    return {
      // Restores saved answers once the page knows them. A row with no label starts with its search open.
      load(records) {
        for (const record of records || []) {
          const row = rows.find(item => item.field === record.field);
          if (row) { row.verdict = record.verdict; row.value = record.correctedValue ?? null; }
        }
        for (const row of rows) {
          if (row.missing && !row.verdict && !row.picker) {
            openPicker(row);
            row.picker.input.blur();
          }
        }
        render();
      },
      complete,
      // Tints the box when it is the one thing still holding back Next Image.
      setAttention(value) {
        root.classList.toggle("needs-answer", value);
      },
      setEnabled(value) {
        if (enabled === value) return;
        enabled = value;
        render();
      }
    };
  }

  window.setupMetadataCheck = setupMetadataCheck;
  window.MetadataCheckSearch = {searchLabels};
})();
