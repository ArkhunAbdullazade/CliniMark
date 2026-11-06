// Student Canvas Manager Class
class StudentCanvasManager {
  constructor(canvasId, sourceImageId, containerId) {
    // Get base canvas functionality by initializing the original CanvasManager
    this.baseManager = new CanvasManager(canvasId, sourceImageId, containerId);
    
    // Additional student-specific properties
    this.groupedAnnotations = []; // Array of annotation groups
    this.currentGroup = null; // Current annotation group
    this.textBoxElement = null; // Element for floating text input
    this.selectedGroupIndex = -1; // Index of the selected group
    const drawBase = this.baseManager.drawCanvas.bind(this.baseManager);
    this.baseManager.drawCanvas = () => {
      drawBase();
      if (!this.baseManager.ready) return;
      for (let i = 0; i < this.groupedAnnotations.length; i++) {
        if (i === this.selectedGroupIndex) continue;
        for (const shape of this.groupedAnnotations[i].annotations) this.baseManager.drawAnnotation(shape, false);
      }
      if (this.baseManager.selectedAnnotation) this.baseManager.drawAnnotation(this.baseManager.selectedAnnotation, true);
    };
    
    // Override the base manager's onAnnotationsChange
    const originalOnAnnotationsChange = this.baseManager.onAnnotationsChange;
    this.baseManager.onAnnotationsChange = () => {
      this.handleAnnotationsChange();
      
      if (typeof originalOnAnnotationsChange === 'function') {
        originalOnAnnotationsChange();
      }
    };
    
    // Initialize the floating textbox
    this.createFloatingTextBox();
    
    // Set up additional event listeners
    this.setupEventListeners();
    
    // Track mouse movement for live textbox positioning
    this.mouseTracker = { x: 0, y: 0 };
    document.addEventListener('mousemove', (e) => {
      this.mouseTracker.x = e.clientX;
      this.mouseTracker.y = e.clientY;
    });
  }
  
