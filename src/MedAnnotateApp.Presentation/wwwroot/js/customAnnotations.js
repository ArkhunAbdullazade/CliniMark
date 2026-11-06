"use strict";

// Custom annotations on the specialist page: findings outlined with the Custom annotation tool and named by the
// specialist because they are not among the highlighted terms. A small window opens above each finished contour for its
// name; saving stores it at once, so the specialist can register as many as they like. Saved ones are listed beside the
// image, with a delete button while the image is still theirs, and drawn in blue while the tool is in use.
(() => {
  function setupCustomAnnotations({manager, imageId, request, showStatus, requestFailed}) {
    const list = document.getElementById("custom-list");
    const items = document.getElementById("custom-items");
    const count = document.getElementById("custom-count");
    const saved = [];
    let box = null, busy = false;

    function render() {
      manager.customShapes = saved.flatMap(item => item.shapes);
      if (count) count.textContent = String(saved.length);
      if (list) list.hidden = saved.length === 0;
      items?.replaceChildren(...saved.map(item => {
        const row = document.createElement("li");
        const name = Object.assign(document.createElement("span"), {className: "custom-term", textContent: item.term});
        const remove = Object.assign(document.createElement("button"), {type: "button", className: "custom-delete", textContent: "×"});
        remove.setAttribute("aria-label", `Delete custom annotation ${item.term}`);
        remove.title = "Delete";
        remove.addEventListener("click", () => removeCustom(item));
        row.append(name, remove);
        return row;
      }));
      manager.drawCanvas();
    }

    // The window sits just above the contour (below it when there is no room), centred on it.
    function place() {
      const shape = manager.pendingCustom;
      if (!box || !shape) return;
      const rect = manager.canvas.getBoundingClientRect();
      const corners = shape.points.map(point => AnnotationGeometry.imageToClient(point, rect, manager.canvas.width, manager.canvas.height));
      const left = Math.min(...corners.map(p => p.x)), right = Math.max(...corners.map(p => p.x));
      const top = Math.min(...corners.map(p => p.y)), bottom = Math.max(...corners.map(p => p.y));
      const width = box.offsetWidth, height = box.offsetHeight;
      box.style.left = `${Math.max(6, Math.min((left + right) / 2 - width / 2, window.innerWidth - width - 6))}px`;
      box.style.top = `${top - height - 10 >= 6 ? top - height - 10 : Math.min(bottom + 10, window.innerHeight - height - 6)}px`;
    }

    function closeBox() {
      box?.remove();
      box = null;
    }

    function openBox() {
      closeBox();
      box = document.createElement("form");
      box.className = "custom-annotation-box";
      box.setAttribute("role", "dialog");
      box.setAttribute("aria-label", "Name this custom annotation");
      box.innerHTML = '<label class="visually-hidden" for="custom-term-input">What does this annotation show?</label>' +
        '<input id="custom-term-input" class="custom-term-input" maxlength="1000" autocomplete="off" placeholder="What does this annotation show?">' +
        '<button type="submit" class="custom-save">Save</button>' +
        '<button type="button" class="custom-cancel" aria-label="Cancel this custom annotation">×</button>' +
        '<p class="custom-error" role="alert" hidden></p>';
      const input = box.querySelector("input"), error = box.querySelector(".custom-error");
      box.addEventListener("submit", event => { event.preventDefault(); save(input, error); });
      box.querySelector(".custom-cancel").addEventListener("click", () => manager.cancelPendingCustom());
      box.addEventListener("keydown", event => {
        if (event.key === "Escape") { event.preventDefault(); manager.cancelPendingCustom(); }
      });
      input.addEventListener("input", () => { error.hidden = true; });
      document.body.append(box);
      place();
      input.focus();
    }

    async function save(input, error) {
      const term = input.value.trim(), shape = manager.pendingCustom;
      if (busy || !shape) return;
      if (!term) { error.textContent = "Name what this annotation shows."; error.hidden = false; input.focus(); return; }
      busy = true;
      box.classList.add("saving");
      try {
        const coordinates = manager.getCustomCoordinatesJSON(shape);
        const result = await request("/MedData/SaveCustomAnnotation", {method: "POST", body: JSON.stringify({Id: imageId, Term: term, Coordinates: coordinates})});
        saved.push({id: result.id, term, shapes: [shape]});
        manager.pendingCustom = null;
        closeBox();
        render();
        showStatus(`Custom annotation "${term}" saved. Draw another, or select Boundary Box or Freehand Contour to return to the current term.`);
      } catch (failure) {
        requestFailed("Saving a custom annotation", failure);
        if (box) { error.textContent = failure.message; error.hidden = false; }
      } finally {
        busy = false;
        box?.classList.remove("saving");
      }
    }

    async function removeCustom(item) {
      if (busy || !confirm(`Delete the custom annotation "${item.term}"?`)) return;
      busy = true;
      try {
        await request(`/MedData/DeleteCustomAnnotation?customAnnotationId=${encodeURIComponent(item.id)}`, {method: "POST"});
        saved.splice(saved.indexOf(item), 1);
        render();
        showStatus(`Custom annotation "${item.term}" deleted.`);
      } catch (failure) {
        requestFailed("Deleting a custom annotation", failure);
        showStatus(failure.message, "error");
      } finally {
        busy = false;
      }
    }

    manager.onCustomShape = shape => shape ? openBox() : closeBox();
    window.addEventListener("resize", place);
    document.addEventListener("scroll", place, true);

    return {
      // Restores the specialist's saved custom annotations once the image (and so its pixel size) is known.
      load(records) {
        saved.length = 0;
        for (const record of records || []) {
          try {
            saved.push({id: record.id, term: record.term, shapes: AnnotationGeometry.deserialize(record.coordinates, manager.canvas.width, manager.canvas.height)});
          } catch { /* A saved contour with other dimensions stays in the data; it is just not drawn here. */ }
        }
        render();
      },
      hasPending: () => Boolean(manager.pendingCustom),
      hasSaved: () => saved.length > 0,
      cancel: () => manager.cancelPendingCustom()
    };
  }
  window.setupCustomAnnotations = setupCustomAnnotations;
})();
