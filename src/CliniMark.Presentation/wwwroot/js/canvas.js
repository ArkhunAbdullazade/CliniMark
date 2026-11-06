// Clockwise arrow (Bootstrap Icons "arrow-clockwise", MIT, bundled in lib/) shown over the rotation handle, with a
// white outline so it stays visible on dark and light images. Browsers without SVG cursors fall back to "grab".
const ROTATE_CURSOR = (() => {
  const shape = "<path fill-rule='evenodd' d='M8 3a5 5 0 1 0 4.546 2.914.5.5 0 0 1 .908-.417A6 6 0 1 1 8 2z'/>" +
    "<path d='M8 4.466V.534a.25.25 0 0 1 .41-.192l2.36 1.966c.12.1.12.284 0 .384L8.41 4.658A.25.25 0 0 1 8 4.466'/>";
  const svg = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='-2 -2 20 20'>" +
    `<g fill='white' stroke='white' stroke-width='2.4' stroke-linejoin='round'>${shape}</g><g fill='black'>${shape}</g></svg>`;
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}") 12 12, grab`;
})();

// Canvas Manager Class
class CanvasManager {
    constructor(canvasId, sourceImageId, containerId) {
      this.canvas = document.getElementById(canvasId);
      this.ctx = this.canvas.getContext("2d");
      this.sourceImage = document.getElementById(sourceImageId);
      this.container = document.getElementById(containerId);
      
      // Annotation state
      this.annotations = [];
      this.selectedAnnotation = null;
      this.currentAction = "none";
      this.currentMode = "rectangle";
      this.offset = { x: 0, y: 0 };
      this.activeHandle = null;
      this.resizingFixedCorner = null;
      this.originalMouseAngle = 0;
      this.originalAnnotationRotation = 0;
      this.originalAnnotation = null;
      this.rectStart = null;
      this.freehandPoints = [];
      this.currentDrawingAnnotation = null;
      this.isFirstDraw = true;
      this.ready = false;
      this.readOnly = false;
      this.imageIdentity = this.sourceImage.currentSrc || this.sourceImage.src;
      
      // Callback for annotation changes
      this.onAnnotationsChange = null;
      this.onInspectionChange = null;
  
      // Custom annotations (specialist page): contours outside the term list, named and saved one at a time by the page.
      // They never join this.annotations, so they cannot change the current term's answer.
      this.customAllowed = false;
      this.customShapes = [];
      this.pendingCustom = null;
      this.onCustomShape = null;

      // Zoom-to-area state
      this.zoomSelection = null;
      this.zoomDialog = null;
      this.zoomedArea = null;
      this._maxZoom = 8;
  
      // Setup event listeners
      this.setupEventListeners();
      this.setupImageLoad();
    }
  
    // RDP Algorithm for freehand simplification
    rdp(points, epsilon) {
      if (points.length < 3) return points;
      
      let dmax = 0;
      let index = 0;
      const start = points[0];
      const end = points[points.length - 1];
      
      for (let i = 1; i < points.length - 1; i++) {
        const d = this.perpendicularDistance(points[i], start, end);
        if (d > dmax) {
          index = i;
          dmax = d;
        }
      }
      
      if (dmax > epsilon) {
        const recResults1 = this.rdp(points.slice(0, index + 1), epsilon);
        const recResults2 = this.rdp(points.slice(index), epsilon);
        return recResults1.slice(0, -1).concat(recResults2);
      } else {
        return [start, end];
      }
    }
  
    perpendicularDistance(p, p1, p2) {
      const num = Math.abs(
        (p2.y - p1.y) * p.x - (p2.x - p1.x) * p.y + p2.x * p1.y - p2.y * p1.x
      );
      const den = Math.hypot(p2.y - p1.y, p2.x - p1.x);
      return den === 0 ? Math.hypot(p.x - p1.x, p.y - p1.y) : num / den;
    }
  
    adaptiveSimplification(points, minPoints = 8, maxPoints = 25) {
      // If we already have fewer points than minPoints, return as is
      if (points.length <= minPoints) return points;
      
      // If we have more than maxPoints, use RDP with varying epsilon to get a good count
      if (points.length > maxPoints) {
        // Start with a small epsilon and gradually increase until we get a reasonable number of points
        let epsilon = 1.5;  // Start with slightly higher epsilon for better simplification
        let simplified = this.rdp(points, epsilon);
        
        // If we still have too many points, increase epsilon until we get a reasonable count
        // but don't go below our minimum
        while (simplified.length > maxPoints && simplified.length > minPoints) {
          epsilon *= 1.5;  // Increase epsilon by 50% each time
          simplified = this.rdp(points, epsilon);
        }
        
        // If we have too few points, decrease epsilon until we get a reasonable count
        while (simplified.length < minPoints && epsilon > 0.5) {
          epsilon /= 1.5;  // Decrease epsilon
          simplified = this.rdp(points, epsilon);
        }
        
        return simplified;
      }
      
      // If number of points is already within our target range, just return them
      return points;
    }
  
    boundaryCheck(annotation) {
      if (annotation.type === "rectangle") {
        // Rectangle corners use original-image pixels.
        const hw = annotation.width / 2;
        const hh = annotation.height / 2;
        const cos = Math.cos(annotation.rotation);
        const sin = Math.sin(annotation.rotation);
        
        const corners = [
          { x: annotation.cx + (-hw * cos - hh * sin), y: annotation.cy + (-hw * sin + hh * cos) }, // top-left
          { x: annotation.cx + (hw * cos - hh * sin), y: annotation.cy + (hw * sin + hh * cos) },   // top-right
          { x: annotation.cx + (hw * cos + hh * sin), y: annotation.cy + (hw * sin - hh * cos) },   // bottom-right
          { x: annotation.cx + (-hw * cos + hh * sin), y: annotation.cy + (-hw * sin - hh * cos) }  // bottom-left
        ];
        
        // Find the bounding box of the rotated rectangle
        let minX = Math.min(...corners.map(c => c.x));
        let minY = Math.min(...corners.map(c => c.y));
        let maxX = Math.max(...corners.map(c => c.x));
        let maxY = Math.max(...corners.map(c => c.y));
        
        // Calculate boundary margins
        const margin = 0;
        const fit = Math.min(1, this.canvas.width / Math.max(1, maxX - minX), this.canvas.height / Math.max(1, maxY - minY));
        if (fit < 1) {
          annotation.width *= fit;
          annotation.height *= fit;
          return this.boundaryCheck(annotation);
        }
        
        // Calculate how much to move the rectangle to stay within bounds
        let moveX = 0;
        let moveY = 0;
        
        if (minX < margin) {
          moveX = margin - minX;
        } else if (maxX > this.canvas.width - margin) {
          moveX = this.canvas.width - margin - maxX;
        }
        
        if (minY < margin) {
          moveY = margin - minY;
        } else if (maxY > this.canvas.height - margin) {
          moveY = this.canvas.height - margin - maxY;
        }
        
        // Apply the movement to the center point
        annotation.cx += moveX;
        annotation.cy += moveY;
        
        return annotation;
      } else if (annotation.type === "freehand") {
        // Keep freehand points within canvas bounds
        const margin = 0;
        annotation.points = annotation.points.map(point => ({
          x: this.clamp(point.x, margin, this.canvas.width - margin),
          y: this.clamp(point.y, margin, this.canvas.height - margin)
        }));
        
        return annotation;
      }
      return annotation;
    }
  
