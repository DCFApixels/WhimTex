// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("ShaderProcessorVisibility source/reference tests");
const assert = context.assert;
context.case("ShaderProcessorVisibility original assertion inputs and source contracts", async () => {
  // Source/control-flow guards; actual activeDocument pixels are checked by ShaderProcessorSmoke.cs.
  const read = p => readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
  const activeDocument = read('WhimTexDocument.cs');
  const standalone = activeDocument.slice(activeDocument.indexOf('private RenderTexture RenderStandaloneUncached('), activeDocument.indexOf('private RenderTexture RenderEffectInput('));
  const branch = standalone.slice(standalone.indexOf('if (layer?.Behaviour is ShaderProcessorLayerBehaviour)'), standalone.indexOf('if (layer?.Behaviour is TargetedLayerBehaviour effect)'));
  assert.match(branch,/CompositeLayers\(container,[\s\S]*firstIndex: index \+ 1\)/);
  assert.match(branch,/if \(!layer.enabled\)\s*\{\s*RenderTexture bypass = input;\s*input = null;\s*return bypass;/);
  assert.ok(standalone.indexOf('return bypass;') < standalone.indexOf('layer.Render(context)'));
  assert.ok(standalone.indexOf('return bypass;') < standalone.indexOf('FinishStage(raw'));
  assert.match(standalone,/finally[\s\S]*if \(input != null && input != accumulatedInput\)[\s\S]*RenderTexture.ReleaseTemporary\(input\)/,
      'Cleanup must release owned inputs, never the caller-owned accumulated input');
  assert.match(activeDocument,/if \(processor.enabled && processor.opacity > 0f/,'Main stack also respects visibility');
  assert.match(activeDocument,/renderStack, includeDisabled: true/,'Ordinary hidden effect sources remain supported');

});
await finish(context);

