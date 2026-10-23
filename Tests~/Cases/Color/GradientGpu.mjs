// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('GradientGpu: source/scalar contracts');
const assert = context.assert;

context.case('GradientGpu original assertions and branches', async () => {
    const read = p => readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
    const layer = read('Layers/GradientLayerBehaviour.cs');
    const render = layer.slice(layer.indexOf('internal override RenderTexture Render'), layer.indexOf('private static void CopyGradient'));
    assert.doesNotMatch(render, /GenerateGradientTexture|new Texture2D|\.Evaluate\(|ReadPixels|\.Apply\(/);
    assert.match(render, /Graphics\.Blit\(null, source, material, 0\)/);
    assert.match(render, /RenderTexture\.active = previous/);
    assert.match(render, /GL\.sRGBWrite = srgb/);
    assert.match(render, /RenderTexture\.ReleaseTemporary\(source\)/);
    const palette = read('WhimTexGradientPalette.cs');
    assert.ok(palette.indexOf('source.EqualsRamp(gradient)') >= 0 && palette.indexOf('source.EqualsRamp(gradient)') < palette.indexOf('gradient.ColorKeys'));
    assert.match(palette, /texture\.GetRawTextureData<Color>\(\)/);
    assert.match(palette, /var evaluated = gradient\.Clone\(\)/);
    assert.match(palette, /evaluated\.WrapMode = WhimTexGradientWrapMode\.Clamp/);
    assert.match(palette, /evaluated\.Mode == WhimTexGradientMode\.Fixed/);
    assert.match(render, /palette\.Bind\(material, gradient/);
    assert.match(palette, /"_GradientWrapMode"/);
    assert.match(layer, /destination = source\.Clone\(\)/);
    assert.match(layer, /previewGradient\.Equals\(evaluated\)/);
    assert.doesNotMatch(layer, /GradientUtility\.ComputeHash/);
    assert.match(layer, /palette\?\.Dispose\(\)/);
    assert.match(palette, /DestroyImmediate\(texture\)/);
    const shader = read('Shaders/Gradient.shader') + '\n' + read('Shaders/GradientPalette.cginc');
    assert.match(shader, /float4[^;]*_GradientIntervals\[129\]/);
    assert.match(shader, /color\.rgb = SpriteDecode\(color\.rgb\)/);
    assert.match(shader, /floor\(uv \* _GradientOutputSize\)/);
    assert.match(shader, /all\(p == 0\)/);
assert.match(shader, /float WrapGradientTime\(float t, int wrapMode\)/);
    assert.match(shader, /frac\(t\)/);
    assert.match(shader, /float mirrored = t - floor\(t \* \.5\) \* 2/);
    assert.match(shader, /return mirrored <= 1 \? mirrored : 2 - mirrored/);
    const key = Math.fround(.1);
    const cancelled = Math.fround(1 - Math.abs(Math.fround(key - 1)));
    assert.ok(cancelled > key, 'The old Mirror formula moved an exact Fixed boundary to the next interval');
    const mirrored = Math.fround(key - Math.fround(Math.floor(Math.fround(key * .5)) * 2));
    assert.equal(mirrored, key, 'Mirror must preserve interior coordinates exactly');
    assert.match(read('Utils.cs'), /DestroyImmediate\(gradientMaterial\)/);
    
    const utils = read('Utils.cs');
    assert.match(utils, /gradient\?\.Clone\(\)/);
    assert.match(utils, /gradient\?\.GetHashCode\(\)/);
    assert.match(read('Layers/Editors/GradientLayerEditorWindow.cs'), /layer\.gradient = GradientUtility\.Create\(evt\.newValue\)/);
});

await finish(context);

