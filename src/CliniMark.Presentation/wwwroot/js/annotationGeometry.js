// The stored coordinate space is the decoded image's original pixels. CSS pixels
// and the canvas backing size never form part of persisted research data.
(function (root) {
  "use strict";
  const round = n => Math.round(n * 1000) / 1000;
  function clientToImage(clientX, clientY, rect, width, height) {
    if (!(rect.width > 0 && rect.height > 0 && width > 0 && height > 0)) throw new Error("Image is not ready.");
    return {
      x: Math.max(0, Math.min(width, (clientX - rect.left) * width / rect.width)),
      y: Math.max(0, Math.min(height, (clientY - rect.top) * height / rect.height))
    };
  }
  function imageToClient(point, rect, width, height) {
    return { x: rect.left + point.x * rect.width / width, y: rect.top + point.y * rect.height / height };
  }
  function corners(shape) {
    if (shape.type === "freehand") return shape.points.map(p => [p.x, p.y]);
    const cos = Math.cos(shape.rotation), sin = Math.sin(shape.rotation);
    return [[-1,-1],[1,-1],[1,1],[-1,1]].map(([sx,sy]) => {
      const x = sx * shape.width / 2, y = sy * shape.height / 2;
      return [shape.cx + x*cos - y*sin, shape.cy + x*sin + y*cos];
    });
  }
  function area(points) {
    return Math.abs(points.reduce((sum, p, i) => {
      const next = points[(i + 1) % points.length];
      return sum + p[0]*next[1] - next[0]*p[1];
    }, 0)) / 2;
  }
  function validate(value, width, height) {
    if (!value || value.version !== 1 || value.coordinateSystem !== "image-pixels") throw new Error("Legacy or unsupported coordinates cannot be edited automatically.");
    if (!Number.isInteger(value.imageWidth) || !Number.isInteger(value.imageHeight) || value.imageWidth < 1 || value.imageHeight < 1) throw new Error("Invalid original image dimensions.");
    if ((width && value.imageWidth !== width) || (height && value.imageHeight !== height)) throw new Error("The image dimensions changed. Do not save against a different image.");
    if (!Array.isArray(value.annotations) || value.annotations.length < 1 || value.annotations.length > 200) throw new Error("Add at least one complete annotation (maximum 200).");
    for (const shape of value.annotations) {
      if (!["rectangle", "freehand"].includes(shape.type) || !Array.isArray(shape.points) || shape.points.length < 3 || shape.points.length > 20000 || (shape.type === "rectangle" && shape.points.length !== 4)) throw new Error("Invalid or incomplete annotation.");
      for (const p of shape.points) {
        if (!Array.isArray(p) || p.length !== 2 || !p.every(Number.isFinite) || p[0] < 0 || p[1] < 0 || p[0] > value.imageWidth || p[1] > value.imageHeight) throw new Error("Annotation coordinates must stay inside the image.");
      }
      if (area(shape.points) < 1) throw new Error("Annotations must enclose at least one original image pixel.");
      if (shape.type === "rectangle") {
        const p=shape.points, ux=p[1][0]-p[0][0], uy=p[1][1]-p[0][1], vx=p[2][0]-p[1][0], vy=p[2][1]-p[1][1];
        const tolerance=.003;
        if (Math.abs(p[0][0]+p[2][0]-p[1][0]-p[3][0])>tolerance || Math.abs(p[0][1]+p[2][1]-p[1][1]-p[3][1])>tolerance || Math.abs(ux*vx+uy*vy)>tolerance*(Math.hypot(ux,uy)+Math.hypot(vx,vy))) throw new Error("Rectangle corners must form a rectangle in drawing order.");
      }
    }
    return value;
  }
  function serialize(shapes, width, height) {
    const data = { version: 1, coordinateSystem: "image-pixels", imageWidth: width, imageHeight: height,
      annotations: shapes.map(shape => ({type: shape.type, points: corners(shape).map(p => p.map(round))})) };
    return JSON.stringify(validate(data, width, height));
  }
  function deserialize(json, width, height) {
    const value = validate(typeof json === "string" ? JSON.parse(json) : json, width, height);
    return value.annotations.map(shape => {
      const p = shape.points;
      if (shape.type === "freehand") return {type: "freehand", points: p.map(([x,y]) => ({x,y}))};
      // Average opposite edges to avoid amplifying the 0.001px vertex rounding.
      const dx = ((p[1][0]-p[0][0])+(p[2][0]-p[3][0]))/2;
      const dy = ((p[1][1]-p[0][1])+(p[2][1]-p[3][1]))/2;
      const vx = ((p[2][0]-p[1][0])+(p[3][0]-p[0][0]))/2;
      const vy = ((p[2][1]-p[1][1])+(p[3][1]-p[0][1]))/2;
      const angle = Math.atan2(dy-vx,dx+vy), cos=Math.cos(angle), sin=Math.sin(angle);
      return { type: "rectangle", cx:p.reduce((sum,q)=>sum+q[0],0)/4, cy:p.reduce((sum,q)=>sum+q[1],0)/4,
        width:dx*cos+dy*sin, height:vy*cos-vx*sin, rotation:angle };
    });
  }
  const api = { clientToImage, imageToClient, corners, area, validate, serialize, deserialize };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  root.AnnotationGeometry = api;
})(typeof window !== "undefined" ? window : globalThis);
