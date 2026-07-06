// UI + import/export for the Frahan nest demo. The engine (globalThis.frahan)
// is the C# nester on WebAssembly; this file only parses geometry, builds the
// JSON request, calls it, and draws the result. Nothing is uploaded anywhere.

'use strict';

let lastResponse = null;
let parts = [];       // each: flat [x0,y0,x1,y1,...] ring (local coords)
let sheetHoles = [];  // defects in the sheet (sheet coords) the nester routes around

const $ = (id) => document.getElementById(id);
const status = (m) => { $('status').textContent = m; };

let sampleActive = false;

// The loader module is ready (engine NOT yet downloaded — that happens lazily
// on the first Nest, so the page and Load-sample stay instant on mobile).
window.addEventListener('frahan-loader-ready', () => {
  $('nest').disabled = false;
  status('ready — engine loads on first Nest (~0.6 MB, once)');
});

// ---- import ---------------------------------------------------------------

$('file').addEventListener('change', async (e) => {
  const f = e.target.files[0];
  if (!f) return;
  const text = await f.text();
  const name = f.name.toLowerCase();
  try {
    const { parts: p, holes: h } = name.endsWith('.svg') ? parseSvg(text) : parseDxf(text);
    parts = p; sheetHoles = h; sampleActive = false;
    status(`imported ${parts.length} part${parts.length === 1 ? '' : 's'}` +
           (h.length ? `, ${h.length} red shape${h.length === 1 ? '' : 's'} treated as defects` : ''));
    drawInput();
  } catch (err) {
    status('import failed: ' + err.message);
  }
});

$('sample').addEventListener('click', () => {
  parts = sampleParts();
  sampleActive = true;
  sheetHoles = $('withHoles').checked ? sampleSheetHoles() : [];
  status(`${parts.length} sample parts` +
         (sheetHoles.length ? `, ${sheetHoles.length} sheet defects to avoid` : ''));
  drawInput();
});

// toggle sample defects on/off without reloading
$('withHoles').addEventListener('change', () => {
  if (!sampleActive) return;
  sheetHoles = $('withHoles').checked ? sampleSheetHoles() : [];
  drawInput();
});

