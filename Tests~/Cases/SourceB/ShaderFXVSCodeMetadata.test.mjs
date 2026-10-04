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
  const cases = JSON.parse(readFileSync(new URL('../../Framework/NodeSupportB/ShaderFXVSCodeMetadata.cases.json', import.meta.url), 'utf8'));

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

  context.case('catalog marker is recognized and reports misplaced markers as a warning', async () => {
    assert.deepEqual(validate('// @whimtex-effect Color/Mask'), []);
    assert.equal(validate('\n// @whimtex-effect Color/Mask')[0].warning, true);
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
