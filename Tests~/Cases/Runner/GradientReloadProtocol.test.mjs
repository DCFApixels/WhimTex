// Mock protocol checks only: no Unity commands, fixtures, or claimed real reload verification.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { normalizeReloadStatus, normalizeEditorStatus, editorStatusNetworkError,
    editorStatusProcessTimeout, EditorStatusProcessTimeoutError, driveReload } from '../Color/GradientReload.mjs';

const context = new TestContext('Deferred native reload protocol: real proof, bounded no-op and recovery');
const assert = context.assert;
const shapes = [value => value, value => JSON.stringify(value)];
const project = 'D:/DCFA/Projects/Test6.6';
const idle = { projectPath: project, compiling: false, domainReloadInProgress: false, playMode: 'stopped' };
const busy = { ...idle, compiling: true };
const begin = { status: 'skipped', checks: 0 };
const end = { status: 'passed', checks: 5 };
const cleanup = { status: 'passed', checks: 1 };
const native = (changes = {}) => ({ status: 'running', checks: 0, message: 'Native state',
    failures: [], queued: false, requested: true, requests: 1, reloaded: false,
    oldDomainCleared: false, errors: [], ...changes });
const queued = native({ queued: true, requested: false, requests: 0 });
const completed = native({ status: 'passed', checks: 3, reloaded: true, oldDomainCleared: true });
const prefix = () => [['editor_status', idle], ['Begin', begin], ['Trigger', queued]];
const suffix = (result = end, disposal = cleanup) => [['editor_status', idle], ['End', result], ['Cleanup', disposal]];
const networkEnvelope = () => ({ success: false, command: 'unity command editor_status', data: null,
    errors: [{ code: 'COMMAND_FAILED', message: "Failed to execute command 'editor_status': Network error: An error occurred while sending the request." }], warnings: [] });
const networkReply = (envelope = networkEnvelope(), overrides = {}) => ({ code: 6, stdout: JSON.stringify(envelope),
    stderr: '', timedOut: false, ...overrides });
const networkError = () => editorStatusNetworkError(networkReply());
const timeoutReply = (overrides = {}) => ({ code: null, stdout: '', stderr: '', timedOut: true, ...overrides });
const statusTimeout = () => editorStatusProcessTimeout(timeoutReply(), 'editor_status');

async function simulate(plan, { shape = shapes[0], repeat, latency = 0 } = {}) {
    // Native polling is only legal after a fresh supported idle probe.
    plan = plan.flatMap(item => item[0] === 'ReloadPoll' ? [['editor_status', idle], item] : [item]);
    const inner = new TestContext('Mocked native transport');
    const commands = [], budgets = [];
    let elapsed = 0, error;
    try {
        await driveReload(inner, { now: () => elapsed, delay: async ms => { elapsed += ms; },
            request: async (name, budget) => {
                commands.push(name); budgets.push(budget);
                assert.ok(budget > 0 && budget <= (name === 'Cleanup' ? 10000 : 8000), 'Bounded per-command budget');
                const expected = plan.shift() ?? (repeat?.[0] === 'ReloadPoll' && name === 'editor_status'
                    ? ['editor_status', idle] : repeat);
                assert.ok(expected, 'No unexpected/fallback/retried command: ' + name);
                assert.equal(name, expected[0]);
                const commandLatency = expected[2] ?? latency;
                elapsed += Math.min(commandLatency, budget);
                if (commandLatency >= budget && !(expected[1] instanceof EditorStatusProcessTimeoutError))
                    throw Error('transport timeout at bounded process deadline');
                if (expected[1] instanceof Error) throw expected[1];
                const result = shape(expected[1]);
                return ['Trigger', 'ReloadPoll'].includes(name) ? normalizeReloadStatus(result) : expected[1];
            } });
    } catch (cause) { error = cause; }
    assert.equal(plan.length, 0, 'All expected commands consumed');
    assert.ok(!commands.includes('recompile') && !commands.includes('recompile_status') && !commands.includes('refresh'),
        'Never uses CLI compilation/refresh fallback');
    assert.ok(commands.filter(name => name === 'Trigger').length <= 1, 'At most one deferred native request');
    return { inner, error, commands, budgets, elapsed };
}
function expectRecovery(result) {
    assert.ok(result.error);
    assert.equal(result.inner.recoveryRequired, true);
    assert.equal(result.commands.includes('End'), false);
    assert.equal(result.commands.includes('Cleanup'), false);
    assert.equal(result.inner.facts.cleanupCommandSuppressed, true);
}

