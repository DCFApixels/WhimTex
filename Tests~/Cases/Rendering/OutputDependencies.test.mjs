// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("OutputDependencies source/reference tests");
const assert = context.assert;
context.case("OutputDependencies original assertion inputs and source contracts", async () => {
  const read = name => readFileSync(new URL(`../../../src/${name}`, import.meta.url), 'utf8');
  const asset = read('WhimTexDocument.Assets.cs');
  const session = read('Editor/WhimTexDocumentSession.cs');
  const window = read('WhimTexWindow.cs');
  const live = read('WhimTexWindow.LiveOutput.cs');
  const change = read('WhimTexDocumentOutputChange.cs');
  assert.match(asset, /OutputTextureChanged\?\.Invoke\(new WhimTexDocumentOutputChange\(this\)\)/);
  assert.match(session, /NotifyOutputTextureChanged\(\)/);
  assert.doesNotMatch(asset, /SaveLegacyAssetForCompatibility|AssetDatabase\.CreateAsset\(/);
  for (const op of ['+=', '-=']) assert.ok(window.includes(`WhimTexDocument.OutputTextureChanged ${op} OnOutputTextureChanged`));
  assert.match(live, /!change.ShouldRefresh\(activeDocument\)/);
  assert.match(live, /outputDependencyDirty = true;\s*RequestCanvasRender\(\)/);
  assert.match(window, /UpdateCanvasRender\(\)\s*\{\s*if \(outputDependencyDirty\)\s*\{\s*outputDependencyDirty = false;\s*ReleaseEffectCache\(\)/);
  assert.match(change, /consumer == source/);
  assert.match(change, /layer\?\.IsGroup == true && UsesTexture\(layer.layers, texture\)/);
  assert.match(change, /visited.Add\(dependency\)/);
  assert.match(change, /return !DependsOnTexture\(source.layers, consumer.OutputTexture/);
  assert.ok(!/AssetDatabase|EditorPrefs|MarkChanged|SetDirty|Undo\./.test(change));
  const handler = live.split('private void OnOutputTextureChanged')[1].split('private bool HasDocumentFile')[0];
  assert.ok(!/MarkChanged|SetDirty|Undo\.|UpdateCanvasRender\(|ReleaseEffectCache\(/.test(handler));

});
await finish(context);

