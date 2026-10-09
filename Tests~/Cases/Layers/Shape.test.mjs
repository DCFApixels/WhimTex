// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("Shape source/reference tests");
const assert = context.assert;
context.case("Shape original assertion inputs and source contracts", async () => {
  // Source/lifecycle contracts. Actual GPU and drag geometry checks: ShapeSmoke.cs via Unity Pipeline.
  const read = p => readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
  const tool = read('WhimTexWindow.Shapes.cs');
  const behaviour = read('Layers/ShapeLayerBehaviour.cs');
  const shader = read('Shaders/Shape.shader');
  const inspector = read('Layers/Editors/ShapeLayerEditorWindow.cs');
  const down = tool.split('private void Down(')[1].split('private bool Valid')[0];
  const up = tool.split('private void Up(')[1].split('internal void Cancel()')[0];
  assert.doesNotMatch(down, /AddLayer\(|\.layers\.Add\(|Undo\./, 'Reservation exists only in gesture-local state');
  assert.doesNotMatch(down, /!owner.HasCanvasLayers/, 'Shapes can create the first layer');
  assert.match(down, /shape = new ShapeLayerBehaviour/);
  assert.match(down, /WhimTexColorInputs.DisplayColor\(settings.fillColor\)/);
  assert.match(tool, /canvas.ToCanvas\(position\)/, 'Creation uses inverse view rotation');
  assert.match(tool, /control \? p : owner.SnapCanvasGuidePoint\(p\)/);
  assert.match(tool, /owner.activeDocument == document && owner.canvasTool == CanvasTool.Shape/);
  assert.match(up, /Valid &&[\s\S]*sqrMagnitude >= 9f/);
  assert.equal((up.match(/owner.AddLayer\(/g) || []).length, 1, 'One existing transactional add on release');
  assert.match(up, /Cancel\(\);\s*owner.AddLayer/);
  assert.match(up, /string namePrefix = shape.kind.ToString\(\);[\s\S]*?Cancel\(\);\s*owner.AddLayer\(container, index, layer, namePrefix\)/,
      'Capture the figure name before Cancel clears the gesture; numbering stays in AddLayer');
  assert.match(up, /document.TryFindLayer\(insertionAnchor/);
  assert.match(tool, /int captured = pointer; pointer = -1;[\s\S]*target.ReleasePointer\(captured\)/,
      'Clear ownership before capture-out callbacks');
  for (const event of ['PointerDown', 'PointerMove', 'PointerUp', 'PointerCaptureOut', 'PointerCancel', 'DetachFromPanel', 'GeometryChanged', 'KeyDown']) {
      assert.match(tool, new RegExp(`RegisterCallback<${event}Event>`));
      assert.match(tool, new RegExp(`UnregisterCallback<${event}Event>`));
  }
  assert.match(tool, /evt.keyCode == KeyCode.Escape\) \{ Cancel\(\); WhimTexUI.ConsumeEvent/);
  assert.match(read('WhimTexWindow.Api.cs'), /window.shapeManipulator != null && window.shapeManipulator.IsDragging/);
  assert.match(read('WhimTexWindow.Zoom.cs'), /CancelCanvasZoomGesture\(\)[\s\S]*?shapeManipulator\?\.Cancel\(\)/);
  assert.match(behaviour, /RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear/);
  assert.match(behaviour, /SetVector\("_ShapeFill", HdrUtility.Decode\(fillColor\)\)/);
  assert.match(behaviour, /applyTransform: false, applyFx: context.applyFx/);
  assert.match(behaviour, /finally[\s\S]*GL.sRGBWrite = srgb;[\s\S]*RenderTexture.active = previous;[\s\S]*ReleaseTemporary\(source\)/);
  assert.match(behaviour, /\[NonSerialized\] private ShapeContour contour/);
  assert.match(shader, /float4 _ShapeSegments\[128\], _ShapeCurves\[128\]/);
  assert.match(read('Layers/ShapeContour.cs'), /cachedHash != next/, 'Contour is rebuilt only when geometry changes');
  assert.match(shader, /fwidth\(distance\)/);
  assert.match(shader, /float fillCoverage = fill \* _ShapeStyle.x/);
  assert.match(inspector, /width.SetEnabled\(layer.stroke\)/);
  assert.match(inspector, /Pixels\("Stroke Width \(px\)"/);
  assert.match(inspector, /Pixels\("Thickness \(px\)"/);
  assert.match(inspector, /thickness.EnableInClassList\("whimtex-hidden", !arc\)/);
  assert.match(down, /arcThickness = settings.arcThickness/, 'Creation copies body thickness independently');
  assert.match(behaviour, /hash.Add\(arcThickness\)/, 'Thickness invalidates the thumbnail');
  assert.match(shader, /float radius = _ShapeArcThickness \* .5/);
  assert.doesNotMatch(shader, /_ShapeKind == 5\) return float4/, 'Arc shares the fill and outline compositor');
  assert.doesNotMatch(inspector, /Number\("Stroke Width/);
  for (const key of ['kind', 'fill', 'fillColor', 'stroke', 'strokeColor', 'strokeWidth', 'arcThickness', 'rectangleCorners', 'polygonCorners', 'outerCorner', 'innerCorner', 'strokePosition', 'lineCap', 'edgeMode', 'startAngle', 'sweepAngle', 'sides', 'innerRadius']) {
      assert.ok(read('Automation/WhimTexApi.Shape.cs').includes(`["${key}"]`), key + ' snapshot');
  }
  assert.match(read('Automation/WhimTexApi.Layers.cs'), /key == "shape" && layer\?\.Behaviour is ShapeLayerBehaviour/);
  assert.match(read('Automation/WhimTexApi.Inspect.cs'), /\["shapeKinds"\]/);

});
await finish(context);
