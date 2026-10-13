// Read-only source guards. Numerical verdicts belong to the native analytic cases.
// Support: Tests~/Framework/test-api.mjs; implementation dependencies are catalogued.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('DistanceField: six artistic modes and data contracts');
const assert = context.assert;
const read = path => readFileSync(new URL('../../../' + path, import.meta.url), 'utf8');
const modes = ['EuclideanAntialiased', 'Sharp', 'Bevel', 'Diamond', 'Square', 'MTSDF'];
const sdf = read('src/Layers/SDFLayerBehaviour.cs');
const outline = read('src/Layers/OutlineLayerBehaviour.cs');
const artistic = read('src/Layers/ArtisticDistanceField.cs');
const shader = read('src/Shaders/SdfGradient.shader');

function parameters(text, name) {
    const match = text.match(new RegExp('\\b(?:public|internal)\\s+static\\s+void\\s+' + name + '\\s*\\(([\\s\\S]*?)\\)'));
    assert.ok(match, 'Declared method: ' + name);
    return match[1].split(',').map(p => p.trim().replace(/\s+\w+(?:\s*=.*)?$/, '').replace(/\s+/g, ''));
}

context.case('Exactly six artistic enum declarations and Round AA label', () => {
    const enums = read('src/Utils.cs');
    const body = enums.match(/\benum\s+DistanceMetric\s*\{([^}]+)\}/);
    assert.ok(body, 'DistanceMetric declaration');
    const declarations = [...body[1].matchAll(/\b(\w+)\s*=\s*(\d+)/g)].map(m => [m[1], Number(m[2])]);
    assert.deepEqual(declarations, modes.map((name, i) => [name, i + 4]), 'Only the current six artistic choices');
    assert.match(body[1], /\[InspectorName\("Round AA"\)\]\s*EuclideanAntialiased/, 'Artist-facing Round AA label');
    const declaredNames = body[1].replace(/\[[^\]]*\]/g, '').split(',').map(s => s.trim().split(/\s*=/)[0]).filter(Boolean);
    assert.deepEqual(declaredNames, modes, 'No additional unnamed or implicit enum entries');
});

context.case('Seven-argument scalar and eight-argument artistic APIs', () => {
    const scalar = ['NativeArray<Color32>', 'NativeArray<float>', 'int', 'int', 'byte', 'int', 'DistanceMetric'];
    assert.deepEqual(parameters(sdf, 'ComputeSignedDistance'), scalar);
    assert.deepEqual(parameters(sdf, 'ComputeArtisticDistance'), [...scalar, 'float']);
    assert.deepEqual(parameters(sdf, 'ComputeMtsdfDistance'),
        ['NativeArray<Color32>', 'NativeArray<float4>', 'int', 'int', 'byte', 'int']);
    assert.match(sdf, /ComputeArtisticDistance\(input,\s*output,\s*width,\s*height,\s*threshold,\s*sourceChannel,\s*metric,\s*4f\)/,
        'Original scalar entry delegates to the artistic contract');
    for (const mode of modes) assert.ok((sdf + artistic).includes('DistanceMetric.' + mode), 'Mode has a production route: ' + mode);
});

