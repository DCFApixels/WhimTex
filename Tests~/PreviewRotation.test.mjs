import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');
const viewport = read('PreviewViewport.cs');
const ui = read('TextureCompositorWindow.UI.cs');
const zoom = read('TextureCompositorWindow.Zoom.cs');
const transform = read('TextureCompositorWindow.Transform.cs');
const selection = read('TextureCompositorWindow.AreaSelectionView.cs');

// Evaluate the actual scalar expressions from the C# rotation helpers, not a second matrix.
function deltaMethod(name) {
    const match = viewport.match(new RegExp(`${name}\\(Vector2 delta\\) => new Vector2\\(\\s*([^,]+), ([^;]+)\\);`));
    assert.ok(match, name);
    return new Function('cosine', 'sine', 'delta', `return [${match[1]}, ${match[2]}];`);
}
const forward = deltaMethod('ToViewDelta'), inverse = deltaMethod('ToCanvasDelta');
const close = (a, b, tolerance = 1e-7) => a.forEach((v, i) => assert.ok(Math.abs(v - b[i]) < tolerance, `${a} vs ${b}`));
const add = (a, b) => a.map((v, i) => v + b[i]);
const sub = (a, b) => a.map((v, i) => v - b[i]);
const mul = (a, b) => a.map((v, i) => v * b[i]);
const div = (a, b) => a.map((v, i) => v / b[i]);
const constrainSource = ui.split('private Vector2 ConstrainPaintingPosition(')[1].split('private void OnPreviewPointerUp(')[0];
const constrainBody = constrainSource.slice(constrainSource.indexOf('{') + 1, constrainSource.lastIndexOf('}'))
    .replace('SetPaintingShift(shift);', '')
    .replace(/(?:Rect|Vector2) (\w+) =/g, 'let $1 =')
    .replace('position - paintingAxisPointerAnchor', 'new Vector2(position.x - state.pointerAnchor.x, position.y - state.pointerAnchor.y)')
    .replace(/paintingAxisAnchor/g, 'state.anchor').replace(/paintingLockedAxis/g, 'state.axis')
    .replace(/Mathf.Abs/g, 'Math.abs').replace(/(\d)f\b/g, '$1');