  // Create the floating textbox element
  createFloatingTextBox() {
    // Create the floating textbox if it doesn't exist yet
    if (!this.textBoxElement) {
      this.textBoxElement = document.createElement('div');
      this.textBoxElement.className = 'floating-textbox';
      this.textBoxElement.style.position = 'fixed';
      this.textBoxElement.style.zIndex = '10000'; // Higher z-index to ensure it's on top
      this.textBoxElement.style.display = 'none';
      
      const input = document.createElement('input');
      input.type = 'text';
      input.placeholder = 'Enter annotation label';
      input.setAttribute('aria-label', 'Annotation label');
      input.className = 'floating-input';
      input.addEventListener('input', () => input.setCustomValidity(''));
      
      const saveBtn = document.createElement('button');
      saveBtn.textContent = 'Save Group';
      saveBtn.className = 'floating-save-btn';
      
      const cancelBtn = document.createElement('button');
      cancelBtn.textContent = 'Cancel';
      cancelBtn.className = 'floating-cancel-btn';
      
      saveBtn.addEventListener('click', () => this.saveCurrentAnnotation());
      cancelBtn.addEventListener('click', () => this.cancelCurrentAnnotation());
      
      // Add keydown event to input for Enter key
      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
          this.saveCurrentAnnotation();
        } else if (e.key === 'Escape') {
          this.cancelCurrentAnnotation();
        }
      });
      
      this.textBoxElement.appendChild(input);
      this.textBoxElement.appendChild(saveBtn);
      this.textBoxElement.appendChild(cancelBtn);
      
      document.body.appendChild(this.textBoxElement);
      
      // Track the current annotation being edited for repositioning
      this.currentAnnotationBeingEdited = null;
    }
  }
  
  // Check if the annotation has a rotation handle and adjust position accordingly
  positionTextBoxForRotatedAnnotation(annotation, posX, posY, textBoxWidth, canvasRect) {
    // If the annotation isn't a rectangle or doesn't have rotation, just return the original position
    if (annotation.type !== 'rectangle' || annotation.rotation === undefined) {
      return { posX, posY };
    }
    
    // Get the rotation handle position (it appears at the top center of the rectangle)
    const handle = AnnotationGeometry.imageToClient({x:annotation.cx,y:annotation.cy-annotation.height/2}, canvasRect, this.baseManager.canvas.width, this.baseManager.canvas.height);
    const handleX = handle.x;
    const handleY = handle.y - 30;
    
    // Calculate the textbox's bottom edge at the current position
    const textBoxBottom = posY + this.textBoxElement.offsetHeight;
    
    // Check if the textbox would overlap with the rotation handle
    if (Math.abs(posX + textBoxWidth/2 - handleX) < 20 && textBoxBottom > handleY - 10) {
      // If it would overlap, move it higher
      return { 
        posX, 
        posY: handleY - this.textBoxElement.offsetHeight - 10 // 10px space between textbox and handle
      };
    }
    
    // Otherwise return the original position
    return { posX, posY };
  }
  
  // Show the textbox above the specified annotation
  showTextBoxForAnnotation(annotation) {
    if (!annotation) return;
    
    // Store the current annotation being edited for repositioning during drags
    this.currentAnnotationBeingEdited = annotation;
    
    // Get the canvas element position
    const canvasRect = this.baseManager.canvas.getBoundingClientRect();
    
    // For rectangle annotations, position above the center
    let posX, posY;
    
    if (annotation.type === 'rectangle') {
      // Center the textbox over the rectangle
      const top = AnnotationGeometry.imageToClient({x:annotation.cx,y:annotation.cy-annotation.height/2}, canvasRect, this.baseManager.canvas.width, this.baseManager.canvas.height);
      posX = top.x;
      
      // Position textbox higher to avoid covering rotation handle (which appears above the rectangle)
      const extraPadding = annotation.rotation !== undefined ? 35 : 15;
      posY = top.y - extraPadding;
    } else if (annotation.type === 'freehand') {
      // For freehand, find the center of the bounding box and position above it
      const bbox = this.baseManager.getBoundingBox(annotation.points);
      const top = AnnotationGeometry.imageToClient({x:(bbox.minX+bbox.maxX)/2,y:bbox.minY}, canvasRect, this.baseManager.canvas.width, this.baseManager.canvas.height);
      posX = top.x;
      posY = top.y - 25;
    }
    
    // First, show the textbox to get its dimensions
    this.textBoxElement.style.display = 'flex';
    
    // Then adjust the position based on textbox dimensions to center it over the annotation
    const textBoxWidth = this.textBoxElement.offsetWidth;
    const textBoxHeight = this.textBoxElement.offsetHeight;
    
    // Adjust X to center the textbox over the annotation
    posX = posX - (textBoxWidth / 2);
    
    // Adjust Y to place the textbox just above the annotation with some padding
    posY = posY - textBoxHeight - 10;
    
    // Try to keep the textbox within the canvas bounds if possible
    // But if the annotation is near the edge, prioritize keeping the textbox visible
    if (posX < canvasRect.left) {
      posX = canvasRect.left + 5; // Small margin from left edge
    } else if (posX + textBoxWidth > canvasRect.right) {
      posX = canvasRect.right - textBoxWidth - 5; // Small margin from right edge
    }
    
    // Only adjust Y if it doesn't push the textbox off-screen at the top
    if (posY < 5) {
      // If textbox would go off-screen at the top, position it below the annotation instead
      if (annotation.type === 'rectangle') {
        posY = AnnotationGeometry.imageToClient({x:annotation.cx,y:annotation.cy+annotation.height/2}, canvasRect, this.baseManager.canvas.width, this.baseManager.canvas.height).y + 30;
      } else {
        const bbox = this.baseManager.getBoundingBox(annotation.points);
        posY = AnnotationGeometry.imageToClient({x:bbox.maxX,y:bbox.maxY}, canvasRect, this.baseManager.canvas.width, this.baseManager.canvas.height).y + 30;
      }
    }
    
    // Check specifically for rotated rectangles and ensure we don't overlap rotation handle
    const adjustedPosition = this.positionTextBoxForRotatedAnnotation(annotation, posX, posY, textBoxWidth, canvasRect);
    posX = adjustedPosition.posX;
    posY = adjustedPosition.posY;
    
    // Position the textbox
    this.textBoxElement.style.left = `${Math.max(6,Math.min(posX,window.innerWidth-textBoxWidth-6))}px`;
    this.textBoxElement.style.top = `${Math.max(6,Math.min(posY,window.innerHeight-textBoxHeight-6))}px`;
    
    // Focus the input
    const input = this.textBoxElement.querySelector('input');
    
    // If we have a current group with a label, prefill the input
    if (this.textBoxGroupId !== this.currentGroup?.id) {
      input.value = this.currentGroup?.label || '';
      this.textBoxGroupId = this.currentGroup?.id;
    }
    
    // Focus after a small delay to ensure the element is fully rendered
    setTimeout(() => input.focus(), 10);
  }
  
  // Hide the floating textbox
  hideTextBox() {
    if (this.textBoxElement) {
      this.textBoxElement.style.display = 'none';
      this.currentAnnotationBeingEdited = null;
    }
  }
  
  // Start a new annotation group
  startNewGroup() {
    // Save the current group if needed
    if (this.currentGroup && this.currentGroup.annotations.length > 0) {
      this.saveGroup(this.currentGroup);
    }
    
    // Create a new group
    this.currentGroup = {
      id: `${Date.now()}-${Math.random()}`, // Local identity only; the server owns persistence identity.
      label: '', // Will be set when the user saves the textbox
      annotations: [], // Will contain all annotations in this group
      color: this.getRandomColor() // Assign a random color to the group
    };
  }
  
  // Save the current annotation group
  saveGroup(group) {
    if (!group) return;
    
    // If the group already exists in groupedAnnotations, update it
    const existingIndex = this.groupedAnnotations.findIndex(g => g.id === group.id);
    if (existingIndex >= 0) {
      this.groupedAnnotations[existingIndex] = { ...group };
    } else {
      // Otherwise add as a new group
      this.groupedAnnotations.push({ ...group });
    }
    
    // Notify that grouped annotations have changed
    this.onGroupedAnnotationsChange();
  }
  
  // Save the current in-progress annotation
  saveCurrentAnnotation() {
    const input = this.textBoxElement.querySelector('input');
    const label = input.value.trim();
    if (!label) { input.setCustomValidity("Enter a label before saving this group."); input.reportValidity(); return; }
    input.setCustomValidity("");
    try { this.baseManager.getAllAnnotationsJSON(); }
    catch (error) { window.alert(error.message); return; }
    
    // Start a new group if we don't have one
    if (!this.currentGroup) {
      this.startNewGroup();
    }
    
    // Set the label for the current group
    this.currentGroup.label = label;
    
    // Save all current annotations to this group
    this.currentGroup.annotations = structuredClone(this.baseManager.annotations);
    
    // Save the group
    this.saveGroup(this.currentGroup);
    
    // Hide the textbox
    this.hideTextBox();
    
    // Clear the canvas for the next annotation
    this.baseManager.resetAnnotation();
    
    // Reset the current group
    this.currentGroup = null;
    this.selectedGroupIndex = -1;
    this.baseManager.drawCanvas();
    this.onGroupedAnnotationsChange();
  }
  
  // Cancel the current annotation
  cancelCurrentAnnotation() {
    // Hide the textbox
    this.hideTextBox();
    
    this.baseManager.resetAnnotation();
    
    // Reset the current group
    this.currentGroup = null;
    this.selectedGroupIndex = -1;
    this.baseManager.drawCanvas();
    this.onGroupedAnnotationsChange();
  }
  
  // Handle changes to the annotations on the canvas
  handleAnnotationsChange() {
    const annotations = this.baseManager.annotations;
    
    // If we have annotations but no current group, start a new one
    if (annotations.length > 0 && !this.currentGroup) {
      this.startNewGroup();
    }
    
    // If we have a current group and annotations, show the textbox for the latest annotation
    if (this.currentGroup && annotations.length > 0) {
      // Show the textbox for the most recently added annotation
      this.showTextBoxForAnnotation(annotations[annotations.length - 1]);
    } else {
      // Hide the textbox if we have no annotations
      this.hideTextBox();
    }
  }
  
  // Edit an existing annotation group
  editGroup(groupIndex) {
    if (groupIndex < 0 || groupIndex >= this.groupedAnnotations.length) return;
    
    if (this.currentGroup && this.baseManager.annotations.length > 0 &&
        !window.confirm("Discard the unsaved changes to the current group?")) return;
    
    // Get the group we want to edit
    const groupToEdit = this.groupedAnnotations[groupIndex];
    
    // Set it as the current group
    this.currentGroup = structuredClone(groupToEdit);
    
    // Set the canvas annotations to match this group
    this.baseManager.setAnnotations(groupToEdit.annotations);
    
    // Set this as the selected group
    this.selectedGroupIndex = groupIndex;
    
    // Show the textbox for the first annotation in the group
    if (groupToEdit.annotations.length > 0) {
      setTimeout(() => {
        // Get the most complex or largest annotation in the group to position the textbox
        let mainAnnotation = groupToEdit.annotations[0];
        
        // Find the best annotation to position the textbox over
        // For rectangles, choose the largest one; for freehand, choose the one with most points
        if (groupToEdit.annotations.length > 1) {
          if (mainAnnotation.type === 'rectangle') {
            // Find the largest rectangle by area
            mainAnnotation = groupToEdit.annotations.reduce((largest, current) => {
              if (current.type === 'rectangle') {
                const currentArea = current.width * current.height;
                const largestArea = largest.width * largest.height;
                return currentArea > largestArea ? current : largest;
              }
              return largest;
            }, mainAnnotation);
          } else if (mainAnnotation.type === 'freehand') {
            // Find the freehand with most points
            mainAnnotation = groupToEdit.annotations.reduce((mostPoints, current) => {
              if (current.type === 'freehand' && current.points.length > mostPoints.points.length) {
                return current;
              }
              return mostPoints;
            }, mainAnnotation);
          }
        }
        
        // Show textbox for the best annotation
        this.showTextBoxForAnnotation(mainAnnotation);
      }, 10);
    }
  }
  
  // Delete an annotation group
  deleteGroup(groupIndex) {
    if (groupIndex < 0 || groupIndex >= this.groupedAnnotations.length) return;
    
    // Remove the group
    this.groupedAnnotations.splice(groupIndex, 1);
    
    // If we're editing this group, clear the canvas
    if (this.selectedGroupIndex === groupIndex) {
      this.baseManager.resetAnnotation();
      this.hideTextBox();
      this.currentGroup = null;
      this.selectedGroupIndex = -1;
    } else if (this.selectedGroupIndex > groupIndex) {
      // Adjust the selected index if needed
      this.selectedGroupIndex--;
    }
    
    // Notify that grouped annotations have changed
    this.onGroupedAnnotationsChange();
    this.baseManager.drawCanvas();
  }
  
  // Get a random color for annotation groups
  getRandomColor() {
    // Array of pleasing, distinguishable colors
    const colors = [
      '#4285F4', // Google Blue
      '#EA4335', // Google Red
      '#FBBC05', // Google Yellow
      '#34A853', // Google Green
      '#9C27B0', // Purple
      '#FF9800', // Orange
      '#03A9F4', // Light Blue
      '#8BC34A', // Light Green
      '#FF5722', // Deep Orange
      '#607D8B'  // Blue Grey
    ];
    
    // Pick a random color from the array
    return colors[Math.floor(Math.random() * colors.length)];
  }
  
  // Set up additional event listeners
  setupEventListeners() {
    // Add window resize event to reposition the textbox if needed
    window.addEventListener('resize', () => {
      if (this.textBoxElement && this.textBoxElement.style.display !== 'none' && 
          this.currentAnnotationBeingEdited) {
        this.showTextBoxForAnnotation(this.currentAnnotationBeingEdited);
      }
    });
    
    // Also handle scroll events for textbox repositioning
    document.addEventListener('scroll', () => {
      if (this.textBoxElement && this.textBoxElement.style.display !== 'none' && 
          this.currentAnnotationBeingEdited) {
        this.showTextBoxForAnnotation(this.currentAnnotationBeingEdited);
      }
    });
    
    // Override the base manager's selectAnnotation method to track current annotation
    const originalSelectAnnotation = this.baseManager.selectAnnotation;
    if (originalSelectAnnotation) {
      this.baseManager.selectAnnotation = (annotation) => {
        originalSelectAnnotation.call(this.baseManager, annotation);
        
        // Then update the current annotation being edited
        if (annotation && this.textBoxElement.style.display !== 'none') {
          this.currentAnnotationBeingEdited = annotation;
          this.showTextBoxForAnnotation(annotation);
        }
      };
    }
    
    // Override handleDragMove for tracking annotation movement
    const originalHandleDragMove = this.baseManager.handleDragMove;
    if (originalHandleDragMove) {
      this.baseManager.handleDragMove = (e) => {
        originalHandleDragMove.call(this.baseManager, e);
        
        // Then update textbox position if needed
        if (this.textBoxElement.style.display !== 'none' && 
            this.currentAnnotationBeingEdited && 
            this.baseManager.selectedAnnotation) {
          this.showTextBoxForAnnotation(this.baseManager.selectedAnnotation);
        }
      };
    }
    
    // Override handleResize for tracking annotation resizing
    const originalHandleResize = this.baseManager.handleResize;
    if (originalHandleResize) {
      this.baseManager.handleResize = (e) => {
        originalHandleResize.call(this.baseManager, e);
        
        // Then update textbox position
        if (this.textBoxElement.style.display !== 'none' && 
            this.currentAnnotationBeingEdited && 
            this.baseManager.selectedAnnotation) {
          this.showTextBoxForAnnotation(this.baseManager.selectedAnnotation);
        }
      };
    }
    
    // Override handleRotate for tracking annotation rotation
    const originalHandleRotate = this.baseManager.handleRotate;
    if (originalHandleRotate) {
      this.baseManager.handleRotate = (e) => {
        originalHandleRotate.call(this.baseManager, e);
        
        // Then update textbox position
        if (this.textBoxElement.style.display !== 'none' && 
            this.currentAnnotationBeingEdited && 
            this.baseManager.selectedAnnotation) {
          this.showTextBoxForAnnotation(this.currentAnnotationBeingEdited);
        }
      };
    }
    
    // Check for animation frame support
    if (window.requestAnimationFrame) {
      // Set up animation frame for smooth textbox tracking during interactions
      const trackAnnotation = () => {
        if (this.textBoxElement.style.display !== 'none' && 
            this.currentAnnotationBeingEdited && 
            (this.baseManager.currentAction === 'move' || 
             this.baseManager.currentAction === 'resize' || 
             this.baseManager.currentAction === 'rotate')) {
          this.showTextBoxForAnnotation(this.currentAnnotationBeingEdited);
        }
        requestAnimationFrame(trackAnnotation);
      };
      
      // Start the animation frame loop
      requestAnimationFrame(trackAnnotation);
    }
  }
  
  // Callback for when grouped annotations change
  onGroupedAnnotationsChange() {
    // This will be overridden by the student page
  }
  
  // Get all annotation groups
  getAnnotationGroups() {
    return this.groupedAnnotations;
  }
  
  // Get a specific annotation group
  getAnnotationGroup(index) {
    if (index < 0 || index >= this.groupedAnnotations.length) return null;
    return this.groupedAnnotations[index];
  }
  
  // Convert all grouped annotations to a format ready for submission
  getAnnotationsForSubmission() {
    // Flatten all groups into a single array of annotations with their labels
    const allAnnotations = [];
    
    this.groupedAnnotations.forEach(group => {
      group.annotations.forEach(annotation => {
        allAnnotations.push({
          ...annotation,
          groupId: group.id,
          label: group.label
        });
      });
    });
    
    return allAnnotations;
  }
  
  // Get the JSON coordinates for all annotations, grouped by their groups
  getAllGroupedAnnotationsJSON() {
    const result = this.groupedAnnotations.map(group => {
      // Get coordinates for each annotation in the group
      const annotationCoordinates = AnnotationGeometry.serialize(group.annotations, this.baseManager.canvas.width, this.baseManager.canvas.height);
      
      return {
        groupId: group.id,
        label: group.label,
        color: group.color,
        annotations: annotationCoordinates
      };
    });
    
    return result;
  }
  
  // Draw the canvas with group coloring
  drawCanvas() {
    // Let the base manager handle the initial drawing
    this.baseManager.drawCanvas();
    
    // If we're editing a group, highlight all its annotations
    if (this.selectedGroupIndex !== -1) {
      const group = this.groupedAnnotations[this.selectedGroupIndex];
      // Add visual indication that these annotations are in a group
      // This is handled by the base canvas manager already
    }
  }
  
  // Proxy methods to base manager for convenience
  switchMode(mode) {
    return this.baseManager.switchMode(mode);
  }
  
  getAnnotations() {
    return this.baseManager.getAnnotations();
  }
  
  setAnnotations(annotations) {
    return this.baseManager.setAnnotations(annotations);
  }
  
  resetAnnotation() {
    this.hideTextBox();
    this.currentGroup = null;
    this.selectedGroupIndex = -1;
    return this.baseManager.resetAnnotation();
  }
  
  getAnnotationCoordinatesJSON(annotation) {
    return this.baseManager.getAnnotationCoordinatesJSON(annotation);
  }
  
  createTools(imageContainer) {
    return this.baseManager.createTools(imageContainer);
  }
  
  updateToolsPosition(imageContainer) {
    return this.baseManager.updateToolsPosition(imageContainer);
  }
}