    // Tool creation and management
    createTools(imageContainer, { custom = false } = {}) {
      const toolsContainer = document.createElement("div");
      toolsContainer.className = "tools-container";
      toolsContainer.style.position = "fixed";
      
      // Each tool slides out its name on hover; the longer description follows after two seconds (hoverDescriptions.js).
      const rectButton = this.createToolButton("rectangle", "../images/rectangle.svg", "Boundary Box", true,
        "Draw a rectangular boundary box around the area. Drag it to move it; use its corner handles to resize, the round handle to rotate, and X to delete.");
      
      const freehandButton = this.createToolButton("freehand", "../images/freehand.svg", "Freehand Contour", false,
        "Draw a closed freehand contour around the area. Drag it to move it, and use X to delete.");
      
      const customButton = custom ? this.createToolButton("custom", "../images/custom-annotation.svg", "Custom annotation", false,
        "Additionally annotate and register any medical term not mentioned or not highlighted in the image description") : null;

      const zoomButton = this.createToolButton("zoom", "../images/zoom-area.svg", "Zoom to Area", false,
        "Drag a box over the image to see it enlarged.");
      
      toolsContainer.appendChild(rectButton);
      toolsContainer.appendChild(freehandButton);
      if (customButton) toolsContainer.appendChild(customButton);
      toolsContainer.appendChild(zoomButton);
      
      document.body.appendChild(toolsContainer);
      this.toolsContainer = toolsContainer;
  
      this.rectButton = rectButton;
      this.freehandButton = freehandButton;
      this.customButton = customButton;
      this.zoomButton = zoomButton;
  
      this.updateToolsPosition(imageContainer);
      return custom ? { rectButton, freehandButton, customButton, zoomButton } : { rectButton, freehandButton, zoomButton };
    }
  
    createToolButton(type, iconSrc, title, isActive, description = "") {
      const button = document.createElement("button");
      button.type = "button";
      button.setAttribute("aria-label", title);
      button.setAttribute("aria-pressed", String(isActive));
      button.className = `tool-button${isActive ? " active" : ""}`;
      button.id = `${type}ModeButton`;
      // The name slides out beside the icon on hover or focus (see annotationWorkspace.css), replacing the slow browser tooltip.
      button.setAttribute("data-label", title);
      if (description) {
        button.setAttribute("data-description", description);
        button.setAttribute("aria-description", description);
      }

      const icon = document.createElement("img");
      icon.src = iconSrc;
      icon.alt = "";
      
      button.appendChild(icon);
      return button;
    }
  
    updateToolsPosition(imageContainer) {
      if (!this.toolsContainer) return;
      
      const canvasRect = this.canvas.getBoundingClientRect();
      const imageContainerRect = imageContainer.getBoundingClientRect();
      
      // Position the tools container with a larger margin of 15px
      // and vertically centered with the canvas
      this.toolsContainer.style.position = "fixed";
      this.toolsContainer.style.left = Math.min(canvasRect.right + 5, window.innerWidth - this.toolsContainer.offsetWidth - 6) + "px";
      this.toolsContainer.style.top = Math.max(6, canvasRect.top + (canvasRect.height / 2) - (this.toolsContainer.offsetHeight / 2)) + "px";
    }
  
    // Mode switching
    switchMode(newMode) {
      // Do nothing if trying to switch to the same mode
      if (this.currentMode === newMode) return;

      // Update the current mode for rectangle/freehand/custom/zoom
      this.cancelZoomSelection();
      // An unnamed custom contour is dropped when another tool is chosen.
      if (this.currentMode === "custom" && this.pendingCustom) this.cancelPendingCustom();
      this.currentMode = newMode;
      
      // Zooming and custom annotation leave the term's shapes alone, so none stays selected with handles that cannot be used.
      if ((newMode === "zoom" || newMode === "custom") && this.selectedAnnotation) this.selectedAnnotation = null;
      this.drawCanvas();

      // Exactly one of rectangle, freehand, custom and zoom is active
      for (const [mode, button] of [["rectangle", this.rectButton], ["freehand", this.freehandButton], ["custom", this.customButton], ["zoom", this.zoomButton]]) {
        if (!button) continue;
        button.classList.toggle("active", mode === newMode);
        button.setAttribute("aria-pressed", String(mode === newMode));
      }
      this.updateCursor();
      this.notifyInspection();
    }

    // Pages show a hint while zoom-to-area or custom annotation replaces drawing for the current term.
    notifyInspection() {
      const special = this.currentMode === "zoom" || this.currentMode === "custom";
      if (this.onInspectionChange) this.onInspectionChange(special, special ? this.currentMode : "zoom");
    }

    // Freehand drawing serves both the term's contours and custom annotations.
    drawsFreehand() {
      return this.currentMode === "freehand" || this.currentMode === "custom";
    }

    // The term tools lock while saving or reviewing; custom annotation follows its own permission from the page.
    locked() {
      return this.readOnly && !(this.currentMode === "custom" && this.customAllowed);
    }

    cancelPendingCustom() {
      if (!this.pendingCustom) return;
      this.pendingCustom = null;
      this.drawCanvas();
      if (this.onCustomShape) this.onCustomShape(null);
    }

    // One custom contour as the same versioned original-pixel JSON as term answers.
    getCustomCoordinatesJSON(shape) {
      if (!this.ready || this.imageIdentity !== (this.sourceImage.currentSrc || this.sourceImage.src)) throw new Error("Wait for the assigned image before saving.");
      return AnnotationGeometry.serialize([shape], this.canvas.width, this.canvas.height);
    }

    drawCustom(shape, pending) {
      if (!shape.points) return;
      this.ctx.save();
      this.ctx.beginPath();
      shape.points.forEach((point, index) => index === 0 ? this.ctx.moveTo(point.x, point.y) : this.ctx.lineTo(point.x, point.y));
      this.ctx.closePath();
      this.ctx.fillStyle = pending ? "rgba(26,111,224,0.14)" : "rgba(26,111,224,0.06)";
      this.ctx.fill();
      this.ctx.setLineDash(pending ? [] : [this.screenPixels(6), this.screenPixels(4)]);
      this.ctx.lineWidth = this.screenPixels(pending ? 3 : 2);
      this.ctx.strokeStyle = "#1a6fe0";
      this.ctx.stroke();
      this.ctx.restore();
    }
  
