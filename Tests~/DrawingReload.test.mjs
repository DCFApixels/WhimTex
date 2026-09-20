import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');
const drawing = read('Layers/DrawingLayerBehaviour.cs');
const compositor = read('TextureCompositor.cs');
function body(source, signature) {
    const at = source.indexOf(signature);
    assert.ok(at >= 0, signature);
    const start = source.indexOf('{', at);
    let depth = 1, end = start + 1;
    while (depth) {
        if (source[end] === '{') depth++;
        if (source[end] === '}') depth--;
        end++;
        assert.ok(end <= source.length);
    }
    return source.slice(start + 1, end - 1);
}
const disable = body(compositor, 'private void OnDisable()');
const destroy = body(compositor, 'private void OnDestroy()');
assert.ok(disable.includes('ReleaseLayerResources(layers, preserveDrawingPixels: true)'));
assert.ok(destroy.includes('ReleaseLayerResources(layers)'));
const visit = body(compositor, 'private static void ReleaseLayerResources(');
assert.ok(visit.includes('preserveDrawingPixels && layer?.Behaviour is DrawingLayerBehaviour drawing'));
assert.ok(visit.includes('drawing.ReleasePaintResources()'));
assert.ok(visit.includes('ReleaseLayerResources(group.layers, preserveDrawingPixels)'));
assert.ok(compositor.includes('bool preserveDrawingPixels = false'), 'Merge/deletion retain destructive cleanup by default');
const paint = body(drawing, 'internal void PaintSegment(');
assert.ok(paint.indexOf('paintSurfaceDirty |= segmentStamps.Count > 0') < paint.indexOf('PaintBrushRenderer.Draw('));
assert.ok(drawing.includes('[NonSerialized] private bool paintSurfaceDirty;'));
assert.ok(!paint.includes('SyncSurfaceToTexture()'), 'Painting does not gain extra readbacks');
const sync = body(drawing, 'internal void SyncSurfaceToTexture()');
assert.ok(sync.indexOf('paintSurfaceDirty = false') > sync.indexOf('pixels.Apply(false, false)'));
const syncPending = new Function('state', 'EnsureDeferredTexture', `with (state) { ${body(drawing, 'internal void SyncPendingSurfaceToTexture()')} }`);
for (const dirty of [false, true]) for (const pixels of [null, {}]) {
    let calls = 0;
    syncPending({ paintSurfaceDirty: dirty, pixels, SyncSurfaceToTexture() { calls++; } }, () => {});
    assert.equal(calls, dirty || pixels === null ? 1 : 0, 'Saving only reads back dirty/new Drawing surfaces');
}
assert.ok(body(compositor, 'internal void SyncDrawingLayerTextures()').includes('drawing.SyncPendingSurfaceToTexture()'));
assert.ok(body(drawing, 'private void ReleasePaintSurface()').includes('paintSurfaceDirty = false'));
// The production method calls the private deferred-texture initializer. Keep that
// dependency explicit in this source-level harness instead of relying on a global
// symbol that is not present in the extracted function scope.
const suspend = new Function('state', 'EnsureDeferredTexture', `with (state) { ${body(drawing, 'internal void ReleasePaintResources()')} }`);
const dispose = new Function('state', `with (state) { ${body(drawing, 'internal override void ReleaseTransientResources()')} }`);
for (const dirty of [false, true]) for (const persistent of [false, true]) {
    const pixels = { format: 'RGBAHalf', value: 2.5, persistent, hideFlags: 1 };
    const calls = [];
    const state = {
        pixels, paintSurfaceDirty: dirty,
        SyncSurfaceToTexture() { calls.push('sync'); state.pixels.value = 3.5; state.paintSurfaceDirty = false; },
        ReleasePaintSurface() { calls.push('release'); state.paintSurfaceDirty = false; },
        AssetDatabase: { Contains(texture) { return texture.persistent; } }, HideFlags: { DontSave: 1 },
        UnityEngine: { Object: { DestroyImmediate(texture) { calls.push('destroy'); texture.destroyed = true; } } },
    };
    suspend(state, () => {});
    assert.equal(state.pixels, pixels, 'Disable preserves native texture identity');
    assert.equal(pixels.value, dirty ? 3.5 : 2.5, 'HDR pixels survive without encoding or clamping');
    assert.deepEqual(calls, dirty ? ['sync', 'release'] : ['release']);
    suspend(state, () => {});
    assert.equal(calls.filter(x => x === 'sync').length, dirty ? 1 : 0, 'Read back only uncommitted pixels once');
    dispose(state);
    assert.equal(pixels.destroyed === true, !persistent, 'Final destruction does not delete persistent assets');
    assert.equal(state.pixels, persistent ? pixels : null);
}
const failed = {
    paintSurfaceDirty: true,
    SyncSurfaceToTexture() { throw new Error('readback failed'); },
    ReleasePaintSurface() { assert.fail('Do not destroy the only pixel copy when readback fails'); },
};
assert.throws(() => suspend(failed, () => {}), /readback failed/);
console.log('Drawing reload source and extracted lifecycle checks passed (Unity/domain reload/GPU not executed).');
