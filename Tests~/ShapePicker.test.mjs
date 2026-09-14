import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const source = read('TextureCompositorWindow.ShapePicker.cs');
const itemBody = source.split('private int ItemAt(Vector2 local)')[1].split('private void UpdateHover')[0];
const itemAt = new Function('local', 'Inset', 'ItemSize', 'Kinds', 'Mathf',
    itemBody.slice(itemBody.indexOf('{') + 1, itemBody.lastIndexOf('}')).replace(/-1;/g, '-1;'));
let checks = 0;
for (const count of [2, 5]) for (let x = -5; x <= 40; x += .5) for (let y = -5; y <= 165; y += .5) {
    const result = itemAt({x,y}, 3, 30, {Length:count}, {FloorToInt:Math.floor});
    const expected = x >= 3 && x < 33 && y >= 3 && y < 3 + count * 30 ? Math.floor((y - 3) / 30) : -1;
    assert.equal(result, expected);
    checks++;
}
// Execute cleanup itself with a synchronous capture-out callback, as Unity can do.
let cancelBody = source.split('internal void Cancel()')[1].split('private void Key')[0];
cancelBody = cancelBody.slice(cancelBody.indexOf('{') + 1, cancelBody.lastIndexOf('}'))
    .replace('int captured = pointer;', 'let captured = pointer;')
    .replace('Array.Clear(items, 0, items.Length);', 'items.fill(null);')
    .replace(/root.UnregisterCallback<\w+>\([^;]+;/g, 'root.unregister();')
    .replace(/\b(pointer|hold|menu|items|hovered|root|target)\b/g, 'state.$1');
const cancel = new Function('state', cancelBody);
const calls = [];
const state = { pointer: 7, hovered: 2, items:[1,2,3,4,5],
    hold:{Pause:()=>calls.push('pause')}, menu:{RemoveFromHierarchy:()=>calls.push('remove')},
    root:{unregister:()=>calls.push('unregister')},
    target:{HasPointerCapture:id=>id===7, ReleasePointer:id=>{ assert.equal(state.pointer,-1); calls.push('release'); }} };
cancel(state); cancel(state);
assert.equal(state.pointer,-1); assert.equal(state.hovered,-1);
assert.equal(state.hold,null); assert.equal(state.root,null); assert.equal(state.menu,null);
assert.deepEqual(state.items,[null,null,null,null,null]);
assert.deepEqual(calls,['pause','remove','unregister','unregister','release']);
assert.match(source,/HoldMilliseconds = 160/);
assert.match(source,/schedule.Execute\(Open\).StartingIn\(HoldMilliseconds\)/);
assert.match(source,/sqrMagnitude >= DragDistance \* DragDistance\) Open\(\)/);
assert.match(source,/evt.pressedButtons & 1\) == 0\) Cancel\(\)/);
assert.match(source,/target.HasPointerCapture\(pointer\)/);
assert.match(source,/Cancel\(\);\s*if \(selection >= 0\)/);
assert.match(source,/if \(selection >= 0 \|\| click\) owner.SetPreviewTool/);
assert.match(source,/bool click = menu == null && target.worldBound.Contains\(current\)/);
assert.match(source,/root.WorldToLocal\(current\) - menuPosition/);
for (const event of ['PointerDown','PointerMove','PointerUp','PointerCaptureOut','PointerCancel','DetachFromPanel','GeometryChanged','KeyDown']) {
    assert.ok(source.includes(`RegisterCallback<${event}Event>`));
    assert.ok(source.includes(`UnregisterCallback<${event}Event>`));
}
assert.match(read('TextureCompositorWindow.Zoom.cs'),/shapePicker\?\.Cancel\(\)/);
assert.match(read('TextureCompositorWindow.Tools.cs'),/shapeToolIcon\?\.SetKind/);
assert.match(read('TextureCompositorWindow.Shapes.cs'),/shapeToolIcon\?\.SetKind/);
const styles = read('WhimTexSplitView.uss');
assert.match(styles,/\.whimtex-shape-picker \{\s*position: absolute;\s*width: 36px;\s*padding: 2px;\s*border-width: 1px;/);
assert.match(styles,/\.whimtex-shape-picker-item \{\s*width: 30px;\s*height: 30px;/);
console.log(`Shape picker: ${checks} extracted hit tests, cleanup/reentrancy and interaction contracts passed (native pointer input not simulated).`);