    // Canvas drawing and manipulation
    drawCanvas() {
      if (!this.ready) return;
      this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
      
      this.ctx.drawImage(this.sourceImage, 0, 0, this.canvas.width, this.canvas.height);
      this.annotations.forEach(ann => this.drawAnnotation(ann, ann === this.selectedAnnotation));
      // Saved custom contours show only while that tool is in use, so they never clutter the term's work.
      if (this.currentMode === "custom") this.customShapes.forEach(shape => this.drawCustom(shape, false));
      if (this.pendingCustom) this.drawCustom(this.pendingCustom, true);
      if (this.currentAction === "draw" && this.currentDrawingAnnotation) {
        if (this.drawsFreehand()) this.drawFreehandPreview();
        else this.drawRectangle(this.currentDrawingAnnotation, true);
      }
    }
  
    drawAnnotation(ann, isSelected) {
      if (ann.type === "rectangle") {
        this.drawRectangle(ann, isSelected);
      } else if (ann.type === "freehand") {
        this.drawFreehand(ann, isSelected);
      }
      
      if (isSelected) {
        this.drawHandles(ann);
      }
    }
  
    drawRectangle(ann, isSelected) {
      this.ctx.save();
      this.ctx.translate(ann.cx, ann.cy);
      this.ctx.rotate(ann.rotation);
      this.ctx.fillStyle = isSelected ? "rgba(255,0,0,0.06)" : "rgba(0,0,255,0.06)";
      this.ctx.fillRect(-ann.width / 2, -ann.height / 2, ann.width, ann.height);
      this.ctx.lineWidth = this.screenPixels(4);
      this.ctx.strokeStyle = "white";
      this.ctx.strokeRect(-ann.width / 2, -ann.height / 2, ann.width, ann.height);
      this.ctx.strokeStyle = isSelected ? "red" : "blue";
      this.ctx.lineWidth = this.screenPixels(2);
      this.ctx.strokeRect(-ann.width / 2, -ann.height / 2, ann.width, ann.height);
      
      this.ctx.restore();
    }
  
    drawFreehand(ann, isSelected) {
      this.ctx.beginPath();
      ann.points.forEach((pt, index) => {
        if (index === 0) this.ctx.moveTo(pt.x, pt.y);
        else this.ctx.lineTo(pt.x, pt.y);
      });
      
      // Close the path if it's not closed
      if (ann.points.length > 2) {
        const first = ann.points[0];
        const last = ann.points[ann.points.length - 1];
        if (Math.hypot(last.x - first.x, last.y - first.y) > 5) {
          this.ctx.lineTo(first.x, first.y);
        }
      }
      
      // Add white border first
      this.ctx.strokeStyle = 'white';
      this.ctx.lineWidth = this.screenPixels(4);
      this.ctx.stroke();
      
      // Then add colored border
      this.ctx.strokeStyle = isSelected ? "red" : "blue";
      this.ctx.lineWidth = this.screenPixels(2);
      this.ctx.stroke();
      
      // Add fill with transparency
      this.ctx.fillStyle = isSelected ? "rgba(255,0,0,0.06)" : "rgba(0,0,255,0.06)";
      this.ctx.fill();
    }

    drawFreehandPreview() {
      if (!this.freehandPoints.length) return;
      this.ctx.save();
      this.ctx.beginPath();
      this.freehandPoints.forEach((point, index) => {
        if (index === 0) this.ctx.moveTo(point.x, point.y);
        else this.ctx.lineTo(point.x, point.y);
      });
      this.ctx.strokeStyle = "white";
      this.ctx.lineWidth = this.screenPixels(4);
      this.ctx.stroke();
      this.ctx.strokeStyle = "red";
      this.ctx.lineWidth = this.screenPixels(2);
      this.ctx.stroke();
      if (this.freehandPoints.length > 2) {
        const first = this.freehandPoints[0], last = this.freehandPoints[this.freehandPoints.length - 1];
        this.ctx.beginPath();
        this.ctx.moveTo(last.x, last.y);
        this.ctx.lineTo(first.x, first.y);
        this.ctx.setLineDash([this.screenPixels(4), this.screenPixels(4)]);
        this.ctx.strokeStyle = "white";
        this.ctx.lineWidth = this.screenPixels(4);
        this.ctx.stroke();
        this.ctx.strokeStyle = "red";
        this.ctx.lineWidth = this.screenPixels(2);
        this.ctx.stroke();
      }
      this.ctx.restore();
    }
  
    drawHandles(ann) {
      if (ann.type === "rectangle") {
        const handles = this.getAnnotationHandles(ann);
        this.drawRectangleHandles(handles);
      } else if (ann.type === "freehand") {
        this.drawFreehandHandles(ann);
      }
    }
  
    drawRectangleHandles(handles) {
      const size = this.screenPixels(8);
      const top = {x: (handles.tl.x + handles.tr.x) / 2, y: (handles.tl.y + handles.tr.y) / 2};
      this.ctx.beginPath();
      this.ctx.moveTo(top.x, top.y);
      this.ctx.lineTo(handles.rotate.x, handles.rotate.y);
      this.ctx.strokeStyle = "white";
      this.ctx.lineWidth = this.screenPixels(3);
      this.ctx.stroke();
      this.ctx.strokeStyle = "#a9000e";
      this.ctx.lineWidth = this.screenPixels(1);
      this.ctx.stroke();
      this.ctx.fillStyle = "white";
      this.ctx.lineWidth = this.screenPixels(1);
      this.ctx.strokeStyle = "#a9000e";
      
      // Draw resize handles
      ['tl', 'tr', 'bl', 'br'].forEach(key => {
        let h = handles[key];
        this.ctx.fillRect(h.x - size/2, h.y - size/2, size, size);
        this.ctx.strokeRect(h.x - size/2, h.y - size/2, size, size);
      });
      
      // Draw rotation handle
      const rHandle = handles.rotate;
      this.ctx.beginPath();
      this.ctx.arc(rHandle.x, rHandle.y, this.screenPixels(5), 0, 2 * Math.PI);
      this.ctx.fillStyle = "#a9000e";
      this.ctx.fill();
      this.ctx.lineWidth = this.screenPixels(1);
      this.ctx.strokeStyle = "white";
      this.ctx.stroke();
      
      this.drawDeleteHandle(handles.delete.x, handles.delete.y);
    }

    drawFreehandHandles(ann) {
      const handle = this.getFreehandDeleteHandle(ann), half = this.deleteHandleSize() / 2;
      this.drawDeleteHandle(handle.x + half, handle.y + half);
    }

    // Delete handle sized for a quick, sure click: an 18px white square with a bold drawn X.
    deleteHandleSize() {
      return this.screenPixels(18);
    }

