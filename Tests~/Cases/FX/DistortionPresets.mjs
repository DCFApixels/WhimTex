// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('DistortionPresets: source/scalar contracts');
const assert = context.assert;

context.case('classic distortions keep their existing unbounded Transform 2D mapping', async () => {
    // CPU reference/contract checks, not a substitute for compiling and rendering HLSL in Unity.
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
    const spherize = (p, strength) => {
        const scale = Math.pow(Math.max(p[0] ** 2 + p[1] ** 2, 1e-12), .5 * (2 ** strength - 1));
        return p.map(x => x * scale);
    };
    const sphereSampleRadius = (radius, strength) => {
        const r = Math.min(Math.max(radius, 0), 1);
        const sphereRadius = strength >= 0 ? 2 * Math.asin(r) / Math.PI : Math.sin(r * Math.PI / 2);
        const amount = Math.abs(strength);
        let mapped = radius + (sphereRadius - radius) * Math.min(amount, 1);
        const excess = Math.max(amount - 1, 0);
        mapped = strength >= 0
            ? mapped * 2 ** (-excess * (1 - mapped))
            : 1 - (1 - mapped) * 2 ** (-excess * mapped);
        return mapped;
    };
    const twirl = (p, degrees) => {
        const a = -degrees * Math.PI / 180 * Math.hypot(...p);
        return [Math.cos(a) * p[0] - Math.sin(a) * p[1], Math.sin(a) * p[0] + Math.cos(a) * p[1]];
    };
    const near = (a, b, message = '') => assert.ok(Math.abs(a - b) < 1e-9, message || `${a} != ${b}`);
    {
        for (const name of ['Twirl']) {
            const code = source(name);
            assert.equal(code.split(/\r?\n/)[0], `// @whimtex-effect Distortion/${name}`);
            assert.match(code, /@param transform2D _Area/);
            assert.match(code, /_Area_ToLocal\(uv\)/);
            assert.match(code, /_Area_ToInput\(/);
            assert.match(code, /return SampleInput\(/);
            assert.doesNotMatch(code, /\b(?:saturate|clamp|lerp|smoothstep|clip|discard)\s*\(/);
            assert.equal((code.match(/\bif\s*\(/g) ?? []).length, 1, 'Only the zero-strength identity branch');
        }
        const spherize = source('Spherize');
        assert.equal(spherize.split(/\r?\n/)[0], '// @whimtex-effect Distortion/Spherize');
        assert.match(spherize, /@param hidden enum _Mode = 0 \{Classic: 0, Sphere: 1\}/);
        assert.match(spherize, /if \(_Mode < 0\.5\)/);
        assert.match(spherize, /float4 result = color/);
        assert.match(spherize, /exp2\(_Strength\)/);
        assert.match(spherize, /pow\(max\(dot\(p, p\), 1e-12\), 0\.5 \* \(exponent - 1\.0\)\)/);
        assert.match(spherize, /result = SampleInput\(_Area_ToInput\(0\.5 \+ p \* scale \* 0\.5\), _Tiling\)/);
        assert.equal((spherize.slice(spherize.indexOf('float4 ApplyFX(')).match(/\breturn\b/g) ?? []).length, 1,
            'ApplyFX has one initialized return path; parameter tooltips are not shader statements');
        assert.match(source('Twirl'), /-radians\(_Angle\) \* length\(p\)/);
    }
});

context.case('Sphere mode maps a circular texture over a sphere and clips its edge', async () => {
    // CPU reference/contract checks, not a substitute for compiling and rendering HLSL in Unity.
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
    const spherize = (p, strength) => {
        const scale = Math.pow(Math.max(p[0] ** 2 + p[1] ** 2, 1e-12), .5 * (2 ** strength - 1));
        return p.map(x => x * scale);
    };
    const sphereSampleRadius = (radius, strength) => {
        const r = Math.min(Math.max(radius, 0), 1);
        const sphereRadius = strength >= 0 ? 2 * Math.asin(r) / Math.PI : Math.sin(r * Math.PI / 2);
        const amount = Math.abs(strength);
        let mapped = radius + (sphereRadius - radius) * Math.min(amount, 1);
        const excess = Math.max(amount - 1, 0);
        mapped = strength >= 0
            ? mapped * 2 ** (-excess * (1 - mapped))
            : 1 - (1 - mapped) * 2 ** (-excess * mapped);
        return mapped;
    };
    const twirl = (p, degrees) => {
        const a = -degrees * Math.PI / 180 * Math.hypot(...p);
        return [Math.cos(a) * p[0] - Math.sin(a) * p[1], Math.sin(a) * p[0] + Math.cos(a) * p[1]];
    };
    const near = (a, b, message = '') => assert.ok(Math.abs(a - b) < 1e-9, message || `${a} != ${b}`);
    {
        const code = source('Spherize');
        assert.match(code, /asin\(clampedRadius\) \* 0\.6366197723675814/);
        assert.match(code, /sin\(clampedRadius \* 1\.5707963267948966\)/);
        assert.match(code, /min\(strength, 1\.0\)/);
        assert.match(code, /float excessStrength = max\(strength - 1\.0, 0\.0\)/);
        assert.match(code, /exp2\(-excessStrength \* \(1\.0 - sampleRadius\)\)/);
        assert.match(code, /exp2\(-excessStrength \* sampleRadius\)/);
        assert.match(code, /float edgeWidth = max\(fwidth\(radius\), 1e-4\)/);
        assert.match(code, /result\.a \*= 1\.0 - smoothstep\(1\.0 - edgeWidth, 1\.0 \+ edgeWidth, radius\)/);
    
        for (const strength of [-8, -2, -1, -.5, 0, .5, 1, 2, 8]) {
            let previous = -1;
            for (let i = 0; i <= 1000; i++) {
                const radius = i / 1000;
                const mapped = sphereSampleRadius(radius, strength);
                assert.ok(Number.isFinite(mapped) && mapped >= previous, `non-monotonic mapping at ${radius}, ${strength}`);
                previous = mapped;
            }
            near(sphereSampleRadius(0, strength), 0);
            near(sphereSampleRadius(1, strength), 1, 'The source mapping meets the circular edge');
        }
        assert.ok(sphereSampleRadius(.5, 1) < .5, 'Positive strength bulges the source toward the center');
        assert.ok(sphereSampleRadius(.5, -1) > .5, 'Negative strength pinches the source toward the edge');
        assert.ok(sphereSampleRadius(.5, 2) < sphereSampleRadius(.5, 1), 'Positive strength continues past 1');
        assert.ok(sphereSampleRadius(.5, -2) > sphereSampleRadius(.5, -1), 'Negative strength continues past -1');
    }
});

context.case('spherize is finite at center and monotonic, including outside normalized bounds', async () => {
    // CPU reference/contract checks, not a substitute for compiling and rendering HLSL in Unity.
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
    const spherize = (p, strength) => {
        const scale = Math.pow(Math.max(p[0] ** 2 + p[1] ** 2, 1e-12), .5 * (2 ** strength - 1));
        return p.map(x => x * scale);
    };
    const sphereSampleRadius = (radius, strength) => {
        const r = Math.min(Math.max(radius, 0), 1);
        const sphereRadius = strength >= 0 ? 2 * Math.asin(r) / Math.PI : Math.sin(r * Math.PI / 2);
        const amount = Math.abs(strength);
        let mapped = radius + (sphereRadius - radius) * Math.min(amount, 1);
        const excess = Math.max(amount - 1, 0);
        mapped = strength >= 0
            ? mapped * 2 ** (-excess * (1 - mapped))
            : 1 - (1 - mapped) * 2 ** (-excess * mapped);
        return mapped;
    };
    const twirl = (p, degrees) => {
        const a = -degrees * Math.PI / 180 * Math.hypot(...p);
        return [Math.cos(a) * p[0] - Math.sin(a) * p[1], Math.sin(a) * p[0] + Math.cos(a) * p[1]];
    };
    const near = (a, b, message = '') => assert.ok(Math.abs(a - b) < 1e-9, message || `${a} != ${b}`);
    {
        for (const strength of [-1, -.5, 0, .5, 1]) {
            assert.deepEqual(spherize([0, 0], strength), [0, 0]);
            let previous = -1;
            for (const radius of [1e-9, .01, .25, .5, 1, 1.01, 2, 10, 100]) {
                const [mapped] = spherize([radius, 0], strength);
                assert.ok(Number.isFinite(mapped) && mapped > previous);
                if (radius >= .01) near(mapped, radius ** (2 ** strength));
                previous = mapped;
            }
        }
        assert.ok(spherize([.5, 0], .5)[0] < .5, 'Positive strength samples closer to center (bulge)');
        assert.ok(spherize([.5, 0], -.5)[0] > .5, 'Negative strength pinches');
        assert.ok(spherize([2, 0], .5)[0] > 2, 'No identity fallback outside the frame');
    }
});

context.case('twirl preserves radius, reverses with angle sign and continues outside the frame', async () => {
    // CPU reference/contract checks, not a substitute for compiling and rendering HLSL in Unity.
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
    const spherize = (p, strength) => {
        const scale = Math.pow(Math.max(p[0] ** 2 + p[1] ** 2, 1e-12), .5 * (2 ** strength - 1));
        return p.map(x => x * scale);
    };
    const sphereSampleRadius = (radius, strength) => {
        const r = Math.min(Math.max(radius, 0), 1);
        const sphereRadius = strength >= 0 ? 2 * Math.asin(r) / Math.PI : Math.sin(r * Math.PI / 2);
        const amount = Math.abs(strength);
        let mapped = radius + (sphereRadius - radius) * Math.min(amount, 1);
        const excess = Math.max(amount - 1, 0);
        mapped = strength >= 0
            ? mapped * 2 ** (-excess * (1 - mapped))
            : 1 - (1 - mapped) * 2 ** (-excess * mapped);
        return mapped;
    };
    const twirl = (p, degrees) => {
        const a = -degrees * Math.PI / 180 * Math.hypot(...p);
        return [Math.cos(a) * p[0] - Math.sin(a) * p[1], Math.sin(a) * p[0] + Math.cos(a) * p[1]];
    };
    const near = (a, b, message = '') => assert.ok(Math.abs(a - b) < 1e-9, message || `${a} != ${b}`);
    {
        for (const p of [[0, 0], [.2, -.4], [1, 0], [2, 3], [-10, 6]]) {
            for (const angle of [-720, -180, 0, 90, 720]) {
                const mapped = twirl(p, angle);
                near(Math.hypot(...mapped), Math.hypot(...p));
                twirl(mapped, -angle).forEach((value, i) => near(value, p[i]));
            }
        }
        const outside = twirl([2, 0], 45);
        near(outside[0], 0);
        near(outside[1], -2);
    }
});

context.case('displacement map supports a single map with an optional strength mask', async () => {
    // CPU reference/contract checks, not a substitute for compiling and rendering HLSL in Unity.
    const source = name => readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
    const spherize = (p, strength) => {
        const scale = Math.pow(Math.max(p[0] ** 2 + p[1] ** 2, 1e-12), .5 * (2 ** strength - 1));
        return p.map(x => x * scale);
    };
    const sphereSampleRadius = (radius, strength) => {
        const r = Math.min(Math.max(radius, 0), 1);
        const sphereRadius = strength >= 0 ? 2 * Math.asin(r) / Math.PI : Math.sin(r * Math.PI / 2);
        const amount = Math.abs(strength);
        let mapped = radius + (sphereRadius - radius) * Math.min(amount, 1);
        const excess = Math.max(amount - 1, 0);
        mapped = strength >= 0
            ? mapped * 2 ** (-excess * (1 - mapped))
            : 1 - (1 - mapped) * 2 ** (-excess * mapped);
        return mapped;
    };
    const twirl = (p, degrees) => {
        const a = -degrees * Math.PI / 180 * Math.hypot(...p);
        return [Math.cos(a) * p[0] - Math.sin(a) * p[1], Math.sin(a) * p[0] + Math.cos(a) * p[1]];
    };
    const near = (a, b, message = '') => assert.ok(Math.abs(a - b) < 1e-9, message || `${a} != ${b}`);
    {
        const code = source('DisplacementMap');
        assert.equal(code.split(/\r?\n/)[0], '// @whimtex-effect Distortion/Displacement Map');
        assert.match(code, /@param texture2D _DisplacementMap = self/);
        assert.match(code, /@param transform2D _MapTransform/);
        assert.match(code, /@param hidden enum _Mode = VectorRG \{VectorRG: 0, Grayscale: 1, ParallaxOcclusion: 2\}/);
        assert.match(code, /@param hidden enum _MaskSource = Constant1 \{Constant1: 0, MapChannel: 1, InputAlpha: 2, SeparateTexture: 3\}/);
        assert.match(code, /float strengthMask = 1\.0/);
        assert.match(code, /ReadStrengthChannel\(mapSample, _MapMaskChannel\)/);
        assert.match(code, /maskValue = color\.a/);
        assert.match(code, /maskValue = lerp\(maskValue, 1\.0 - maskValue, step\(0\.5001, _InvertMask\)\)/);
        assert.match(code, /texture2D _StrengthMask = none/);
        assert.match(code, /tex2D\(_StrengthMask, strengthMaskUV\)/);
        assert.match(code, /displacementPixels \* strengthMask \* _CanvasSize\.zw/);
        assert.match(code, /@formerlyserializedas\(_InputEdge\)\s*\/\/ @param enum _Tiling = Clamp \{Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3\}/);
        assert.match(code, /float2 AddressMapUV\(/);
        assert.doesNotMatch(code, /float2 AddressInputUV\(/, 'Input addressing uses the shared sampler');
        assert.match(code, /float4 distorted = SampleInput\(inputUV, _Tiling, _RepeatFiltering\)/);
        assert.match(code, /@param enum _ParallaxSteps = Balanced \{Fast: 4, Balanced: 8, High: 16, Ultra: 32\}/);
        assert.match(code, /float2 TraceParallax\(float2 uv, float strengthMask, float2 mapDDX, float2 mapDDY, out float hitHeight\)/);
        assert.match(code, /tex2Dgrad\(_DisplacementMap, mapUV, mapDDX, mapDDY\)/);
        assert.match(code, /float2 mapDDX = ddx\(mapUV\)/);
        assert.match(code, /for \(int i = 0; i < 32; i\+\+\)/);
        assert.match(code, /^\/\/ \/\/ @param bool _SelfShadow = false/m, 'Self-shadow settings remain commented out');
        assert.match(code, /^\/\/ float TraceParallaxSelfShadow\(/m, 'Self-shadow tracing remains commented out');
        assert.match(code, /^    \/\/ if \(_Mode > 1\.5 && _SelfShadow > 0\.5\)/m, 'Self-shadow application remains disabled');
        assert.doesNotMatch(code, /^\s*if \(_Mode > 1\.5 && _SelfShadow > 0\.5\)/m);
        assert.equal((code.match(/^\/\/ @if\b/gm) ?? []).length, (code.match(/^\/\/ @endif\b/gm) ?? []).length,
            'Conditional parameter blocks are balanced');
        let directiveDepth = 0;
        for (const line of code.split(/\r?\n/)) {
            if (/^\/\/ @if\b/.test(line)) directiveDepth++;
            if (/^\/\/ @param\b/.test(line)) assert.ok(directiveDepth <= 1, 'Parameter conditions are not nested');
            if (/^\/\/ @endif\b/.test(line)) directiveDepth--;
        }
        assert.equal(directiveDepth, 0, 'All conditional parameter blocks are closed');
        const applyFX = code.slice(code.indexOf('float4 ApplyFX('));
        assert.match(applyFX, /if \(_Amount <= 0\.0\) return color;/, 'Zero Amount is an exact bypass');
        assert.equal((applyFX.match(/\breturn\b/g) ?? []).length, 2, 'Exact bypass plus one initialized result path');
    
        const vectorOffset = (sample, neutral, strength, mask) => sample.map((value, i) => 2 * (value - neutral) * strength[i] * mask);
        const strengthMask = (source, value) => source === 'Constant1' ? 1 : value;
        vectorOffset([.5, .5], .5, [80, -40], strengthMask('Constant1', 0)).forEach(value => near(value, 0));
        vectorOffset([1, 0], .5, [80, -40], strengthMask('Constant1', 0)).forEach((value, i) => near(value, [80, 40][i]));
        vectorOffset([1, 0], .5, [80, -40], strengthMask('MapChannel', .25)).forEach((value, i) => near(value, [20, 10][i]));
    }
});

context.case('every distortion exposes a coordinate-strength control, not a result crossfade', async () => {
    const bindings = { Spherize: '_Strength', Twirl: '_Angle', RadialShear: '_Strength',
        PolarCoordinates: '_Amount', DisplacementMap: '_Amount' };
    for (const [name, parameter] of Object.entries(bindings)) {
        const code = readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
        assert.equal(code.split(/\r?\n/)[1], `// @control(${parameter})`);
        assert.equal((code.match(/^\/\/ @control\(/gm) ?? []).length, 1, 'One FX header binding');
    }
    const displacement = readFileSync(new URL('../../../src/FXPresets/DisplacementMap.hlsl', import.meta.url), 'utf8');
    assert.match(displacement, /@param hidden float _Amount = 1 \[0 \.\. ~2\]/);
    assert.match(displacement, /strengthMask \*= max\(_Amount, 0\.0\);\s*if \(_Mode > 1\.5\)/);
    assert.match(displacement, /TraceParallax\(uv, strengthMask,/);
    assert.match(displacement, /displacementPixels \* strengthMask \* _CanvasSize\.zw/);
});

context.case('transform and distortion presets share input tiling without changing their defaults', async () => {
    const files = ['UVTransform', 'Spherize', 'Twirl', 'RadialShear', 'PolarCoordinates', 'DisplacementMap'];
    for (const name of files) {
        const code = readFileSync(new URL(`../../../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
        const parameter = name === 'UVTransform' ? '_InputTiling' : '_Tiling';
        const declaration = code.split(/\r?\n/).find(line => line.startsWith('// @param') && line.includes(`enum ${parameter} `));
        assert.ok(declaration, `${name} declares input addressing`);
        assert.ok(declaration.includes(`= ${name === 'UVTransform' ? 'Clip' : 'Clamp'} {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3}`));
        assert.equal((code.match(new RegExp(`enum ${parameter}\\b`, 'g')) ?? []).length, 1, 'One addressing field');
        const functionSource = code.slice(code.indexOf('float4 ApplyFX('));
        assert.ok(functionSource.includes(`, ${parameter}`), `${name} calls the common sampler`);
        assert.doesNotMatch(code, /float2 AddressInputUV\(/, 'No duplicate image-addressing implementation');
        assert.doesNotMatch(declaration, /Unbounded|Source/, 'Only real raster sampling modes');
        assert.ok(code.lastIndexOf('// @param transform2D') < code.indexOf(declaration), 'Tiling follows frame parameters');
        if (name !== 'UVTransform') {
            const previousName = name === 'DisplacementMap' ? '_InputEdge' : '_InputTiling';
            assert.ok(code.includes(`// @formerlyserializedas(${previousName})\n${declaration}`) ||
                code.includes(`// @formerlyserializedas(${previousName})\r\n${declaration}`), 'Rename metadata belongs to the tiling declaration');
        }
    }
    const builder = readFileSync(new URL('../../../src/ShaderFXSourceBuilder.cs', import.meta.url), 'utf8');
    assert.match(builder, /float4 SampleInput\(float2 uv\) \{ return tex2D\(_MainTex, uv\); \}/, 'Original sampling function stays intact');
    assert.match(builder, /float4 SampleInput\(float2 uv, float tiling\)/);
    assert.match(builder, /float4 SampleInput\(float2 uv, float tiling, float filterRepeat\)/);
    assert.match(builder, /_WhimTex_InputFilter < 0\.5 \|\| filterRepeat < 0\.5/);
    assert.match(builder, /float2 size = _MainTex_TexelSize.zw;/, 'Uses actual input texture dimensions');
    assert.equal((builder.match(/tex2Dlod\(_MainTex,/g) ?? []).length, 4, 'Repeat bilinear sampling wraps all four texels');
    const layer = readFileSync(new URL('../../../src/Layers/Layer.cs', import.meta.url), 'utf8');
    assert.match(layer, /SetFloat\("_WhimTex_InputFilter", current.filterMode == FilterMode.Point \? 0f : 1f\)/);
    const compatibility = readFileSync(new URL('../../../src/WhimTexFileCompatibility0125.cs', import.meta.url), 'utf8');
    assert.match(compatibility, /7d755646c7a839e478c67bb36a2189f8/);
    assert.match(compatibility, /filtering.floatValue = 0f;/, 'Previous linked repeat filtering is retained');
    const negative = readFileSync(new URL('../../../src/FXPresets/Negative.hlsl', import.meta.url), 'utf8');
    assert.doesNotMatch(negative, /_InputTiling|_InputEdge|_Tiling/, 'Pointwise color FX do not gain unrelated controls');
});

await finish(context);
