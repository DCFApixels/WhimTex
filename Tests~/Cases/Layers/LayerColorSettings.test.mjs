// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("LayerColorSettings source/reference tests");
const assert = context.assert;
context.case("LayerColorSettings original assertion inputs and source contracts", async () => {
  // UI source contracts; does not launch or compile Unity.
  const read = p => readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
  const view = read('LayerColorSettingsView.cs');
  assert.match(view, /new Slider\("Opacity", 0f, 1f\)/);
  assert.match(view, /name = "layerOpacity", showInputField = true/);
  assert.match(view, /bindings.Track\(opacity, \(\) => layer.opacity\)/);
  assert.match(view, /float.IsNaN\(evt.newValue\) \? 0f : Mathf.Clamp01\(evt.newValue\)/);
  assert.match(view, /apply\("Change Layer Opacity", \(\) => layer.opacity = value\)/);
  assert.match(view, /GroupBlend\(blendGroup,/);
  assert.match(view, /blendGroup.compositing = passThrough \? GroupCompositing.PassThrough : GroupCompositing.Isolated/);
  assert.match(view, /if \(!passThrough\) blendGroup.blendMode = value/);
  assert.match(view, /new EnumField\("Blend Mode", layer.blendMode\)/);
  assert.match(view, /bindings.Track\(mode, \(\) => \(Enum\)layer.blendMode\)/);
  assert.match(view, /apply\("Change Layer Blend Mode", \(\) => layer.blendMode = \(BlendMode\)evt.newValue\)/);
  assert.ok(view.indexOf('card.Add(opacity)') < view.indexOf('new EnumField("Color Range"'));
  assert.doesNotMatch(view, /opacity.SetEnabled\(active\)|mode.SetEnabled\(active\)/, 'Pass Through disables ranges, not opacity/blend controls');
  const rows = read('TextureCompositorWindow.UI.cs');
  assert.match(rows, /ApplySelectedOpacity\(layer, evt.newValue\)/);
  assert.match(rows, /ApplySelectedBlend\(layer, \(BlendMode\)evt.newValue\)/);
  assert.match(read('WhimTexUI.cs'), /LayerColorSettingsView.Build\(root, layer, apply, bindings,/);

});
await finish(context);

