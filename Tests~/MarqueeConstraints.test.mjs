import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const source = read('TextureCompositorWindow.AreaSelectionView.cs');
function body(signature, next) {
    const block = source.split(signature)[1].split(next)[0];
    return block.slice(block.indexOf('{') + 1, block.lastIndexOf('}'));
}
class Vector2 { constructor(x, y) { this.x = x; this.y = y; } }
const geometry = body('private static Vector2 ConstrainMarquee(', 'private void UpdateCurrent(')
    .replace('Vector2 delta = end - start;', 'const delta = {x:end.x-start.x, y:end.y-start.y};')
    .replace('float size =', 'const size =')
    .replace(/Mathf.Max/g, 'Math.max').replace(/Mathf.Abs/g, 'Math.abs')
    .replace('return start + new Vector2(', 'return new Vector2(')
    .replace('delta.x <', 'start.x + (delta.x <').replace('-size : size,', '-size : size),')
    .replace('delta.y <', 'start.y + (delta.y <').replace('-size : size);', '-size : size));')
    .replace(/0f/g, '0');
const constrain = new Function('start', 'end', 'Vector2', geometry);
let checks = 0;
for (let x = -20; x <= 20; x++) for (let y = -20; y <= 20; y++) {
    const start = {x:2.5, y:-7.25}, end = {x:start.x+x, y:start.y+y};
    const value = constrain(start, end, Vector2);
    assert.equal(Math.abs(value.x-start.x), Math.abs(value.y-start.y));
    assert.equal(Math.abs(value.x-start.x), Math.max(Math.abs(x), Math.abs(y)));
    checks++;
}
const update = body('private void UpdateCurrent(', 'private void KeyDown(')
    .replace(/\b(pointerPosition|shiftStartsCombine|Current|RectangleDragging|Start)\b/g, 'state.$1');
const apply = new Function('state', 'position', 'shift', 'control', 'CanvasPoint', 'ConstrainMarquee', 'owner', update);
const state = {Start:new Vector2(0,0), RectangleDragging:true, shiftStartsCombine:false};
const point = new Vector2(9, 4);
const updateState = shift => apply(state, point, shift, true, p=>p,
    (a,b)=>constrain(a,b,Vector2), {areaSelectionOverlay:{MarkDirtyRepaint(){}}});
updateState(true); assert.deepEqual(state.Current, new Vector2(9,9));
updateState(false); assert.deepEqual(state.Current, point);
state.shiftStartsCombine = true;
updateState(true); assert.deepEqual(state.Current, point);
updateState(false); updateState(true); assert.deepEqual(state.Current, new Vector2(9,9));
state.RectangleDragging = false;
updateState(true); assert.deepEqual(state.Current, point);
assert.equal(source.match(/UpdateCurrent\(evt.localPosition, evt.shiftKey, evt.ctrlKey\)/g).length, 2);
for (const event of ['KeyDown','KeyUp']) {
    assert.ok(source.includes(`RegisterCallback<${event}Event>`));
    assert.ok(source.includes(`UnregisterCallback<${event}Event>`));
}
assert.match(read('TextureCompositorWindow.Tools.cs'), /tool == PreviewTool.Shape \|\| tool == PreviewTool.RectangleSelect\)\s*button.Add\(new ToolDropdownMarker\(\)\)/);
assert.match(read('WhimTexSplitView.uss'), /\.whimtex-tool-dropdown-marker \{\s*position: absolute;\s*right: 1px;\s*bottom: 1px;/);
console.log(`Marquee constraints: ${checks} geometry cases, Shift transitions, pointer-up and dropdown marker contracts passed.`);
