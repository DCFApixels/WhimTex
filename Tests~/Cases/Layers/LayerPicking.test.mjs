// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("LayerPicking source/reference tests");
const assert = context.assert;
context.case("LayerPicking original assertion inputs and source contracts", async () => {
  const read = name => readFileSync(new URL(`../../../src/${name}`, import.meta.url), 'utf8');
  const renderer = read('WhimTexDocument.LayerPicking.cs');
  const window = read('WhimTexWindow.LayerPicking.cs');
  const ui = read('WhimTexWindow.UI.cs');
  const settings = read('WhimTexUserSettings.cs');
  const settingsWindow = read('WhimTexUserSettingsWindow.cs');
  const expression = renderer.match(/MeetsPickThreshold\(float alpha, float threshold\) => ([^;]+);/)[1];
  const hit = new Function('alpha', 'threshold', `return ${expression.replace(/(\d)f\b/g, '$1')};`);
  assert.equal(hit(0, 0), false);
  assert.equal(hit(.0999756, .1), true);
  assert.equal(hit(.099, .1), false);
  assert.equal(hit(1, 1), true);
  assert.equal(hit(NaN, .1), false);
  assert.match(settings, /DefaultLayerPickAlphaThreshold = .1f/);
  assert.match(settings, /EditorPrefs.SetFloat\(LayerPickAlphaKey, value\)/);
  assert.match(settings, /LayerPickAlphaThreshold = DefaultLayerPickAlphaThreshold/);
  for (const source of [window, settingsWindow]) {
      assert.match(source, /"Alpha ≥ %"/);
      assert.match(source, /LayerPickAlphaThreshold = evt.newValue \* .01f/);
      assert.match(source, /LayerPickAlphaThreshold \* 100f/);
  }
  assert.match(ui, /OnCanvasPointerDown\(PointerDownEvent evt\)\s*\{\s*ObserveBrushPointer\([^;]+;\s*canvasPointerControl = evt.ctrlKey;\s*if \(HandleLayerPickPointerDown\(evt\)\)/);
  assert.equal((ui.match(/HandleLayerPickPointerDown\(evt\)/g) ?? []).length, 1);
  assert.match(window, /canvasTool != CanvasTool.None/);
  assert.match(window, /evt.button != 0 \|\| evt.altKey/);
  assert.match(window, /evt.target != toolkitCanvas/);
  assert.match(window, /evt.pressedButtons != 1/);
  assert.match(window, /toolkitCanvas.ToCanvas\(evt.localPosition\)/);
  assert.match(window, /if \(tiledCanvas\) uv = new Vector2\(Mathf.Repeat/);
  assert.match(window, /if \(evt.shiftKey\)/);
  assert.match(window, /selectedLayerIds.Remove\(hit.Id\)/);
  assert.match(window, /else SelectOnlyLayer\(hit\?\.Id\)/);
  assert.match(window, /evt.ctrlKey \|\| evt.commandKey/);
  assert.match(window, /GetLayerPickContinuation\(evt.localPosition, insideGroups, evt.shiftKey\)/);
  assert.match(window, /continuation, cycle: true/);
  assert.match(renderer, /enteredGroups.Add\(selectedLayer\)/);
  assert.match(renderer, /enteredGroups.Add\(parent\)/);
  assert.match(renderer, /insideGroups \|\| enteredGroups\?\.Contains\(layer\) == true/);
  assert.match(window, /groupExpansion\[parent.Id\] = true/);
  assert.match(renderer, /WhimTexProfiler.ReadPixels\(sample, new Rect\(x, y, 1, 1\), 0, 0, false\)/);
  assert.match(renderer, /collectingErrors = previousErrors/);
  assert.match(renderer, /effectCache = previousCache/);
  assert.match(renderer, /FindClippingBaseIndex/);
  assert.ok(!/MarkChanged\(|Undo\.|SetDirty\(/.test(renderer + window), 'Picking only changes window selection');
  assert.match(ui, /BindCanvasSettingsRow\(pickRow, CanvasTool.None\)/);
  assert.match(read('WhimTexWindow.Tools.cs'), /Layer Select \(V\)/);
  assert.match(window, /PickLayerAtPixelWithGeometry/);
  assert.match(window, /PickVectorGeometry/);
  assert.match(window, /DrawSelectedVectorContours/);
  assert.match(renderer, /GeometryAlpha\(layer\)/);
  for (const name of ['WhimTexWindow.Tools.cs', 'WhimTexWindow.Vector.cs', 'WhimTexWindow.VectorTools.cs', 'WhimTexWindow.Cursors.cs'])
    assert.ok(!read(name).includes('VectorSelect'), `${name}: removed tool has no implementation`);

});
await finish(context);

