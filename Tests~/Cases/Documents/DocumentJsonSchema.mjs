// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('DocumentJsonSchema: source/scalar contracts');
const assert = context.assert;

context.case('DocumentJsonSchema original assertions and branches', async () => {
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
    const schema = JSON.parse(fs.readFileSync(path.join(root, 'Documentation~/AI/document.schema.json')));
    // The generated schema deliberately uses this small, dependency-free Draft 2020-12 vocabulary.
    const keywords = new Set(['$schema', '$ref', '$defs', 'title', 'type', 'enum', 'const', 'anyOf', 'allOf',
      'properties', 'additionalProperties', 'required', 'items', 'prefixItems', 'minItems', 'maxItems', 'minLength', 'maxLength', 'pattern', 'minimum', 'exclusiveMinimum', 'maximum', 'deprecated', 'description', 'default', 'not', 'contentEncoding']);
    function validate(value, rule, location = '$') {
      for (const key of Object.keys(rule)) assert(keywords.has(key), `Unsupported schema keyword ${key}`);
      if (rule.$ref) validate(value, schema.$defs[rule.$ref.slice('#/$defs/'.length)], location);
      if (rule.anyOf) {
        const errors = [];
        const valid = rule.anyOf.some(r => { try { validate(value, r, location); return true; } catch (e) { errors.push(e.message); return false; } });
        assert(valid, `${location}: no allowed variant: ${errors.join('; ')}`);
      }
      if (rule.allOf) for (const r of rule.allOf) validate(value, r, location);
      if (rule.not) {
        let matches = false;
        try { validate(value, rule.not, location); matches = true; } catch {}
        assert(!matches, `${location}: excluded combination`);
      }
      if ('const' in rule) assert.deepEqual(value, rule.const, `${location}: constant`);
      if (rule.enum) assert(rule.enum.includes(value), `${location}: enum ${value}`);
      if (typeof value === 'string') {
        assert(value.length >= (rule.minLength ?? 0) && value.length <= (rule.maxLength ?? Infinity), `${location}: string length`);
        if (rule.pattern) assert(new RegExp(rule.pattern).test(value), `${location}: string pattern`);
      }
      if (typeof value === 'number') {
        assert(Number.isFinite(value), `${location}: non-finite number`);
        assert(value >= (rule.minimum ?? -Infinity) && value <= (rule.maximum ?? Infinity), `${location}: number range`);
        if ('exclusiveMinimum' in rule) assert(value > rule.exclusiveMinimum, `${location}: exclusive lower bound`);
      }
      if (rule.type) {
        const types = [].concat(rule.type);
        const actual = value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value;
        assert(types.includes(actual) || types.includes('integer') && Number.isInteger(value), `${location}: ${actual} is not ${types}`);
      }
      if (value && typeof value === 'object' && !Array.isArray(value)) {
        for (const key of rule.required ?? []) assert(key in value, `${location}: missing ${key}`);
        for (const [key, item] of Object.entries(value)) {
          if (rule.properties?.[key]) validate(item, rule.properties[key], `${location}.${key}`);
          else if (rule.additionalProperties === false) assert.fail(`${location}: unknown ${key}`);
        }
      }
      if (Array.isArray(value)) {
        assert(value.length >= (rule.minItems ?? 0) && value.length <= (rule.maxItems ?? Infinity), `${location}: tuple length`);
        value.forEach((v, i) => {
          const itemRule = rule.prefixItems?.[i] ?? rule.items;
          if (itemRule) validate(v, itemRule, `${location}[${i}]`);
        });
      }
    }
    const defaultsSource = fs.readFileSync(path.join(root, 'src/WhimTexJsonDefaultsV3.cs'), 'utf8');
    const defaultsLiteral = defaultsSource.match(/internal const string Data = @"((?:[^"]|"")*)";/);
    assert(defaultsLiteral, 'Frozen JSON defaults must be available independently of Unity');
    const defaults = JSON.parse(defaultsLiteral[1].replaceAll('""', '"'));
    const layoutLiteral = defaultsSource.match(/internal const string LayoutData = @"((?:[^"]|"")*)";/);
    assert(layoutLiteral, 'FX layout defaults must be frozen independently of runtime initializers');
    Object.assign(defaults, JSON.parse(layoutLiteral[1].replaceAll('""', '"')));
    const vectorDefaultsLiteral = defaultsSource.match(/internal const string VectorData = @"((?:[^"]|"")*)";/);
    assert(vectorDefaultsLiteral, 'Vector defaults must be frozen independently of runtime initializers');
    for (const [type, value] of Object.entries(JSON.parse(vectorDefaultsLiteral[1].replaceAll('""', '"')))) {
      assert(!(type in defaults), `${type}: additive vector defaults must not override existing contracts`);
      defaults[type] = value;
    }
    const shapeFillLiteral = defaultsSource.match(/internal const string ShapeFillData = @"((?:[^"]|"")*)";/);
    assert(shapeFillLiteral, 'Shared Shape fill defaults must be frozen independently of runtime initializers');
    Object.assign(defaults.ShapeLayerBehaviour, JSON.parse(shapeFillLiteral[1].replaceAll('""', '"')));
    defaults.ShapeLayerBehaviour.gradient = structuredClone(defaults.WhimTexGradient);
    defaults.VectorShape.gradient = structuredClone(defaults.WhimTexGradient);
    defaults.ShapeLayerBehaviour.strokeGradient = structuredClone(defaults.WhimTexGradient);
    defaults.VectorShape.strokeGradient = structuredClone(defaults.WhimTexGradient);
    const layerBooleanLiteral = defaultsSource.match(/internal const string LayerBooleanData = @"((?:[^"]|"")*)";/);
    assert(layerBooleanLiteral, 'Boolean operand defaults are frozen');
    Object.assign(defaults.Layer, JSON.parse(layerBooleanLiteral[1].replaceAll('""', '"')));
    const previewLiteral = defaultsSource.match(/internal const string PreviewControlData = @"((?:[^"]|"")*)";/);
    assert(previewLiteral, 'Preview metadata defaults are explicit');
    Object.assign(defaults.ShaderFXParameterControl, JSON.parse(previewLiteral[1].replaceAll('""', '"')));
    const cyclicLiteral = defaultsSource.match(/internal const string CyclicRangeData = @"((?:[^"]|"")*)";/);
    assert(cyclicLiteral, 'Cyclic metadata defaults are explicit');
    for (const type of ['ShaderFXParameter', 'ShaderFXParameterControl'])
      Object.assign(defaults[type], JSON.parse(cyclicLiteral[1].replaceAll('""', '"')));
    const textFillLiteral = defaultsSource.match(/internal const string TextFillData = @"((?:[^"]|"")*)";/);
    assert(textFillLiteral, 'Text fill defaults match the runtime JSON reader');
    Object.assign(defaults.TextLayerBehaviour, JSON.parse(textFillLiteral[1].replaceAll('""', '"')));
    defaults.TextLayerBehaviour.gradient = structuredClone(defaults.WhimTexGradient);
    const valueDefaultsSource = fs.readFileSync(path.join(root, 'src/WhimTexDocumentJson.Values.cs'), 'utf8');
    for (const field of ['containers', 'enableIf']) {
      assert.ok(valueDefaultsSource.includes(`defaultsV3["ShaderFXParameterControl"]["${field}"] = new JArray();`),
        `${field}: explicit empty metadata default`);
      defaults.ShaderFXParameterControl[field] = [];
    }
    const freeformLiteral = valueDefaultsSource.match(/defaultsV3\["GradientLayerBehaviour"\]\["freeform"\] = JObject.Parse\(@"((?:[^"]|"")*)"\);/);
    const pointLiteral = valueDefaultsSource.match(/defaultsV3\["FreeformGradientPoint"\] = JObject.Parse\(@"((?:[^"]|"")*)"\);/);
    assert(freeformLiteral && pointLiteral, 'Freeform defaults are frozen independently of runtime constructors');
    defaults.FreeformGradient = JSON.parse(freeformLiteral[1].replaceAll('""', '"'));
    defaults.FreeformGradientPoint = JSON.parse(pointLiteral[1].replaceAll('""', '"'));
    defaults.GradientLayerBehaviour.freeform = structuredClone(defaults.FreeformGradient);
    for (const type of ['ShapeLayerBehaviour', 'VectorShape']) {
      defaults[type].freeform = structuredClone(defaults.FreeformGradient);
      defaults[type].strokeFreeform = structuredClone(defaults.FreeformGradient);
      validate({ fillMode: 'Freeform', freeform: defaults.FreeformGradient, strokeMode: 'Freeform', strokeFreeform: defaults.FreeformGradient }, schema.$defs[type]);
      for (const fields of [{ fillMode: 'Freeform', freeform: null }, { strokeMode: 'Freeform', strokeFreeform: null },
        { freeform: { points: [] } }, { strokeFreeform: { points: [{ position: [0, 0], color: [1, 0, 0, 1], influence: 0 }] } }])
        assert.throws(() => validate(fields, schema.$defs[type]));
    }
    assert.throws(() => validate({ fillMode: 'Freeform' }, schema.$defs.TextLayerBehaviour), 'Text excludes Freeform');
    const fillModes = ['None', 'Solid', 'LinearGradient', 'RadialGradient', 'SquareGradient', 'AngularGradient', 'Freeform'];
    for (const type of ['ShapeLayerBehaviour', 'VectorShape']) {
      const rule = schema.$defs[type];
      assert.deepEqual(rule.properties.fillMode.enum, fillModes, `${type}: complete shared fill enum`);
      assert.equal(defaults[type].fillMode, 'Solid', `${type}: Solid default`);
      assert.deepEqual(defaults[type].gradientStart, [0, 0]);
      assert.deepEqual(defaults[type].gradientEnd, [1, 0]);
      assert.equal(defaults[type].gradientRepetitions, 1, `${type}: angular repetitions default`);
      assert.equal(rule.properties.gradientRepetitions.default, 1);
      assert.equal(rule.properties.gradientRepetitions.minimum, .0001);
      for (const fillMode of fillModes) {
        validate({ fillMode, gradient: defaults.WhimTexGradient, gradientStart: [-.1, .2], gradientEnd: [.8, 1.2] }, rule);
        validate({ fillMode, gradientStart: [.2, .2], gradientEnd: [.2, .2] }, rule);
        for (const gradientRepetitions of [.0001, .125, 1, 2.5, 1e30]) validate({ fillMode, gradientRepetitions }, rule);
        for (const gradientRepetitions of [0, -1, .00001, 1e40, Infinity, NaN, null, '2'])
          assert.throws(() => validate({ fillMode, gradientRepetitions }, rule), `${type}: invalid repetitions in ${fillMode}`);
        if (fillMode.endsWith('Gradient')) assert.throws(() => validate({ fillMode, gradient: null }, rule));
      }
      assert.throws(() => validate({ fillMode: 'UnknownGradient' }, rule));
      assert.throws(() => validate({ fillMode: 6 }, rule));
      assert.throws(() => validate({ gradientStart: [0] }, rule));
      assert.throws(() => validate({ gradientEnd: [0, Infinity] }, rule));
      assert.deepEqual(rule.properties.strokeMode.enum, fillModes, `${type}: complete stroke enum`);
      assert.equal(defaults[type].strokeMode, 'Solid');
      assert.deepEqual(defaults[type].strokeGradientStart, [0, 0]);
      assert.deepEqual(defaults[type].strokeGradientEnd, [1, 0]);
      assert.equal(defaults[type].strokeGradientRepetitions, 1);
      for (const strokeMode of fillModes) {
        validate({ strokeMode, strokeGradient: defaults.WhimTexGradient, strokeGradientStart: [-.1,.2], strokeGradientEnd: [.8,1.2] }, rule);
        for (const strokeGradientRepetitions of [.0001,.125,1,2.5,1e30]) validate({ strokeMode, strokeGradientRepetitions }, rule);
        for (const strokeGradientRepetitions of [0,-1,.00001,1e40,Infinity,NaN,null,'2'])
          assert.throws(() => validate({ strokeMode, strokeGradientRepetitions }, rule));
        if (strokeMode.endsWith('Gradient')) assert.throws(() => validate({ strokeMode, strokeGradient: null }, rule));
      }
      assert.throws(() => validate({ strokeMode: 'unknown' }, rule));
      assert.throws(() => validate({ strokeMode: 6 }, rule));
      assert.throws(() => validate({ strokeGradientStart: [0] }, rule));
      assert.throws(() => validate({ strokeGradientEnd: [0,Infinity] }, rule));
    }
    const identities = new Set(['$type', '$id', '$name', 'contentOmitted', 'id', 'recoveryId', 'shaderKey']);
    for (const [type, definition] of Object.entries(schema.$defs)) {
      assert(type in defaults, `${type}: missing frozen default object`);
      for (const [field, rule] of Object.entries(definition.properties)) {
        if (identities.has(field)) continue;
        assert(field in defaults[type], `${type}.${field}: omitted input would use an unfrozen initializer or zero/null`);
        // A layer must explicitly supply its behaviour, despite the unused null baseline.
        if (type === 'Layer' && field === 'behaviour') continue;
        // Mandatory payloads have no valid omitted value; callers must supply them.
        if (['WhimTexComment', 'WhimTexCommentMessage', 'WhimTexCommentAttachment', 'WhimTexCommentMask', 'WhimTexAgentReceipt', 'VectorBrushDraft'].includes(type) && definition.required?.includes(field)) continue;
        validate(defaults[type][field], rule, `defaults.${type}.${field}`);
      }
    }
    const directory = path.join(root, 'Samples~/AgentTextures');
    const files = fs.readdirSync(directory).filter(f => f.endsWith('.whimtex.json'));
    assert.equal(files.length, 38);
    for (const file of files) {
      const recipe = JSON.parse(fs.readFileSync(path.join(directory, file)));
      assert(!('kind' in recipe), `${file}: obsolete discriminator still written`);
      validate(recipe, schema, file);
    }
    const clipboardDirectory = path.join(root, 'Documentation~/Examples/Clipboard');
    const clipboardFiles = fs.readdirSync(clipboardDirectory).filter(f => f.endsWith('.json'));
    assert.equal(clipboardFiles.length, 9);
    for (const file of clipboardFiles) validate(JSON.parse(fs.readFileSync(path.join(clipboardDirectory, file))), schema, file);
    const compatibilityDirectory = path.join(root, 'Tests~/Fixtures/Compatibility0125');
    const compatibilityFiles = ['procedural-Full.json', 'procedural-FullOptimized.json', 'procedural-Compact.json', 'fragment.json'];
    for (const file of compatibilityFiles) {
      const archived = JSON.parse(fs.readFileSync(path.join(compatibilityDirectory, file)));
      assert.equal(archived.version, 1, 'Historical inputs are unchanged');
      assert.throws(() => validate(archived, schema, file), 'Version 1 is outside the current schema');
    }
    assert.equal(schema.$defs.WhimTexDocument.properties.spriteSlices, undefined);
    assert.ok(schema.$defs.Layer.properties.fx);
    assert.equal(schema.$defs.Layer.properties.modifiers, undefined);
    assert.deepEqual(schema.$defs.TextLayerBehaviour.properties.overflow.enum, ['None', 'Clip', 'Ellipsis']);
    for (const overflow of ['None', 'Clip', 'Ellipsis']) validate({ format: 'whimtex.document', version: 3,
      layers: [{ id: 'text', behaviour: { $type: 'TextLayerBehaviour', overflow } }] }, schema);
    assert.throws(() => validate({ format: 'whimtex.document', version: 3,
      layers: [{ id: 'text', behaviour: { $type: 'TextLayerBehaviour', overflow: 'unknown' } }] }, schema));
    const layerFx = fields => ({ format: 'whimtex.document', version: 3,
      layers: [{ id: 'fx', behaviour: { $type: 'ColorFillLayerBehaviour' }, ...fields }] });
    validate(layerFx({ fx: [] }), schema);
    validate(layerFx({ fx: [{ $type: 'ShaderFX', $id: 'owned', code: 'float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return c;}' }, { $ref: 'owned' }, null] }), schema);
    for (const type of ['UnityEngine.Material', 'UnityEngine.Shader', 'DCFApixels.WhimTex.ShaderFX'])
      assert.throws(() => validate(layerFx({ fx: [{ $asset: { guid: 'missing', path: 'Assets/RemovedFX.asset', localId: '0', type } }] }), schema), 'Only HLSL model entries and internal identities are allowed');
    assert.throws(() => validate(layerFx({ modifiers: [] }), schema));
    for (const fields of [{ fx: [], modifiers: [] }, { fx: null, modifiers: [] }, { modifiers: null, fx: [] }])
      assert.throws(() => validate(layerFx(fields), schema));
    for (const file of ['Documentation~/AI/README.md', 'Documentation~/JSON_FORMAT.md'])
      for (const match of fs.readFileSync(path.join(root, file), 'utf8').matchAll(/\x60\x60\x60json\s*\n([\s\S]*?)\x60\x60\x60/g)) {
        const value = JSON.parse(match[1]);
        if (value.format === 'whimtex.gradient') continue;
        validate(value, schema, file);
      }
    const empty = { format: 'whimtex.document', version: 3, document: {}, layers: [] };
    const comments = value => ({ ...empty, document: { comments: value } });
    const note = { id: 'thread', anchor: [-.5, 1.25], color: [.9, .3, .1, 1], messages: [{ id: 'message', text: 'Note' }] };
    validate(comments([note]), schema);
    assert.equal(defaults.WhimTexComment.awaitingReview, false);
    for (const fields of [{ resolved: false, awaitingReview: false }, { awaitingReview: true }, { resolved: true }])
      validate(comments([{ ...note, ...fields }]), schema);
    assert.throws(() => validate(comments([{ ...note, resolved: true, awaitingReview: true }]), schema));
    assert.equal(defaults.WhimTexComment.useAuthorColor, false, 'Omitted automatic mode preserves old manual colors');
    for (const colorAuthorId of [null, '', '0123456789abcdef0123456789ABCDEF'])
      validate(comments([{ ...note, useAuthorColor: true, colorAuthorId }]), schema);
    for (const authorColor of [[0, 0, 0, 0], [.2, .5, .7, 1]])
      validate(comments([{ ...note, messages: [{ ...note.messages[0], authorColor, authorId: '0123456789abcdef0123456789ABCDEF' }] }]), schema);
    for (const invalid of [null, {}, { ...note, messages: [] }, { ...note, id: ' ' },
      { ...note, useAuthorColor: 'true' }, { ...note, colorAuthorId: 'not-a-profile' },
      { ...note, messages: [{ ...note.messages[0], authorColor: [1, 0, 0, 0] }] },
      { ...note, messages: [{ ...note.messages[0], authorColor: [1, 0, 0, .5] }] },
      { ...note, messages: [{ ...note.messages[0], authorId: 'not-a-profile' }] },
      { ...note, color: [2, 0, 0, 1] }, { ...note, color: [1, 0, 0, 0] },
      { ...note, messages: [{ id: 'message', text: ' ' }] },
      { ...note, messages: [{ id: 'message', text: 'x'.repeat(16385) }] }])
      assert.throws(() => validate(comments([invalid]), schema));
    validate(empty, schema);
    const vectorLayer = settings => ({ ...empty, layers: [{ id: 'vector-layer', behaviour: { $type: 'VectorLayerBehaviour', ...settings } }] });
    const draft = { width: 2, height: 1, coverage: 'AP8=', shapeId: 'figure' };
    validate(vectorLayer({ shapes: [], brushDraft: null }), schema);
    validate(vectorLayer({ brushDraft: draft }), schema);
    assert.deepEqual(defaults.VectorBrushDraft.frame, [0, 0, 1, 1]);
    const booleanLayer = fields => ({ ...empty, layers: [{ id: 'boolean', behaviour: { $type: 'BooleanGroupLayerBehaviour', ...fields } }] });
    validate(booleanLayer({}), schema);
    validate(booleanLayer({ style: { fillMode: 'Solid', stroke: true, contours: [] } }), schema);
    assert.throws(() => validate(booleanLayer({ style: { contours: [{ points: [] }] } }), schema));
    assert.equal(defaults.BooleanGroupLayerBehaviour.appearance, 'Group');
    for (const appearance of ['Group', 'Layers']) validate(booleanLayer({ appearance }), schema);
    assert.throws(() => validate(booleanLayer({ appearance: 'Unknown' }), schema));
    for (const booleanOperation of ['Union', 'Subtract', 'Intersect', 'Xor', 'Combine'])
      validate({ ...empty, layers: [{ id: 'operand', booleanOperation, behaviour: { $type: 'VectorLayerBehaviour' } }] }, schema);
    assert.equal(schema.$defs.VectorBrushDraft.properties.coverage.contentEncoding, 'base64');
    for (const invalid of [{ ...draft, width: 0 }, { ...draft, height: 1025 }, { ...draft, coverage: [0, 255] },
      { ...draft, tolerance: .1 }, { ...draft, expansion: 257 }, { ...draft, shapeId: '' }])
      assert.throws(() => validate(vectorLayer({ brushDraft: invalid }), schema));
    for (const type of ['SDFLayerBehaviour', 'OutlineLayerBehaviour']) for (const mode of ['Contour', 'CornerFit', 'Spring']) {
      const metric = schema.$defs[type].properties.metric;
      assert.equal(metric.default, 'EuclideanAntialiased', 'Experimental modes do not change the default');
      assert(metric.enum.includes(mode), `${mode} is a persisted metric`);
      const document = settings => ({ ...empty, layers: [{ id: 'experimental', behaviour: { $type: type, metric: mode, ...settings } }] });
      validate(document({}), schema);
      if (type === 'SDFLayerBehaviour') {
        for (const encoding of ['Gradient', 'LinearData']) validate(document({ encoding }), schema);
        assert.throws(() => validate(document({ encoding: 'MultiChannelData' }), schema));
      }
    }
    validate({ format: 'whimtex.document', version: 3, layers: [] }, schema);
    for (const document of [{}, { width: 64 }, { height: 32 }, { outputSrgb: false }])
      validate({ ...empty, document }, schema);
    for (const document of [null, [], 'invalid', 1])
      assert.throws(() => validate({ ...empty, document }, schema));
    for (const kind of ['document', 'fragment', 'layers']) assert.throws(() => validate({ ...empty, kind }, schema));
    assert.equal(schema.properties.kind, undefined);
    assert.throws(() => validate({ ...empty, kind: 1 }, schema));
    assert.throws(() => validate({ ...empty, format: 'whimtex.layers' }, schema));
    assert.throws(() => validate({ ...empty, document: { unknown: 1 } }, schema));
    for (const layers of [null, [null], [{ id: '', behaviour: { $type: 'NoiseLayerBehaviour' } }]])
      assert.throws(() => validate({ ...empty, layers }, schema));
    for (const width of [0, 16385, 1.5, '8', null])
      assert.throws(() => validate({ ...empty, document: { width } }, schema));
    for (const pixelScale of [.00001, .25, 1, 2.5, 16384])
      validate({ ...empty, document: { pixelScale } }, schema);
    for (const pixelScale of [0, -1, 16385, Infinity, NaN, '2', null])
      assert.throws(() => validate({ ...empty, document: { pixelScale } }, schema));
    const noise = (settings) => ({ ...empty, layers: [{ id: 'fixture', behaviour: { $type: 'NoiseLayerBehaviour', ...settings } }] });
    for (const settings of [{ scale: '8' }, { scale: 1e40 }, { noiseType: '999' }, { noiseType: 1 },
      { seed: 2.5 }, { seed: 2147483648 }, { offset: [0, '1', 0] }, { offset: [0] }, { offset: [0, 1, 2, 3] }])
      assert.throws(() => validate(noise(settings), schema));
    validate(noise({ offset: [0, 1] }), schema);
    const shape = (settings) => ({ ...empty, layers: [{ id: 'shape', behaviour: { $type: 'ShapeLayerBehaviour', ...settings } }] });
    const vector = (vectorValue) => ({ ...empty, layers: [{ id: 'vector', behaviour: { $type: 'ColorFillLayerBehaviour' },
      fx: [{ $type: 'ShaderFX', parameters: [{ type: 'Vector', vectorValue }] }] }] });
    for (const value of [[1, 2], [1, 2, 3], [1, 2, 3, 4]]) validate(vector(value), schema);
    for (const value of [[1], [1, 2, 3, 4, 5]]) assert.throws(() => validate(vector(value), schema));
    validate(shape({ rectangleCorners: [{ amount: .1 }, { style: 'Bevel', amount: .4 }, {}, {}], edgeMode: 'Step' }), schema);
    validate(shape({ kind: 'Arc', arcThickness: 24, fill: true, fillColor: [1, 0, 0, 1],
      stroke: true, strokeColor: [0, 0, 1, 1], strokeWidth: 3, strokePosition: 'Outside' }), schema);
    for (const strokeJoin of ['Round', 'Miter', 'Bevel']) for (const strokeCap of ['Butt', 'Round', 'Square'])
      validate(shape({ strokeJoin, strokeCap, miterLimit: 4, dashPattern: [3, 2, .01, 2], dashOffset: -2 }), schema);
    validate(shape({ dashPattern: null }), schema);
    for (const fields of [{ strokeJoin: 'Unknown' }, { strokeCap: 'Unknown' }, { miterLimit: .5 },
      { dashPattern: [0, 1] }, { dashPattern: [-1] }, { dashPattern: Array(257).fill(1) }, { dashPatternRelative: false }])
      assert.throws(() => validate(shape(fields), schema));
    for (const arcThickness of ['24', null, [24]]) assert.throws(() => validate(shape({ kind: 'Arc', arcThickness }), schema));
    assert.throws(() => validate(shape({ cornerRoundness: [0, 0, 0, 0] }), schema));
    const color = (value) => ({ ...empty, layers: [{ id: 'color', behaviour: { $type: 'ColorFillLayerBehaviour', storedColor: value } }] });
    validate(color([1, 2, 3, 4]), schema);
    assert.throws(() => validate(color([1, 2, 3]), schema));
    validate(noise({ scale: 0, scaleY: 0, seed: -2147483648, offset: [-100, 200, 0] }), schema);
    validate(noise({ scale: 2000, warpStrength: -25 }), schema);
    const curve = (key) => ({ ...empty, layers: [{ id: 'curve', behaviour: { $type: 'ColorFillLayerBehaviour' },
      fx: [{ $type: 'ShaderFX', parameters: [{ type: 'Curve', curveValue: { preWrap: 'Default', postWrap: 'Default', keys: [key] } }] }] }] });
    validate(curve([0, 1, 'Infinity', '-Infinity', 0.3, 0.3, 'Both']), schema);
    for (const key of [[0, 1, 'NaN', 0, 0, 0, 'None'], [0, 1, 0, 0, 0, 0, '999'], ['Infinity', 1, 0, 0, 0, 0, 'None']])
      assert.throws(() => validate(curve(key), schema));
});

await finish(context);
