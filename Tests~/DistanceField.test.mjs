import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const sdf = read('src/Layers/SDFLayerBehaviour.cs'), outline = read('src/Layers/OutlineLayerBehaviour.cs');
function body(text, signature) {
    const at = text.indexOf(signature); assert.ok(at >= 0, signature);
    const start = text.indexOf('{', at); let end = start + 1, depth = 1;
    while (depth) { if (text[end] === '{') depth++; if (text[end] === '}') depth--; end++; assert.ok(end <= text.length); }
    return text.slice(start + 1, end - 1);
}
function extract(text, signature, ...args) {
    const code = body(text, signature)
        .replace(/\b(?:float|int|bool)\s+(\w+)/g, 'let $1')
        .replace(/(\d+(?:\.\d*)?(?:e[+-]?\d+)?)f\b/gi, '$1')
        .replace(/\(int\)/g, '')
        .replace('let y = index / width;', 'let y = Math.floor(index / width);');
    return new Function('state', ...args, `with(state) { ${code} }`);
}
const math = { ...Object.fromEntries(['min', 'max', 'sqrt', 'abs', 'sign'].map(k => [k, Math[k]])), saturate: x => Math.max(0, Math.min(1, x)) };
const source = sdf.slice(sdf.indexOf('internal static class DistanceFieldSource'));
const isObject = extract(source, 'public static bool IsObject(', 'color', 'sourceChannel', 'threshold');
const state = { math, Value: (color, channel) => channel === 4 ? .2126 * color.r + .7152 * color.g + .0722 * color.b : color[['a', 'r', 'g', 'b'][channel]] };
const DistanceFieldSource = {
    Value: state.Value,
    IsObject: (c, ch, t) => isObject(state, c, ch, t),
};
const init = extract(sdf.slice(sdf.indexOf('internal struct InitializeExactDistancesJob')), 'public void Execute(', 'index');
const pass = extract(sdf.slice(sdf.indexOf('internal struct ExactDistanceTransformPassJob')), 'public void Execute(', 'lineIndex');
const finish = extract(sdf.slice(sdf.indexOf('internal struct FinalizeExactSignedDistanceJob')), 'public void Execute(', 'index');
const crossings = extract(sdf.slice(sdf.indexOf('internal struct ContourCrossingsJob')), 'public void Execute(', 'lineIndex');
const finishContour = extract(sdf.slice(sdf.indexOf('internal struct FinalizeContourDistanceJob')), 'public void Execute(', 'index');
function field(input, width, height, aa, threshold = 128) {
    const n = input.length, largeValue = (width * width + height * height) * 4 + 1;
    const outer = Array(n), inner = Array(n), flags = [0, 0];
    const ctx = { input, width, height, math, DistanceFieldSource, antialiased: aa, sourceChannel: 0, threshold,
        distanceToObject: outer, distanceToBackground: inner, coverageFlags: flags, threadIndex: 0, largeValue };
    for (let i = 0; i < n; i++) init(ctx, i);
    const seeds = [outer.slice(), inner.slice()];
    if (!flags[0] || !flags[1]) return { values: Array(n).fill((flags[0] ? -1 : 1) * (aa ? 1e10 : Math.hypot(width, height))), seeds, empty: true };
    const temp = Array(n), vertices = Array(Math.max(width, height)), boundaries = Array(vertices.length + 1);
    if (aa) {
        const cross = { math, DistanceFieldSource, input, output: temp, threshold, sourceChannel: 0, largeValue,
            lineLength: width, lineStride: 1, lineStartStride: width };
        for (let y = 0; y < height; y++) crossings(cross, y);
        const job = { math, input: temp, output: outer, vertices, boundaries, threadIndex: 0,
            scratchLineLength: vertices.length, lineLength: height, lineStride: width, lineStartStride: 1 };
        for (let x = 0; x < width; x++) pass(job, x);
        Object.assign(cross, { lineLength: height, lineStride: width, lineStartStride: 1 });
        for (let x = 0; x < width; x++) crossings(cross, x);
        Object.assign(job, { output: inner, lineLength: width, lineStride: 1, lineStartStride: width });
        for (let y = 0; y < height; y++) pass(job, y);
        Object.assign(ctx, { output: outer, other: inner });
        for (let i = 0; i < n; i++) finishContour(ctx, i);
        return { values: outer, seeds, empty: false };
    }
    for (const distances of [outer, inner]) {
        const job = { math, input: distances, output: temp, vertices, boundaries, threadIndex: 0,
            scratchLineLength: vertices.length, lineLength: height, lineStride: width, lineStartStride: 1 };
        for (let x = 0; x < width; x++) pass(job, x);
        Object.assign(job, { input: temp, output: distances, lineLength: width, lineStride: 1, lineStartStride: width });
        for (let y = 0; y < height; y++) pass(job, y);
    }
    ctx.output = outer;
    for (let i = 0; i < n; i++) finish(ctx, i);
    return { values: outer, seeds, empty: false };
}
let checks = 0;
const rgba = a => ({ r: 0, g: 0, b: 0, a });
for (const aa of [false, true]) for (const threshold of [0, 64, 128, 254, 255])
for (let w = 1; w <= 8; w++) for (let h = 1; h <= 7; h++) {
    const input = Array.from({ length: w * h }, (_, i) => rgba((i * 73 + w * 37 + h * 11) % 256));
    const { values, seeds, empty } = field(input, w, h, aa, threshold);
    if (empty) { assert.ok(values.every(Number.isFinite)); continue; }
    const points = [];
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
        const a = input[y * w + x].a;
        for (const [dx, dy] of [[1, 0], [0, 1]]) if (x + dx < w && y + dy < h) {
            const b = input[(y + dy) * w + x + dx].a;
            if ((a > threshold) !== (b > threshold)) {
                const t = (threshold - a) / (b - a);
                points.push([x + dx * t, y + dy * t]);
            }
        }
    }
    for (let i = 0; i < values.length; i++) {
        const distances = seeds.map(s => Math.min(...s.map((v, j) => v + (i % w - j % w) ** 2 + (Math.floor(i / w) - Math.floor(j / w)) ** 2)));
        const expected = aa ? (input[i].a > threshold ? -1 : 1) * Math.min(...points.map(([x,y]) => Math.hypot(i % w - x, Math.floor(i / w) - y)))
            : input[i].a > threshold ? -Math.sqrt(distances[1]) : Math.sqrt(distances[0]);
        assert.ok(Math.abs(values[i] - expected) < 1e-8); checks++;
    }
}
for (const a of [0, 255]) for (const aa of [false, true])
    assert.ok(field(Array(16).fill(rgba(a)), 4, 4, aa).values.every(v => Number.isFinite(v) && (a === 255 ? v < 0 : v > 0)));
