// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const context = new TestContext("ProceduralClipboard source/reference tests");
const assert = context.assert;
context.case("ProceduralClipboard original assertion inputs and source contracts", async () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
  const read = p => fs.readFileSync(path.join(root, p), 'utf8');
  const originalArgs = [...process.argv];
  try {
    process.argv.push('--check');
    await import('../../../Documentation~/scripts/build-agent-fields-schema.mjs');
  } finally {
    process.argv.splice(0, process.argv.length, ...originalArgs);
  }
  const schema = JSON.parse(read('Documentation~/AI/agent-fields.schema.json'));
  // Deliberately only the schema vocabulary emitted by our generator, not a general JSON Schema implementation.
  function matches(rule, value) {
    if (typeof rule === 'boolean') return rule;
    if (rule.$ref) return matches(schema.$defs[rule.$ref.split('/').pop()], value);
    if (rule.oneOf) return rule.oneOf.filter(s => matches(s, value)).length === 1;
    if ('const' in rule && value !== rule.const) return false;
    if (rule.enum && !rule.enum.includes(value)) return false;
    if (rule.type === 'object') return value !== null && !Array.isArray(value) && typeof value === 'object' &&
      (rule.required ?? []).every(k => k in value) && Object.entries(value).every(([k, v]) =>
        k in (rule.properties ?? {}) ? matches(rule.properties[k], v) : matches(rule.additionalProperties ?? true, v));
    if (rule.type === 'array') return Array.isArray(value) && value.length >= (rule.minItems ?? 0) && value.length <= (rule.maxItems ?? Infinity) &&
      value.every((v, i) => matches(rule.prefixItems?.[i] ?? rule.items, v));
    if (rule.type === 'string') return typeof value === 'string' && value.length >= (rule.minLength ?? 0) &&
      value.length <= (rule.maxLength ?? Infinity) && (!rule.pattern || new RegExp(rule.pattern).test(value));
    if (rule.type === 'boolean') return typeof value === 'boolean';
    if (rule.type === 'number' || rule.type === 'integer') return typeof value === 'number' && Number.isFinite(value) &&
      (rule.type !== 'integer' || Number.isInteger(value)) && value >= rule.minimum && value <= rule.maximum;
    return true;
  }
  for (const [name, file] of Object.entries({ noise: 'Noise', shape: 'Shape', blur: 'Blur', normalMap: 'NormalMap', makeSeamless: 'MakeSeamless', fillPattern: 'FillPattern' })) {
    const source = read(`src/Automation/WhimTexApi.${file}.cs`);
    const declared = [...source.match(/Keys\(value,([\s\S]*?)\);/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]).sort();
    assert.deepEqual(Object.keys(schema.$defs[name].properties).sort(), declared, `${name} keys differ from implementation`);
  }
  for (const size of [64, [32, 64]])
    assert.ok(matches(schema.$defs.fillPattern, { size, cellColor: 'Random', colorBlend: 'ReplaceRGB', seed: -2147483648, variation: .7 }));
  for (const bad of [{ size: [0, 32] }, { variation: 2 }, { cellColor: 'Unknown' }, { seed: 1.5 }])
    assert.equal(matches(schema.$defs.fillPattern, bad), false);
  const paste = read('src/WhimTexWindow.AreaSelection.cs');
  assert.ok(paste.indexOf('IsProceduralClipboard(clipboardText)') < paste.indexOf('WhimTexDocument copiedLayers = LayerClipboard.Current'));
  assert.match(paste, /IsTextInputTarget\(target\)/);
  assert.match(paste, /resize && HasCanvasLayers/);
  assert.ok(paste.indexOf('generated.Compile()') < paste.indexOf('PasteProceduralClipboard(generated, resize)'),
    'Effects compile before the tree is handed to the paste');
  assert.match(paste, /finally \{ generated.Dispose\(\); \}/, 'Every parsed tree is disposed after paste/cancel');
  const linked = read('src/WhimTexWindow.ImageUrl.cs');
  assert.match(linked, /TryGetOriginalAspectTransform\(/, 'A downloaded image is fitted, not resampled');
  assert.match(linked, /ImageUrlMaximumBytes = 64 \* 1024 \* 1024/);
  assert.match(linked, /BeginImageUrlDownload\(activeDocument, uri.AbsoluteUri, InsertDownloadedImage\)/);
  assert.match(linked, /EditorApplication.update -= PollImageUrl/);
  assert.match(linked, /imageUrlApply = null/);
  assert.match(read('src/WhimTexWindow.BrushClipboard.cs'), /BeginImageUrlDownload\(activeDocument,url/);
  assert.doesNotMatch(linked, /clipboardPasteData|BeginImageUrlBatch|imageUrlJobs|DescribeImageHosts/);
  const parser = read('src/Automation/WhimTexApi.Clipboard.cs');
  assert.match(parser, /ReadForInsertion\(text, width, height, false\)/);
  assert.match(parser, /TryPrepareDocumentEffect\(out string warning\)/);
  assert.match(parser, /!Warnings.Contains\(warning\)/);
  assert.doesNotMatch(parser, /whimtex\.layers|SetClipboardGradient|FindPortableParameter|ReadPortableFileAsset|CanvasFilter|Images/);
  assert.ok(linked.indexOf('"Paste with warnings"') < linked.indexOf('PasteCopiedLayers(data.Document'), 'Warnings precede insertion');
  assert.match(linked, /"Paste", "Cancel"\)\) return;/);
  assert.doesNotMatch(read('src/WhimTexWindow.Selection.cs'), /canvasFilter/);
  for (const name of ['ValidatePortableSource', 'ExportPortableIncludes', 'PortableMaximumBytes'])
    assert.ok(!read('src/ShaderFXSourceBuilder.cs').includes(name), name + ' retired');
  assert.ok(!read('src/ShaderFXPresetWriter.cs').includes('BuildPortableSource'));
  for (const name of ['PortableImageUrl', 'RememberImageUrl', 'pixelsRevision', 'originalImageRevision', 'originalImageUrl'])
    assert.ok(!read('src/Layers/DrawingLayerBehaviour.cs').includes(name), name + ' retired from working model');
  assert.ok(!fs.existsSync(path.join(root, 'Documentation~/AI/layers.schema.json')), 'Old envelope schema retired');
  assert.ok(!fs.existsSync(path.join(root, 'Tests~/Fixtures/LegacyClipboard')) || fs.readdirSync(path.join(root, 'Tests~/Fixtures/LegacyClipboard')).length === 0, 'Old fixtures retired');
  for (const name of ['README.md', 'README-RU.md']) {
    assert.match(read(name), /^<!--[\s\S]*?AI_AUTHORING\.md[\s\S]*?-->/);
    assert.match(read(name).replace(/<!--[\s\S]*?-->/g, ''), /\]\(AI_AUTHORING\.md\)/);
  }
  const guide = read('Documentation~/AI/README.md');
  assert.match(guide, /reader is removed/);
  assert.match(guide, /0\.12\.5/);
  for (const [name, text] of [['AI/README.md', guide],
                              ['en/ai-authoring.md', read('Documentation~/en/ai-authoring.md')],
                              ['ru/ai-authoring.md', read('Documentation~/ru/ai-authoring.md')]]) {
    for (const blocked of ['no external assets', 'self-contained JSON', 'cannot supply texture assets', 'only procedural layers'])
      assert.ok(!text.toLowerCase().includes(blocked), name + ' still tells an AI that images are impossible: ' + blocked);
  }
  for (const file of ['Documentation~/en/automation.md', 'Documentation~/ru/automation.md'])
    for (const blocked of ['additionally insert images', 'дополнительно умеет вставлять изображения'])
      assert.ok(!read(file).toLowerCase().includes(blocked), file + ' must not reserve image insertion for the connected agent');

});
await finish(context);