context.case('Shared source contour, explicit miter limit and image borders', () => {
    const source = ['NativeArray<Color32>', 'NativeArray<float>', 'int', 'int', 'byte', 'int', 'DistanceMetric', 'Vector2', 'SourceEdges'];
    assert.deepEqual(parameters(sdf, 'ComputeSourceDistance'), source);
    assert.deepEqual(parameters(sdf, 'ComputeSourceField'), [...source, 'float', 'NativeArray<float4>']);
    for (const [name, text] of [['SDF', sdf], ['Outline', outline]]) {
        assert.match(text, /metric\s*=\s*DistanceMetric\.EuclideanAntialiased/, name + ' defaults to Round AA');
        assert.match(text, /public\s+float\s+miterLimit\s*=\s*4f/, name + ' declares join control');
        assert.match(text, /RequiresColorInput\s*=>\s*sourceChannel\s*!=\s*SourceChannel\.Alpha/, name + ' respects chosen data channels');
        assert.match(text, /ComputeSourceField\(/, name + ' uses the shared source field');
    }
    assert.match(outline, /metric,\s*Vector2\.zero,\s*SDFLayerBehaviour\.SourceEdges\.Transparent,\s*miterLimit,\s*default/,
        'Outline includes the transparent image-border contour');
    assert.match(outline, /antialiasedDistance\s*=\s*true/, 'Every Outline selection consumes subpixel distance');
});

context.case('Round preserves the sampled threshold-crossing AA baseline', () => {
    assert.match(sdf, /if\s*\(metric\s*!=\s*DistanceMetric\.EuclideanAntialiased\s*&&\s*width\s*>\s*1\s*&&\s*height\s*>\s*1\)/,
        'Round retains its AA path while two-dimensional artistic modes use contour geometry');
    assert.match(sdf, /ComputeAntialiasedSignedDistance\(input,\s*output,\s*background,\s*width,\s*height,\s*threshold,\s*sourceChannel\)/,
        'Round scalar path calls the preserved AA computation');
    for (const job of ['DistanceCoverageJob', 'ContourCrossingsJob', 'ExactDistanceTransformPassJob', 'FinalizeContourDistanceJob'])
        assert.ok(sdf.includes(job), 'Round AA pipeline retains ' + job);
    assert.match(sdf, /if\s*\(\(a\s*>\s*threshold\)\s*!=\s*\(b\s*>\s*threshold\)\)/,
        'Crossings use the strict sample threshold sign');
    assert.match(sdf, /nearest\s*=\s*previous\s*\+\s*step\s*\*\s*\(threshold\s*-\s*a\)\s*\/\s*\(b\s*-\s*a\)/,
        'Round interpolates adjacent-sample crossing points');
    assert.match(sdf, /math\.sqrt\(math\.min\(output\[index\],\s*other\[index\]\)\)/,
        'Round takes the nearest horizontal or vertical seed distance');
    assert.match(sdf, /DistanceFieldSource\.IsObject\(input\[index\],\s*sourceChannel,\s*threshold\)\s*\?\s*-distance\s*:\s*distance/,
        'Round distance sign comes from source samples');
});

context.case('MTSDF keeps RGB edge distances and alpha true distance through SDF upload', () => {
    assert.match(artistic, /NativeArray<float4>\s+output/, 'Native four-channel field');
    assert.match(artistic, /trueDistance\s*=\s*math\.min\(trueDistance,\s*euclidean\)/, 'Alpha keeps finite-edge Euclidean distance');
    assert.match(artistic, /new\s+float4\((?:sign\s*\*\s*)?pseudo,\s*sign\s*\*\s*trueDistance\)/, 'RGB and alpha store separate quantities');
    assert.match(artistic, /Median\(result\.xyz\)/, 'Scalar MTSDF consumes stored RGB median');
    assert.match(sdf, /resultTexture\.SetPixelData\(multiChannel,\s*0\)/, 'SDF uploads the four-channel buffer');
    assert.match(sdf, /SetInt\("_MultiChannel",\s*multiChannel\.IsCreated\s*\?\s*1\s*:\s*0\)/, 'Shader knows which texture contains MTSDF');
    assert.match(sdf, /SetInt\("_DataOutput",\s*encoding\s*==\s*OutputEncoding\.MultiChannelData\s*\?\s*1\s*:\s*0\)/,
        'Data output is explicitly selected');
    assert.match(shader, /if\s*\(_DataOutput\s*!=\s*0\)\s*return\s+saturate\(\.5\s*\+\s*\.5\s*\*\s*field\s*\/\s*_MaxDistance\)/,
        'Multi-channel data remaps all four stored distances');
    assert.match(shader, /max\(min\(field\.r,\s*field\.g\),\s*min\(max\(field\.r,\s*field\.g\),\s*field\.b\)\)/,
        'Artistic remap reads RGB median');
});

context.case('Agent inspection and editing expose the same artist choices', () => {
    const inspect = read('src/Automation/WhimTexApi.Inspect.cs');
    const edit = read('src/Automation/WhimTexApi.Layers.cs');
    assert.ok(inspect.includes('System.Enum.GetNames(typeof(DistanceMetric))'), 'API discovers the actual six choices');
    for (const name of ['miterLimit', 'outlineOffset', 'fillCenter', 'fillColor']) {
        assert.ok(edit.includes('settings, "' + name + '"') || edit.includes('settings["' + name + '"]'), 'API accepts ' + name);
        assert.ok(inspect.includes('settings["' + name + '"]'), 'API inspects ' + name);
    }
    assert.match(edit, /MultiChannelData[\s\S]*?DistanceMetric\.MTSDF/, 'Invalid combinations receive an explicit diagnostic');
    assert.ok(read('src/EffectRenderCache.cs').includes('if (effect.RequiresColorInput) colorSources.Add(input)'),
        'Source-channel data requirements participate in dependency tracking');
    assert.ok(read('src/WhimTexDocument.cs').includes('effect.RequiresColorInput, includeDisabled: true'),
        'Specific inputs retain selected color channels when their layer is disabled');
});

context.case('Analytic and render cases declare executable support and profiles', () => {
    const catalog = JSON.parse(read('Tests~/scripts/test-catalog.json'));
    const find = id => {
        const entry = catalog.scenarios.find(s => s.id === id);
        assert.ok(entry, 'Registered case ' + id);
        return entry;
    };
    const scalar = find('distance-field-unity-v2');
    const layers = find('distance-field-modes-v2');
    const render = find('distance-field-render-v2');
    assert.deepEqual(scalar.supportFiles, ['Tests~/Framework/TestApi.cs']);
    assert.deepEqual(layers.supportFiles, ['Tests~/Framework/TestApi.cs', 'Tests~/Cases/Painting/DistanceFieldTests.cs']);
    assert.deepEqual(render.supportFiles, ['Tests~/Framework/TestApi.cs']);
    assert.deepEqual(render.effects, ['temp-files', 'windows', 'user-state'], 'Render case declares its contact sheet and transient control-test window');
    for (const entry of [scalar, layers, render]) {
        assert.equal(entry.requiresUnity, true, 'Native execution is explicit: ' + entry.id);
        assert.ok(catalog.profiles['distance-fields'].includes(entry.id), 'Distance profile selects ' + entry.id);
    }
    assert.equal(find('distance-field-v2').requiresUnity, false, 'Source guards can run independently');
});

await finish(context);