    drawDeleteHandle(x, y) {
      const half = this.deleteHandleSize() / 2, arm = half * 0.5;
      this.ctx.fillStyle = "white";
      this.ctx.fillRect(x - half, y - half, half * 2, half * 2);
      this.ctx.lineWidth = this.screenPixels(1);
      this.ctx.strokeStyle = "black";
      this.ctx.strokeRect(x - half, y - half, half * 2, half * 2);
      this.ctx.beginPath();
      this.ctx.moveTo(x - arm, y - arm);
      this.ctx.lineTo(x + arm, y + arm);
      this.ctx.moveTo(x + arm, y - arm);
      this.ctx.lineTo(x - arm, y + arm);
      this.ctx.lineWidth = this.screenPixels(2);
      this.ctx.lineCap = "round";
      this.ctx.stroke();
      this.ctx.lineCap = "butt";
    }
  
    // Zoom-to-area: a blue box drawn over the image opens that area enlarged in a dialog. It only
    // inspects, so it never changes shapes, the unsaved state or the drawing timer.
    startZoomSelection(e) {
      const box = document.createElement("div");
      box.className = "zoom-selection";
      this.container.appendChild(box);
      const start = this.pointInDisplay(e);
      this.zoomSelection = { pointerId: e.pointerId, start, end: start, box };
      this.placeZoomBox(box, start.x, start.y, 0, 0);
    }

    updateZoomSelection(e) {
      const selection = this.zoomSelection;
      if (!selection || e.pointerId !== selection.pointerId) return;
      const start = selection.start, end = selection.end = this.pointInDisplay(e);
      this.placeZoomBox(selection.box, Math.min(start.x, end.x), Math.min(start.y, end.y), Math.abs(end.x - start.x), Math.abs(end.y - start.y));
    }

    finishZoomSelection(e) {
      const selection = this.zoomSelection;
      if (!selection || e.pointerId !== selection.pointerId) return;
      this.updateZoomSelection(e);
      this.zoomSelection = null;
      const rect = this.canvas.getBoundingClientRect(), { start, end, box } = selection;
      let left = Math.min(start.x, end.x), top = Math.min(start.y, end.y);
      let width = Math.abs(end.x - start.x), height = Math.abs(end.y - start.y);
      if (width < 8 || height < 8) {
        // A click enlarges a square around that point.
        width = height = Math.min(rect.width, rect.height) / 3;
        left = this.clamp(end.x - width / 2, 0, rect.width - width);
        top = this.clamp(end.y - height / 2, 0, rect.height - height);
        this.placeZoomBox(box, left, top, width, height);
      }
      const scaleX = this.canvas.width / rect.width, scaleY = this.canvas.height / rect.height;
      this.openZoomView({ x: left * scaleX, y: top * scaleY, width: width * scaleX, height: height * scaleY }, box);
    }

    cancelZoomSelection() {
      if (this.zoomSelection) this.zoomSelection.box.remove();
      this.zoomSelection = null;
    }

    pointInDisplay(e) {
      const rect = this.canvas.getBoundingClientRect();
      return { x: this.clamp(e.clientX - rect.left, 0, rect.width), y: this.clamp(e.clientY - rect.top, 0, rect.height) };
    }

    placeZoomBox(box, left, top, width, height) {
      Object.assign(box.style, { left: `${left}px`, top: `${top}px`, width: `${width}px`, height: `${height}px` });
    }

    // The area fills the dialog, drawn from the full-resolution source image, but never beyond 8x its size on
    // the page, where enlarging only adds blur.
    openZoomView(region, box) {
      const dialog = this.zoomDialog || this.createZoomDialog();
      const onPage = this.canvas.getBoundingClientRect().width / this.canvas.width;
      // Some darkened page stays visible around the dialog; its header and padding take about 88px.
      const roomWidth = Math.min(window.innerWidth * 0.92, 1600) - 24, roomHeight = window.innerHeight * 0.9 - 88;
      const scale = Math.min(roomWidth / region.width, roomHeight / region.height, onPage * this._maxZoom);
      const width = Math.max(1, Math.round(region.width * scale)), height = Math.max(1, Math.round(region.height * scale));
      const view = dialog.querySelector("canvas");
      const density = Math.min(window.devicePixelRatio || 1, 4096 / Math.max(width, height));
      view.width = Math.max(1, Math.round(width * density));
      view.height = Math.max(1, Math.round(height * density));
      view.style.width = `${width}px`;
      view.style.height = `${height}px`;
      const ctx = view.getContext("2d");
      ctx.imageSmoothingQuality = "high";
      ctx.clearRect(0, 0, view.width, view.height);
      ctx.drawImage(this.sourceImage, region.x, region.y, region.width, region.height, 0, 0, view.width, view.height);
      this.zoomedArea = box;
      if (!dialog.open) dialog.showModal();
    }

    createZoomDialog() {
      const dialog = document.createElement("dialog");
      dialog.className = "annotation-dialog zoom-dialog";
      // No visible title: the enlarged image speaks for itself, so the header holds only the close button.
      dialog.setAttribute("aria-label", "Enlarged area");
      dialog.innerHTML = '<div class="annotation-dialog-header zoom-dialog-header"><button type="button" class="btn-close" aria-label="Close"></button></div>' +
        '<div class="zoom-dialog-body"><canvas role="img" aria-label="Enlarged part of the image"></canvas></div>';
      dialog.querySelector(".btn-close").addEventListener("click", () => dialog.close());
      // Only a press that starts and ends on the dark backdrop closes it, so a drag that leaves the dialog does not.
      let fromBackdrop = false;
      dialog.addEventListener("pointerdown", e => { fromBackdrop = e.target === dialog; });
      dialog.addEventListener("click", e => { if (fromBackdrop && e.target === dialog) dialog.close(); });
      dialog.addEventListener("close", () => {
        if (this.zoomedArea) this.zoomedArea.remove();
        this.zoomedArea = null;
      });
      document.body.appendChild(dialog);
      this.zoomDialog = dialog;
      return dialog;
    }

    closeZoomView() {
      if (this.zoomDialog && this.zoomDialog.open) this.zoomDialog.close();
    }

    // Ends any gesture or zoom without keeping it and leaves the canvas read only, e.g. when the image
    // is released while its page is open.
    stopInteraction() {
      if (this.originalAnnotation && this.selectedAnnotation) Object.assign(this.selectedAnnotation, this.originalAnnotation);
      this.closeZoomView();
      this.customAllowed = false;
      this.switchMode("rectangle");
      this.selectedAnnotation = null;
      this.resetDrawingState();
      this.readOnly = true;
      if (this.toolsContainer) this.toolsContainer.querySelectorAll("button").forEach(button => { button.disabled = true; });
      this.drawCanvas();
      this.updateCursor();
    }