// Red shapes are suggested as sheet defects; everything else is a part. Lets a
// user mark defects in their CAD by colour (red stroke/fill), shown distinctly.
function isReddish(el) {
  const s = ((el.getAttribute('stroke') || '') + ' ' + (el.getAttribute('fill') || '') + ' ' +
             (el.getAttribute('style') || '')).toLowerCase();
  if (/#f00\b|#ff0000|\bred\b|#e0(0|1|2)|#b3261e|#d0021b|#cc0000/.test(s)) return true;
  const m = s.match(/rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
  if (m) { const r = +m[1], g = +m[2], b = +m[3]; return r > 140 && g < 90 && b < 90; }
  return false;
}

// SVG: <rect>, <polygon>, <polyline>, and <path> (M/L/H/V/Z, lines only; curves
// are flattened coarsely). Coordinates are taken as-is (assume a flat drawing).
function parseSvg(text) {
  const doc = new DOMParser().parseFromString(text, 'image/svg+xml');
  const parts = [], holes = [];
  const push = (el, pts) => { if (pts.length >= 6) (isReddish(el) ? holes : parts).push(pts); };
  doc.querySelectorAll('rect').forEach((r) => {
    const x = +r.getAttribute('x') || 0, y = +r.getAttribute('y') || 0;
    const w = +r.getAttribute('width') || 0, h = +r.getAttribute('height') || 0;
    push(r, [x, y, x + w, y, x + w, y + h, x, y + h]);
  });
  doc.querySelectorAll('polygon, polyline').forEach((p) => {
    const nums = (p.getAttribute('points') || '').trim().split(/[\s,]+/).map(Number).filter((n) => !isNaN(n));
    push(p, nums);
  });
  doc.querySelectorAll('path').forEach((p) => push(p, flattenPath(p.getAttribute('d') || '')));
  return { parts, holes };
}

// minimal path flattener: absolute M/L/H/V/Z; ignores curves' control points
// and lands on their endpoints (coarse but keeps a closed ring).
function flattenPath(d) {
  const toks = d.match(/[a-zA-Z]|-?\d*\.?\d+(?:e-?\d+)?/g) || [];
  const pts = []; let i = 0, cx = 0, cy = 0, cmd = '';
  const num = () => +toks[i++];
  while (i < toks.length) {
    const t = toks[i];
    if (/[a-zA-Z]/.test(t)) { cmd = t; i++; } // command
    switch (cmd) {
      case 'M': case 'L': cx = num(); cy = num(); pts.push(cx, cy); break;
      case 'm': case 'l': cx += num(); cy += num(); pts.push(cx, cy); break;
      case 'H': cx = num(); pts.push(cx, cy); break;
      case 'h': cx += num(); pts.push(cx, cy); break;
      case 'V': cy = num(); pts.push(cx, cy); break;
      case 'v': cy += num(); pts.push(cx, cy); break;
      case 'C': case 'c': case 'S': case 's': case 'Q': case 'q': case 'T': case 't': case 'A': case 'a': {
        // skip control params, keep the endpoint (coarse flatten)
        const n = (cmd === 'C' || cmd === 'c') ? 6 : (cmd === 'S' || cmd === 's' || cmd === 'Q' || cmd === 'q') ? 4 : (cmd === 'A' || cmd === 'a') ? 7 : 2;
        for (let k = 0; k < n - 2; k++) num();
        const ex = num(), ey = num();
        if (cmd === cmd.toLowerCase()) { cx += ex; cy += ey; } else { cx = ex; cy = ey; }
        pts.push(cx, cy); break;
      }
      case 'Z': case 'z': i++; break;
      default: i++; break;
    }
  }
  return pts;
}

// DXF: LINE segments chained into loops, and LWPOLYLINE / POLYLINE vertices.
// Entity-section group-code parser (10/20 = x/y, 62 = colour, 8 = layer). Red
// colour (ACI 1) or a hole/defect/void layer marks a shape as a sheet defect.
function parseDxf(text) {
  const lines = text.split(/\r?\n/);
  const parts = [], holes = [];
  const isDefect = (color, layer) => color === 1 || /hole|defect|void/i.test(layer || '');
  const bucket = (color, layer) => (isDefect(color, layer) ? holes : parts);
  let i = 0;
  while (i < lines.length && lines[i].trim() !== 'ENTITIES') i++;
  let curType = null, ring = [], segs = [], seg = {};
  let curColor = 0, curLayer = '', segsColor = 0, segsLayer = '';
  const flushPoly = () => { if (ring.length >= 6) bucket(curColor, curLayer).push(ring); ring = []; };
  const flushLines = () => {
    if (segs.length === 0) return;
    const used = new Array(segs.length).fill(false);
    let cur = segs[0]; used[0] = true;
    const pts = [cur.x1, cur.y1, cur.x2, cur.y2];
    let ex = cur.x2, ey = cur.y2, added = true;
    while (added) {
      added = false;
      for (let k = 0; k < segs.length; k++) {
        if (used[k]) continue;
        const s = segs[k];
        if (near(s.x1, ex) && near(s.y1, ey)) { pts.push(s.x2, s.y2); ex = s.x2; ey = s.y2; used[k] = true; added = true; }
        else if (near(s.x2, ex) && near(s.y2, ey)) { pts.push(s.x1, s.y1); ex = s.x1; ey = s.y1; used[k] = true; added = true; }
      }
    }
    if (pts.length >= 6) bucket(segsColor, segsLayer).push(pts);
    segs = [];
  };
  for (; i < lines.length - 1; i += 2) {
    const code = lines[i].trim();
    const val = lines[i + 1].trim();
    if (code === '0') {
      if (curType === 'LWPOLYLINE' || curType === 'POLYLINE') flushPoly();
      if (val === 'ENDSEC') { flushLines(); break; }
      curType = val; seg = {}; curColor = 0; curLayer = '';
    } else if (code === '62') { curColor = +val; if (curType === 'LINE') segsColor = +val; }
    else if (code === '8') { curLayer = val; if (curType === 'LINE') segsLayer = val; }
    else if (curType === 'LINE') {
      if (code === '10') seg.x1 = +val; else if (code === '20') seg.y1 = +val;
      else if (code === '11') seg.x2 = +val; else if (code === '21') { seg.y2 = +val; segs.push(seg); seg = {}; }
    } else if (curType === 'LWPOLYLINE' || curType === 'POLYLINE') {
      if (code === '10') seg.x = +val; else if (code === '20') { ring.push(seg.x, +val); seg = {}; }
    }
  }
  if (curType === 'LWPOLYLINE' || curType === 'POLYLINE') flushPoly();
  flushLines();
  return { parts, holes };
}
const near = (a, b) => Math.abs(a - b) < 1e-6;

function sampleParts() {
  const r = (w, h) => [0, 0, w, 0, w, h, 0, h];
  const tri = (s) => [0, 0, s, 0, s / 2, s];
  return [r(120, 70), r(120, 70), r(90, 90), r(140, 40), tri(90), tri(90),
          r(60, 110), r(80, 80), r(70, 70), r(110, 45),
          [0, 0, 70, 0, 100, 50, 40, 80, -10, 40]];
}

// Sheet defects (in sheet coords, default 600x400) the nester must route
// around: a rectangular void mid-sheet and an angled corner defect. This is
// the hole-aware capability that plain rectangle-strip nesters lack.
function sampleSheetHoles() {
  return [
    [230, 150, 380, 150, 380, 250, 230, 250],   // central rectangular void
    [470, 60, 560, 60, 560, 150],                // angled defect (top-right)
  ];
}

// ---- nest -----------------------------------------------------------------

$('nest').addEventListener('click', async () => {
  if (parts.length === 0) { status('import or load parts first'); return; }
  // lazy-download the engine on first use (keeps the page instant on mobile)
  if (!globalThis.frahan) {
    status('loading engine (~0.6 MB, one time)…');
    $('nest').disabled = true;
    try { await globalThis.frahanBoot(); }
    catch (e) { status('engine load failed: ' + e.message); $('nest').disabled = false; return; }
    $('nest').disabled = false;
  }
  const W = +$('sheetW').value, H = +$('sheetH').value;
  const req = {
    Sheet: [0, 0, W, 0, W, H, 0, H],
    SheetHoles: sheetHoles,
    Parts: parts,
    Spacing: +$('spacing').value,
    BaseRotations: +$('rot').value,
    MultiStart: +$('ms').value,
    BoundaryMode: $('boundary').checked ? 1 : 0,
    MinBoundaryContact: 0.2,
  };
  status('nesting…');
  // let the status paint before the (synchronous) wasm call
  setTimeout(() => {
    const t0 = performance.now();
    let respJson;
    try { respJson = globalThis.frahan.nest(JSON.stringify(req)); }
    catch (e) { status('nest error: ' + e.message); return; }
    const resp = JSON.parse(respJson);
    lastResponse = resp;
    drawResult(resp, W, H);
    const ms = (performance.now() - t0).toFixed(0);
    $('report').textContent =
      `Placed ${resp.PlacedCount}/${resp.PartCount} · density ${(resp.Density * 100).toFixed(1)}% · ` +
      `${resp.Valid ? 'valid (0 overlap)' : 'INVALID'} · ${ms} ms · ${resp.Note}`;
    status('done');
    $('dl-svg').disabled = false;
  }, 20);
});

// ---- render ---------------------------------------------------------------

function drawInput() {
  const W = +$('sheetW').value, H = +$('sheetH').value;
  const svg = $('canvas'); svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  svg.innerHTML = sheetRect(W, H) + holesSvg();
  // lay parts out in a preview strip along the top (not nested yet)
  let x = 4;
  for (const p of parts) {
    const bb = bbox(p);
    svg.insertAdjacentHTML('beforeend', polyEl(translate(p, x - bb.minx, 4 - bb.miny), 'part'));
    x += (bb.maxx - bb.minx) + 6;
  }
}

function drawResult(resp, W, H) {
  const svg = $('canvas'); svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  svg.innerHTML = sheetRect(W, H) + holesSvg();
  for (const pl of resp.Placed) {
    const cls = resp.Valid ? 'part' : 'part invalid';
    svg.insertAdjacentHTML('beforeend', polyEl(pl.PlacedOuter, cls));
  }
}

const sheetRect = (W, H) => `<rect class="sheet" x="0" y="0" width="${W}" height="${H}"/>`;
const holesSvg = () => sheetHoles.map((h) => polyEl(h, 'hole')).join('');
function polyEl(flat, cls) {
  let d = '';
  for (let i = 0; i + 1 < flat.length; i += 2) d += `${flat[i]},${flat[i + 1]} `;
  return `<polygon class="${cls}" points="${d.trim()}"/>`;
}
function bbox(p) {
  let minx = Infinity, miny = Infinity, maxx = -Infinity, maxy = -Infinity;
  for (let i = 0; i + 1 < p.length; i += 2) {
    minx = Math.min(minx, p[i]); maxx = Math.max(maxx, p[i]);
    miny = Math.min(miny, p[i + 1]); maxy = Math.max(maxy, p[i + 1]);
  }
  return { minx, miny, maxx, maxy };
}
function translate(p, dx, dy) {
  const q = new Array(p.length);
  for (let i = 0; i + 1 < p.length; i += 2) { q[i] = p[i] + dx; q[i + 1] = p[i + 1] + dy; }
  return q;
}

// ---- export ---------------------------------------------------------------

$('dl-svg').addEventListener('click', () => {
  if (!lastResponse) return;
  const W = +$('sheetW').value, H = +$('sheetH').value;
  let body = `<rect x="0" y="0" width="${W}" height="${H}" fill="none" stroke="#888"/>`;
  for (const h of sheetHoles) {
    let hp = '';
    for (let i = 0; i + 1 < h.length; i += 2) hp += `${h[i]},${h[i + 1]} `;
    body += `<polygon points="${hp.trim()}" fill="none" stroke="#b3261e" stroke-dasharray="5 4"/>`;
  }
  for (const pl of lastResponse.Placed) {
    let pts = '';
    for (let i = 0; i + 1 < pl.PlacedOuter.length; i += 2) pts += `${pl.PlacedOuter[i]},${pl.PlacedOuter[i + 1]} `;
    body += `<polygon points="${pts.trim()}" fill="none" stroke="#3a6ea5"/>`;
  }
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}">${body}</svg>`;
  const url = URL.createObjectURL(new Blob([svg], { type: 'image/svg+xml' }));
  const a = document.createElement('a');
  a.href = url; a.download = 'frahan-nested.svg'; a.click();
  URL.revokeObjectURL(url);
});
