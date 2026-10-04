import assert from 'node:assert/strict';
import fs from 'node:fs';
const read = p => fs.readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const panel = read('src/Editor/LayerPreviewPanel.cs');
assert.doesNotMatch(panel, /public int channel;/);
assert.match(panel, /channelMask = 15/);
assert.match(panel, /state\.channelMask &= 15/);
const kernel = read('src/Layers/GaussianKernel.cs');
assert.match(kernel, /const int Capacity = 128/);
assert.match(kernel, /buffer\.Length != Capacity/);
assert.equal((kernel.match(/SetVectorArray\("_Kernel"/g) || []).length, 1);
for (const file of ['GaussianBlurRenderer.cs', 'SharpenRenderer.cs']) {
  const renderer = read('src/Layers/' + file);
  assert.match(renderer, /GaussianKernel\.Set\(/);
  assert.doesNotMatch(renderer, /Math\.Exp|SetVectorArray\("_Kernel"/);
}
const brush = read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs');
assert.match(brush, /new Vector4\[GaussianKernel\.Capacity\]/);
assert.match(brush, /GaussianKernel\.Upload\(blur, \.2f, 5, BlurBrushKernel\)/);
for (const file of ['src/Automation/WhimTexApi.Export.cs', 'src/TextureCompositorWindow.Export.cs']) {
  const caller = read(file);
  assert.match(caller, /WhimTexRasterEncoder\.Encode\(/);
  assert.doesNotMatch(caller, /HdrUtility\.ToLdr|EncodeTo(?:PNG|JPG|TGA|EXR)/);
}
const encoder = read('src/WhimTexRasterEncoder.cs');
assert.match(encoder, /enum RasterImageFormat \{ Png, Jpeg, Tga, Exr \}/);
assert.match(encoder, /finally \{ UnityEngine\.Object\.DestroyImmediate\(ldr\); \}/);
const serializer = read('src/WhimTexDocumentSerializer.cs');
assert.match(serializer, /ModelReadResult Deserialize/);
assert.doesNotMatch(serializer, /Last(?:SkippedFields|MissingTypes|UnresolvedReferences)|static readonly (?:List<string>|HashSet<string>) _(?:missing|skipped|unresolved)/);
assert.match(serializer, /Array\.AsReadOnly\(skippedFields\.ToArray\(\)\)/);
assert.match(serializer, /Array\.AsReadOnly\(missingTypes\.ToArray\(\)\)/);
assert.match(serializer, /Array\.AsReadOnly\(unresolvedReferences\.ToArray\(\)\)/);
assert.match(serializer, /return context\.Result\(result\)/);
assert.match(serializer, /private const int FormatVersion = 1/);
const file = read('src/WhimTexDocumentFile.cs');
assert.doesNotMatch(file, /WhimTexDocumentSerializer\.Last/);
for (const name of ['SkippedFields', 'MissingTypes', 'UnresolvedReferences'])
  assert.match(file, new RegExp('read\\.' + name));
assert.match(file, /document\.documentLoadWarning = warnings\.Count == 0/);
console.log('PASS: R01-R04 canonical state, shared kernel/encoder and operation-owned diagnostics contracts.');