const constrain = new Function('position', 'shift', 'toolkitPreviewCanvas', 'state', 'Vector2', constrainBody);
class Point {
    constructor(x, y) { this.x = x; this.y = y; }
    get sqrMagnitude() { return this.x * this.x + this.y * this.y; }
}
let checks = 0;
for (const angle of [0, 17, 45, 90, 133, 179.9, -90, -178, 270, 1081]) {
    const c = Math.cos(angle * Math.PI / 180), s = Math.sin(angle * Math.PI / 180);
    const rotate = (fn, a) => fn(c, s, { x: a[0], y: a[1] });
    for (const extent of [[808, 408], [100, 2000], [1920, 1080]]) {
        const pivot = mul(extent, [.5, .5]);
        const toView = a => add(pivot, rotate(forward, sub(a, pivot)));
        const toCanvas = a => add(pivot, rotate(inverse, sub(a, pivot)));
        for (const uv of [[.3, .6], [-1.2, 2.4]]) for (const size of [[672, 336], [1700, 850]]) {
            const image = { x: -37, y: 51, width: size[0], height: size[1] };
            const canvas = { ImageRect: image, ToView: p => new Point(...toView([p.x, p.y])) };
            const anchor = canvas.ToView(new Point(image.x + uv[0] * size[0], image.y + (1 - uv[1]) * size[1]));
            for (const delta of [[30, 4], [-30, 4], [4, 30], [4, -30]]) {
                const state = { anchor: new Point(...uv), pointerAnchor: anchor, axis: 0 };
                const position = new Point(anchor.x + delta[0], anchor.y + delta[1]);
                const result = constrain(position, true, canvas, state, Point);
                const horizontal = Math.abs(delta[0]) >= Math.abs(delta[1]);
                assert.equal(state.axis, horizontal ? 1 : 2);
                close([result.x, result.y], horizontal ? [position.x, anchor.y] : [anchor.x, position.y]);
                const changedDirection = constrain(new Point(anchor.x + 90, anchor.y + 110), true, canvas, state, Point);
                assert.equal(horizontal ? changedDirection.y : changedDirection.x, horizontal ? anchor.y : anchor.x);
                assert.equal(constrain(position, false, canvas, state, Point), position);
                state.axis = 0;
                const near = constrain(new Point(anchor.x + .5, anchor.y + .5), true, canvas, state, Point);
                close([near.x, near.y], [anchor.x, anchor.y]);
                assert.equal(state.axis, 0);
                state.pointerAnchor = new Point(anchor.x + 7, anchor.y);
                const snappedStart = constrain(new Point(anchor.x + 7, anchor.y + 3), true, canvas, state, Point);
                assert.equal(state.axis, 2, 'Guide attraction must not bias Shift direction toward the raw-to-snapped offset');
                close([snappedStart.x, snappedStart.y], [anchor.x, anchor.y + 3]);
                checks += 6;
            }
        }
        for (const point of [[0, 0], [203, 116], [-1000, 2000], pivot]) {
            close(toCanvas(toView(point)), point);
            const size = [672, 336], position = [-37, 51], nextSize = mul(size, [1.2, 1.2]);
            const anchor = toCanvas(point), uv = div(sub(anchor, position), size);
            // Same anchored-zoom and center formula as PreviewViewport.ZoomAt.
            const center = add(uv, div(sub(pivot, anchor), nextSize));
            const nextPosition = sub(pivot, mul(center, nextSize));
            close(toView(add(nextPosition, mul(uv, nextSize))), point);
            const pan = [37, -21];
            close(sub(toView(add(position, rotate(inverse, pan))), toView(position)), pan);
            // A rotated surface is positioned by its rotated center, then rotated locally.
            const surfaceCenter = add(position, mul(size, [.5, .5]));
            close(add(toView(surfaceCenter), rotate(forward, sub(point, surfaceCenter))), toView(point));
            checks += 4;
        }
        const corners = [[0, 0], [extent[0], 0], extent, [0, extent[1]]];
        const canvasCorners = corners.map(toCanvas);
        for (const [u, v] of [[0, 0], [.3, .7], [.5, .5], [1, 1]]) {
            // Tiled UVs are affine: a single full-viewport quad works at every angle.
            const interpolated = add(canvasCorners[0], add(
                mul(sub(canvasCorners[1], canvasCorners[0]), [u, u]),
                mul(sub(canvasCorners[3], canvasCorners[0]), [v, v])));
            close(interpolated, toCanvas(mul(extent, [u, v])));
            checks++;
        }
    }
}
assert.match(viewport, /anchor = ToCanvas\(viewport, anchor\)/);
assert.match(viewport, /selectionCenter = ToCanvas\(viewport, selection.center\)/);
assert.match(viewport, /delta = ToCanvasDelta\(delta\)/);
assert.match(viewport, /Mathf.Round\(degrees \/ 90f\) \* 90f/);
assert.match(viewport, /Mathf.Abs\(degrees - nearest\) <= 3f/);
assert.match(zoom, /rotating = panning && evt.shiftKey/);
assert.match(zoom, /new FloatField\("Angle °"\)\s*\{\s*isDelayed = true/);
assert.match(zoom, /SetPreviewRotation\(evt.newValue\);\s*previewRotationField.SetValueWithoutNotify\(previewViewport.Rotation\)/);
assert.match(zoom, /SetViewRotation\(degrees, snap: false\)/);
assert.match(zoom, /!HasPreviewLayers \|\| float.IsNaN\(degrees\) \|\| float.IsInfinity\(degrees\)/);
assert.match(zoom, /previewRotationField.SetValueWithoutNotify\(displayedPreviewRotation\)/);
const styles = read('WhimTexSplitView.uss');
assert.match(styles, /\.whimtex-view-field\s*\{\s*width: 130px;/);
assert.match(styles, /\.whimtex-view-field > \.unity-base-field__label\s*\{\s*min-width: 0;/);
assert.match(zoom, /new FloatField\("Zoom %"\)\s*\{\s*isDelayed = true/);
assert.match(zoom, /SetPreviewZoomPercent\(evt.newValue\);\s*previewZoomPercent.SetValueWithoutNotify\(toolkitPreviewCanvas.PixelScale \* 100f\)/);
assert.match(zoom, /percent <= 0f \|\| float.IsNaN\(percent\) \|\| float.IsInfinity\(percent\)/);
assert.match(zoom, /ZoomAt\(toolkitPreviewCanvas.contentRect.center, percent \/ 100f\)/);
assert.match(zoom, /previewZoomPercent.SetValueWithoutNotify\(scale \* 100f\)/);
assert.match(zoom, /freeRotation \+= Vector2.SignedAngle\(from, to\)/);
assert.match(zoom, /disableSnap \? freeRotation : owner.SnapPreviewGuideRotation\(freeRotation, includeCanvasAxes: true\)/);
assert.match(zoom, /SetViewRotation\(rotation, snap: false\)/);
assert.match(zoom, /RotateTo\(point, evt.ctrlKey\)/);
assert.match(zoom, /if \(!owner.HasPreviewLayers/);
assert.match(zoom, /pointerId = -1;\s*if \(captured >= 0/);
for (const event of ['Down', 'Move', 'Up'])
    for (const action of ['Register', 'Unregister'])
        assert.ok(zoom.includes(`${action}Callback<Pointer${event}Event>(On${event}, TrickleDown.TrickleDown)`));
assert.match(ui, /presentedRotation == viewport.Rotation/);
assert.match(ui, /PositionSurface\(checker, presentationRect, !tiled\)/);
assert.match(ui, /PositionSurface\(image, ImageRect, true\)/);
assert.match(ui, /rect.position = ToView\(rect.center\) - rect.size \* 0.5f/);
assert.match(ui, /Vector2 canvasCursor = ToCanvas\(cursorPosition\)/);
assert.match(ui, /x = viewport.ToViewDelta\(x\)/);
assert.match(ui, /y = viewport.ToViewDelta\(y\)/);
assert.match(constrainSource, /anchor = toolkitPreviewCanvas.ToView\(new Vector2/);
assert.ok(!constrainSource.includes('ToCanvas'), 'Shift constrains in screen space before the shared painting conversion');
assert.match(read('TextureCompositorWindow.cs'), /mousePosition = toolkitPreviewCanvas.ToCanvas\(mousePosition\)/);
assert.match(read('TextureCompositorWindow.Tiling.cs'), /ImageRect.Contains\(toolkitPreviewCanvas.ToCanvas\(position\)\)/);
assert.match(transform, /lastPointerPosition = point;\s*point = owner.toolkitPreviewCanvas.ToCanvas\(point\)/);
assert.match(selection, /point = owner.toolkitPreviewCanvas.ToCanvas\(point\)/);
assert.match(selection, /Rect bounds = owner.toolkitPreviewCanvas.VisibleCanvasBounds/);
assert.match(selection, /PreviewPoint\(new Vector2\(gesture.Current.x, gesture.Start.y\), image\)/);
assert.ok(!/RenderTexture|Undo\.|SetDirty|SerializeField/.test(viewport));
console.log(`Preview rotation: ${checks} geometry checks and integration source checks passed (Unity/UI not executed).`);
