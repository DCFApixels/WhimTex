import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const src = read('TextureCompositorWindow.Guides.cs');
const ui = read('TextureCompositorWindow.UI.cs');
const view = read('PreviewViewport.cs');
const close = (a, b, epsilon = 1e-6) => assert.ok(Math.abs(a - b) < epsilon, `${a} vs ${b}`);
class V {
    constructor(x, y) { this.x = x; this.y = y; }
    get sqrMagnitude() { return this.x * this.x + this.y * this.y; }
}
const add = (a, b) => new V(a.x + b.x, a.y + b.y);
const sub = (a, b) => new V(a.x - b.x, a.y - b.y);
const mul = (a, s) => new V(a.x * s, a.y * s);
const dot = (a, b) => a.x * b.x + a.y * b.y;
function rotation(name, angle) {
    const m = view.match(new RegExp(`${name}\\(Vector2 delta\\) => new Vector2\\(\\s*([^,]+), ([^;]+)\\);`));
    const fn = new Function('cosine', 'sine', 'delta', 'V', `return new V(${m[1]}, ${m[2]});`);
    return delta => fn(Math.cos(angle), Math.sin(angle), delta, V);
}
const signature = src.indexOf('private static bool ClipLine(');
const start = src.indexOf('{', signature);
let depth = 1, end = start + 1;
for (; depth; end++) { if (src[end] === '{') depth++; if (src[end] === '}') depth--; }
const body = src.slice(start + 1, end - 1)
    .replace('a = b = default;', 'let a, b;')
    .replace('bool Axis(float origin, float delta, float min, float max)', 'function Axis(origin, delta, min, max)')
    .replace(/float (\w+) =/g, 'let $1 =')
    .replace(/float.NegativeInfinity/g, '-Infinity').replace(/float.PositiveInfinity/g, 'Infinity')
    .replace(/float.IsNaN/g, 'Number.isNaN').replace(/float.IsInfinity/g, 'infinite')
    .replace(/Mathf.Abs/g, 'Math.abs').replace(/Mathf.Min/g, 'Math.min').replace(/Mathf.Max/g, 'Math.max')
    .replace(/(\d)f\b/g, '$1')
    .replace('point + direction * first', 'add(point, mul(direction, first))')
    .replace('point + direction * last', 'add(point, mul(direction, last))')
    .replace('return true;', 'return [a, b];');
const clip = new Function('bounds', 'point', 'direction', 'add', 'mul', 'infinite', body);
const bounds = { xMin: 0, yMin: 0, xMax: 900, yMax: 600, width: 900, height: 600 };
const center = new V(450, 300);
const clipLine = (p, d) => clip(bounds, p, d, add, mul, v => Math.abs(v) === Infinity);
let checks = 0;
const alignmentExpression = src.match(/bool aligned = ([^;]+);/)[1];
const alignedToView = new Function('direction', `return ${alignmentExpression
    .replace(/Mathf.Min/g, 'Math.min').replace(/Mathf.Abs/g, 'Math.abs').replace(/(\d)f\b/g, '$1')};`);
