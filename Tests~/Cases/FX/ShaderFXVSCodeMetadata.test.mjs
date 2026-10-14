// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { buildVsix } from '../../../ExternalTools~/WhimTexVSCode/build-vsix.mjs';
const context = new TestContext("ShaderFXVSCodeMetadata source/reference tests");
const assert = context.assert;
try {
  const require = createRequire(import.meta.url);
  const { validate } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
  const cases = JSON.parse(readFileSync(new URL('../../Fixtures/ShaderFXVSCodeMetadata.node.cases.json', import.meta.url), 'utf8'));

  for (const { name, source, valid } of cases) context.case(name, async () => {
    const errors = validate(source).filter(d => !d.warning);
    assert.equal(errors.length === 0, valid, JSON.stringify(errors));
  });

  context.case('all bundled FX presets, including the Mask screenshot, validate without warnings', async () => {
    const folder = new URL('../../../src/FXPresets/', import.meta.url);
    for (const file of readdirSync(folder).filter(f => f.endsWith('.hlsl'))) {
      assert.deepEqual(validate(readFileSync(new URL(file, folder), 'utf8')), [], file);
    }
  });

  context.case('tooltips and disabled examples do not produce declarations', async () => {
    assert.deepEqual(validate('// @param curve _Profile // @param broken ;\n/*\n// @param bad _Bad\n*/'), []);
  });

  context.case('seed modifiers enforce Unity float integer bounds without name inference', async () => {
    const { parseParameter } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
    assert.equal(parseParameter('label(Seed) seed hidden float _Seed = 2.5 [~2.1 .. ~3.8]').seed, true);
    assert.equal(parseParameter('float _Seed = 0 [0 .. 10]').seed, false);
    for (const declaration of ['seed bool _Seed', 'seed enum _Seed {A:0}', 'seed float _Seed',
      'seed float _Seed [0 ..]', 'seed float _Seed [0.1 .. 0.9]', 'seed seed float _Seed [0 .. 10]',
      'seed float _Seed [-16777218 .. 0]', 'seed float _Seed [0 .. 16777218]'])
      assert.throws(() => parseParameter(declaration), undefined, declaration);
    for (const n of [-16777216, 0, 16777216]) assert.equal(parseParameter(`seed float _Seed = ${n} [${n} .. ${n}]`).seed, true);
  });

  context.case('catalog marker is recognized and reports misplaced markers as a warning', async () => {
    assert.deepEqual(validate('// @whimtex-effect Color/Mask'), []);
    assert.equal(validate('\n// @whimtex-effect Color/Mask')[0].warning, true);
  });

  context.case('logical pixel metadata supports only explicit numeric lengths', async () => {
    const { parseParameter } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
    for (const kind of ['float', 'float2', 'float3', 'float4'])
      assert.equal(parseParameter(`hidden label(Length) px ${kind} _Length`).pixelUnits, true);
    assert.equal(parseParameter('float _Radius').pixelUnits, false);
    assert.deepEqual(validate('// @param px float _A\n// @param px float _A [0 .. 8]'), []);
    assert.ok(validate('// @param px float _A\n// @param float _A').some(d => !d.warning));
    for (const declaration of ['px bool _A', 'px color _A', 'px normal _A', 'px point _A',
      'px texture2D _A', 'px curve _A', 'px gradient _A', 'px transform2D _A', 'px enum _A {A:0}',
      'px seed float _A [0 .. 100]', 'px px float _A'])
      assert.throws(() => parseParameter(declaration), undefined, declaration);
  });

  context.case('removed migration directives warn without creating aliases', async () => {
    for (const source of ['// @formerlyserializedas(_Old)',
      '// @formerlyserializedas(_Old)\n// @param float _New',
      '// @formerlyserializedas(_Old)\n// @formerlyserializedas(_Old)\n// @param float _New']) {
      const messages = validate(source);
      assert.ok(messages.length > 0 && messages.every(d => d.warning && /Unknown WhimTex directive/.test(d.message)));
    }
  });

  context.case('effect control is optional, warning-only and last declaration wins', async () => {
    const param = '\n// @param hidden float _Opacity = 1 [0..1]';
    assert.deepEqual(validate('// @control(_Opacity)' + param), []);
    assert.deepEqual(validate('// @whimtex-effect Color/Test\n// @control(_Opacity)' + param), []);
    const duplicate = validate('// @control(_Missing)\n// @control(_Opacity)' + param);
    assert.equal(duplicate.length, 1);
    assert.ok(duplicate[0].warning && /last declaration wins/.test(duplicate[0].message));
    for (const source of ['// @control(_Missing)', '// @control()', '\n// @control(_Opacity)' + param]) {
      const warnings = validate(source);
      assert.ok(warnings.length && warnings.every(d => d.warning));
    }
    assert.deepEqual(validate('/*\n// @control(_Missing)\n*/'), []);
    assert.equal(validate('// @control(_Opacity)\n// @control()' + param).length, 2);
  });

  context.case('installer checks VSIX content as well as its unchanged version', async () => {
    const source = readFileSync(new URL('../../../src/Editor/ShaderFXExternalCode.cs', import.meta.url), 'utf8');
    assert.match(source, /sha\.ComputeHash\(archive\)/);
    assert.match(source, /File\.ReadAllText\(installedMarker\)\.Trim\(\) == extensionStamp/);
    assert.match(source, /pendingExtensionStamp, utf8WithoutBom/);
  });

  context.case('bundled VSIX includes the current validator and grammar', async () => {
    const archive = readFileSync(new URL('../../../ExternalTools~/WhimTexVSCode/whimtex-fx-tools.vsix', import.meta.url));
    assert.ok(archive.equals(buildVsix()), 'Run ExternalTools~/WhimTexVSCode/build-vsix.mjs');
  });

} catch (error) {
  context.case('Fixture initialization', async () => { throw error; });
}
await finish(context);
