import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const layer = read('src/Layers/DrawingLayerBehaviour.BrushPreview.cs');
const ui = read('src/TextureCompositorWindow.BrushPreview.cs');
const body = layer.match(/internal static Vector2 BrushPreviewPoint[^]*?\{([^]*?)\n        \}/)[1]
  .replace('float margin', 'let margin').replace('new Vector2', 'point').replaceAll('Mathf.', 'math.')
  .replace(/(\d)f\b/g, '$1');
const math = { Min: Math.min, Lerp: (a,b,t) => a+(b-a)*t, Sin: Math.sin, PI: Math.PI };
const point = new Function('t','width','height','math','point','marginScale',body);
for (const width of [128, 300, 596, 768]) {
  let last = -1;
  for (let i=0; i<=96; i++) {
    const [x,y] = point(i/96,width,192,math,(x,y)=>[x,y],1);
    assert.ok(x > last && x > 0 && x < 1); last=x;
    assert.ok(y >= .32-1e-7 && y <= .68+1e-7);
  }
  assert.equal(point(0,width,192,math,(x,y)=>[x,y],1)[1],.5);
}
assert.ok(layer.includes('PaintSegment(from, to, width, height, i == 1, parameters)'));
assert.ok(layer.includes('BeginStroke(from)') && layer.includes('EndStroke()'));
assert.ok(layer.includes('RenderTexture.active = previous'));
assert.ok(ui.includes('new DrawingLayerBehaviour()'));
assert.ok(ui.includes('JsonUtility.FromJson<BrushDynamics>'));
const sizeExpression = ui.match(/float size = ([^;]+);/)[1]
  .replaceAll('Mathf.', 'math.').replace('paintSettings.brushSize', 'size')
  .replace(/(\d)f\b/g, '$1');
const sampleSize = new Function('size', 'brushStrokePreviewScale', 'math', `return ${sizeExpression}`);
const sizeMath = { Max: Math.max, Clamp: (v,min,max) => Math.min(max,Math.max(min,v)) };
for (const size of [1, 8, 28, 103, 4096]) {
  const base = sampleSize(size,1,sizeMath);
  for (const scale of [.05,.1,.25,.5,1])
    assert.equal(sampleSize(size,scale,sizeMath),Math.max(1,base*scale));
}
assert.ok(ui.includes('new Slider("Preview Scale (%)", 5f, 100f)'));
const scaleCallback = ui.match(/scale.RegisterValueChangedCallback[^]*?\{([^]*?)\n            \}/)[1];
assert.ok(scaleCallback.includes('brushStrokePreviewDirty = true'));
assert.ok(!/paintSettings|ApplyPaintToolChange|SavePaintToolSettings/.test(scaleCallback),
  'Preview slider must not modify brush settings');
assert.ok(!ui.includes('dynamics.scatter') && !ui.includes('dynamics.sizeJitter'),
  'Variation must not trigger preview auto-fit');
assert.ok(ui.includes('if (!brushStrokePreviewDirty) return'));
assert.ok(ui.includes('EditorApplication.isCompiling || EditorApplication.isUpdating'));
assert.ok(ui.includes('brushStrokePreviewSchedule?.Pause()'));
assert.ok(ui.includes('ReleaseTransientResources()'));
assert.ok(read('src/TextureCompositorWindow.cs').includes('ReleaseBrushStrokePreview();'));
for(const forbidden of ['Undo.', 'AssetDatabase.', 'ReadPixels(', 'SyncSurfaceToTexture(', 'SaveAssets('])
  assert.ok(!(layer+ui).includes(forbidden), forbidden);
console.log('Brush stroke preview geometry/lifecycle/source checks passed (Unity/GPU not executed).');
