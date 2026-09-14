import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { test } from 'node:test';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const blur = read('src/Layers/BlurLayerBehaviour.cs');
const ui = read('src/Layers/Editors/BlurLayerEditorWindow.cs');
const api = read('src/Automation/WhimTexApi.Blur.cs');

test('one serialized Blur type dispatches to the existing filters, applying output settings once', () => {
    assert.match(blur, /public sealed class BlurLayerBehaviour : TargetedLayerBehaviour/);
    assert.match(blur, /enum BlurType \{ Gaussian, Linear, Circular \}/);
    assert.match(blur, /public BlurType mode;/);
    assert.match(blur, /mode == BlurType.Gaussian\s*\? GaussianBlurRenderer.RenderBlur\(this, context\) : MotionBlurRenderer.RenderBlur\(this, context\)/);
    assert.doesNotMatch(blur, /ApplyTransformAndModifiers|selectedBlurType|DefaultBlurType|FormerlySerializedAs/);
    for (const name of ['Gaussian', 'Motion']) {
        const renderer = read(`src/Layers/${name}BlurRenderer.cs`);
        assert.match(renderer, /layer.ApplyTransformAndModifiers\(straight, context\)/);
        assert.match(renderer, /layer.ApplyTransformAndModifiers\(context.input, context\)/);
        assert.ok(!existsSync(new URL(`../src/Layers/${name}BlurLayerBehaviour.cs`, import.meta.url)));
    }
    assert.match(read('src/Layers/MotionBlurRenderer.cs'), /layer.mode == BlurType.Circular/);
    assert.match(read('src/EffectRenderCache.cs'), /JsonUtility.ToJson\(layer\)/);
});

test('unified properties expose all settings and only hide inactive controls without resetting them', () => {
    for (const key of ['mode', 'strength', 'radius', 'distance', 'angle', 'arc', 'center', 'direction', 'edges']) {
        assert.ok(ui.includes(`layer.${key}`), key);
        assert.ok(api.includes(`["${key}"]`), `Snapshot ${key}`);
        assert.ok(api.includes(`"${key}"`), `Setting ${key}`);
    }
    assert.ok(ui.includes('() => layer.mode = (BlurType)evt.newValue'));
    assert.match(ui, /gaussian.EnableInClassList\("whimtex-hidden", layer.mode != BlurType.Gaussian\)/);
    assert.match(ui, /direction.EnableInClassList\("whimtex-hidden", layer.mode == BlurType.Gaussian\)/);
    assert.match(ui, /linear.EnableInClassList\("whimtex-hidden", layer.mode != BlurType.Linear\)/);
    assert.match(ui, /circular.EnableInClassList\("whimtex-hidden", layer.mode != BlurType.Circular\)/);
    const window = read('src/TextureCompositorWindow.cs');
    assert.match(window, /new GUIContent\(descriptor.MenuName\)/);
    assert.match(window, /new GUIContent\("Add Inside\/" \+ descriptor.InsideMenuName\)/);
    assert.doesNotMatch(window, /new GUIContent\("(?:Add Inside\/)?(?:Gaussian Blur|Motion Blur)/);
    const factory = read('src/Automation/WhimTexApi.Layers.cs');
    assert.match(factory, /LayerTypeRegistry.Find\(type\)/);
    assert.match(read('src/LayerTypeRegistry.cs'), /new Entry\("blur", "Blur", "Blur", "Blur", typeof\(BlurLayerBehaviour\)/);
    assert.doesNotMatch(factory, /"gaussianBlur"|"motionBlur"/);
});

test('1D noise projects before warp and keeps the original 2D path', () => {
    const shader = read('src/Shaders/Noise.shader');
    assert.match(shader, /float2 p = \(i.uv - .5\) \* _NoiseDomain.xy \* _NoiseScale \+ _NoiseDomain.zw/);
    assert.match(shader, /if \(_NoiseOneD != 0\)/);
    assert.match(shader, /p = float2\(dot\(centered, _NoiseAxis.xy\), 0.0\) \+ _NoiseDomain.zw/);
    assert.ok(shader.indexOf('dot(centered, _NoiseAxis.xy)') < shader.indexOf('fnlDomainWarp2D'));
    assert.match(read('src/Layers/NoiseLayerBehaviour.cs'), /enum NoiseDimensions \{ TwoD, OneD \}/);
    assert.match(read('src/Layers/Editors/NoiseLayerEditorWindow.cs'), /axis.EnableInClassList\("whimtex-hidden", layer.dimensions != NoiseLayerBehaviour.NoiseDimensions.OneD\)/);
});

test('1D domain stays constant along stripes on rectangular canvases and at any direction', () => {
    // Mathematical reference for the shader projection, not a GPU/FastNoiseLite execution.
    const project = (uv, w, h, scale, angle, offset) => {
        const a = angle * Math.PI / 180, shortest = Math.min(w, h);
        return [(uv[0] - .5) * w / shortest * scale * Math.cos(a)
            + (uv[1] - .5) * h / shortest * scale * Math.sin(a) + offset[0], offset[1]];
    };
    for (const [w, h] of [[512, 512], [5000, 1200], [150, 800]])
        for (const angle of [-180, -73, 0, 37, 90, 180])
            for (const scale of [.01, 8, 1000])
                for (const offset of [[0, 0], [-3, 4], [10000, -10000]]) {
                    const a = angle * Math.PI / 180, uv = [.29, .64], distance = 21;
                    const alongStripe = [uv[0] - Math.sin(a) * distance / w, uv[1] + Math.cos(a) * distance / h];
                    const p = project(uv, w, h, scale, angle, offset);
                    const q = project(alongStripe, w, h, scale, angle, offset);
                    p.forEach((v, i) => assert.ok(Math.abs(v - q[i]) < 1e-8));
                    const acrossStripe = [uv[0] + Math.cos(a) * distance / w, uv[1] + Math.sin(a) * distance / h];
                    const r = project(acrossStripe, w, h, scale, angle, offset);
                    assert.ok(Math.abs(r[0] - p[0] - distance / Math.min(w, h) * scale) < 1e-8);
                }
});
