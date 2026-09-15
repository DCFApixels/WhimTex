import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const fnl = read('src/Shaders/ThirdParty/FastNoiseLite.hlsl');
const layer = read('src/Layers/NoiseLayerBehaviour.cs');
const shader = read('src/Shaders/Noise.shader');
const api = read('src/Automation/WhimTexApi.Noise.cs');
const ui = read('src/Layers/Editors/NoiseLayerEditorWindow.cs');
const guard = '#ifndef WHIMTEX_FASTNOISELITE_INCLUDED\n#define WHIMTEX_FASTNOISELITE_INCLUDED\n\n';
assert.ok(fnl.includes(guard) && fnl.endsWith('#endif\n'), 'Built-in noise has a duplicate-include guard');
const upstream = fnl.replace(guard, '').replace(/#endif\n$/, '');
assert.equal(createHash('sha256').update(upstream).digest('hex'),
    '275f0e558ea7fd967dd0a3f47f14397759e9e6da403d11c290484707dfb8c2cb', 'Pinned upstream HLSL is unchanged');

const mappings = {
    NoiseType: ['FNL_NOISE_OPENSIMPLEX2', 'FNL_NOISE_OPENSIMPLEX2S', 'FNL_NOISE_CELLULAR', 'FNL_NOISE_PERLIN', 'FNL_NOISE_VALUE_CUBIC', 'FNL_NOISE_VALUE'],
    FractalType: ['FNL_FRACTAL_NONE', 'FNL_FRACTAL_FBM', 'FNL_FRACTAL_RIDGED', 'FNL_FRACTAL_PINGPONG'],
    CellularDistance: ['FNL_CELLULAR_DISTANCE_EUCLIDEAN', 'FNL_CELLULAR_DISTANCE_EUCLIDEANSQ', 'FNL_CELLULAR_DISTANCE_MANHATTAN', 'FNL_CELLULAR_DISTANCE_HYBRID'],
    CellularReturn: ['FNL_CELLULAR_RETURN_TYPE_CELLVALUE', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2ADD', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2SUB', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2MUL', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2DIV'],
    WarpType: [null, 'FNL_DOMAIN_WARP_OPENSIMPLEX2', 'FNL_DOMAIN_WARP_OPENSIMPLEX2_REDUCED', 'FNL_DOMAIN_WARP_BASICGRID']
};
for (const [type, constants] of Object.entries(mappings)) {
    const entries = layer.match(new RegExp(`public enum ${type} \\{([^}]+)\\}`))[1].split(',').map(s => s.trim());
    assert.equal(entries.length, constants.length + (type === 'NoiseType' ? 2 : 0), type);
    if (type === 'NoiseType') assert.equal(entries[6], 'WhiteNoise', 'White Noise appends without renumbering FastNoiseLite algorithms');
    if (type === 'NoiseType') assert.equal(entries[7], 'BlueNoise', 'Blue Noise appends without renumbering previous algorithms');
    constants.forEach((name, index) => {
        if (name === null) return;
        const value = Number(fnl.match(new RegExp(`#define ${name} (\\d+)\\b`))[1]);
        assert.equal(value, index - (type === 'WarpType' ? 1 : 0), `${type}.${entries[index]}`);
    });
}
for (const [, name] of layer.matchAll(/material\.Set(?:Integer|Float|Vector)\("([^"]+)"/g))
    assert.match(shader, new RegExp(`\\b${name}\\b`), `Shader uniform ${name}`);
for (const [, field] of layer.matchAll(/^        public (?:\w+) (\w+)(?:\s*=.*)?;/gm)) {
    assert.ok(api.includes(`"${field}"`), `API setting: ${field}`);
    assert.ok(ui.includes(`layer.${field}`), `UI setting: ${field}`);
}
assert.match(layer, /SetInteger\("_NoiseSeed", seed\)/, 'No lossy float conversion of seed');
assert.match(shader, /#pragma target 4\.5/);
assert.match(shader, /if \(_NoiseType == 6 \|\| _NoiseType == 7\)/);
assert.ok(shader.indexOf('WhiteNoise(i.uv)') < shader.indexOf('fnl_state state'), 'White Noise bypasses fractal and warp');
assert.match(ui, /fractalChoice.EnableInClassList\("whimtex-hidden", isWhite\)/);
assert.match(ui, /warpChoice.EnableInClassList\("whimtex-hidden", isWhite\)/);
assert.match(shader, /return float4\(rgb, 1\.0\)/);
assert.match(shader, /if \(_NoiseEncoding == 0\) rgb = SpriteDecode\(rgb\)/);
assert.doesNotMatch(layer, /ReadPixels|GetPixels|SetPixels|GetRawTextureData/);
assert.doesNotMatch(ui, /\.Clear\(|\.isDelayed\s*=\s*true/);
assert.match(ui, /ImmediatePreviewUpdates => true/);
assert.match(read('src/Utils.cs'), /protected virtual bool ImmediatePreviewUpdates => false/);
assert.match(read('src/TextureCompositorWindow.cs'), /immediate \|= GetSelectedLayer\(\)\?\.Behaviour is NoiseLayerBehaviour/);
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-hidden,\s*\.whimtex-brush-setting--hidden\s*\{\s*display: none;/);
assert.match(read('src/LayerTypeRegistry.cs'), /new Entry\("noise", "Noise", "Noise", "Noise Layer", typeof\(NoiseLayerBehaviour\)/);
assert.match(read('src/Automation/WhimTexApi.Layers.cs'), /LayerTypeRegistry.Find\(type\)/);
assert.match(read('src/Automation/WhimTexApi.Inspect.cs'), /LayerTypeRegistry.Find\(layer\?\.Behaviour\?\.GetType\(\)\)\?\.ApiId/);
console.log('Noise source contracts passed: pinned HLSL, enum/uniform mappings, API/UI coverage and live GPU path.');
