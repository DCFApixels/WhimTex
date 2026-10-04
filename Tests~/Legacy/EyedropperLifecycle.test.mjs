import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('../src/TextureCompositorWindow.Eyedropper.cs', import.meta.url), 'utf8');
function body(signature) {
    const signatureStart = source.indexOf(signature);
    assert.ok(signatureStart >= 0, signature);
    const start = source.indexOf('{', signatureStart);
    let depth = 1, end = start + 1;
    while (depth && end < source.length) {
        if (source[end] === '{') depth++;
        if (source[end] === '}') depth--;
        end++;
    }
    return source.slice(start + 1, end - 1);
}
// Execute the assignment/branching bodies, with Unity services replaced by fakes.
// This does not execute C#, native windows, focus, screen reading or event dispatch.
function extract(signature, ...parameters) {
    const translated = body(signature).replaceAll('catch (Exception exception)', 'catch (exception)')
        .replace('bool returnToOwner =', 'let returnToOwner =')
        .replace('pendingPickColor = displayedSampleColor;', 'pendingPickColor = { ...displayedSampleColor };');
    const run = new Function('state', ...parameters, `with (state) { ${translated} }`);
    return (state, ...args) => run.call(state, state, ...args);
}
const queue = extract('private void QueuePick()');
const finish = extract('internal void Finish(bool returnToOwner)', 'returnToOwner');
const cleanup = extract('private void Cleanup()');
const tryCleanup = extract('private void TryCleanup(Action action)', 'action');
const fail = extract('private void Fail(Exception exception)', 'exception');
const reuse = extract('private bool CanReuseDisplayedSample(double now)', 'now');

const selection = {
    screenPosition: { x: 100, y: 200 }, pendingPickPosition: null, pendingPickAlpha: 0,
    owner: { paintSettings: { brushColor: { a: .25 } } }, pendingPick: false,
    pendingPickUsesDisplayedSample: false, pendingPickColor: null,
    displayedSampleColor: { r: 1, g: 0, b: 0, a: 1 },
    EditorApplication: { timeSinceStartup: 1 }, CanReuseDisplayedSample() { return true; },
    DeferCoveredSample() {},
};
queue(selection);
selection.screenPosition = { x: 800, y: 900 };
selection.owner.paintSettings.brushColor.a = .9;
assert.deepEqual(selection.pendingPickPosition, { x: 100, y: 200 });
assert.equal(selection.pendingPickAlpha, .25);
assert.equal(selection.pendingPickUsesDisplayedSample, true);
selection.displayedSampleColor.r = 0;
selection.displayedSampleColor.b = 1;
assert.deepEqual(selection.pendingPickColor, { r: 1, g: 0, b: 0, a: .25 },
    'A pending click keeps the displayed color at click time, including its own alpha');
assert.equal(selection.displayedSampleColor.a, 1, 'Brush alpha does not alter the displayed sample');
queue(selection);
assert.deepEqual(selection.pendingPickPosition, { x: 800, y: 900 }, 'A new click/drag replaces the pending intent');
assert.equal(selection.pendingPickAlpha, .9);

const point = (x, y) => ({ x, y, Equals(other) { return this.x === other.x && this.y === other.y; } });
for (const [x, y] of [[120, 70], [2300.5, 600.25], [-1000.5, 80]]) {
    const state = { hasDisplayedSample: false, screenPosition: point(x, y), displayedSamplePosition: point(x, y), pointerStableSince: 1 };
    assert.equal(reuse(state, 2), false, 'No sample on session startup');
    state.hasDisplayedSample = true;
    assert.equal(reuse(state, 1.02), false, 'A freshly moved pointer requires a new 11x11 read');
    assert.equal(reuse(state, 1.08), true, 'A stable matching position can use the displayed color');
    state.screenPosition = point(x + 1, y);
    assert.equal(reuse(state, 2), false, 'Neighboring pixels must never reuse the cached sample');
    state.screenPosition = point(x, y);
    state.pointerStableSince = 2;
    assert.equal(reuse(state, 2.01), false, 'Returning to an old point starts stability timing again');
}

