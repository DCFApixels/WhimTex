// Scalar/source checks only. Does not compile or open Unity.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const source = read('src/TextureCompositorWindow.LayerAutoScroll.cs');
const body = source.match(/static float EdgeSpeed\(Rect viewport, Vector2 point\)\s*\{([^}]+)\}/)[1]
  .replace(/float /g, 'let ').replace(/(\d)f\b/g, '$1').replace(/Mathf.Min/g, 'Math.min');
const speed = new Function('viewport', 'point', body);
const clampBody = source.match(/static float ClampOffset\(float value, float low, float high\)\s*\{([^}]+)\}/)[1]
  .replace(/float /g, 'let ').replace(/(\d)f\b/g, '$1').replace(/Mathf.Max/g, 'Math.max')
  .replace(/Mathf.Clamp/g, 'clamp');
const offset = new Function('value','low','high','clamp',clampBody);
const clamp = (x,a,b) => x < a ? a : x > b ? b : x;
for (const high of [-1000,-242,-1,0,1,300]) for (const value of [-900,-1,0,8,1000]) {
  const actual = offset(value,0,high,clamp);
  assert.ok(actual >= 0 && actual <= Math.max(0,high));
}
assert.equal(offset(8,0,-242,clamp),0,'Short lists stay at the top');
assert.equal(offset(208,0,300,clamp),208,'Long lists still scroll');
function rect(x, y, width, height) {
  return { width, height, yMin:y, yMax:y+height,
    Contains: p => p.x >= x && p.x < x+width && p.y >= y && p.y < y+height };
}
const view = rect(100,200,300,400);
const belowBody = source.match(/static bool IsBelowLayerList\(Rect viewport, float endY, Vector2 point\) =>\s*([^;]+);/)[1];
const below = new Function('viewport','endY','point', `return ${belowBody};`);
for (const endY of [200, 300, 600, 800]) {
  for (const point of [{x:150,y:199},{x:150,y:200},{x:150,y:299},{x:150,y:300},{x:150,y:599},{x:150,y:600},{x:99,y:400},{x:400,y:400}])
    assert.equal(below(view,endY,point),view.Contains(point) && point.y >= endY);
}
assert.equal(below(view,300,{x:150,y:450}),true,'Empty viewport space accepts bottom insertion');
assert.equal(below(view,800,{x:150,y:599}),false,'A scrolled long list must not treat rows as background');
assert.match(source,/owner.PerformLayerDrop\(dragged, owner.compositor.layers, owner.compositor.layers.Count, null\)/);
assert.match(source,/finally \{ owner.ClearLayerDragData\(\); \}/);
assert.match(source,/if \(owner.UpdateLayerListEndDrop\(pointer\)\) return/,'Auto-scroll preserves bottom insertion feedback');
assert.match(read('src/TextureCompositorWindow.UI.cs'),/contentViewport.AddManipulator\(new LayerListEndDropManipulator\(this\)\)/);
assert.equal(speed(view, {x:150,y:200}), -480);
assert.equal(speed(view, {x:150,y:216}), -240);
assert.equal(speed(view, {x:150,y:232}), 0);
assert.equal(speed(view, {x:150,y:400}), 0);
assert.equal(speed(view, {x:150,y:568}), 0);
assert.equal(speed(view, {x:150,y:584}), 240);
assert.equal(speed(view, {x:150,y:599}), 465);
for (const p of [{x:99,y:201},{x:400,y:599},{x:150,y:199},{x:150,y:600}])
  assert.equal(speed(view,p),0,'No scrolling over header/footer/outside list');
for (const height of [0,1,20,64,400]) {
  const r=rect(0,0,100,height);
  assert.equal(speed(r,{x:50,y:height/2}),0);
  for(let i=0;i<100;i++) assert.ok(Math.abs(speed(r,{x:50,y:height*i/100}))<=480);
}
for (const event of ['DragUpdatedEvent','DragPerformEvent','DragExitedEvent']) {
  assert.ok(source.includes(`RegisterCallback<${event}>`));
  assert.ok(source.includes(`UnregisterCallback<${event}>`));
}
assert.match(source,/RegisterCallback<DragUpdatedEvent>\(OnDragUpdated, TrickleDown.TrickleDown\)/);
assert.ok(source.includes('target.schedule.Execute(Tick).Every(16)'));
assert.ok(source.includes('timer?.Pause()'));
assert.ok(source.includes('owner.GetDraggedLayer() == null'));
assert.ok(source.includes('target.panel.Pick(pointer)'));
assert.ok(source.includes('owner.TryGetToolkitDrop('));
assert.ok(source.includes('scroll.verticalScroller.lowValue, scroll.verticalScroller.highValue'));
assert.ok(read('src/TextureCompositorWindow.cs').includes('layerDragAutoScroll?.Stop();'));
assert.ok(read('src/TextureCompositorWindow.UI.cs').includes('new LayerDragAutoScrollManipulator(this, toolkitSettingsScroll)'));
console.log('Layer auto-scroll scalar/source checks passed (Unity UI not executed).');