const partial = field([rgba(0), rgba(64), rgba(128), rgba(192), rgba(255)], 5, 1, true).values;
assert.deepEqual(partial, [2, 1, 0, -1, -2], 'Wide alpha ramp preserves actual pixel distances');
for (const spread of [2, 6, 20, 60]) {
    const ramp = Array.from({ length: 129 }, (_, x) => rgba(Math.max(0, Math.min(255, Math.round(128 + (64 - x) * 127 / spread)))));
    const values = field(ramp, 129, 1, true).values;
    for (let x = 0; x < 129; x++) assert.ok(Math.abs(values[x] - (x - 64)) < 1e-8, 'Softness must not move contour or inflate distances');
}
for (const spread of [1, 8, 20]) {
    const w = 65, h = 65, radius = 19.4;
    const pixels = Array.from({ length: w * h }, (_, i) => rgba(Math.round(Math.max(0, Math.min(255,
        128 + (radius - Math.hypot(i % w - 32, Math.floor(i / w) - 32)) * 127 / spread)))));
    const values = field(pixels, w, h, true).values;
    for (let i = 0; i < values.length; i++) {
        const analytic = Math.hypot(i % w - 32, Math.floor(i / w) - 32) - radius;
        assert.ok(Math.abs(values[i] - analytic) < .8, 'Soft circle stays within subpixel error of the contour');
        const transposed = (i % w) * h + Math.floor(i / w);
        assert.ok(Math.abs(values[i] - values[transposed]) < 1e-8, 'No horizontal/vertical bias');
    }
}
const binaryA = field([rgba(0), rgba(140), rgba(255)], 3, 1, false).values;
const binaryB = field([rgba(0), rgba(180), rgba(255)], 3, 1, false).values;
assert.deepEqual(binaryA, binaryB, 'Legacy threshold mode remains unchanged');
assert.notDeepEqual(field([rgba(0), rgba(140), rgba(255)], 3, 1, true).values,
    field([rgba(0), rgba(180), rgba(255)], 3, 1, true).values, 'AA retains partial coverage changes');

