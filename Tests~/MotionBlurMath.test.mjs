// CPU reference + source contracts only. Does not compile or run Unity.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
let checks = 0;
function near(a, b, label, tolerance = 1e-8) {
  assert.ok(Math.abs(a - b) <= tolerance, `${label}: ${a} vs ${b}`); checks++;
}
const layer = read('src/Layers/MotionBlurRenderer.cs');
const settings = read('src/Layers/BlurLayerBehaviour.cs');
const shader = read('src/Shaders/MotionBlur.shader');
const ui = read('src/Layers/Editors/BlurLayerEditorWindow.cs');
const api = read('src/Automation/WhimTexApi.Blur.cs');
const fullLimit = +layer.match(/FullSampleLimit = (\d+)/)[1];
const fastLimit = +layer.match(/InteractiveSampleLimit = (\d+)/)[1];
const segments = (path, limit = fullLimit) => Math.max(1, Math.min(limit - 1, Math.ceil(path)));
function trajectory(x, y, w, h, { mode = 'Linear', distance = 16, angle = 0, arc = 15, center = [.5, .5], bias = 0, limit = fullLimit } = {}) {
  const rotation = angle * Math.PI / 180, radians = arc * Math.PI / 180;
  const px = x + .5 - center[0] * w, py = y + .5 - center[1] * h;
  const count = segments(mode === 'Linear' ? distance : Math.hypot(px, py) * radians, limit);
  return Array.from({ length: count + 1 }, (_, k) => {
    const t = k / count - .5 + bias;
    const weight = (k === 0 || k === count ? .5 : 1) / count;
    if (mode === 'Linear') return [x - Math.cos(rotation) * distance * t, y - Math.sin(rotation) * distance * t, weight];
    const a = -radians * t, c = Math.cos(a), s = Math.sin(a);
    return [center[0] * w + c * px - s * py - .5, center[1] * h + s * px + c * py - .5, weight];
  });
}
function address(p, size, edge) {
  if (edge === 'Repeat') return p - Math.floor(p / size) * size;
  if (edge === 'Mirror') { const q = p - Math.floor(p / (2 * size)) * 2 * size; return Math.min(q, 2 * size - 1 - q); }
  return Math.max(0, Math.min(size - 1, p));
}
function sample(data, w, h, x, y, edge) {
  function at(x, y) {
    if (edge === 'Transparent' && (x < 0 || y < 0 || x >= w || y >= h)) return [0, 0, 0, 0];
    const c = data[address(y, h, edge) * w + address(x, w, edge)];
    return [c[0] * c[3], c[1] * c[3], c[2] * c[3], c[3]];
  }
  const ix = Math.floor(x), iy = Math.floor(y), fx = x - ix, fy = y - iy;
  const colors = [at(ix, iy), at(ix + 1, iy), at(ix, iy + 1), at(ix + 1, iy + 1)];
  return [0, 1, 2, 3].map(c => colors[0][c] * (1 - fx) * (1 - fy) + colors[1][c] * fx * (1 - fy) +
    colors[2][c] * (1 - fx) * fy + colors[3][c] * fx * fy);
}
function pixel(data, w, h, x, y, settings = {}, edge = 'Transparent') {
  const total = [0, 0, 0, 0];
  for (const [sx, sy, weight] of trajectory(x, y, w, h, settings)) {
    const color = sample(data, w, h, sx, sy, edge);
    for (let c = 0; c < 4; c++) total[c] += color[c] * weight;
  }
  return total.map((c, i) => i === 3 ? c : total[3] > 0 ? c / total[3] : 0);
}

