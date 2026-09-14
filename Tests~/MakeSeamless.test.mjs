import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const shader = read('src/Shaders/MakeSeamless.shader');
const body = shader.match(/float Weight\(float position, float direction\)\s*\{([\s\S]*?)\}/)[1];
// Execute the actual shader's scalar weighting formula, not a separately maintained approximation.
const weight = new Function('position', 'direction', '_BlendWidth', '_Falloff', 'saturate',
    body.replaceAll('float ', 'let ').replaceAll('pow(', 'Math.pow('));
const clamp = x => Math.max(0, Math.min(1, x));
const w = (p, direction, width, falloff) => weight(p, direction, width, falloff, clamp);
const mix = (a, b, t) => a.map((v, c) => v * (1 - t) + b[c] * t);
const premul = c => [c[0] * c[3], c[1] * c[3], c[2] * c[3], c[3]];
const straight = c => c[3] > 0 ? [c[0]/c[3], c[1]/c[3], c[2]/c[3], c[3]] : [0,0,0,0];
let checks = 0;
function same(a, b) { a.forEach((v, i) => { assert.ok(Math.abs(v - b[i]) < 1e-8, `${a} != ${b}`); checks++; }); }
function render(input, width, height, horizontal, vertical, fade, falloff) {
    const sample = (x, y) => premul(input[y * width + x]);
    return input.map((_, i) => {
        const x = i % width, y = Math.floor(i / width);
        const position = (p, n) => clamp(((p + .5) / n - .5 / n) / Math.max(1 - 1/n, .000001));
        const wx = w(position(x, width), horizontal, fade, falloff);
        const wy = w(position(y, height), vertical, fade, falloff);
        const a = sample(x, y), b = sample(width - 1 - x, y);
        const c = sample(x, height - 1 - y), d = sample(width - 1 - x, height - 1 - y);
        const result = mix(mix(a, b, wx), mix(c, d, wx), wy);
        same(result, mix(mix(a, c, wy), mix(b, d, wy), wx)); // axes commute, including corners
        return straight(result);
    });
}
for (const [width, height] of [[1,1], [1,9], [8,1], [2,2], [17,11], [32,23]]) {
    const input = Array.from({length: width*height}, (_, i) =>
        [i % 7 - 2, (i % 13) * .5, (i % 5) * .33, (i % 9) / 8]);
    for (const h of [0,1,2]) for (const v of [0,1,2])
    for (const fade of [.001,.1,.2,.5]) for (const falloff of [.25,1,4]) {
        const result = render(input, width, height, h, v, fade, falloff);
        if (h) for (let y=0; y<height; y++) same(result[y*width], result[y*width+width-1]);
        if (v) for (let x=0; x<width; x++) same(result[x], result[(height-1)*width+x]);
        if (!h && !v) result.forEach((p,i) => same(p, input[i][3] ? input[i] : [0,0,0,0]));
        if (h && v) {
            const sourceX = h === 1 ? 0 : width-1, sourceY = v === 1 ? 0 : height-1;
            const source = input[sourceY*width+sourceX];
            same(result[0], source[3] ? source : [0,0,0,0]);
        }
    }
}
const hidden = Array.from({length: 9}, (_, i) => i < 4 ? [8,0,0,1] : [0,64,0,0]);
const result = render(hidden,9,1,1,0,.5,1);
assert.ok(result.some(c => c[3] > 0 && c[3] < 1));
for (const c of result) if (c[3]) same(c.slice(0,3), [8,0,0]);
assert.equal(w(1,1,.2,1), 1);
assert.equal(w(0,1,.2,1), 0);
assert.equal(w(0,2,.2,1), 1);
assert.equal(w(1,2,.2,1), 0);
assert.ok(w(.9,1,.2,4) < w(.9,1,.2,1));
assert.match(shader, /float2 position = saturate\(\(uv - .5 \* texel\) \/ max\(1 - texel, .000001\)\)/);
assert.match(shader, /float4 c = lerp\(lerp\(original, acrossX, x\), lerp\(acrossY, acrossBoth, x\), y\)/);
assert.match(shader, /c\.rgb \* c\.a/);
assert.match(shader, /c\.a > 0 \? c\.rgb \/ c\.a : 0/);
const layer = read('src/Layers/MakeSeamlessLayerBehaviour.cs');
assert.match(layer, /RequiresColorInput => true/);
assert.match(layer, /RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear/);
assert.match(layer, /RenderTexture.ReleaseTemporary\(result\)/);
assert.match(layer, /GL.sRGBWrite = srgb/);
assert.match(layer, /horizontal == HorizontalDirection.Off && vertical == VerticalDirection.Off/);
assert.match(read('src/LayerTypeRegistry.cs'), /new Entry\("makeSeamless", "Make Seamless", "Make Seamless", "Make Seamless", typeof\(MakeSeamlessLayerBehaviour\)/);
assert.match(read('src/Automation/WhimTexApi.Layers.cs'), /SetMakeSeamless\(seamless,/);
assert.match(read('src/Automation/WhimTexApi.Inspect.cs'), /LayerTypeRegistry.Find\(layer\?\.Behaviour\?\.GetType\(\)\)\?\.ApiId/);
assert.match(read('src/Automation/WhimTexApi.Inspect.cs'), /settings\["makeSeamless"\] = MakeSeamlessSnapshot/);
for (const key of ['horizontal','vertical','blendWidth','falloff']) {
    const api = read('src/Automation/WhimTexApi.MakeSeamless.cs');
    assert.ok(api.includes(`["${key}"] = layer.${key}`));
    assert.ok(api.includes(`(value, "${key}", layer.${key}`));
}
assert.match(read('src/Utils.cs'), /DestroyImmediate\(makeSeamlessMaterial\)/);
assert.match(read('src/TextureCompositorWindow.Inspector.cs'), /MakeSeamlessLayerEditorWindow.BuildFields/);
console.log(`Make Seamless: ${checks} numerical checks plus integration contracts passed.`);

const editor = read('src/Layers/Editors/MakeSeamlessLayerEditorWindow.cs');
assert.ok(editor.indexOf('root.Add(BuildEdgeSelector') < editor.indexOf('new EnumField("Horizontal"'));
const modes = { Off: 0, LeftToRight: 1, RightToLeft: 2, BottomToTop: 1, TopToBottom: 2 };
const mappings = { left: ['horizontal',2], right: ['horizontal',1], top: ['vertical',1], bottom: ['vertical',2] };
for (const [edge, [axis, selected]] of Object.entries(mappings)) {
    const block = editor.slice(editor.indexOf(`AddEdge("${edge}"`)).split(');')[0];
    const expr = block.match(/\(\) => layer\.(horizontal|vertical) = ([\s\S]*)/);
    assert.equal(expr[1], axis);
    const toggle = new Function('layer', `layer.${axis} = ${expr[2].replace(/MakeSeamlessLayerBehaviour\.\w+Direction\.(\w+)/g, (_, mode) => modes[mode])}`);
    for (const start of [0,1,2]) for (const other of [0,1,2]) {
        const layer = axis === 'horizontal' ? {horizontal:start, vertical:other} : {horizontal:other, vertical:start};
        toggle(layer);
        assert.equal(layer[axis], start === selected ? 0 : selected);
        assert.equal(layer[axis === 'horizontal' ? 'vertical' : 'horizontal'], other);
    }
}
assert.match(editor, /bindings.Add\(Refresh\)/);
assert.match(editor, /new Button\(\(\) => applyChange\("Change Seamless Direction", toggle\)\)/);
assert.match(editor, /EnableInClassList\("whimtex-seamless-edge--selected", selected\(\)\)/);
console.log('Seamless edge selector: extracted toggle logic, destination mapping and shared bindings passed.');
