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
      'properties', 'additionalProperties', 'required', 'items', 'prefixItems', 'minItems', 'maxItems', 'minLength', 'minimum', 'maximum', 'deprecated', 'description', 'not']);
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
      if (typeof value === 'string') assert(value.length >= (rule.minLength ?? 0), `${location}: string length`);
      if (typeof value === 'number') {
        assert(Number.isFinite(value), `${location}: non-finite number`);
        assert(value >= (rule.minimum ?? -Infinity) && value <= (rule.maximum ?? Infinity), `${location}: number range`);
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
    const defaultsSource = fs.readFileSync(path.join(root, 'src/WhimTexJsonDefaultsV2.cs'), 'utf8');
    const defaultsLiteral = defaultsSource.match(/internal const string Data = @"((?:[^"]|"")*)";/);
    assert(defaultsLiteral, 'Frozen JSON defaults must be available independently of Unity');
    const defaults = JSON.parse(defaultsLiteral[1].replaceAll('""', '"'));
    const identities = new Set(['$type', '$id', '$name', 'contentOmitted', 'id', 'recoveryId', 'shaderKey']);
    for (const [type, definition] of Object.entries(schema.$defs)) {
      assert(type in defaults, `${type}: missing frozen default object`);
      for (const [field, rule] of Object.entries(definition.properties)) {
        if (identities.has(field)) continue;
        assert(field in defaults[type], `${type}.${field}: omitted input would use an unfrozen initializer or zero/null`);
        // A layer must explicitly supply its behaviour, despite the unused null baseline.
        if (type === 'Layer' && field === 'behaviour') continue;
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
    for (const overflow of ['None', 'Clip', 'Ellipsis']) validate({ format: 'whimtex.document', version: 2,
      layers: [{ id: 'text', behaviour: { $type: 'TextLayerBehaviour', overflow } }] }, schema);
    assert.throws(() => validate({ format: 'whimtex.document', version: 2,
      layers: [{ id: 'text', behaviour: { $type: 'TextLayerBehaviour', overflow: 'unknown' } }] }, schema));
    const layerFx = fields => ({ format: 'whimtex.document', version: 2,
      layers: [{ id: 'fx', behaviour: { $type: 'ColorFillLayerBehaviour' }, ...fields }] });
    validate(layerFx({ fx: [] }), schema);
    assert.throws(() => validate(layerFx({ modifiers: [] }), schema));
    for (const fields of [{ fx: [], modifiers: [] }, { fx: null, modifiers: [] }, { modifiers: null, fx: [] }])
      assert.throws(() => validate(layerFx(fields), schema));
    for (const file of ['Documentation~/AI/README.md', 'Documentation~/JSON_FORMAT.md'])
      for (const match of fs.readFileSync(path.join(root, file), 'utf8').matchAll(/\x60\x60\x60json\s*\n([\s\S]*?)\x60\x60\x60/g)) {
        const value = JSON.parse(match[1]);
        if (value.format === 'whimtex.gradient') continue;
        validate(value, schema, file);
      }
    const empty = { format: 'whimtex.document', version: 2, document: {}, layers: [] };
    validate(empty, schema);
    validate({ format: 'whimtex.document', version: 2, layers: [] }, schema);
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