    // Helper functions
    getAnnotationHandles(ann) {
      const hw = ann.width / 2, hh = ann.height / 2;
      const localHandles = {
        tl: { x: -hw, y: -hh },
        tr: { x: hw, y: -hh },
        bl: { x: -hw, y: hh },
        br: { x: hw, y: hh },
        rotate: { x: 0, y: -hh - this.screenPixels(30) },
        delete: { x: hw + this.screenPixels(14), y: -hh - this.screenPixels(14) },
      };
      
      const handles = {};
      for (let key in localHandles) {
        const local = localHandles[key];
        const cos = Math.cos(ann.rotation),
              sin = Math.sin(ann.rotation);
        handles[key] = {
          x: ann.cx + local.x * cos - local.y * sin,
          y: ann.cy + local.x * sin + local.y * cos,
        };
        if (key === "delete" || key === "rotate") {
          const margin = this.screenPixels(key === "delete" ? 10 : 7);
          handles[key].x = this.clamp(handles[key].x, margin, this.canvas.width - margin);
          handles[key].y = this.clamp(handles[key].y, margin, this.canvas.height - margin);
        }
      }
      return handles;
    }
  
    getBoundingBox(points) {
      let minX = Infinity, minY = Infinity,
          maxX = -Infinity, maxY = -Infinity;
      
      points.forEach(pt => {
        minX = Math.min(minX, pt.x);
        minY = Math.min(minY, pt.y);
        maxX = Math.max(maxX, pt.x);
        maxY = Math.max(maxY, pt.y);
      });
      
      return { minX, minY, maxX, maxY };
    }
  
    isPointInAnnotation(ann, x, y) {
      if (ann.type === "rectangle") {
        const dx = x - ann.cx, dy = y - ann.cy;
        const cos = Math.cos(-ann.rotation),
              sin = Math.sin(-ann.rotation);
        const localX = dx * cos - dy * sin;
        const localY = dx * sin + dy * cos;
        return Math.abs(localX) <= ann.width / 2 && Math.abs(localY) <= ann.height / 2;
      } else if (ann.type === "freehand") {
        for (let i = 0; i < ann.points.length - 1; i++) {
          if (this.distanceToSegment({ x, y }, ann.points[i], ann.points[i + 1]) < this.screenPixels(5)) {
            return true;
          }
        }
        return false;
      }
    }
  
    distanceToSegment(p, v, w) {
      const l2 = (w.x - v.x) ** 2 + (w.y - v.y) ** 2;
      if (l2 === 0) return Math.hypot(p.x - v.x, p.y - v.y);
      let t = ((p.x - v.x) * (w.x - v.x) + (p.y - v.y) * (w.y - v.y)) / l2;
      t = Math.max(0, Math.min(1, t));
      const proj = {
        x: v.x + t * (w.x - v.x),
        y: v.y + t * (w.y - v.y)
      };
      return Math.hypot(p.x - proj.x, p.y - proj.y);
    }
  
    isPointNearHandle(point, handle, radius = 8) {
      const dx = point.x - handle.x,
            dy = point.y - handle.y;
      return Math.hypot(dx, dy) < this.screenPixels(radius);
    }
  
    // State management
    resetDrawingState() {
      this.currentDrawingAnnotation = null;
      this.rectStart = null;
      this.freehandPoints = [];
      this.currentAction = "none";
      this.originalAnnotation = null;
      this.activeHandle = null;
    }
  
    resetAnnotation() {
      this.annotations = [];
      this.selectedAnnotation = null;
      this.currentAction = "none";
      this.rectStart = null;
      this.freehandPoints = [];
      this.currentDrawingAnnotation = null;
      this.drawCanvas();
      this.isFirstDraw = true;
    }
  
    // Event handlers setup
    setupEventListeners() {
      this.canvas.tabIndex = 0;
      this.canvas.setAttribute("aria-label", "Medical image annotation canvas. Draw with the pointer. Delete removes the selected shape.");
      this.canvas.addEventListener("keydown", e => {
        if (this.readOnly || !this.selectedAnnotation || !["Delete", "Backspace"].includes(e.key)) return;
        e.preventDefault();
        this.annotations = this.annotations.filter(annotation => annotation !== this.selectedAnnotation);
        this.selectedAnnotation = null;
        this.resetDrawingState(); this.drawCanvas();
        if (this.onAnnotationsChange) this.onAnnotationsChange();
      });
      this.canvas.addEventListener("pointerdown", e => {
        if (e.button !== 0 || !this.ready) return;
        // Zooming works while saved terms are reviewed too; it never changes shapes.
        if (this.currentMode === "zoom") {
          if (this.zoomSelection) return;
          e.preventDefault();
          this.canvas.setPointerCapture(e.pointerId);
          this.startZoomSelection(e);
          return;
        }
        if (this.locked()) return;
        e.preventDefault();
        this.canvas.focus({preventScroll:true});
        this.canvas.setPointerCapture(e.pointerId);
        this.handleMouseDown(e);
        this.updateCursor(this.eventPoint(e));
      });
      this.canvas.addEventListener("pointermove", e => this.zoomSelection ? this.updateZoomSelection(e) : this.handleMouseMove(e));
      this.canvas.addEventListener("pointerup", e => {
        if (this.zoomSelection) this.finishZoomSelection(e);
        else this.handleMouseUp(e);
        if (this.canvas.hasPointerCapture(e.pointerId)) this.canvas.releasePointerCapture(e.pointerId);
      });
      this.canvas.addEventListener("pointercancel", () => {
        if (this.zoomSelection) { this.cancelZoomSelection(); return; }
        // Interrupted gestures must never become saveable partial shapes.
        if (this.originalAnnotation && this.selectedAnnotation) Object.assign(this.selectedAnnotation, this.originalAnnotation);
        this.resetDrawingState(); this.drawCanvas();
        this.updateCursor();
      });
      this.canvas.addEventListener("pointerleave", () => {
        if (this.currentAction === "none") this.updateCursor();
      });
    }
  
    setupImageLoad() {
      const loaded = () => {
        if (!this.sourceImage.naturalWidth || !this.sourceImage.naturalHeight) {
          this.ready = false;
          this.reportImageError("This image has no usable size, so it cannot be annotated. Reload the page.");
          return;
        }
        const identity = this.sourceImage.currentSrc || this.sourceImage.src;
        if (identity !== this.imageIdentity) {
          this.ready = false;
          this.reportImageError("The source image changed. Reload this assignment before annotating.");
          return;
        }
        // The backing store always has the original dimensions. Device pixel ratio
        // therefore does not change geometry, and CSS resizing cannot change it.
        this.canvas.width = this.sourceImage.naturalWidth;
        this.canvas.height = this.sourceImage.naturalHeight;
        this.ready = true;
        this.resizeDisplay();
        this.canvas.dispatchEvent(new CustomEvent("annotationimageready"));
      };
      this.sourceImage.addEventListener("load", loaded);
      this.sourceImage.addEventListener("error", () => {
        this.ready = false;
        this.reportImageError("This image could not be loaded, so annotations cannot be saved. Check your connection and reload the page.");
      });
      if (this.sourceImage.complete) {
        if (this.sourceImage.naturalWidth) loaded();
        else this.reportImageError("This image could not be loaded. Check your connection and reload the page.");
      }
      window.addEventListener("resize", () => this.resizeDisplay());
      if (typeof ResizeObserver !== "undefined") {
        this.resizeObserver = new ResizeObserver(() => this.resizeDisplay());
        this.resizeObserver.observe(this.container.closest(".image-viewport") || this.container.closest(".left-column") || this.container.parentElement);
      }
      document.addEventListener("scroll", () => {
        this.updateToolsPosition(document.getElementById("image-container"));
      }, true);
    }

