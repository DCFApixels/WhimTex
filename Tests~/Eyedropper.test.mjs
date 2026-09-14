import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const eye = read('src/TextureCompositorWindow.Eyedropper.cs');
const window = read('src/TextureCompositorWindow.cs');
const uss = read('src/WhimTexSplitView.uss');
const lensStyle = uss.match(/\.whimtex-eyedropper-lens\s*\{([^}]+)\}/)[1];
for (const edge of ['left', 'top', 'right', 'bottom']) assert.match(lensStyle, new RegExp(`\\b${edge}: 0;`));
assert.ok(!/\b(?:width|height):/.test(lensStyle.replace(/border-width:[^;]+;/g, '')),
    'Lens background fills the actual popup instead of leaving an unstyled strip');
for (const part of ['sample', 'swatch']) {
    const style = uss.match(new RegExp(`\\.whimtex-eyedropper-${part}\\s*\\{([^}]+)\\}`))[1];
    assert.ok(style.includes('left: 50%') && style.includes('margin-left: -44px') && style.includes('width: 88px'));
}
assert.ok(eye.includes('InternalEditorUtility.ReadScreenPixelUnderCursor(point, SampleSide, SampleSide)'));
assert.equal((eye.match(/InternalEditorUtility.ReadScreenPixelUnderCursor\(/g) ?? []).length, 1,
    'Picking and magnification share one 11x11 screen read; no independent 1x1 rounding');
assert.ok(!/RenderPreview|EyeDropper.Start|KeyCode.Escape|DllImport|globalEventHandler/.test(eye));
assert.ok(!/DrawIcon|iconRect|target.Add\(lens\)/.test(eye));
assert.ok(!uss.includes('whimtex-eyedropper-cursor'));
assert.ok(eye.includes('private sealed class ScreenEyedropperWindow : EditorWindow'));
assert.ok(eye.includes('ShowPopup()'));
assert.ok(eye.includes('GetBoundsOfDesktopAtPoint(point)'));
assert.ok(!eye.includes('ScreenEyedropperPointerPosition'));
assert.ok(eye.includes('GetMethod("GetCurrentMousePosition",'));
assert.ok(eye.includes('Delegate.CreateDelegate(typeof(Func<Vector2>), MousePosition)'));
assert.ok(eye.includes('internal Vector2 GetScreenPosition() => getScreenPosition()'));
const screenWindow = eye.split('private sealed class ScreenEyedropperWindow')[1].split('private sealed class ScreenEyedropperCapture')[0];
assert.ok(!screenWindow.includes('GUIToScreenPoint'), 'Popup coordinates do not depend on the current IMGUI view');
const move = screenWindow.split('private void OnMove(')[1].split('private void OnDown(')[0];
assert.ok(move.indexOf('evt.imguiEvent == null') < move.indexOf('MoveSample('), 'Ignore synthetic pointer moves');
assert.ok(move.includes('EventType.MouseMove') && move.includes('EventType.MouseDrag'));
const sampleMove = screenWindow.split('private void MoveSample(')[1].split('private void QueuePick(')[0];
assert.ok(!sampleMove.includes('position ='), 'Do not move the native window during pointer dispatch');
assert.ok(sampleMove.includes('screenPosition = capture.GetScreenPosition()'), 'Read absolute native pointer position');
assert.ok(!sampleMove.includes('panelPoint') && !sampleMove.includes('evt.position'), 'No moving-window coordinate feedback');
const tick = screenWindow.split('private void Tick()')[1].split('private void ReadSample(')[0];
assert.ok(tick.indexOf('MoveSample()') < tick.indexOf('MoveLens()'), 'Refresh the actual pointer even without a pointer event');
assert.ok(tick.indexOf('MoveLens()') < tick.indexOf('ReadSample(false)'), 'Move the lens before deferred sampling');
assert.ok(!screenWindow.includes('ReadPixels'), 'Screen samples still use the desktop reader, not texture readback');
const cursorFactory = eye.split('private static Texture2D CreateScreenEyedropperCursor')[1].split('private sealed class PreviewEyedropperManipulator')[0];
for (const check of [
    'TextureFormat.RGBA32, false, !source.isDataSRGB', 'alphaIsTransparency = true',
    'if (source.isReadable)', 'copy.SetPixels32(source.GetPixels32())', 'Graphics.Blit(source, temporary)',
    'copy.ReadPixels(', 'copy.Apply(false, false)', 'UnityEngine.Object.DestroyImmediate(copy)',
    'GL.sRGBWrite = previousSrgb', 'RenderTexture.active = previous', 'RenderTexture.ReleaseTemporary(temporary)'
]) assert.ok(cursorFactory.includes(check), check);
assert.ok(!cursorFactory.includes('source.alphaIsTransparency ='), 'Never modify the built-in icon');
assert.equal((eye.match(/FindTexture\("EyeDropper.Large"\)/g) ?? []).length, 1, 'One icon copy per session');
assert.ok(screenWindow.includes('new ScreenEyedropperCapture(this, cursorTexture)'), 'Both cursor APIs share the owned copy');
assert.ok(!screenWindow.includes('Texture2D cursorTexture = EditorGUIUtility.FindTexture'));
assert.ok(!screenWindow.includes('contentRect.Contains'), 'No preview or popup bounds restriction after activation');
assert.ok(!screenWindow.includes('PointerLeaveEvent'), 'Leaving a view must not end desktop sampling');
assert.ok(eye.includes('focusedWindow != owner'), 'Alt hover must not steal focus from another application');
assert.ok(window.includes('if (!OwnsScreenEyedropper) CancelPreviewEyedropper()'));
assert.ok(eye.includes('focusedWindow != this'), 'External focus loss ends the session');
assert.ok(eye.includes('private void OnLostFocus() { if (!starting) Finish(false); }'));
assert.ok(eye.includes('private sealed class ScreenEyedropperLensWindow : EditorWindow { }'));
assert.ok(screenWindow.includes('BuildLens(lensWindow.rootVisualElement)'));
assert.ok(!eye.includes('new ScreenEyedropperCapture(lensWindow'), 'Native capture never owns the visible magnifier');
const moveLens = screenWindow.split('private void MoveLens()')[1].split('private void Tick()')[0];
assert.ok(moveLens.includes('lensWindow.position = next;'));
assert.ok(!/^\s*position = /m.test(moveLens), 'Only the presentation window is positioned with an offset');
assert.ok(eye.includes('lensWindow.position.Overlaps(probe)'), 'Only the visible lens can obscure sampled pixels');
const nativeCapture = eye.split('private sealed class ScreenEyedropperCapture')[1];
assert.ok(nativeCapture.includes('SetInvisible.Invoke(container, null)'));
assert.ok(nativeCapture.indexOf('SetInvisible.Invoke(container, null)') < nativeCapture.indexOf('setOpen(true)'));
assert.ok(eye.includes('if (!evt.altKey) Finish(true)'));
const up = screenWindow.split('private void OnUp(')[1].split('private void OnKeyDown')[0];
assert.ok(up.includes('if (!evt.altKey) { Finish(true); return; }'));
assert.ok(up.includes('CapturePointer(PointerId.mousePointerId)'));
assert.ok(up.includes('capture?.Refresh()'), 'Picking a color must not close the held-Alt session');
assert.ok(eye.includes('GetMethod("StealMouseCapture", InstanceFlags)'));
assert.ok(eye.includes('GetMethod("SetEyeDropperOpen", InstanceFlags)'));
assert.ok(eye.includes('GetMethod("SetCurrentViewCursor",'));
assert.ok(eye.includes('EditorGUIUtility.FindTexture("EyeDropper.Large")'));
assert.ok(eye.includes('MouseCursor.CustomCursor : MouseCursor.ArrowPlus'));
assert.ok(eye.includes('setOpen(false)'));
assert.ok(eye.includes('InternalEditorUtility.ResetCursor()'));
assert.ok(eye.includes('color.a = alpha;'));
assert.ok(!eye.includes('HdrUtility.Encode'));
assert.ok(eye.includes('TextureFormat.RGBA32, false, true'));
assert.ok(eye.includes('filterMode = FilterMode.Point'));
assert.ok(eye.includes('sourceOccluded = true;'));
assert.ok(eye.includes('clearUntil = EditorApplication.timeSinceStartup + 0.05;'));
assert.ok(eye.includes('if (now < clearUntil && (!force || sourceOccluded)) return;'),
    'Quick picks do not wait for magnifier warm-up unless its old window covered the source');
assert.ok(eye.includes('nextRead = now + 1.0 / 30.0;'));
const cleanup = screenWindow.split('private void Cleanup()')[1].split('private void Cancel()')[0];
for (const check of [
    'if (closing) return', 'closing = true', 'picking = pendingPick = false',
    'EditorApplication.update -= Tick', 'AssemblyReloadEvents.beforeAssemblyReload -= Cancel',
    'EditorApplication.quitting -= Cancel', 'capture?.Dispose()', 'ReleasePointer(PointerId.mousePointerId)',
    'TryCleanup(ReleaseUnityShortcutSuppression)', 'UnityEngine.Object.DestroyImmediate(sampleTexture)', 'controller?.Closed(this)'
]) assert.ok(cleanup.includes(check), check);
assert.ok(cleanup.indexOf('rootVisualElement.style.cursor = StyleKeyword.Null') < cleanup.indexOf('UnityEngine.Object.DestroyImmediate(cursorTexture)'));
assert.ok(cleanup.indexOf('capture?.Dispose()') < cleanup.indexOf('UnityEngine.Object.DestroyImmediate(cursorTexture)'));
assert.ok(cleanup.includes('lensWindow.Close()'));
assert.ok(cleanup.indexOf('lensWindow.Close()') < cleanup.indexOf('UnityEngine.Object.DestroyImmediate(cursorTexture)'));
for (const action of ['capture?.Dispose()', 'lensWindow.Close()', 'controller?.Closed(this)'])
    assert.ok(cleanup.includes(`TryCleanup(() => ${action})`), `${action} cannot abort the remaining cleanup`);
assert.ok(cleanup.includes('TryCleanup(() => { if (sampleTexture != null)'));
assert.ok(cleanup.includes('TryCleanup(() => { if (cursorTexture != null)'));
const finish = screenWindow.split('internal void Finish(')[1].split('private void ReturnFocus(')[0];
assert.ok(finish.includes('returnToOwner && pendingPick && capture != null'));
assert.ok(finish.indexOf('finishAfterPick = true') < finish.indexOf('Cleanup()'));
assert.ok(finish.includes('TryCleanup(Close)') && finish.includes('TryCleanup(ReturnFocus)'));
const fail = screenWindow.split('private void Fail(')[1].split('private void TryCleanup(')[0];
assert.ok(fail.indexOf('SuppressUntilAltReleased()') < fail.indexOf('Finish(false)'));
assert.ok(!fail.includes('Finish(true)'), 'Failures cancel, never flush an untrusted pending sample');
for (const method of ['OnMove', 'OnDown', 'OnUp', 'Tick']) {
    const body = screenWindow.split(`private void ${method}(`)[1].split('\n            private ')[0];
    assert.ok(body.includes('catch (Exception exception) { Fail(exception); }'), `${method} contains native API failures`);
}
const queue = screenWindow.split('private void QueuePick()')[1].split('private void DeferCoveredSample(')[0];
assert.ok(queue.includes('pendingPickPosition = screenPosition'));
assert.ok(queue.includes('pendingPickAlpha = owner.paintSettings.brushColor.a'));
assert.ok(queue.includes('DeferCoveredSample()'));
assert.ok(!sampleMove.includes('pendingPickPosition ='), 'Hover cannot replace the queued click');
assert.ok(move.includes('if (picking) QueuePick()'), 'Only a held-button drag replaces a queued sample');
assert.ok(eye.includes('commitPick ? pendingPickAlpha : owner.paintSettings.brushColor.a'));
assert.ok(!eye.includes('if (pendingPick) ApplySample(color)'), 'Magnifier updates cannot commit a different pixel');
const sample = screenWindow.split('private void ReadSample(')[1].split('private void ApplySample(')[0];
assert.ok(sample.includes('Vector2 point = commitPick ? pendingPickPosition : screenPosition'));
assert.ok(sample.includes('Color color = PreviewScreenSample(pixels, SampleSide,'));
assert.ok(sample.includes('displayedSampleColor = color') && sample.includes('if (commitPick) ApplySample(color)'));
assert.ok(sample.indexOf('sampleTexture.SetPixels(pixels)') < sample.indexOf('if (commitPick) ApplySample(color)'));
assert.ok(sample.indexOf('ApplySample(pendingPickColor)') < sample.indexOf('ReadScreenPixelUnderCursor('),
    'Stable clicks reuse their captured magnifier color before any new screen read');
assert.ok(sampleMove.includes('if (!screenPosition.Equals(previous)) pointerStableSince = EditorApplication.timeSinceStartup'));
assert.ok(queue.includes('pendingPickColor = displayedSampleColor'));
assert.ok(queue.includes('pendingPickColor.a = pendingPickAlpha'));
assert.ok(tick.includes('if (!finishAfterPick) MoveSample()'));
assert.ok(tick.includes('if (finishAfterPick && !pendingPick) Finish(true)'));
const ui = read('src/TextureCompositorWindow.UI.cs');
assert.match(ui, /name = "WhimTex Transparent Cursor",\s*hideFlags = HideFlags.HideAndDontSave,\s*alphaIsTransparency = true/);
for (const side of [1, 3, 11]) {
    const pixels = Array.from({length: side * side}, (_, i) => i);
    const center = Math.floor(side / 2) * side + Math.floor(side / 2);
    assert.equal(pixels[center], (side * side - 1) / 2);
}
const dimensions = eye.match(/const float width = ([\d.]+)f, height = ([\d.]+)f, gap = ([\d.]+)f/);
const [width, height, gap] = dimensions.slice(1).map(Number);
for (const [bx, by, w, h] of [[0, 0, 200, 96], [0, 0, 1200, 900], [-1920, 0, 1920, 1080], [1920, -1440, 2560, 1440]])
    for (let x = 0; x <= w; x += 17) for (let y = 0; y <= h; y += 17) {
        const px = bx + x, py = by + y;
        const left = px + gap + width <= bx + w ? px + gap : px - gap - width;
        const top = Math.max(by, Math.min(Math.max(by, by + h - height), py + gap + height > by + h ? py - gap - height : py + gap));
        assert.ok(left > px + 7 || left + width < px - 7, 'Magnifier never covers the sampling patch');
        assert.ok(Number.isFinite(top));
        if (w > 2 * (width + gap)) assert.ok(left >= bx && left + width <= bx + w, 'Stay on the current monitor');
        if (h >= height) assert.ok(top >= by && top + height <= by + h);
    }
assert.ok(eye.includes('if (x + width > bounds.xMax) x = point.x - gap - width;'));
for (const center of [500, 960, 1280]) {
    const positions = [center - 2, center - 1, center, center + 1, center + 2];
    const offsets = positions.map(x => (x + gap + width <= center * 2 ? x + gap : x - gap - width) - x);
    assert.deepEqual(offsets, [gap, gap, gap, gap, gap], 'Crossing the screen center does not flip the magnifier');
}
console.log('Desktop eyedropper source and placement reference checks passed (Unity/input capture not executed).');
