import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { test } from 'node:test';
import { buildVsix } from '../ExternalTools~/WhimTexVSCode/build-vsix.mjs';
const require = createRequire(import.meta.url);
const { validate } = require('../ExternalTools~/WhimTexVSCode/metadata.js');
const cases = JSON.parse(readFileSync(new URL('ShaderFXVSCodeMetadata.cases.json', import.meta.url), 'utf8'));

for (const { name, source, valid } of cases) test(name, () => {
  const errors = validate(source).filter(d => !d.warning);
  assert.equal(errors.length === 0, valid, JSON.stringify(errors));
});

test('all bundled FX presets, including the Mask screenshot, validate without warnings', () => {
  const folder = new URL('../src/FXPresets/', import.meta.url);
  for (const file of readdirSync(folder).filter(f => f.endsWith('.hlsl'))) {
    assert.deepEqual(validate(readFileSync(new URL(file, folder), 'utf8')), [], file);
  }
});

test('tooltips and disabled examples do not produce declarations', () => {
  assert.deepEqual(validate('// @param curve _Profile // @param broken ;\n/*\n// @param bad _Bad\n*/'), []);
});

test('catalog marker is recognized and reports misplaced markers as a warning', () => {
  assert.deepEqual(validate('// @whimtex-effect Color/Mask'), []);
  assert.equal(validate('\n// @whimtex-effect Color/Mask')[0].warning, true);
});

test('installer checks VSIX content as well as its unchanged version', () => {
  const source = readFileSync(new URL('../src/Editor/ShaderFXExternalCode.cs', import.meta.url), 'utf8');
  assert.match(source, /sha\.ComputeHash\(archive\)/);
  assert.match(source, /File\.ReadAllText\(installedMarker\)\.Trim\(\) == extensionStamp/);
  assert.match(source, /pendingExtensionStamp, utf8WithoutBom/);
});

test('bundled VSIX includes the current validator and grammar', () => {
  const archive = readFileSync(new URL('../ExternalTools~/WhimTexVSCode/whimtex-fx-tools.vsix', import.meta.url));
  assert.ok(archive.equals(buildVsix()), 'Run ExternalTools~/WhimTexVSCode/build-vsix.mjs');
});
