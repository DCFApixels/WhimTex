import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const layer = read('src/Layers/GaussianBlurRenderer.cs');
const settings = read('src/Layers/BlurLayerBehaviour.cs');
const shader = read('src/Shaders/GaussianBlur.shader');
const motion = read('src/Shaders/MotionBlur.shader');
const api = read('src/Automation/WhimTexApi.Blur.cs');
const ui = read('src/Layers/Editors/BlurLayerEditorWindow.cs');
const finish = s => s.slice(s.indexOf('float4 unpremultiply('), s.indexOf('ENDCG')).trim();
assert.equal(finish(shader), finish(motion), 'Gaussian and Motion use identical premultiplied mixing/density output');
const expression = shader.match(/alpha = (a \* _Strength[^;]+);/)[1];
const density = new Function('a', '_Strength', `return ${expression};`);
let checks = 0;
for (let i = 0; i <= 1000; i++) {
    const a = i / 1000;
    let previous = a;
    for (const strength of [1, 1.01, 1.5, 2, 3, 4]) {
        const result = density(a, strength);
        assert.ok(Number.isFinite(result) && result >= previous - 1e-12 && result <= 1);
        if (a === 0 || a === 1) assert.equal(result, a);
        previous = result; checks++;
    }
}
assert.ok(Math.abs(density(.125, 2) - 2 / 9) < 1e-12);
assert.match(shader, /source\.rgb \*= source\.a/);
assert.match(shader, /c = lerp\(source, c, _Strength\)/);
assert.match(shader, /float3 color = c\.a > 0 \? c\.rgb \/ c\.a : 0/);
assert.match(settings, /public float strength = 1f/);
assert.match(settings, /public const float MaximumStrength = 4f/);
assert.match(layer, /float\.IsNaN\(strength\) \|\| float\.IsInfinity\(strength\) \? 1f/);
assert.ok(layer.indexOf('if (amount == 0f || pixels <= .0001f)') < layer.indexOf('Material material'));
assert.match(layer, /SetFloat\("_Strength", amount\)/);
assert.match(layer, /SetTexture\("_SourceTex", amount < 1f \? context\.input : null\)/);
assert.match(layer, /finally\s*\{\s*material\.SetTexture\("_SourceTex", null\)/);
assert.ok(api.includes('"strength", "radius", "distance"'));
assert.match(api, /Number\(value, "strength", layer\.strength, 0f, BlurLayerBehaviour\.MaximumStrength\)/);
assert.match(api, /\["strength"\] = layer\.strength/);
assert.ok(ui.includes('Slider(root, "Strength (%)"'));
assert.ok(ui.includes('() => layer.strength * 100f, value => layer.strength = value / 100f'));

assert.match(read('src/Automation/WhimTexApi.Inspect.cs'), /BlurSnapshot\(new BlurLayerBehaviour\(\)\)/);
console.log(`Gaussian Strength: ${checks} density checks and Motion parity/UI/API/bypass contracts passed (Unity/GPU not executed).`);