for (const w of [1, 17, 5000]) for (const h of [1, 25, 5000])
  for (const mode of ['Linear', 'Circular']) for (const bias of [-.5, 0, .5])
    for (const limit of [fastLimit, fullLimit]) {
      const settings = { mode, distance: 512, arc: 360, center: [.2, .7], bias, limit };
      const path = trajectory(0, 0, w, h, settings);
      assert.ok(path.length <= limit && path.length >= 2); checks++;
      near(path.reduce((sum, p) => sum + p[2], 0), 1, 'Normalized exposure');
      if (mode === 'Circular') {
        const radius = Math.hypot(.5 - .2 * w, .5 - .7 * h);
        for (const [x, y] of path) near(Math.hypot(x + .5 - .2 * w, y + .5 - .7 * h), radius, 'Circular in pixels, not elliptical UV');
      }
    }

const w = 33, h = 25, cx = 16, cy = 12;
const input = Array.from({ length: w * h }, () => [0, 8, 0, 0]);
input[cy * w + cx] = [4, 0, 0, 1];
for (const angle of [0, 90]) for (let offset = -6; offset <= 6; offset++) {
  const x = cx + (angle === 0 ? offset : 0), y = cy + (angle === 90 ? offset : 0);
  const actual = pixel(input, w, h, x, y, { distance: 8, angle });
  const expected = Math.abs(offset) > 4 ? 0 : Math.abs(offset) === 4 ? 1 / 16 : 1 / 8;
  near(actual[3], expected, 'Linear impulse');
  if (expected > 0) { near(actual[0], 4, 'HDR preserved'); near(actual[1], 0, 'Hidden RGB excluded'); }
}
for (const mode of ['Linear', 'Circular']) {
  const p = pixel(input, w, h, cx, cy, { mode, distance: 0, arc: 0 });
  p.forEach((c, i) => near(c, input[cy * w + cx][i], 'Zero amount identity'));
}
near(pixel(input, w, h, cx, cy, { mode: 'Circular', arc: 180 })[3], 1, 'Rotation center stays fixed');
assert.ok(pixel(input, w, h, cx + 4, cy, { distance: 8, bias: .5 })[3] > .1);
near(pixel(input, w, h, cx - 4, cy, { distance: 8, bias: .5 })[3], 0, 'Forward linear direction');
const forward = trajectory(cx + 5, cy, w, h, { mode: 'Circular', arc: 90, bias: .5 });
assert.ok(forward.at(-1)[1] < cy); checks++; // Inverse sampling makes the visible trail turn counterclockwise.
for (const edge of ['Clamp', 'Repeat', 'Mirror']) for (const mode of ['Linear', 'Circular']) {
  const data = Array.from({ length: w * h }, () => [.25, .5, 4, .7]);
  for (const [x, y] of [[0, 0], [cx, cy], [w - 1, h - 1]]) {
    pixel(data, w, h, x, y, { mode, arc: 190, angle: 37, center: [.1, .8] }, edge)
      .forEach((c, i) => near(c, data[0][i], 'Constant under ' + edge));
  }
}
const seam = Array.from({ length: w * h }, () => [0, 0, 0, 0]);
seam[cy * w] = [1, 0, 0, 1];
assert.ok(pixel(seam, w, h, w - 1, cy, { distance: 8 }, 'Repeat')[3] > 0); checks++;
near(pixel(seam, w, h, w - 1, cy, { distance: 8 }, 'Clamp')[3], 0, 'No opposite edge under Clamp');

// Recurrence in the shader versus direct rotations, including long arcs on nonsquare canvases.
for (const arc of [1, 15, 180, 360]) for (const bias of [-.5, 0, .5]) {
  const path = trajectory(4999, 1499, 5000, 1500, { mode: 'Circular', arc, bias });
  const step = -arc * Math.PI / 180 / (path.length - 1);
  let px = path[0][0] + .5 - 2500, py = path[0][1] + .5 - 750;
  for (const [x, y] of path) {
    near(px + 2499.5, x, 'Rotation recurrence X', 1e-6);
    near(py + 749.5, y, 'Rotation recurrence Y', 1e-6);
    [px, py] = [Math.cos(step) * px - Math.sin(step) * py, Math.sin(step) * px + Math.cos(step) * py];
  }
}