context.case('native payload object/string shapes retain all proof and errors', () => {
    for (const shape of shapes)
        for (const payload of [queued, native(), native({ reloaded: true }), completed,
            native({ status: 'failed', failures: ['native failure'], errors: ['native failure'] })])
            assert.deepEqual(normalizeReloadStatus(shape(payload)), payload);
});
context.case('malformed native payloads cannot masquerade as proof', () => {
    for (const payload of [null, [], { status: 'up_to_date' }, native({ status: 'other' }),
        native({ requests: 1.5 }), native({ checks: -1 }), native({ requested: 'false' }),
        native({ failures: ['unexpected'] }), native({ status: 'failed' }),
        native({ errors: '[]' }), native({ errors: [1] }), native({ oldDomainCleared: undefined })])
        for (const shape of shapes) assert.throws(() => normalizeReloadStatus(shape(payload)));
    assert.throws(() => normalizeReloadStatus('{broken JSON'));
});
context.case('real proof and idle are both required before original five End checks', async () => {
    for (const shape of shapes) {
        const result = await simulate([...prefix(), ['ReloadPoll', native()],
            ['ReloadPoll', native({ reloaded: true })], ['ReloadPoll', completed],
            ['editor_status', busy], ...suffix()], { shape });
        assert.equal(result.error, undefined);
        assert.notEqual(result.inner.recoveryRequired, true);
        assert.equal(result.inner.facts.nativeTriggerRequests, 1);
        assert.equal(result.inner.facts.nativeResults.reload.checks, 3);
        assert.equal(result.inner.facts.nativeResults.end.checks, 5);
        assert.equal(result.inner.facts.nativeResults.cleanup.checks, 1);
        assert.equal(result.commands.at(-1), 'Cleanup');
    }
});
context.case('busy compile/reload interval issues only supported Editor status probes', async () => {
    const result = await simulate([...prefix(), ['editor_status', busy],
        ['editor_status', { ...idle, domainReloadInProgress: true }], ['editor_status', busy],
        ['ReloadPoll', completed], ...suffix()]);
    assert.equal(result.error, undefined);
    const triggerIndex = result.commands.indexOf('Trigger');
    const pollIndex = result.commands.indexOf('ReloadPoll');
    assert.deepEqual(result.commands.slice(triggerIndex + 1, pollIndex),
        ['editor_status', 'editor_status', 'editor_status', 'editor_status']);
    assert.equal(result.inner.facts.editorPollStates.filter(state => state.compiling || state.domainReloadInProgress).length, 3);
});
context.case('no-op request times out within operation bound without retry/End/Cleanup', async () => {
    const result = await simulate(prefix(), { repeat: ['ReloadPoll', native()] });
    expectRecovery(result);
    assert.ok(result.elapsed >= 43000 && result.elapsed <= 45000);
    assert.ok(result.commands.filter(name => name === 'ReloadPoll').length < 90);
    assert.equal(result.commands.at(-1), 'ReloadPoll');
});
context.case('beforeAssemblyReload alone is not completed domain proof', async () => {
    const result = await simulate(prefix(), { repeat: ['ReloadPoll', native({ reloaded: true })] });
    expectRecovery(result);
    assert.ok(result.elapsed <= 45000);
});
context.case('native proof cannot bypass an indefinitely busy Editor', async () => {
    const result = await simulate([...prefix(), ['ReloadPoll', completed]], { repeat: ['editor_status', busy] });
    expectRecovery(result);
    assert.ok(result.elapsed <= 45000);
});
context.case('terminal proof inconsistencies stop commands and require recovery', async () => {
    for (const shape of shapes)
        for (const payload of [native({ status: 'passed', checks: 3, reloaded: true }),
            { ...completed, requests: 2 }, { ...completed, queued: true }, { ...completed, checks: 0 },
            native({ status: 'failed', failures: ['Request threw'], errors: ['Request threw'] }),
            native({ status: 'failed', requests: 0, requested: false,
                failures: ['Deferred idle guard rejected'], errors: ['Deferred idle guard rejected'] }),
            native({ status: 'failed', failures: ['assembly compile error'], errors: ['assembly compile error'] }),
            native({ checks: 1 }), { status: 'up_to_date' }]) {
            const result = await simulate([...prefix(), ['ReloadPoll', payload]], { shape });
            expectRecovery(result);
            assert.equal(result.commands.at(-1), 'ReloadPoll');
        }
});
context.case('unknown/rejected Trigger never falls back or retries', async () => {
    for (const payload of [null, { ...queued, requested: true },
        native({ status: 'failed', failures: ['duplicate request'], errors: ['duplicate request'] })]) {
        const result = await simulate([['editor_status', idle], ['Begin', begin], ['Trigger', payload]]);
        expectRecovery(result);
        assert.equal(result.commands.at(-1), 'Trigger');
    }
});
context.case('transport uncertainty stops at Begin/Trigger/Poll without cleanup commands', async () => {
    for (const plan of [
        [['editor_status', idle], ['Begin', new Error('transport timeout')]],
        [['editor_status', idle], ['Begin', begin], ['Trigger', new Error('transport timeout')]],
        [...prefix(), ['ReloadPoll', new Error('transport timeout')]]
    ]) {
        const result = await simulate(plan);
        expectRecovery(result);
        assert.ok(String(result.error).includes('transport timeout'));
    }
});
context.case('post-reload status transport uncertainty also stops without End/Cleanup', async () => {
    const result = await simulate([...prefix(), ['ReloadPoll', completed], ['editor_status', new Error('lost idle reply')]]);
    expectRecovery(result);
    assert.equal(result.commands.at(-1), 'editor_status');
});
context.case('known End assertion failure still safely cleans and retains primary evidence', async () => {
    for (const shape of shapes) {
        const failedEnd = { status: 'failed', checks: 3, failures: ['HDR mismatch'] };
        const result = await simulate([...prefix(), ['ReloadPoll', completed], ...suffix(failedEnd)], { shape });
        assert.ok(String(result.error).includes('HDR mismatch'));
        assert.notEqual(result.inner.recoveryRequired, true);
        assert.equal(result.inner.facts.nativeResults.end.checks, 3);
        assert.equal(result.commands.at(-1), 'Cleanup');
    }
});
context.case('cleanup failure retains both primary and restoration failure evidence', async () => {
    const result = await simulate([...prefix(), ['ReloadPoll', completed],
        ...suffix({ status: 'failed', checks: 2, failures: ['owner missing'] },
            { status: 'failed', checks: 0, failures: ['focus restoration failed'] })]);
    assert.ok(String(result.error).includes('owner missing'));
    assert.ok(String(result.error).includes('focus restoration failed'));
    assert.equal(result.inner.recoveryRequired, true);
    assert.equal(result.commands.at(-1), 'Cleanup');
});
context.case('End/Cleanup transport uncertainty issues no subsequent commands', async () => {
    for (const plan of [
        [...prefix(), ['ReloadPoll', completed], ['editor_status', idle], ['End', new Error('End timeout')]],
        [...prefix(), ['ReloadPoll', completed], ['editor_status', idle], ['End', end], ['Cleanup', new Error('Cleanup timeout')]]
    ]) {
        const result = await simulate(plan);
        assert.ok(result.error);
        assert.equal(result.inner.recoveryRequired, true);
        assert.ok(result.commands.at(-1) === 'End' || result.commands.at(-1) === 'Cleanup');
    }
});
context.case('per-command latency consumes the single bounded deadline', async () => {
    const result = await simulate(prefix(), { repeat: ['ReloadPoll', native()], latency: 7500 });
    expectRecovery(result);
    assert.ok(result.elapsed <= 45000);
    assert.ok(result.commands.length < 8);
    assert.ok(result.budgets.at(-1) < 8000);
});
context.case('known setup failure cleans fixture; known preflight failure never creates it', async () => {
    const failedSetup = await simulate([['editor_status', idle], ['Begin', { status: 'failed', checks: 0 }],
        ['Cleanup', cleanup]]);
    assert.ok(failedSetup.error);
    assert.notEqual(failedSetup.inner.recoveryRequired, true);
    assert.equal(failedSetup.commands.includes('Trigger'), false);
    const preflight = await simulate([['editor_status', busy]]);
    assert.ok(preflight.error);
    assert.notEqual(preflight.inner.recoveryRequired, true);
    assert.equal(preflight.commands.length, 1);
});
context.case('only exact read-only CLI network restart envelope is reconnectable', () => {
    assert.ok(networkError() instanceof Error);
    const candidates = [networkReply(networkEnvelope(), { code: 1 }),
        networkReply(networkEnvelope(), { timedOut: true }), networkReply(networkEnvelope(), { error: 'spawn failure' }),
        networkReply(networkEnvelope(), { stdout: '{malformed' }),
        networkReply({ ...networkEnvelope(), success: true }),
        networkReply({ ...networkEnvelope(), data: {} }),
        networkReply({ ...networkEnvelope(), command: 'unity command run_script' }),
        networkReply({ ...networkEnvelope(), errors: [] }),
        networkReply({ ...networkEnvelope(), errors: [{ code: 'NETWORK_ERROR', message: 'Network error' }] }),
        networkReply({ ...networkEnvelope(), errors: [{ code: 'COMMAND_FAILED', message: 'Compilation failed' }] }),
        networkReply({ ...networkEnvelope(), errors: [...networkEnvelope().errors, { code: 'OTHER', message: 'Unknown' }] }),
        networkReply({ ...networkEnvelope(), target: { projectPath: 'D:/DuneFTL' } }),
        networkReply({ ...networkEnvelope(), projectPath: 'D:/DuneFTL' })];
    for (const reply of candidates) assert.equal(editorStatusNetworkError(reply), null);
});
context.case('matching Editor status requires strict project and state for both wire shapes', () => {
    for (const shape of shapes) {
        assert.deepEqual(normalizeEditorStatus(shape(idle)), idle);
        for (const invalid of [null, [], { ...idle, projectPath: 'D:/DuneFTL' },
            { ...idle, projectPath: undefined }, { ...idle, compiling: 'false' },
            { ...idle, domainReloadInProgress: undefined }, { ...idle, playMode: 'unknown' }])
            assert.throws(() => normalizeEditorStatus(shape(invalid)));
    }
});
context.case('observed compilation reconnects read-only then requires matching idle and full native proof', async () => {
    for (const shape of shapes) {
        const result = await simulate([...prefix(), ['editor_status', busy],
            ['editor_status', networkError()], ['editor_status', networkError()],
            ['editor_status', { ...idle, domainReloadInProgress: true }], ['editor_status', networkError()],
            ['ReloadPoll', completed], ...suffix()], { shape });
        assert.equal(result.error, undefined);
        assert.notEqual(result.inner.recoveryRequired, true);
        assert.equal(result.inner.facts.editorReconnectErrors.length, 3);
        assert.equal(result.inner.facts.editorReconnectStatusReplies.length, 2);
        assert.equal(result.inner.facts.observedCompilation, true);
        assert.equal(result.inner.facts.nativeResults.reload.checks, 3);
        assert.equal(result.inner.facts.nativeResults.end.checks, 5);
        assert.equal(result.inner.facts.nativeResults.cleanup.checks, 1);
        assert.deepEqual(result.commands.slice(result.commands.indexOf('Trigger') + 1, result.commands.indexOf('ReloadPoll')),
            Array(6).fill('editor_status'), 'No C# while disconnected or busy');
    }
});
context.case('persistent status disconnect exhausts the existing deadline and retains uncertainty', async () => {
    const result = await simulate([...prefix(), ['editor_status', busy]], { repeat: ['editor_status', networkError()] });
    expectRecovery(result);
    assert.ok(result.elapsed >= 43000 && result.elapsed <= 45000);
    assert.ok(result.inner.facts.editorReconnectErrors.length > 1);
    assert.ok(result.commands.slice(result.commands.indexOf('Trigger') + 1).every(name => name === 'editor_status'));
});
context.case('network failure without Trigger acknowledgement or observed compilation is not retried', async () => {
    const preflight = await simulate([['editor_status', networkError()]]);
    assert.ok(preflight.error);
    assert.equal(preflight.inner.recoveryRequired, true);
    assert.equal(preflight.commands.length, 1);
    const pending = await simulate([...prefix(), ['editor_status', networkError()]]);
    expectRecovery(pending);
    assert.equal(pending.inner.facts.editorReconnectErrors, undefined);
    const domainOnly = await simulate([...prefix(), ['editor_status', { ...idle, domainReloadInProgress: true }],
        ['editor_status', networkError()]]);
    expectRecovery(domainOnly);
    assert.equal(domainOnly.inner.facts.editorReconnectErrors, undefined, 'A busy reload flag alone is not observed native compilation');
});
context.case('wrong-project or malformed reconnect status stops before native polling/writes', async () => {
    for (const status of [{ ...idle, projectPath: 'D:/DuneFTL' }, { ...idle, projectPath: undefined },
        { ...idle, compiling: 'false' }, { ...idle, playMode: 'playing' }]) {
        const result = await simulate([...prefix(), ['editor_status', busy], ['editor_status', networkError()],
            ['editor_status', status]]);
        expectRecovery(result);
        assert.equal(result.commands.includes('ReloadPoll'), false);
        assert.equal(result.inner.facts.editorReconnectErrors.length, 1);
    }
});
context.case('successful reconnect without real native marker cannot turn green', async () => {
    const result = await simulate([...prefix(), ['editor_status', busy], ['editor_status', networkError()]],
        { repeat: ['ReloadPoll', native({ reloaded: true })] });
    expectRecovery(result);
    assert.equal(result.inner.facts.editorReconnectErrors.length, 1);
    assert.equal(result.inner.facts.nativeResults.reload, undefined);
    assert.ok(result.elapsed <= 45000);
});
context.case('script calls and post-proof status failures are never reconnect-retried', async () => {
    for (const plan of [
        [['editor_status', idle], ['Begin', begin], ['Trigger', networkError()]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', networkError()]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', completed], ['editor_status', networkError()]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', completed], ['editor_status', idle], ['End', networkError()]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', completed], ['editor_status', idle], ['End', end], ['Cleanup', networkError()]]
    ]) {
        const result = await simulate(plan);
        assert.ok(result.error);
        assert.equal(result.inner.recoveryRequired, true);
        assert.equal(result.inner.facts.editorReconnectErrors, undefined);
        assert.ok(result.commands.filter(name => name === 'Trigger').length === 1);
    }
});
context.case('own read-only status process timeout needs exact command/project and empty evidence', () => {
    assert.ok(statusTimeout() instanceof EditorStatusProcessTimeoutError);
    for (const command of ['Begin', 'Trigger', 'ReloadPoll', 'End', 'Cleanup', 'run_script', 'recompile', undefined])
        assert.equal(editorStatusProcessTimeout(timeoutReply(), command), null);
    assert.equal(editorStatusProcessTimeout(timeoutReply(), 'editor_status', 'D:/DuneFTL'), null);
    for (const reply of [null, timeoutReply({ timedOut: false }), timeoutReply({ code: undefined }),
        timeoutReply({ error: 'spawn failure' }), timeoutReply({ error: '' }), timeoutReply({ stderr: 'Execution failed' }),
        timeoutReply({ target: { projectPath: 'D:/DuneFTL' } }), timeoutReply({ errors: ['unknown'] }),
        timeoutReply({ stdout: '{unknown JSON' }), timeoutReply({ stdout: JSON.stringify({ projectPath: 'D:/DuneFTL' }) }),
        timeoutReply({ stdout: JSON.stringify(idle) }), timeoutReply({ stdout: JSON.stringify(networkEnvelope()) })])
        assert.equal(editorStatusProcessTimeout(reply, 'editor_status'), null,
            'Timeout never overrides unknown JSON, target/error or completed reply evidence');
});
context.case('scoped status process timeout after acknowledged Trigger and observed compilation recovers', async () => {
    for (const shape of shapes) {
        const result = await simulate([...prefix(), ['editor_status', busy],
            ['editor_status', networkError()], ['editor_status', statusTimeout(), 8000],
            ['editor_status', { ...idle, domainReloadInProgress: true }, 4000],
            ['ReloadPoll', completed], ...suffix()], { shape });
        assert.equal(result.error, undefined);
        assert.notEqual(result.inner.recoveryRequired, true);
        assert.equal(result.inner.facts.editorReconnectTimeouts.length, 1);
        assert.equal(result.inner.facts.nativeTriggerRequests, 1);
        assert.equal(result.commands.filter(name => name === 'Trigger').length, 1);
        assert.equal(result.commands.filter(name => name === 'Begin').length, 1);
        assert.equal(result.commands.filter(name => name === 'ReloadPoll').length, 1);
        assert.equal(result.inner.facts.nativeResults.reload.checks, 3);
        assert.equal(result.inner.facts.nativeResults.end.checks, 5);
        assert.equal(result.inner.facts.nativeResults.cleanup.checks, 1);
        assert.ok(result.elapsed >= 12000 && result.elapsed <= 45000);
        assert.ok(result.commands.slice(result.commands.indexOf('Trigger') + 1,
            result.commands.indexOf('ReloadPoll')).every(name => name === 'editor_status'));
    }
});
context.case('persistent scoped status process timeouts consume the original deadline without native retries', async () => {
    const result = await simulate([...prefix(), ['editor_status', busy]],
        { repeat: ['editor_status', statusTimeout(), 8000] });
    expectRecovery(result);
    assert.ok(result.elapsed >= 43000 && result.elapsed <= 45000);
    assert.ok(result.inner.facts.editorReconnectTimeouts.length > 1);
    assert.ok(result.commands.slice(result.commands.indexOf('Trigger') + 1).every(name => name === 'editor_status'));
});
context.case('pretrigger and unobserved-compilation status process timeouts stop', async () => {
    const pretrigger = await simulate([['editor_status', statusTimeout(), 8000]]);
    assert.ok(pretrigger.error);
    assert.equal(pretrigger.inner.recoveryRequired, true);
    assert.equal(pretrigger.commands.length, 1);
    assert.equal(pretrigger.inner.facts.editorReconnectTimeouts, undefined);
    for (const plan of [[...prefix(), ['editor_status', statusTimeout(), 8000]],
        [...prefix(), ['editor_status', { ...idle, domainReloadInProgress: true }],
            ['editor_status', statusTimeout(), 8000]]]) {
        const result = await simulate(plan);
        expectRecovery(result);
        assert.equal(result.inner.facts.editorReconnectTimeouts, undefined);
    }
});
context.case('other commands and post-proof status process timeouts always stop', async () => {
    for (const plan of [
        [['editor_status', idle], ['Begin', statusTimeout(), 8000]],
        [['editor_status', idle], ['Begin', begin], ['Trigger', statusTimeout(), 8000]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', statusTimeout(), 8000]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', completed], ['editor_status', statusTimeout(), 8000]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', completed], ['editor_status', idle], ['End', statusTimeout(), 8000]],
        [...prefix(), ['editor_status', busy], ['ReloadPoll', completed], ['editor_status', idle], ['End', end], ['Cleanup', statusTimeout(), 10000]]
    ]) {
        const result = await simulate(plan);
        assert.ok(result.error);
        assert.equal(result.inner.recoveryRequired, true);
        assert.equal(result.inner.facts.editorReconnectTimeouts, undefined);
        assert.ok(result.commands.filter(name => name === 'Trigger').length <= 1);
    }
});
context.case('malformed or wrong-project status after timeout still stops before native proof', async () => {
    for (const state of ['{unknown JSON', { ...idle, projectPath: 'D:/DuneFTL' },
        { ...idle, compiling: 'false' }, { ...idle, playMode: 'playing' }]) {
        const result = await simulate([...prefix(), ['editor_status', busy],
            ['editor_status', statusTimeout(), 8000], ['editor_status', state]]);
        expectRecovery(result);
        assert.equal(result.commands.includes('ReloadPoll'), false);
        assert.equal(result.inner.facts.editorReconnectTimeouts.length, 1);
    }
});
context.case('generic status transport failure after observed compile is not restart evidence', async () => {
    for (const message of ['status timeout', 'wrong project target', 'missing CLI JSON', 'execution failed']) {
        const result = await simulate([...prefix(), ['editor_status', busy], ['editor_status', new Error(message)]]);
        expectRecovery(result);
        assert.equal(result.inner.facts.editorReconnectErrors, undefined);
    }
});
await finish(context);