const executeOutline = extract(outline.slice(outline.indexOf('internal struct OutlineJob')), 'public void Execute(', 'index');
const OutlineLayerBehaviour = { OutlinePosition: { Outside: 0, Inside: 1, Center: 2 } };
const Color = function(r, g, b, a) { Object.assign(this, { r, g, b, a }); };
for (const aa of [false, true]) for (const position of [0, 1, 2]) for (const width of [0, .25, .5, 1, 2, 4]) for (const soft of [0, .5, 1, 4]) {
    const ctx = { math, OutlineLayerBehaviour, Color, signedDistances: [-3, -2, -1, 1, 2, 3], output: [],
        antialiasedDistance: aa, outlinePosition: position, outlineWidth: width, outlineSoftness: soft,
        outlineOffset: 0, fillCenter: false, fillColor: { r: 1, g: 1, b: 1, a: 1 },
        outlineColor: { r: 3, g: .2, b: 0, a: .7 }, width: 6, height: 1 };
    for (let i = 0; i < 6; i++) executeOutline(ctx, i);
    assert.ok(ctx.output.every(c => Number.isFinite(c.a) && c.a >= 0 && c.a <= .7 && Math.abs(c.r - 3) < 1e-12));
    assert.equal(ctx.output.some(c => c.a > 0), width > 0, 'Thin bands survive; zero width stays empty');
}
function renderRow({ offset = 0, filled = false, fillAlpha = 1, borderAlpha = 1, width = 4, soft = 1 } = {}) {
    const signedDistances = Array.from({ length: 161 }, (_, i) => (i - 80) / 8);
    const ctx = { math, OutlineLayerBehaviour, Color, signedDistances, output: [], width: signedDistances.length, height: 1,
        antialiasedDistance: true, outlinePosition: 0, outlineWidth: width, outlineSoftness: soft, outlineOffset: offset,
        fillCenter: filled, fillColor: { r: 0, g: 0, b: 2, a: fillAlpha }, outlineColor: { r: 3, g: 0, b: 0, a: borderAlpha } };
    for (let i = 0; i < signedDistances.length; i++) executeOutline(ctx, i);
    return ctx.output;
}
const filled = renderRow({ filled: true });
assert.ok(filled.slice(0, 100).every(c => Math.abs(c.a - 1) < 1e-12), 'Fill and border share a seam without alpha loss');
assert.equal(filled[0].b, 2, 'Fill HDR color is preserved');
assert.equal(filled[96].r, 3, 'Border HDR color is preserved');
assert.equal(renderRow()[0].a, 0, 'Hollow center remains transparent');
for (const value of [1e10, -1e10]) for (const fillCenter of [false, true]) {
    const ctx = { math, OutlineLayerBehaviour, Color, signedDistances: [value], output: [], width: 1, height: 1,
        antialiasedDistance: true, outlinePosition: 0, outlineWidth: 1000, outlineSoftness: 1000, outlineOffset: 1000,
        fillCenter, fillColor: { r: 1, g: 1, b: 1, a: 1 }, outlineColor: { r: 1, g: 1, b: 1, a: 1 } };
    executeOutline(ctx, 0);
    assert.equal(ctx.output[0].a, value < 0 && fillCenter ? 1 : 0, 'Uniform masks never create a false border');
}
assert.ok(renderRow({ filled: true, fillAlpha: 0 }).every((c, i) => c.a === renderRow()[i].a), 'Transparent fill matches hollow border');
const shifted = renderRow({ offset: -2 }), unshifted = renderRow();
for (let i = 0; i < shifted.length - 16; i++) assert.ok(Math.abs(shifted[i].a - unshifted[i + 16].a) < 1e-12, 'Offset translates without resizing');
for (const filled of [false, true]) for (const soft of [0, 1, 10]) for (const width of [0, .5, 4])
    assert.ok(renderRow({ filled, soft, width, fillAlpha: .4, borderAlpha: .7 }).every(c => c.a >= 0 && c.a <= 1 && Number.isFinite(c.r)));
for (const name of ['outlineOffset', 'fillCenter', 'fillColor']) {
    assert.ok(read('src/Automation/WhimTexApi.Layers.cs').includes(`settings, "${name}"`) || read('src/Automation/WhimTexApi.Layers.cs').includes(`settings["${name}"]`));
    assert.ok(read('src/Automation/WhimTexApi.Inspect.cs').includes(`settings["${name}"]`));
}
assert.ok(read('src/WhimTexPsdExporter.cs').includes('!layer.fillCenter && layer.outlineOffset == 0f'));
assert.ok(sdf.includes('RequiresColorInput => sourceChannel != SourceChannel.Alpha'));
assert.ok(read('src/EffectRenderCache.cs').includes('if (effect.RequiresColorInput) colorSources.Add(input)'));
assert.ok(read('src/TextureCompositor.cs').includes('effect.RequiresColorInput, includeDisabled: true'));
const enums = read('src/Utils.cs');
for (const [name, value] of [['EuclideanExact', 0], ['EuclideanApproximate', 1], ['Manhattan', 2], ['Chebyshev', 3], ['EuclideanAntialiased', 4]])
    assert.ok(enums.includes(`${name} = ${value}`));
assert.ok(read('src/Automation/WhimTexApi.Inspect.cs').includes('System.Enum.GetNames(typeof(DistanceMetric))'));
console.log(`Distance field: ${checks} extracted EDT vs brute-force checks, AA/Outline and source/API contracts passed (Unity/Burst/GPU not executed).`);