    resizeDisplay() {
      if (!this.ready) return;
      const imageContainer = this.container.parentElement;
      const column = imageContainer.closest(".left-column");
      const viewport = imageContainer.closest(".image-viewport");
      // The tool strip sits just right of the centred image, so keep its width (plus a gap) free on both sides.
      const toolRoom = this.toolsContainer ? 2 * (this.toolsContainer.offsetWidth + 6) : 0;
      const availableWidth = Math.max(1, (viewport ? viewport.clientWidth : column ? column.clientWidth : window.innerWidth - 80) - 8 - toolRoom);
      // The buttons sit inside the viewport directly under the image, so their row is kept free below it.
      const buttons = viewport?.querySelector?.(".buttons-section");
      const buttonRoom = buttons ? buttons.offsetHeight + (parseFloat(getComputedStyle(viewport).rowGap) || 0) : 0;
      const availableHeight = viewport ? Math.max(1, viewport.clientHeight - 8 - buttonRoom) : 520;
      const scale = Math.min(1, availableWidth / this.canvas.width, availableHeight / this.canvas.height);
      const width = this.canvas.width * scale, height = this.canvas.height * scale;
      this.canvas.style.width = `${width}px`;
      this.canvas.style.height = `${height}px`;
      this.container.style.width = `${width}px`;
      this.container.style.height = `${height}px`;
      imageContainer.style.width = `${width}px`;
      imageContainer.style.height = `${height}px`;
      this.drawCanvas();
      this.updateToolsPosition(imageContainer);
    }

    reportImageError(message) {
      const status = document.getElementById("annotation-status");
      if (status) { status.textContent = message; status.dataset.state = "error"; }
      this.canvas.dispatchEvent(new CustomEvent("annotationimageerror", {detail: {message}}));
    }

    screenPixels(value) {
      const width = this.canvas.getBoundingClientRect().width;
      return width > 0 ? value * this.canvas.width / width : value;
    }

    eventPoint(e) {
      return AnnotationGeometry.clientToImage(e.clientX, e.clientY, this.canvas.getBoundingClientRect(), this.canvas.width, this.canvas.height);
    }

    getFreehandDeleteHandle(annotation) {
      const bounds = this.getBoundingBox(annotation.points), gap = this.screenPixels(5), size = this.deleteHandleSize();
      return { x: this.clamp(bounds.maxX + gap, 0, this.canvas.width - size), y: this.clamp(bounds.minY - gap, 0, this.canvas.height - size) };
    }
  
    // Mouse event handlers
    updateCursor(point) {
      let cursor = "crosshair", hint = "Drag to draw an annotation.";
      if (this.currentMode === "zoom" && this.ready) {
        cursor = "zoom-in";
        hint = "Drag a box over an area, or click a point, to see it enlarged.";
      } else if (!this.ready || this.locked()) {
        cursor = "default";
        hint = "";
      } else if (this.currentMode === "custom") {
        hint = this.pendingCustom ? "Name and save this custom annotation, or cancel it." : "Draw a closed contour around the finding, then name it.";
      } else if (this.currentAction === "rotate") {
        cursor = ROTATE_CURSOR; hint = "Drag to rotate.";
      } else if (this.currentAction === "move") {
        cursor = "grabbing"; hint = "Drag to move.";
      } else if (this.currentAction !== "draw") {
        let corner = this.currentAction === "resize" ? this.activeHandle : null;
        const selected = this.selectedAnnotation;
        if (point && selected && selected.type === "rectangle") {
          const handles = this.getAnnotationHandles(selected);
          if (this.isPointNearHandle(point, handles.rotate)) {
            cursor = ROTATE_CURSOR; hint = "Drag to rotate.";
          } else {
            corner = corner || ['tl', 'tr', 'bl', 'br'].find(key => this.isPointNearHandle(point, handles[key]));
            if (!corner && this.isPointNearHandle(point, handles.delete, 11)) {
              cursor = "pointer"; hint = "Delete this annotation.";
            }
          }
        } else if (point && selected && selected.type === "freehand") {
          const handle = this.getFreehandDeleteHandle(selected), size = this.deleteHandleSize();
          if (point.x >= handle.x && point.x <= handle.x + size && point.y >= handle.y && point.y <= handle.y + size) {
            cursor = "pointer"; hint = "Delete this annotation.";
          }
        }
        if (corner && selected) {
          const angle = selected.rotation + (corner === "tl" || corner === "br" ? Math.PI / 4 : -Math.PI / 4);
          const direction = ((Math.round(angle / (Math.PI / 4)) % 4) + 4) % 4;
          cursor = ["ew-resize", "nwse-resize", "ns-resize", "nesw-resize"][direction];
          hint = "Drag this corner to resize.";
        } else if (cursor === "crosshair" && point && this.annotations.some(annotation => this.isPointInAnnotation(annotation, point.x, point.y))) {
          cursor = "move"; hint = "Drag to move. Select to show editing handles.";
        }
      }
      this.canvas.style.cursor = cursor;
      this.canvas.title = hint;
    }

