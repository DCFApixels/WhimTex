import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { tests, candidates } from './scripts/audit-sources.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const audit = {tests, methodCandidates: candidates};
for (const file of ['LayerPersistenceSetup.cs', 'LayerPersistenceVerify.cs', 'LayerPersistenceCleanup.cs', 'SpriteEditorCompileSmoke.cs'])
  assert.ok(!audit.tests.some(test => test.file === file), file);
for (const [file, name] of [
  ['src/TextureCompositor.cs', 'CloneEmbeddedShaderFX'],
  ['src/TextureCompositor.cs', 'CloneDrawingLayerTextures'],
  ['src/Layers/SDFLayerBehaviour.cs', 'ConvertDistance'],
  ['src/MissingLayerRecovery.cs', 'GradientTime'],
  ['src/TextureCompositorWindow.UI.cs', 'FillRect'],
  ['src/WhimTexDocumentSerializer.cs', 'ReflectedFieldCount']
]) {
  // Reports mention removed symbols too: textual reference counts cannot guard their removal.
  assert.doesNotMatch(read(file), new RegExp('\\b' + name + '\\s*\\('), name);
  assert.ok(!audit.methodCandidates.some(candidate => candidate.name === name), name);
}
assert.equal(audit.tests.find(test => test.file === 'SoftRangeSmoke.cs').primary, 'SoftRangeSmoke.Main');
assert.equal(audit.tests.find(test => test.file === 'TwoChoiceDropdownSmoke.cs').primary, 'TwoChoiceDropdownSmoke.Main');
for (const file of ['CanvasViewFooterSmoke.cs', 'ContentFillUiSmoke.cs', 'UvUiSmoke.cs'])
  assert.equal(audit.tests.find(test => test.file === file).family, 'multi-step');
for (const file of ['GradientClipboardCleanupSmoke.cs', 'RemainingLegacyCleanupSmoke.cs', 'UserSettingsCleanupSmoke.cs'])
  assert.equal(audit.tests.find(test => test.file === file).family, 'regression');
assert.match(read('src/Layers/GaussianKernel.cs'), /const int Capacity = 128/);
assert.match(read('src/Layers/GaussianKernel.cs'), /buffer\.Length != Capacity/);
assert.match(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /new Vector4\[GaussianKernel\.Capacity\]/);
assert.match(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /GaussianKernel\.Upload\(blur, \.2f, 5, BlurBrushKernel\)/);
for (const file of ['GaussianBlurRenderer.cs', 'SharpenRenderer.cs'])
  assert.match(read('src/Layers/' + file), /GaussianKernel\.Set\(/);
assert.doesNotMatch(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /SetVectorArray\("_Kernel", new/);
console.log('PASS: audit classifications, retired tests/helpers and full shared Gaussian kernel capacity.');
