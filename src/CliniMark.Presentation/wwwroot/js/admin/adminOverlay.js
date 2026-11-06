// Read-only overlay of saved annotations for the admin image page. Shapes are drawn as SVG in the stored
// original-pixel space, so they stay exact at any display size. Validation reuses AnnotationGeometry.
(function (root) {
  "use strict";
  // Fixed categorical order (never cycled); anything past eight folds into one neutral "other" colour.
  const PALETTE = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];
  const OTHER = "#898781";

  function parse(items, geometry) {
    const shapes = [], problems = [];
    for (const item of items) {
      let value;
      try {
        value = geometry.validate(JSON.parse(item.coordinates));
      } catch (error) {
        problems.push({ id: item.id, term: item.term, annotator: item.annotator, reason: "stored in an older or invalid format, so it is not drawn" });
        continue;
      }
      value.annotations.forEach((shape, index) => shapes.push({
        key: `${item.id}-${index}`, id: item.id, term: item.term || "(no label)", annotator: item.annotator, role: item.role,
        type: shape.type, points: shape.points, width: value.imageWidth, height: value.imageHeight
      }));
    }
    return { shapes, problems };
  }

  function layerKey(shape, mode) {
    return mode === "annotator" ? `${shape.role}: ${shape.annotator}` : `${shape.role}: ${shape.term}`;
  }

  function layers(shapes, mode) {
    const names = [];
    for (const shape of shapes) {
      const name = layerKey(shape, mode);
      if (!names.includes(name)) names.push(name);
    }
    return names.map((name, index) => ({ name, colour: index < PALETTE.length ? PALETTE[index] : OTHER, count: shapes.filter(shape => layerKey(shape, mode) === name).length }));
  }

  function dimensions(shapes) {
    const sizes = [...new Set(shapes.map(shape => `${shape.width}x${shape.height}`))];
    return sizes.map(size => size.split("x").map(Number));
  }

  const api = { parse, layers, layerKey, dimensions, PALETTE, OTHER };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  root.AdminOverlay = api;

  if (typeof document === "undefined") return;
  const source = document.getElementById("overlay-data");
  const image = document.getElementById("admin-image");
  if (!source || !image || !root.AnnotationGeometry) return;
  const data = JSON.parse(source.textContent);
  const parsed = parse([...data.answers, ...data.groups, ...(data.customs || [])], root.AnnotationGeometry);
  const stage = document.getElementById("image-stage");
  const list = document.getElementById("layer-list");
  const notes = document.getElementById("overlay-notes");
  const svgNs = "http://www.w3.org/2000/svg";
  const hidden = new Set();
  let mode = "term";

  function note(text, kind) {
    const line = document.createElement("div");
    line.className = `alert alert-${kind} py-1 px-2 mb-1`;
    line.textContent = text;
    notes.appendChild(line);
  }

  function draw() {
    stage.querySelectorAll("svg").forEach(svg => svg.remove());
    list.innerHTML = "";
    if (!parsed.shapes.length) {
      list.innerHTML = '<span class="muted small">No shapes saved yet.</span>';
      return;
    }
    const [width, height] = dimensions(parsed.shapes)[0];
    const svg = document.createElementNS(svgNs, "svg");
    svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
    svg.setAttribute("preserveAspectRatio", "none");
    const legend = layers(parsed.shapes, mode);
    const colours = new Map(legend.map(layer => [layer.name, layer.colour]));
    for (const shape of parsed.shapes) {
      const name = layerKey(shape, mode);
      if (hidden.has(name)) continue;
      const points = shape.points.map(point => point.join(",")).join(" ");
      const halo = document.createElementNS(svgNs, "polygon");
      halo.setAttribute("points", points);
      halo.setAttribute("fill", "none");
      halo.setAttribute("stroke", "#ffffff");
      halo.setAttribute("stroke-width", "4");
      halo.setAttribute("vector-effect", "non-scaling-stroke");
      halo.dataset.shape = shape.id;
      const polygon = document.createElementNS(svgNs, "polygon");
      polygon.setAttribute("points", points);
      polygon.setAttribute("fill", colours.get(name));
      polygon.setAttribute("fill-opacity", "0.12");
      polygon.setAttribute("stroke", colours.get(name));
      polygon.setAttribute("stroke-width", "2");
      polygon.setAttribute("vector-effect", "non-scaling-stroke");
      polygon.dataset.shape = shape.id;
      const title = document.createElementNS(svgNs, "title");
      title.textContent = `${shape.term} — ${shape.annotator} (${shape.role.toLowerCase()})`;
      polygon.appendChild(title);
      polygon.addEventListener("mouseenter", () => focus(shape.id));
      polygon.addEventListener("mouseleave", () => focus(null));
      svg.append(halo, polygon);
    }
    stage.appendChild(svg);

    for (const layer of legend) {
      const label = document.createElement("label");
      label.className = "layer-toggle";
      const box = document.createElement("input");
      box.type = "checkbox";
      box.className = "form-check-input m-0";
      box.checked = !hidden.has(layer.name);
      box.addEventListener("change", () => { if (box.checked) hidden.delete(layer.name); else hidden.add(layer.name); draw(); });
      const swatch = document.createElement("span");
      swatch.className = "layer-swatch";
      swatch.style.background = layer.colour;
      const text = document.createElement("span");
      text.textContent = `${layer.name} (${layer.count})`;
      label.append(box, swatch, text);
      list.appendChild(label);
    }
  }

  function focus(id) {
    const svg = stage.querySelector("svg");
    if (svg) {
      svg.classList.toggle("has-focus", Boolean(id));
      svg.querySelectorAll("polygon").forEach(polygon => polygon.classList.toggle("focus", polygon.dataset.shape === id));
    }
    document.querySelectorAll("tr[data-shape]").forEach(row => row.classList.toggle("focus", row.dataset.shape === id));
  }

  document.querySelectorAll("tr[data-shape]").forEach(row => {
    row.addEventListener("mouseenter", () => focus(row.dataset.shape));
    row.addEventListener("mouseleave", () => focus(null));
  });
  document.querySelectorAll("input[name=colour-by]").forEach(input =>
    input.addEventListener("change", () => { mode = input.value; hidden.clear(); draw(); }));

  function checkSize() {
    for (const problem of parsed.problems) note(`${problem.term || "A shape"} by ${problem.annotator}: ${problem.reason}.`, "warning");
    const sizes = dimensions(parsed.shapes);
    if (sizes.length > 1) note("Shapes were saved against different image sizes; they are drawn against the first size.", "warning");
    if (sizes.length && image.naturalWidth && (sizes[0][0] !== image.naturalWidth || sizes[0][1] !== image.naturalHeight))
      note(`Shapes were saved on a ${sizes[0][0]}×${sizes[0][1]} image, but the link now serves ${image.naturalWidth}×${image.naturalHeight}. Shapes are scaled to fit.`, "warning");
  }

  if (image.complete && image.naturalWidth) checkSize();
  else {
    image.addEventListener("load", checkSize, { once: true });
    image.addEventListener("error", () => note("The image link did not load. Shapes are listed but cannot be shown over the image.", "danger"), { once: true });
  }
  draw();
})(typeof window !== "undefined" ? window : globalThis);