assert.match(shader, /clamp\(ceil\(pathLength\), 1, _SampleLimit - 1\)/);
assert.match(shader, /c\.rgb \* c\.a/);
assert.match(shader, /c\.rgb \/ c\.a/);
assert.match(shader, /\(i\.uv - _Center\) \* _CanvasSize/);
assert.match(shader, /sincos\(-_Arc \/ count/);
assert.match(shader, /\(k & 31\) == 0/);
assert.match(shader, /_MainTex_TexelSize\.w \/ _CanvasSize\.y/);
assert.doesNotMatch(layer, /GetPixels|ReadPixels|SetPixels|Undo\./);
assert.match(layer, /interactive \? InteractiveSampleLimit : FullSampleLimit/);
assert.match(layer, /reduction < 4/);
assert.match(settings, /RequiresColorInput => true/);
for (const key of ['mode', 'strength', 'distance', 'angle', 'arc', 'center', 'direction', 'edges']) {
  assert.ok(api.includes('["' + key + '"]'), 'Snapshot ' + key);
  assert.ok(ui.includes('layer.' + key), 'UI ' + key);
}
assert.doesNotMatch(ui, /\.Clear\(|\.style\.|isDelayed/);
assert.match(read('src/EffectRenderCache.cs'), /effect\.RequiresColorInput/);
assert.match(read('src/TextureCompositor.cs'), /effect\.RequiresColorInput/);
assert.match(read('src/LayerTypeRegistry.cs'), /new Entry\("blur", "Blur", "Blur", "Blur", typeof\(BlurLayerBehaviour\)/);
assert.match(read('src/Automation/WhimTexApi.Inspect.cs'), /blurDefaults/);
assert.match(read('src/Utils.cs'), /DestroyImmediate\(motionBlurMaterial\)/);
function strengthMix(source, blur, strength) {
  const premul = c => c.map((v, i) => i === 3 ? v : v * c[3]);
  const a = premul(source), b = premul(blur);
  const c = strength < 1 ? a.map((v, i) => v * (1 - strength) + b[i] * strength) : b;
  const color = c.slice(0, 3).map(v => c[3] > 0 ? v / c[3] : 0);
  const alpha = strength <= 1 ? c[3] : c[3] * strength / (1 + c[3] * (strength - 1));
  return [...color, alpha];
}
for (const alpha of [0, 1e-8, .01, .125, .5, .9, 1]) {
  let previous = alpha;
  for (const strength of [1, 1.001, 1.5, 2, 3, 4]) {
    const result = strengthMix([0, 8, 0, 0], [4, .5, .1, alpha], strength);
    assert.ok(result[3] >= previous - 1e-12 && result[3] <= 1);
    previous = result[3]; checks++;
    if (alpha > 0) result.slice(0, 3).forEach((v, i) => near(v, [4, .5, .1][i], 'Gain preserves HDR color'));
    if (alpha === 0 || alpha === 1) near(result[3], alpha, 'Gain preserves empty/opaque alpha');
  }
}
const original = [4, 0, 0, 1], blurred = [0, 2, 0, .25];
strengthMix(original, blurred, 0).forEach((v, i) => near(v, original[i], 'Strength zero'));
strengthMix(original, blurred, 1).forEach((v, i) => near(v, blurred[i], 'Strength one parity'));
strengthMix(original, blurred, .5).forEach((v, i) => near(v, [3.2, .4, 0, .625][i], 'Premultiplied strength mix'));
near(strengthMix(original, [4, 0, 0, .125], 2)[3], 2 / 9, 'Double strength density');
assert.match(settings, /public float strength = 1f/);
assert.match(layer, /amount == 0f/);
assert.match(layer, /SetTexture\("_SourceTex", null\)/);
assert.match(shader, /lerp\(source, c, _Strength\)/);
assert.match(shader, /a \* _Strength \/ \(1 \+ a \* \(_Strength - 1\)\)/);
console.log(`Motion Blur: ${checks} CPU math checks and source contracts passed. GPU validation is separate.`);
