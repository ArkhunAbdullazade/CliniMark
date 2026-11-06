// Prints one saved-coordinates document per line, made by the real canvas and geometry code. Used by CoordinateTests.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const geometry = require('../../src/CliniMark.Presentation/wwwroot/js/annotationGeometry.js');

const window = {};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/CliniMark.Presentation/wwwroot/js/canvas.js'), 'utf8'), {window, document: {}, console});
const proto = window.CanvasManager.prototype;

// A fixed seed, so a failure can be reproduced.
let state = 58021014;
const random = () => { state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state / 4294967296; };
const between = (low, high) => low + random() * (high - low);
const sizes = [[1, 1], [40, 3000], [3000, 40], [300, 200], [1200, 800], [1600, 1000], [6000, 4000], [99999, 99999]];

const count = Number(process.argv[2] || 1000);
const lines = [];
for (let i = 0; i < count; i++) {
  const [width, height] = sizes[Math.floor(random() * sizes.length)];
  const manager = Object.create(proto);
  manager.canvas = {width, height};
  const shapes = [];
  for (let n = 1 + Math.floor(random() * 3); n > 0; n--) {
    if (random() < 0.6) {
      // The smallest rectangle the page can make: one image pixel each way (or the whole of a tiny image).
      const rectangle = {type: 'rectangle', cx: between(0, width), cy: between(0, height),
        width: Math.min(width, between(1.01, width + 1)), height: Math.min(height, between(1.01, height + 1)),
        rotation: random() < 0.3 ? 0 : between(-Math.PI * 2, Math.PI * 2)};
      if (width < 2 || height < 2) { Object.assign(rectangle, {cx: width / 2, cy: height / 2, width, height, rotation: 0}); }
      const fitted = manager.boundaryCheck(rectangle);
      // Fitting a long rotated box into a narrow image can shrink it below one pixel; the page says so on save.
      if (fitted.width * fitted.height >= 1) shapes.push(fitted);
    } else {
      const centre = {x: between(0, width), y: between(0, height)};
      const radius = Math.max(1.5, between(0, Math.min(width, height) / 2));
      const points = [];
      for (let p = 0, total = 3 + Math.floor(random() * 40); p < total; p++) {
        const angle = (p / total) * Math.PI * 2;
        points.push({x: centre.x + Math.cos(angle) * radius * between(0.7, 1), y: centre.y + Math.sin(angle) * radius * between(0.7, 1)});
      }
      points.push({...points[0]});
      const freehand = manager.boundaryCheck({type: 'freehand', points});
      // The page refuses outlines that enclose under one pixel before they are ever saved.
      if (geometry.area(freehand.points.map(p => [p.x, p.y])) >= 2) shapes.push(freehand);
    }
  }
  if (!shapes.length) { i--; continue; }
  lines.push(geometry.serialize(shapes, width, height));
}
process.stdout.write(lines.join('\n') + '\n');
