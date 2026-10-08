// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('CanvasViewGuides: source/scalar contracts');
const assert = context.assert;

context.case('CanvasViewGuides original assertions and branches', async () => {
    const read = p => readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
    const src = read('WhimTexWindow.Guides.cs');
    const ui = read('WhimTexWindow.UI.cs');
    const view = read('CanvasViewport.cs');
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
                
            }
            
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
    assert.ok(!cancel.includes('canvasGuides.Remove') && !cancel.includes('canvasGuides.Add'));
    const update = src.split('private void Update(Vector2 point)')[1].split('private void Move')[0];
    assert.ok(!update.includes('canvasGuides['), 'Drag preview must not change committed guides');
    assert.match(src, /if \(discard\)\s*\{\s*owner.RememberCanvasGuides\(\);\s*owner.canvasGuides.RemoveAt\(movingIndex\)/);
    assert.match(src, /else if \(!discard && owner.canvasGuides.Count < MaxCanvasGuides\)\s*\{\s*owner.RememberCanvasGuides\(\);\s*owner.canvasGuides.Add\(pending\)/);
    assert.match(src, /!control && !alt/);
    const guideToolPolicy = src.match(/private bool CanMoveCanvasGuides => ([\s\S]*?);/)[1];
    const contextTools = read('WhimTexWindow.ContextTools.cs');
    const temporaryPolicy = contextTools.match(/private static bool IsTemporaryCanvasTool\(CanvasTool tool\) =>\s*([\s\S]*?);/)[1];
    const isTemporary = new Function('tool', `return ${temporaryPolicy.replace(/CanvasTool\.(\w+)/g, '"$1"')};`);
    const evaluatePolicy = new Function('canvasTool', 'IsTemporaryCanvasTool', `return ${guideToolPolicy.replace(/CanvasTool\.(\w+)/g, '"$1"')};`);
    const canInteract = tool => evaluatePolicy(tool, isTemporary);
    const toolsSource = read('WhimTexWindow.Tools.cs');
    const toolNames = toolsSource.match(/enum CanvasTool\s*\{([^}]+)\}/)[1].split(',').map(name => name.trim());
    for (const tool of toolNames)
        assert.equal(canInteract(tool), ['None', 'Transform', 'Zoom', 'FXTransform', 'FXPoint', 'FXNormal'].includes(tool), `${tool}: guide interaction policy`);
    assert.equal(canInteract('Unknown'), false);
    const beginPolicy = src.match(/private bool CanGrab\(bool control, bool alt\) => ([\s\S]*?);/)[1];
    assert.ok(!beginPolicy.includes('CanMoveCanvasGuides'), 'Creating a guide must not be restricted by the selected tool');
    for (const manipulator of ['pointManipulator', 'normalManipulator'])
        assert.ok(beginPolicy.includes(`!(owner.${manipulator}?.IsDragging ?? false)`), 'An active FX parameter drag blocks guide capture');
    assert.match(src, /private int Hit\(Vector2 point, bool toolPriority = true\)\s*\{\s*if \(!owner.CanMoveCanvasGuides \|\|/);
    const guideHit = src.split('private int Hit(Vector2 point, bool toolPriority = true)')[1].split('internal bool WantsCursor')[0];
    assert.ok(guideHit.indexOf('owner.CanvasToolWantsPointer(point)') < guideHit.indexOf('for ('),
        'Guide hover yields to any active tool before selecting a guide');
    const transformSource = read('WhimTexWindow.Transform.cs');
    const priority = transformSource.split('internal bool WantsPointer(Vector2 point)')[1].split('private void OnDown')[0];
    assert.match(priority, /!owner.IsCanvasTransformEnabled/);
    assert.match(priority, /HitTest\(owner.toolkitCanvas.ToCanvas\(point\), owner.CurrentCanvasTransform/);
    assert.match(priority, /owner.CanvasFXParameter == null/);
    assert.match(priority, /return hit >= 0;/);
    for (const tool of ['canvasTransformManipulator', 'pointManipulator', 'normalManipulator'])
        assert.ok(src.includes(`${tool}?.WantsPointer(point) == true`), 'Guide cursor respects each temporary tool hit-test');
    const continuePolicy = src.match(/private bool CanContinueDrag => ([\s\S]*?);/)[1];
    const canContinue = new Function('Ready', 'movingIndex', 'owner', `return ${continuePolicy};`);
    for (const tool of toolNames) {
        const owner = { CanMoveCanvasGuides: canInteract(tool) };
        assert.equal(canContinue(true, -1, owner), true, `${tool}: new guide can be placed`);
        assert.equal(canContinue(true, 0, owner), canInteract(tool), `${tool}: moving an existing guide`);
        assert.equal(canContinue(false, -1, owner), false, 'Missing canvas cancels creation');
        owner.canvasGuidesLocked = true;
        assert.equal(canContinue(true, 0, owner), false, 'Locked guides cannot be moved');
        assert.equal(canContinue(true, -1, owner), true, 'Locking existing guides does not prohibit creating a new one');
        owner.canvasGuidesLocked = false;
        owner.canvasGuidesHidden = true;
        assert.equal(canContinue(true, 0, owner), false, 'Hidden guides cannot be moved');
    }
    assert.match(src, /WantsCursor\(Vector2 point, bool alt\) => IsDragging \|\|/);
    assert.match(src, /CanGrab\(controlHeld, alt\) && \(RailAt\(point\) >= 0 \|\| Hit\(point\) >= 0\)/);
    assert.ok(!src.includes('rail.pickingMode ='), 'Edge strips remain interactive for every tool');
    const down = src.split('private void Down(PointerDownEvent evt)')[1].split('private void Update(Vector2 point)')[0];
    assert.ok(!down.includes('CanMoveCanvasGuides'), 'Creation must not be blocked before the rail hit test');
    assert.match(down, /int hit = rail < 0 \? Hit\(point, evt.button == 0\) : -1;/);
    assert.match(src, /if \(!CanContinueDrag \|\|/);
    assert.match(src, /if \(CanContinueDrag\)/);
    assert.match(src, /bounds, owner.CanMoveCanvasGuides && !owner.canvasGuidesLocked &&\s*\(i == hovered \|\| i == owner.selectedCanvasGuide\), false/);
    assert.ok(!src.match(/private bool Ready => ([\s\S]*?);/)[1].includes('CanMoveCanvasGuides'), 'Guide visibility must not depend on the selected tool');
    const setTool = read('WhimTexWindow.ContextTools.cs').split('private void ChangeCanvasTool(CanvasTool tool)')[1];
    assert.ok(setTool.indexOf('CancelCanvasZoomGesture();') >= 0 && setTool.indexOf('CancelCanvasZoomGesture();') < setTool.indexOf('canvasTool = tool;'), 'Switching tools must cancel an uncommitted guide drag');
    assert.match(read('WhimTexWindow.Zoom.cs'), /CancelCanvasZoomGesture\(\)\s*\{\s*canvasGuideManipulator\?\.Cancel\(\);/);
    assert.match(src, /owner.areaSelectionManipulator\?\.HasGesture/);
    for (const [event, callback] of [['PointerDown', 'Down'], ['PointerMove', 'Move'], ['PointerUp', 'Up']])
        for (const op of ['Register', 'Unregister'])
            assert.ok(src.includes(`${op}Callback<${event}Event>(${callback})`), 'Guide gestures use the final bubble-phase fallback');
    for (const op of ['Register', 'Unregister'])
        assert.ok(src.includes(`${op}Callback<WheelEvent>(Wheel, TrickleDown.TrickleDown)`));
    assert.ok(ui.indexOf('BuildCanvasGuides();') < ui.indexOf('BuildCanvasZoomTool();'));
    assert.match(src, /toolkitCanvas\.AddBelowToolOverlays\(canvasGuideOverlay\)/);
    assert.match(ui, /AddBelowToolOverlays\(VisualElement element\) => Insert\(IndexOf\(overlay\), element\)/,
        'Guides draw above the image and below every tool overlay, including the brush cursor');
    assert.ok(ui.indexOf('toolkitCanvas.AddManipulator(canvasGuideManipulator);') > ui.indexOf('RegisterCallback<PointerDownEvent>(OnCanvasPointerDown)'),
        'Guide fallback is registered after all tools and standard canvas input');
    assert.match(src, /RegisterCallback<PointerDownEvent>\(canvasGuideManipulator.RailDown, TrickleDown.TrickleDown\)/);
    assert.match(src, /UnregisterCallback<PointerDownEvent>\(RailDown, TrickleDown.TrickleDown\)/);
    for (const tool of ['Point', 'Normal']) {
        const code = read(`WhimTexWindow.${tool}.cs`);
        assert.match(code, /!WantsPointer\(e.localPosition\)/, 'Hover and actual tool capture share one hit-test');
    }
    assert.match(ui, /KeyCode.Escape && canvasGuideManipulator\?\.IsDragging == true/);
    assert.match(read('WhimTexWindow.cs'), /ClearCanvasGuides\(\);\s*canvasGuidesDocument = next;[\s\S]*?activeDocument = next/);
    assert.ok(!/\bUndo\.|RenderTexture|MarkChanged|SetDirty/.test(src), 'Guides remain window-local and outside the render/Undo pipeline');
    assert.match(src, /whimtex-canvas-surface/);
    assert.match(src, /ViewChanged \+= canvasGuideOverlay.MarkDirtyRepaint/);
    const colorBody = src.match(/Color lineColor = ([\s\S]*?)\s*for \(int pass/)[1];
    const evaluateGuideColor = new Function('aligned', 'highlight', 'deleting', 'Color', 'WhimTexUserSettings',
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
    const styles = read('WhimTexSplitView.uss');
    assert.match(styles, /\.whimtex-guide-rail--left\s*\{[^}]*width: 8px;/);
    assert.match(styles, /\.whimtex-guide-rail--top\s*\{[^}]*height: 8px;/);
});

await finish(context);
