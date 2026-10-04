// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("RemainingLegacyCleanup source/reference tests");
const assert = context.assert;
context.case("RemainingLegacyCleanup original assertion inputs and source contracts", async () => {
  const read = path => readFileSync(new URL('../../../' + path, import.meta.url), 'utf8');
  const window = read('src/TextureCompositorWindow.DocumentFile.cs');
  for (const name of ['documentFileOwner', 'documentFileGuid', 'documentFilePath']) assert.ok(!window.includes(name), name);
  assert.match(window, /path = WhimTexDocumentService.PathOf\(document\);\s*return !string.IsNullOrEmpty\(path\);/);
  const fx = read('src/ShaderFX.cs'), catalog = read('src/ShaderFX.Catalog.cs');
  assert.ok(!fx.includes('declaredInCode'));
  assert.ok(!catalog.includes('UsesCodeParameters'));
  assert.ok(!fx.includes('UpgradeTransformHelpers') && !read('src/ShaderFXSourceBuilder.cs').includes('UpgradeTransformHelpers'));
  assert.ok(!read('src/Editor/ShaderFXEditor.cs').includes('ShaderFXParameterDrawer'));
  assert.ok(!read('src/Editor/ShaderFXParameterView.cs').includes('if (control == null)'));
  const live = read('src/Automation/WhimTexApi.LiveFx.cs');
  assert.ok(!live.includes('ReadLiveFxParameters'));
  assert.match(live, /else Keys\(spec, "op", "index", "code"\);/);
  assert.match(read('src/Automation/WhimTexApi.FxOperations.cs'), /ReadFxParameterValue\(ShaderFXParameter target, JToken token/);
  const bridge = read('src/WhimTexFileCompatibility0125.cs');
  assert.match(bridge, /typeof\(ShaderFXParameter\).*declaredInCode/);
  assert.match(bridge, /DeclareSavedParameters/);
  assert.ok(!read('src/ShaderFXMetadata.cs').includes('match.declaredInCode'));
  assert.ok(!read('src/ShaderFXPresetWriter.cs').includes('UsesCodeParameters'));
  const binary = read('src/WhimTexDocumentSerializer.cs');
  assert.ok(!binary.includes('IsExactFloat'));
  assert.ok(!binary.includes('if (declared == typeof(Vector'));
  assert.match(binary, /case TagFloat: return _reader.ReadSingle\(\);/);
  assert.ok(!binary.includes('Contains(sampling)'));
  const json = read('src/WhimTexDocumentJson.cs');
  assert.match(json, /CheckKeys\(root, "format", "version", "document", "layers", "writeMode"\)/);
  assert.ok(!read('src/Layers/NoiseLayerBehaviour.cs').includes('scaleY == 0'));
  assert.ok(!read('src/Layers/FillPatternSettings.cs').includes('sizeY == 0'));
  assert.ok(!read('src/Layers/ShapeLayerBehaviour.cs').includes('result[i] < 0'));
  const schema = JSON.parse(read('Documentation~/AI/document.schema.json'));
  assert.equal(schema.properties.kind, undefined);
  assert.equal(schema.$defs.ShaderFXParameter.properties.declaredInCode.deprecated, true);
  assert.equal(schema.$defs.ShapeLayerBehaviour.properties.roundness.deprecated, true);
  assert.match(read('src/WhimTexDocumentContainer.cs'), /!result.Contains\(IntegrityBlock\).*manifest is missing/g);
  for (const block of read('Documentation~/LiveAgentAPI.md').matchAll(/```json\s+([\s\S]*?)```/g)) {
      const request = JSON.parse(block[1]);
      for (const entry of request.layer?.fx ?? request.fx ?? []) assert.equal(entry.parameters, undefined);
  }

});
await finish(context);