    handleMouseDown(e) {
      if (!this.ready || this.locked()) return;
      const { x: mouseX, y: mouseY } = this.eventPoint(e);
      // Custom annotation only draws new contours; one waits for its name before the next can start.
      if (this.currentMode === "custom") {
        if (this.pendingCustom) return;
        this.selectedAnnotation = null;
        this.currentAction = "draw";
        this.freehandPoints = [{ x: mouseX, y: mouseY }];
        this.currentDrawingAnnotation = { type: "freehand", points: [...this.freehandPoints] };
        this.drawCanvas();
        return;
      }
      
      // Check for freehand delete button first
      if (this.selectedAnnotation && this.selectedAnnotation.type === "freehand") {
        const handle = this.getFreehandDeleteHandle(this.selectedAnnotation);
        const handleX = handle.x, handleY = handle.y, size = this.deleteHandleSize();
        
        if (mouseX >= handleX && mouseX <= handleX + size &&
            mouseY >= handleY && mouseY <= handleY + size) {
          this.annotations = this.annotations.filter(a => a !== this.selectedAnnotation);
          this.selectedAnnotation = null;
          this.drawCanvas();
          
          // Notify about annotation change
          if (this.onAnnotationsChange) {
            this.onAnnotationsChange();
          }
          return;
        }
      }
      
      // Check if we clicked on the selected annotation's handle
      if (this.selectedAnnotation && this.selectedAnnotation.type === "rectangle") {
        const handles = this.getAnnotationHandles(this.selectedAnnotation);
        
        // Check rotation handle
        if (this.isPointNearHandle({ x: mouseX, y: mouseY }, handles.rotate)) {
          this.currentAction = "rotate";
          this.originalAnnotation = structuredClone(this.selectedAnnotation);
          this.originalMouseAngle = Math.atan2(mouseY - this.selectedAnnotation.cy, mouseX - this.selectedAnnotation.cx);
          this.originalAnnotationRotation = this.selectedAnnotation.rotation;
          return;
        }
        
        // Check resize handles before delete: at the image edge the delete handle is pushed next to a corner,
        // and a mistaken resize is harmless while a mistaken delete loses the shape.
        for (const corner of ['tl', 'tr', 'bl', 'br']) {
          if (this.isPointNearHandle({ x: mouseX, y: mouseY }, handles[corner])) {
            this.currentAction = "resize";
            this.activeHandle = corner;
            this.resizingFixedCorner = {
              tl: 'br', tr: 'bl', bl: 'tr', br: 'tl'
            }[corner];

            // Store the original annotation with all properties
            this.originalAnnotation = JSON.parse(JSON.stringify(this.selectedAnnotation));

            // Store fixed corner location in rotated space
            const fixedCornerPos = handles[this.resizingFixedCorner];
            this.fixedCornerX = fixedCornerPos.x;
            this.fixedCornerY = fixedCornerPos.y;

            return;
          }
        }

        // Check delete handle
        if (this.isPointNearHandle({ x: mouseX, y: mouseY }, handles.delete, 11)) {
          this.annotations = this.annotations.filter(a => a !== this.selectedAnnotation);
          this.selectedAnnotation = null;
          this.drawCanvas();

          // Notify about annotation change
          if (this.onAnnotationsChange) {
            this.onAnnotationsChange();
          }
          return;
        }
      }
      
      // Check if we clicked on an existing annotation
      for (let i = this.annotations.length - 1; i >= 0; i--) {
        const ann = this.annotations[i];
        if (this.isPointInAnnotation(ann, mouseX, mouseY)) {
          this.selectedAnnotation = ann;
          this.currentAction = "move";
          this.offset = { x: mouseX, y: mouseY };
          this.originalAnnotation = structuredClone(ann);
          this.drawCanvas();
          return;
        }
      }
      
      // If we didn't click on anything, start drawing a new annotation
      this.selectedAnnotation = null;
      
      if (this.currentMode === "rectangle") {
        this.currentAction = "draw";
        this.rectStart = { x: mouseX, y: mouseY };
        this.currentDrawingAnnotation = {
          type: "rectangle",
          cx: mouseX,
          cy: mouseY,
          width: 0,
          height: 0,
          rotation: 0
        };
      } else if (this.currentMode === "freehand") {
        this.currentAction = "draw";
        this.freehandPoints = [{ x: mouseX, y: mouseY }];
        this.currentDrawingAnnotation = {
          type: "freehand",
          points: [...this.freehandPoints]
        };
      }
      
      this.drawCanvas();
    }
  
    handleMouseMove(e) {
      if (!this.ready || this.locked()) return;
      const { x: mouseX, y: mouseY } = this.eventPoint(e);
      this.updateCursor({x: mouseX, y: mouseY});
      
      switch (this.currentAction) {
        case "move":
          if (this.selectedAnnotation) {
            const dx = mouseX - this.offset.x, dy = mouseY - this.offset.y;
            if (this.selectedAnnotation.type === "rectangle") {
              this.selectedAnnotation.cx = this.originalAnnotation.cx + dx;
              this.selectedAnnotation.cy = this.originalAnnotation.cy + dy;
            } else {
              const bounds = this.getBoundingBox(this.originalAnnotation.points);
              const boundedX = this.clamp(dx, -bounds.minX, this.canvas.width - bounds.maxX);
              const boundedY = this.clamp(dy, -bounds.minY, this.canvas.height - bounds.maxY);
              this.selectedAnnotation.points = this.originalAnnotation.points.map(p => ({x:p.x+boundedX,y:p.y+boundedY}));
            }
            this.selectedAnnotation = this.boundaryCheck(this.selectedAnnotation);
            this.drawCanvas();
          }
          break;
          
        case "resize":
          if (this.selectedAnnotation && this.selectedAnnotation.type === "rectangle" && this.originalAnnotation) {
            // Get original rotation from stored annotation
            const rotation = this.originalAnnotation.rotation;
            
            // Get the fixed corner position from when we started the resize
            const fixedCornerX = this.fixedCornerX;
            const fixedCornerY = this.fixedCornerY;

            // Transform mouse coordinates to rectangle's rotated coordinate system
            const translatedMouseX = mouseX - fixedCornerX;
            const translatedMouseY = mouseY - fixedCornerY;
            
            // Rotate to align with rectangle's orientation
            const cos = Math.cos(-rotation);
            const sin = Math.sin(-rotation);
            const rotatedMouseX = translatedMouseX * cos - translatedMouseY * sin;
            const rotatedMouseY = translatedMouseX * sin + translatedMouseY * cos;
            
            // Calculate dimensions in rotated space, never below what the page lets a new shape be
            const width = Math.max(this.minimumShapeSize(), Math.abs(rotatedMouseX));
            const height = Math.max(this.minimumShapeSize(), Math.abs(rotatedMouseY));
            
            // Calculate center point
            // First determine the direction of resize based on which corner is being dragged
            const dirX = rotatedMouseX >= 0 ? 1 : -1;
            const dirY = rotatedMouseY >= 0 ? 1 : -1;
            
            // Calculate new center point in rotated space
            const rotatedCenterX = (width / 2) * dirX;
            const rotatedCenterY = (height / 2) * dirY;
            
            // Transform the center back to image coordinates.
            const screenCos = Math.cos(rotation);
            const screenSin = Math.sin(rotation);
            const centerOffsetX = rotatedCenterX * screenCos - rotatedCenterY * screenSin;
            const centerOffsetY = rotatedCenterX * screenSin + rotatedCenterY * screenCos;
            
            // Final center position
            const centerX = fixedCornerX + centerOffsetX;
            const centerY = fixedCornerY + centerOffsetY;
            
            // Update the rectangle
            this.selectedAnnotation.width = width;
            this.selectedAnnotation.height = height;
            this.selectedAnnotation.cx = centerX;
            this.selectedAnnotation.cy = centerY;
            this.selectedAnnotation.rotation = rotation; // Preserve the original rotation
            
            // Apply boundary check
            this.selectedAnnotation = this.boundaryCheck(this.selectedAnnotation);
            this.drawCanvas();
          }
          break;
          
        case "rotate":
          if (this.selectedAnnotation && this.selectedAnnotation.type === "rectangle") {
            const newAngle = Math.atan2(mouseY - this.selectedAnnotation.cy, mouseX - this.selectedAnnotation.cx);
            const angleDiff = newAngle - this.originalMouseAngle;
            this.selectedAnnotation.rotation = this.originalAnnotationRotation + angleDiff;
            
            // Apply boundary check after rotation
            this.selectedAnnotation = this.boundaryCheck(this.selectedAnnotation);
            
            this.drawCanvas();
          }
          break;
          
        case "draw":
          if (this.currentMode === "rectangle" && this.rectStart) {
            const width = Math.abs(mouseX - this.rectStart.x);
            const height = Math.abs(mouseY - this.rectStart.y);
            const cx = (mouseX + this.rectStart.x) / 2;
            const cy = (mouseY + this.rectStart.y) / 2;
            
            this.currentDrawingAnnotation = {
              type: "rectangle",
              cx,
              cy,
              width,
              height,
              rotation: 0
            };
            
            this.currentDrawingAnnotation = this.boundaryCheck(this.currentDrawingAnnotation);
            this.drawCanvas();
          } else if (this.drawsFreehand()) {
            const last = this.freehandPoints[this.freehandPoints.length - 1];
            if (!last || Math.hypot(mouseX-last.x, mouseY-last.y) >= 0.25) this.freehandPoints.push({ x: mouseX, y: mouseY });
            this.drawCanvas();
          }
          break;
      }
    }
  
