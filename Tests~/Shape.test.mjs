// Source/lifecycle contracts. Actual GPU and drag geometry checks: ShapeSmoke.cs via Unity Pipeline.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const tool = read('TextureCompositorWindow.Shapes.cs');
const behaviour = read('Layers/ShapeLayerBehaviour.cs');
const shader = read('Shaders/Shape.shader');
const inspector = read('Layers/Editors/ShapeLayerEditorWindow.cs');
const down = tool.split('private void Down(')[1].split('private bool Valid')[0];
const up = tool.split('private void Up(')[1].split('internal void Cancel()')[0];
assert.doesNotMatch(down, /AddLayer\(|\.layers\.Add\(|Undo\./, 'Reservation exists only in gesture-local state');
assert.doesNotMatch(down, /!owner.HasPreviewLayers/, 'Shapes can create the first layer');
assert.match(down, /shape = new ShapeLayerBehaviour/);
assert.match(down, /SpriteEditorColorInputs.DisplayColor\(settings.fillColor\)/);
assert.match(tool, /canvas.ToCanvas\(position\)/, 'Creation uses inverse view rotation');
assert.match(tool, /control \? p : owner.SnapPreviewGuidePoint\(p\)/);
assert.match(tool, /owner.compositor == document && owner.previewTool == PreviewTool.Shape/);
assert.match(up, /Valid &&[\s\S]*sqrMagnitude >= 9f/);
assert.equal((up.match(/owner.AddLayer\(/g) || []).length, 1, 'One existing transactional add on release');
assert.match(up, /Cancel\(\);\s*owner.AddLayer/);
assert.match(up, /string namePrefix = shape.kind.ToString\(\);\s*Cancel\(\);\s*owner.AddLayer\(container, index, layer, namePrefix\)/,
    'Capture the figure name before Cancel clears the gesture; numbering stays in AddLayer');
assert.match(up, /document.TryFindLayer\(insertionAnchor/);
assert.match(tool, /int captured = pointer; pointer = -1;[\s\S]*target.ReleasePointer\(captured\)/,
    'Clear ownership before capture-out callbacks');
for (const event of ['PointerDown', 'PointerMove', 'PointerUp', 'PointerCaptureOut', 'PointerCancel', 'DetachFromPanel', 'GeometryChanged', 'KeyDown']) {
    assert.match(tool, new RegExp(`RegisterCallback<${event}Event>`));
    assert.match(tool, new RegExp(`UnregisterCallback<${event}Event>`));
}
assert.match(tool, /evt.keyCode == KeyCode.Escape\) \{ Cancel\(\); SpriteEditorUI.ConsumeEvent/);
assert.match(read('TextureCompositorWindow.Api.cs'), /window.shapeManipulator != null && window.shapeManipulator.IsDragging/);
assert.match(read('TextureCompositorWindow.Zoom.cs'), /CancelPreviewZoomGesture\(\)[\s\S]*?shapeManipulator\?\.Cancel\(\)/);
assert.match(behaviour, /RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear/);
assert.match(behaviour, /SetVector\("_ShapeFill", HdrUtility.Decode\(fillColor\)\)/);
assert.match(behaviour, /applyTransform: false, applyModifiers: context.applyModifiers/);
assert.match(behaviour, /finally[\s\S]*GL.sRGBWrite = srgb;[\s\S]*RenderTexture.active = previous;[\s\S]*ReleaseTemporary\(source\)/);
assert.match(behaviour, /\[NonSerialized\] private Vector4\[\] polygonVertices/);
assert.match(shader, /float4 _ShapeVertices\[64\]/);
assert.doesNotMatch(shader.split('float PolygonDistance')[1].split('float4 frag')[0], /\b(?:sin|cos)\(/,
    'Polygon trigonometry runs once on CPU when shape parameters change, not per pixel');
assert.match(shader, /fwidth\(distance\)/);
assert.match(shader, /float fillCoverage = \(outer - strokeCoverage\) \* _ShapeStyle.x/);
assert.match(inspector, /width.SetEnabled\(layer.stroke\)/);
assert.match(inspector, /new FloatField\("Stroke Width \(px\)"\)/);
assert.doesNotMatch(inspector, /Number\("Stroke Width/);
for (const key of ['kind', 'fill', 'fillColor', 'stroke', 'strokeColor', 'strokeWidth', 'roundness', 'sides', 'innerRadius']) {
    assert.ok(read('Automation/SpriteEditorApi.Shape.cs').includes(`["${key}"]`), key + ' snapshot');
}
assert.match(read('Automation/SpriteEditorApi.Layers.cs'), /key == "shape" && layer\?\.Behaviour is ShapeLayerBehaviour/);
assert.match(read('Automation/SpriteEditorApi.Inspect.cs'), /\["shapeKinds"\]/);
console.log('Shape integration: gesture lifecycle, rotated coordinates, HDR, API and shader resource contracts passed. GPU checks are separate.');