for (const cancel of [false, true]) {
    const calls = [];
    const state = {
        closing: false, pendingPick: true, capture: {}, picking: true, finishAfterPick: false,
        Cleanup() { calls.push('cleanup'); state.closing = true; state.pendingPick = false; },
        Close() { calls.push('close'); }, ReturnFocus() { calls.push('focus'); },
        TryCleanup(action) { action(); },
    };
    finish(state, !cancel);
    if (cancel) assert.deepEqual(calls, ['cleanup', 'close'], 'Cancellation never commits a pending pick');
    else {
        assert.deepEqual(calls, [], 'Alt release retains the pending pick until sampling is ready');
        assert.equal(state.finishAfterPick, true);
        assert.equal(state.picking, false);
        state.pendingPick = false;
        finish(state, true);
        assert.deepEqual(calls, ['cleanup', 'close', 'focus']);
    }
    finish(state, true);
    assert.equal(calls.filter(x => x === 'close').length, 1, 'Reentrant finish is harmless');
}

const steps = ['cursor', 'capture', 'pointer', 'shortcuts', 'image', 'lensCursor', 'lensClose', 'sample', 'texture', 'controller'];
for (const failure of [null, ...steps]) {
    const visited = [], errors = [];
    const step = name => {
        visited.push(name);
        if (name === failure) throw new Error(name);
    };
    const style = name => ({ set cursor(value) { step(name); } });
    const state = {
        closing: false, picking: true, pendingPick: true, shortcutsSuppressed: true,
        EditorApplication: { update: 0, quitting: 0 }, AssemblyReloadEvents: { beforeAssemblyReload: 0 },
        Tick: 0, Cancel: 0, StyleKeyword: { Null: null }, PointerId: { mousePointerId: 0 },
        rootVisualElement: { style: style('cursor'), HasPointerCapture() { return true; }, ReleasePointer() { step('pointer'); } },
        capture: { Dispose() { step('capture'); } },
        ReleaseUnityShortcutSuppression() { step('shortcuts'); },
        magnified: { set image(value) { step('image'); } },
        lensWindow: { rootVisualElement: { style: style('lensCursor') }, Close() { step('lensClose'); } },
        sampleTexture: 'sample', cursorTexture: 'texture',
        UnityEngine: { Object: { DestroyImmediate(texture) { step(texture); } } },
        current: null, controller: { Closed() { step('controller'); }, SuppressUntilAltReleased() { state.failed = true; } },
        Debug: { LogException(error) { errors.push(error); } }, failed: false,
        TryCleanup(action) { tryCleanup(state, action); },
    };
    state.current = state;
    cleanup(state);
    assert.deepEqual(visited, steps, `Cleanup continues after ${failure ?? 'no failure'}`);
    assert.equal(errors.length, failure === null ? 0 : 1);
    assert.equal(state.failed, failure !== null);
    for (const field of ['capture', 'lensWindow', 'sampleTexture', 'cursorTexture']) assert.equal(state[field], null);
    assert.equal(state.shortcutsSuppressed, false);
    assert.equal(state.pendingPick, false);
    assert.equal(state.current, null);
    cleanup(state);
    assert.equal(visited.length, steps.length, 'Cleanup is idempotent');
}
for (const ownsFocus of [true, false]) {
    const calls = [];
    const state = {
        closing: false, focusedWindow: null, pendingPick: true, failed: false,
        controller: { SuppressUntilAltReleased() { calls.push('latch'); state.failed = true; } },
        Finish(returnFocus) {
            assert.equal(returnFocus, false, 'A failure cannot wait for or apply a pending pick');
            assert.equal(state.failed, true, 'Latch is set before returning focus');
            state.closing = true;
            calls.push('finish');
        },
        ReturnFocus() { calls.push('focus'); }, TryCleanup(action) { action(); },
        Debug: { LogException() { calls.push('log'); } },
        owner: { ShowNotification() { calls.push('notice'); } }, GUIContent: function () {},
    };
    if (ownsFocus) state.focusedWindow = state;
    fail(state, new Error('native failure'));
    fail(state, new Error('duplicate callback'));
    assert.deepEqual(calls, ['latch', 'finish', ...(ownsFocus ? ['focus'] : []), 'log', 'notice']);
}
console.log('Eyedropper extracted pending-pick/finish/cleanup checks passed, including injected failures (Unity not executed).');