    handleMouseUp(e) {
      if (!this.ready || this.locked()) return;
      let changed = false;
      
      // Handle end of resizing to ensure rotation is properly maintained
      if (this.currentAction === "resize") {
        this.handleResizeEnd();
      }
      if (["move", "resize", "rotate"].includes(this.currentAction) && this.originalAnnotation && this.selectedAnnotation) {
        const original = this.originalAnnotation, current = this.selectedAnnotation;
        changed = current.type === "rectangle"
          ? ["cx", "cy", "width", "height", "rotation"].some(key => current[key] !== original[key])
          : current.points.length !== original.points.length || current.points.some((point, index) => point.x !== original.points[index].x || point.y !== original.points[index].y);
      }
      
      if (this.currentAction === "draw") {
        if (this.currentMode === "rectangle") {
          const { x: mouseX, y: mouseY } = this.eventPoint(e);
          
          const width = Math.abs(mouseX - this.rectStart.x);
          const height = Math.abs(mouseY - this.rectStart.y);
          
          if (width >= this.minimumShapeSize() && height >= this.minimumShapeSize()) {
            const cx = (mouseX + this.rectStart.x) / 2;
            const cy = (mouseY + this.rectStart.y) / 2;
            
            const newAnnotation = {
              type: "rectangle",
              cx,
              cy,
              width,
              height,
              rotation: 0
            };
            
            const checkedAnnotation = this.boundaryCheck(newAnnotation);
            this.annotations.push(checkedAnnotation);
            this.selectedAnnotation = checkedAnnotation;
            
            if (this.isFirstDraw) {
              this.isFirstDraw = false;
              if (typeof window.setAnnotationStartTime === 'function') {
                window.setAnnotationStartTime();
              }
            }
            
            changed = true;
          }
        } else if (this.drawsFreehand() && this.freehandPoints.length > 2) {
          // Keep sampled image-space vertices. The previous adaptive reduction to
          // 25 points could move a research boundary by an unbounded distance.
          const simplified = this.freehandPoints.map(p => ({...p}));
          const bounds = this.getBoundingBox(simplified);
          if (bounds.maxX - bounds.minX < this.minimumShapeSize() || bounds.maxY - bounds.minY < this.minimumShapeSize()) {
            this.resetDrawingState(); this.drawCanvas(); return;
          }
          const first = simplified[0];
          simplified.push({ ...first });
          if (AnnotationGeometry.area(simplified.map(p => [p.x,p.y])) < 1 || simplified.length > 20000) {
            const status = document.getElementById("annotation-status");
            if (status) {
              status.textContent = simplified.length > 20000 ? "This outline has too many points. Draw a shorter outline before saving." : "The outline must enclose an area. Draw a closed boundary before saving.";
              status.dataset.state = "error";
            }
            this.resetDrawingState(); this.drawCanvas(); return;
          }
          
          const newAnnotation = {
            type: "freehand",
            points: simplified
          };
          
          const checkedAnnotation = this.boundaryCheck(newAnnotation);
          if (this.currentMode === "custom") {
            // Held apart until the page saves it under the specialist's own name; the term's shapes and timer are untouched.
            this.pendingCustom = checkedAnnotation;
            this.resetDrawingState();
            this.drawCanvas();
            this.updateCursor(this.eventPoint(e));
            if (this.onCustomShape) this.onCustomShape(checkedAnnotation);
            return;
          }
          this.annotations.push(checkedAnnotation);
          this.selectedAnnotation = checkedAnnotation;
          
          if (this.isFirstDraw) {
            this.isFirstDraw = false;
            if (typeof window.setAnnotationStartTime === 'function') {
              window.setAnnotationStartTime();
            }
          }
          
          changed = true;
        }
      }
      
      this.resetDrawingState();
      this.drawCanvas();
      this.updateCursor(this.eventPoint(e));
      if (changed && this.onAnnotationsChange) this.onAnnotationsChange();
    }
  
    // A shaky click must not become a saved shape too small to see, and validation needs at least one image pixel.
    minimumShapeSize() {
      return Math.max(1, this.screenPixels(3));
    }

    // Utility functions
    clamp(val, min, max) {
      return Math.min(Math.max(val, min), max);
    }
  
    // Public methods for external use
    getAnnotations() {
      return this.annotations;
    }
  
    setAnnotations(newAnnotations) {
      this.annotations = structuredClone(newAnnotations);
      this.selectedAnnotation = null;
      this.resetDrawingState();
      this.drawCanvas();
    }

    loadCoordinates(json) {
      if (!this.ready) throw new Error("Wait for the image to load.");
      this.setAnnotations(AnnotationGeometry.deserialize(json, this.canvas.width, this.canvas.height));
    }
  
    // Get coordinates for a single annotation
    getAnnotationCoordinatesJSON(annotation) {
      return AnnotationGeometry.corners(annotation).map(p => p.map(n => Math.round(n*1000)/1000));
    }
  
    // Get all annotations as JSON
    getAllAnnotationsJSON() {
      if (!this.ready || this.currentAction !== "none" || this.imageIdentity !== (this.sourceImage.currentSrc || this.sourceImage.src)) throw new Error("Finish drawing and wait for the assigned image before saving.");
      return AnnotationGeometry.serialize(this.annotations, this.canvas.width, this.canvas.height);
    }
  
    getAnnotationCornersOriginal(ann) {
      return this.getAnnotationCoordinatesJSON(ann);
    }
  
    // Add a helper method for handling resize end
    handleResizeEnd() {
      if (this.currentAction === "resize" && this.selectedAnnotation && this.originalAnnotation) {
        // Apply any final adjustments to the resized annotation
        // This ensures rotation is properly maintained
        this.selectedAnnotation.rotation = this.originalAnnotation.rotation;
        
        // resetDrawingState clears the snapshot after change detection.
      }
    }
  
  }
  
  // Export the CanvasManager class
  window.CanvasManager = CanvasManager;