for (const creation of [0, 17, 45, -90, 133, 179.9]) for (const vertical of [true, false]) {
    const angle = creation * Math.PI / 180;
    const forward = rotation('ToViewDelta', angle), inverse = rotation('ToCanvasDelta', angle);
    const normal = inverse(vertical ? new V(1, 0) : new V(0, 1));
    const direction = new V(-normal.y, normal.x);
    const screenDirection = forward(direction);
    close(vertical ? screenDirection.x : screenDirection.y, 0);
    assert.equal(alignedToView(screenDirection), true, 'New guides align with the current view at any canvas angle');
    for (const turn of [-180, -90, 0, 90, 180, 270, 360, 720]) {
        const rotated = rotation('ToViewDelta', (creation + turn) * Math.PI / 180)(direction);
        assert.equal(alignedToView(rotated), true, 'Quarter turns preserve the bright style');
        for (const offset of [-45, -.01, .01, 17, 89]) {
            const oblique = rotation('ToViewDelta', (creation + turn + offset) * Math.PI / 180)(direction);
            assert.equal(alignedToView(oblique), false, 'Style follows current view axes, not document axes');
            checks++;
        }
        checks++;
    }
    for (const scale of [.2, 1, 7]) for (const origin of [new V(-20, 60), new V(570, -910)]) {
        const toView = p => add(center, forward(sub(p, center)));
        const toCanvas = p => add(center, inverse(sub(p, center)));
        const pointer = new V(380, 260);
        const distance = dot(mul(sub(toCanvas(pointer), origin), 1 / scale), normal);
        const base = mul(normal, distance);
        for (const offset of [-900, 0, 500]) {
            const doc = add(base, mul(direction, offset));
            const screen = toView(add(origin, mul(doc, scale)));
            close(vertical ? screen.x : screen.y, vertical ? pointer.x : pointer.y);
            close(dot(doc, normal), distance);
            checks += 2;
        }
        for (const after of [0, 31, -71, 180]) {
            const nextRotation = rotation('ToViewDelta', after * Math.PI / 180);
            const nextDirection = nextRotation(direction);
            const expected = rotation('ToViewDelta', (after - creation) * Math.PI / 180)(screenDirection);
            close(nextDirection.x, expected.x); close(nextDirection.y, expected.y);
            const point = add(center, nextRotation(sub(add(origin, mul(base, scale)), center)));
            const segment = clipLine(point, nextDirection);
            if (segment) for (const endpoint of segment) {
                assert.ok(endpoint.x >= -.00001 && endpoint.x <= 900.00001 && endpoint.y >= -.00001 && endpoint.y <= 600.00001);
                close(dot(sub(endpoint, point), new V(-nextDirection.y, nextDirection.x)), 0);
            }
            checks += 2;
        }
    }
}
assert.deepEqual(clipLine(new V(400, 300), new V(0, 1)), [new V(400, 0), new V(400, 600)]);
assert.deepEqual(clipLine(new V(400, 300), new V(1, 0)), [new V(0, 300), new V(900, 300)]);
assert.equal(clipLine(new V(-10, 0), new V(0, 1)), false);
assert.equal(clipLine(new V(NaN, 0), new V(0, 1)), false);
assert.equal(clipLine(new V(0, Infinity), new V(1, 0)), false);
assert.equal(clipLine(new V(0, 0), new V(0, 0)), false);
assert.match(src, /ToCanvasDelta\(rail == 0 \? Vector2.right : Vector2.up\).normalized/);
assert.match(src, /Vector2.Dot\(\(point - canvas.ImageRect.position\) \/ canvas.PixelScale, normal\)/);
assert.match(src, /pending.position = PositionAt\(point, pending.normal\) \+ grabOffset/);
const cancel = src.split('internal void Cancel()')[1].split('private void Leave')[0];
assert.ok(cancel.indexOf('pointer = -1') < cancel.indexOf('ReleasePointer'));
assert.ok(!cancel.includes('previewGuides.Remove') && !cancel.includes('previewGuides.Add'));
const update = src.split('private void Update(Vector2 point)')[1].split('private void Move')[0];
assert.ok(!update.includes('previewGuides['), 'Drag preview must not change committed guides');
assert.match(src, /if \(discard\)\s*\{\s*owner.RememberPreviewGuides\(\);\s*owner.previewGuides.RemoveAt\(movingIndex\)/);
assert.match(src, /else if \(!discard && owner.previewGuides.Count < MaxPreviewGuides\)\s*\{\s*owner.RememberPreviewGuides\(\);\s*owner.previewGuides.Add\(pending\)/);
assert.match(src, /!control && !alt/);
const guideToolPolicy = src.match(/private bool CanMovePreviewGuides => ([\s\S]*?);/)[1];
const canInteract = new Function('previewTool', `return ${guideToolPolicy.replace(/PreviewTool\.(\w+)/g, '"$1"')};`);
const toolsSource = read('TextureCompositorWindow.Tools.cs');
const toolNames = toolsSource.match(/enum PreviewTool\s*\{([^}]+)\}/)[1].split(',').map(name => name.trim());
for (const tool of toolNames)
    assert.equal(canInteract(tool), ['None', 'Transform', 'Zoom'].includes(tool), `${tool}: guide interaction policy`);
assert.equal(canInteract('Unknown'), false);
const beginPolicy = src.match(/private bool CanGrab\(bool control, bool alt\) => ([\s\S]*?);/)[1];
assert.ok(!beginPolicy.includes('CanMovePreviewGuides'), 'Creating a guide must not be restricted by the selected tool');
assert.match(src, /private int Hit\(Vector2 point\)\s*\{\s*if \(!owner.CanMovePreviewGuides \|\|/);
const continuePolicy = src.match(/private bool CanContinueDrag => ([\s\S]*?);/)[1];
const canContinue = new Function('Ready', 'movingIndex', 'owner', `return ${continuePolicy};`);
for (const tool of toolNames) {
    const owner = { CanMovePreviewGuides: canInteract(tool) };
    assert.equal(canContinue(true, -1, owner), true, `${tool}: new guide can be placed`);
    assert.equal(canContinue(true, 0, owner), canInteract(tool), `${tool}: moving an existing guide`);
    assert.equal(canContinue(false, -1, owner), false, 'Missing canvas cancels creation');
    owner.previewGuidesLocked = true;
    assert.equal(canContinue(true, 0, owner), false, 'Locked guides cannot be moved');
    assert.equal(canContinue(true, -1, owner), true, 'Locking existing guides does not prohibit creating a new one');
    owner.previewGuidesLocked = false;
    owner.previewGuidesHidden = true;
    assert.equal(canContinue(true, 0, owner), false, 'Hidden guides cannot be moved');
}
assert.match(src, /WantsCursor\(Vector2 point, bool alt\) => IsDragging \|\|/);
assert.match(src, /CanGrab\(controlHeld, alt\) && \(RailAt\(point\) >= 0 \|\| Hit\(point\) >= 0\)/);
assert.ok(!src.includes('rail.pickingMode ='), 'Edge strips remain interactive for every tool');
const down = src.split('private void Down(PointerDownEvent evt)')[1].split('private void Update(Vector2 point)')[0];
assert.ok(!down.includes('CanMovePreviewGuides'), 'Creation must not be blocked before the rail hit test');
assert.match(down, /int hit = rail < 0 \? Hit\(point\) : -1;/);
assert.match(src, /if \(!CanContinueDrag \|\|/);
assert.match(src, /if \(CanContinueDrag\)/);
assert.match(src, /bounds, owner.CanMovePreviewGuides && !owner.previewGuidesLocked &&\s*\(i == hovered \|\| i == owner.selectedPreviewGuide\), false/);
assert.ok(!src.match(/private bool Ready => ([\s\S]*?);/)[1].includes('CanMovePreviewGuides'), 'Guide visibility must not depend on the selected tool');
const setTool = read('TextureCompositorWindow.Transform.cs').split('private void SetPreviewTool(PreviewTool tool)')[1].split('private bool HandlePreviewTransformKey')[0];
assert.ok(setTool.indexOf('CancelPreviewZoomGesture();') < setTool.indexOf('previewTool = tool;'), 'Switching tools must cancel an uncommitted guide drag');
assert.match(read('TextureCompositorWindow.Zoom.cs'), /CancelPreviewZoomGesture\(\)\s*\{\s*previewGuideManipulator\?\.Cancel\(\);/);
assert.match(src, /owner.areaSelectionManipulator\?\.HasGesture/);
for (const [event, callback] of [['PointerDown', 'Down'], ['PointerMove', 'Move'], ['PointerUp', 'Up'], ['Wheel', 'Wheel']])
    for (const op of ['Register', 'Unregister'])
        assert.ok(src.includes(`${op}Callback<${event}Event>(${callback}, TrickleDown.TrickleDown)`));
assert.ok(ui.indexOf('BuildPreviewGuides();') < ui.indexOf('BuildPreviewZoomTool();'));
assert.match(ui, /KeyCode.Escape && previewGuideManipulator\?\.IsDragging == true/);
assert.match(read('TextureCompositorWindow.cs'), /ClearPreviewGuides\(\);\s*previewGuidesDocument = next;\s*compositor = next/);
assert.ok(!/\bUndo\.|RenderTexture|MarkChanged|SetDirty/.test(src), 'Guides remain window-local and outside the render/Undo pipeline');
assert.match(src, /sprite-editor-preview-surface/);
assert.match(src, /ViewChanged \+= previewGuideOverlay.MarkDirtyRepaint/);
const colorBody = src.match(/Color lineColor = ([\s\S]*?)\s*for \(int pass/)[1];
const evaluateGuideColor = new Function('aligned', 'highlight', 'deleting', 'Color', 'SpriteEditorUserSettings',
    `let lineColor = ${colorBody.replace(/(\d)f\b/g, '$1')} return lineColor;`);
class Color { constructor(r, g, b, a) { Object.assign(this, { r, g, b, a }); } }
const palette = {
    get GuideAlignedColor() { return new Color(.2, .85, 1, 1); },
    get GuideAngledColor() { return new Color(.5, .7, .8, 1); },
    get GuideActiveColor() { return new Color(1, .6, .2, 1); },
};
const guideColor = (aligned, highlight, deleting, Color) => evaluateGuideColor(aligned, highlight, deleting, Color, palette);
assert.ok(guideColor(true, false, false, Color).a > guideColor(false, false, false, Color).a);
for (const aligned of [false, true]) {
    assert.equal(guideColor(aligned, true, false, Color).a, 1, 'Hover/selection remains visible');
    assert.deepEqual(guideColor(aligned, true, false, Color), palette.GuideActiveColor, 'Active guide uses the configured color');
    assert.deepEqual(guideColor(aligned, true, true, Color), new Color(1, .35, .25, .9), 'Deletion color takes priority');
}
const styles = read('SpriteEditorSplitView.uss');
assert.match(styles, /\.sprite-editor-guide-rail--left\s*\{[^}]*width: 8px;/);
assert.match(styles, /\.sprite-editor-guide-rail--top\s*\{[^}]*height: 8px;/);
console.log(`Preview guides: ${checks} angle/position checks, extracted line clipping and input/lifecycle contracts passed (Unity not executed).`);
