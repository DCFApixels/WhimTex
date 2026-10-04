// Transport-only protocol checks: every Editor interaction is mocked; no Unity is launched.
import path from 'node:path';
import fs from 'node:fs';
import { TestContext, finish, resultMarker } from '../../Framework/test-api.mjs';
import { root, validateCatalog, classifyReply, runScenario, bundleSources } from '../../scripts/run-tests.mjs';
import { orchestrateReload, removeOwnedSource, editorStatusProcessTimeout, EditorStatusProcessTimeoutError } from './ReloadOrchestration.mjs';

const protocol = new TestContext('Pure native reload protocol: mocks only, NOT actual Unity/reload coverage');
const assert = protocol.assert;
const test = (name, body) => protocol.case(name, body);
const project = path.resolve(root, '../..');
const timeoutReply = (overrides = {}) => ({ code: null, stdout: '', stderr: '', timedOut: true, ...overrides });
const envelope = result => ({ code: 0, stdout: JSON.stringify({ success: true, data: { success: true, result } }) });
const networkGap = (overrides = {}) => ({ code: 6, stdout: JSON.stringify({ success: false,
    command: 'unity command editor_status', data: null, errors: [{ code: 'COMMAND_FAILED',
        message: "Failed to execute command 'editor_status': Network error: An error occurred while sending the request." }],
    warnings: [], ...overrides }) });
const structured = (status, checks, message, extra = {}, objectResult = false) => {
    const result = { status, checks, message, failures: status === 'failed' ? [message] : [], ...extra };
    return envelope({ success: true, result: objectResult ? result : JSON.stringify(result) });
};
function phases(calls) { return calls.map(argv => argv[1] === 'run_script' ? argv[argv.indexOf('--entry') + 1].split('.').at(-1) : argv[1]); }

async function mock(options = {}) {
    const context = new TestContext('Mock native reload protocol, not Unity coverage');
    const calls = [];
    let triggerCount = 0, nativeRequests = 0, callbackQueued = false, editorBusy = false;
    let time = 0, editorPoll = 0, markerPoll = 0;
    let reconnectRemaining = options.networkGaps ?? 0;
    let networkStarted = false;
    const phaseCalls = {};
    const type = options.type ?? 'DocumentReloadTests';
    context.case('mock serial orchestration', () => orchestrateReload(context, {
        type, project, file: path.join(project, 'Temp/WhimTex/test-runs/mock.cs'), runId: '0'.repeat(32)
    }, {
        now: () => time, delay: async ms => { time += ms; },
        invoke: async (argv, budget) => {
            calls.push(argv);
            assert.ok(budget <= 8000 && budget >= 1500);
            assert.equal(argv[argv.indexOf('--project-path') + 1], project);
            const attemptStarted = time;
            time += options.slow ? 2000 : 1;
            const name = argv[1];
            assert.ok(['editor_status', 'run_script'].includes(name), 'No CLI Refresh/recompile/status fallback');
            const phase = name === 'run_script' ? argv[argv.indexOf('--entry') + 1].split('.').at(-1) : name;
            phaseCalls[phase] = (phaseCalls[phase] ?? 0) + 1;
            if (name === 'run_script') {
                assert.equal(editorBusy, false, 'Never run_script while native compilation/domain reload is observed');
                assert.equal(argv[argv.indexOf('--args') + 1], JSON.stringify(['0'.repeat(32)]));
                if (phase === 'Trigger') { triggerCount++; assert.equal(triggerCount, 1); callbackQueued = true; }
                if (phase === 'Cleanup') callbackQueued = false; // Actual main-thread callback detached before fixture disposal.
            }
            const injection = options.injectReplies?.find(item => item.phase === phase && item.occurrence === phaseCalls[phase]);
            if (injection) {
                time += injection.durationMs ?? 0;
                if (injection.error) throw Error(injection.error);
                return injection.value;
            }
            if (phase === 'editor_status' && options.statusTimeoutForeverAfter && phaseCalls[phase] >= options.statusTimeoutForeverAfter) {
                time = attemptStarted + budget; // The process budget includes dispatch, not an extra millisecond.
                return timeoutReply();
            }
            if (options.replaceReply?.phase === phase) {
                if (options.replaceReply.error) throw Error(options.replaceReply.error);
                return options.replaceReply.value;
            }
            if (options.lostPhase === phase) return { timedOut: true, code: null, stdout: '' };
            if (name === 'editor_status') {
                if (triggerCount && (networkStarted || editorPoll >= (options.gapAfterEditorPolls ?? 1))
                    && (options.persistentNetworkGap || reconnectRemaining > 0)) {
                    networkStarted = true;
                    reconnectRemaining--;
                    return options.badReconnectReply ?? networkGap();
                }
                const state = !triggerCount ? options.busyBeforeBegin ? 'compiling' : 'idle'
                    : options.busyForever ? 'compiling'
                    : (options.busyStates ?? [])[editorPoll++] ?? 'idle';
                editorBusy = state !== 'idle';
                return envelope({ projectPath: options.wrongProject || options.wrongProjectAfterReconnect && networkStarted ? path.join(project, 'different-project') : project,
                    compiling: state === 'compiling', ...(options.unknownEditor && triggerCount || options.unknownAfterReconnect && networkStarted ? {} : { domainReloadInProgress: state === 'reloading' }),
                    playMode: options.playing && triggerCount ? 'playing' : 'stopped' });
            }
            if (phase === 'ReloadPoll') {
                const stage = options.neverCallback ? 'queued' : options.neverReload ? 'issued'
                    : (options.pollStates ?? ['done'])[Math.min(markerPoll++, (options.pollStates ?? ['done']).length - 1)];
                const issued = stage !== 'queued' && !options.failedRequest;
                if (issued) { nativeRequests = 1; callbackQueued = false; }
                if (options.failedRequest) callbackQueued = false;
                const fields = { requestQueued: callbackQueued, requestCount: nativeRequests,
                    beforeReload: ['before', 'done'].includes(stage) ? 1 : 0, oldDomainGone: stage === 'done',
                    requestError: options.failedRequest ? 'RequestScriptCompilation rejected' : '',
                    compilationErrors: options.compilerErrors ? ['native compiler error'] : [] };
                return structured(options.failedRequest || options.compilerErrors ? 'failed' : stage === 'done' ? 'passed' : 'running',
                    5, 'owned reload markers', fields, options.objectResult);
            }
            return structured(options.failedPhase === phase || options.failedCleanup && phase === 'Cleanup' ? 'failed' : 'passed',
                phase === 'Cleanup' ? 0 : 3, phase + (options.failedPhase === phase ? ': original assertion failed' : ''),
                type === 'DocumentReloadTests' && phase === 'Verify' ? {
                    coverageBranch: options.carrierOnly ? 'carrier-only' : 'surviving-window', partialCoverage: Boolean(options.carrierOnly)
                } : {}, options.objectResult);
        }
    }));
    const result = await context.run();
    // Advancing a mocked update after cleanup must not start a previously queued request.
    if (callbackQueued && phases(calls).includes('Cleanup')) nativeRequests++;
    return { result, calls, context, triggerCount, nativeRequests, callbackQueued, time };
}

for (const type of ['DocumentReloadTests', 'GuideReloadTests']) for (const objectResult of [false, true])
test(type + ' single native trigger, structured object/string: ' + objectResult, async () => {
    const { result, calls, nativeRequests, triggerCount } = await mock({ type, objectResult });
    assert.equal(result.status, 'passed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(result.checks, 20, 'Count child assertions and actual Node assertions');
    assert.equal(nativeRequests, 1);
    assert.equal(triggerCount, 1);
    assert.deepEqual(phases(calls), ['editor_status', 'Begin', 'editor_status', 'Trigger', 'editor_status', 'ReloadPoll',
        'editor_status', 'Verify', 'editor_status', 'Cleanup']);
});
test('busy intervals issue only editor_status; no script compilation during native compilation/reload', async () => {
    const { result, calls } = await mock({ busyStates: ['compiling', 'compiling', 'reloading', 'idle'] });
    assert.equal(result.status, 'passed');
    assert.deepEqual(phases(calls).slice(4, 9), ['editor_status', 'editor_status', 'editor_status', 'editor_status', 'ReloadPoll']);
    assert.equal(phases(calls).filter(p => p === 'Trigger').length, 1);
});
test('stale idle waits for queued -> issued -> beforeReload -> old domain gone, then Verify', async () => {
    const { result, calls } = await mock({ pollStates: ['queued', 'issued', 'before', 'done'] });
    assert.equal(result.status, 'passed');
    assert.equal(phases(calls).filter(p => p === 'ReloadPoll').length, 4);
    assert.equal(phases(calls).filter(p => p === 'Trigger').length, 1);
    assert.equal(result.facts.ReloadPoll.testResult.beforeReload, 1);
    assert.equal(result.facts.ReloadPoll.testResult.oldDomainGone, true);
    assert.equal(phases(calls).indexOf('Verify') > phases(calls).lastIndexOf('ReloadPoll'), true);
});
test('expected native endpoint restart reconnects with ONLY read-only status requests', async () => {
    const { result, calls, triggerCount, nativeRequests } = await mock({ busyStates: ['compiling', 'idle'], networkGaps: 3 });
    assert.equal(result.status, 'passed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(result.facts.readOnlyReconnects.length, 3);
    assert.equal(triggerCount, 1);
    assert.equal(nativeRequests, 1);
    assert.deepEqual(phases(calls).slice(4, 10), ['editor_status', 'editor_status', 'editor_status', 'editor_status', 'editor_status', 'ReloadPoll']);
    for (const phase of ['Begin', 'Trigger', 'ReloadPoll', 'Verify', 'Cleanup'])
        assert.equal(phases(calls).filter(p => p === phase).length, 1, 'No C# retry across the connection gap: ' + phase);
    assert.equal(result.facts.ReloadPoll.testResult.beforeReload, 1);
    assert.equal(result.facts.ReloadPoll.testResult.oldDomainGone, true);
});
test('gap immediately after acknowledged Trigger is expected even if compilation was between status samples', async () => {
    const { result, calls } = await mock({ networkGaps: 2, gapAfterEditorPolls: 0 });
    assert.equal(result.status, 'passed');
    assert.equal(result.facts.readOnlyReconnects.length, 2);
    assert.equal(phases(calls).filter(p => p === 'Trigger').length, 1);
});
test('persistent native endpoint loss expires within original deadline and retains lock/fixtures', async () => {
    const { result, calls, time } = await mock({ persistentNetworkGap: true, slow: true, busyStates: ['compiling'] });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.ok(time < 45000);
    assert.equal(phases(calls).includes('ReloadPoll'), false);
    assert.equal(phases(calls).includes('Verify'), false);
    assert.equal(phases(calls).includes('Cleanup'), false);
    assert.equal(phases(calls).filter(p => p === 'Trigger').length, 1);
});
test('read-only process timeout recognizer is identical to strict D helper and never overrides reply evidence', () => {
    const d = fs.readFileSync(path.join(root, 'Tests~/Cases/UnityD/GradientReload.mjs'), 'utf8').replaceAll('\r\n', '\n');
    const dFunction = d.match(/export function editorStatusProcessTimeout[\s\S]*?\n}/)[0].replace('export ', '');
    assert.equal(editorStatusProcessTimeout.toString().replaceAll('\r\n', '\n'), dFunction);
    assert.ok(editorStatusProcessTimeout(timeoutReply(), 'editor_status', project) instanceof EditorStatusProcessTimeoutError);
    for (const command of ['Begin', 'Trigger', 'ReloadPoll', 'Verify', 'Cleanup', 'run_script', 'recompile', undefined])
        assert.equal(editorStatusProcessTimeout(timeoutReply(), command, project), null);
    assert.equal(editorStatusProcessTimeout(timeoutReply(), 'editor_status', path.join(project, 'different-project')), null);
    for (const reply of [null, [], timeoutReply({ timedOut: false }), timeoutReply({ code: undefined }),
        timeoutReply({ error: 'spawn failure' }), timeoutReply({ error: '' }), timeoutReply({ stderr: 'Execution failed' }),
        timeoutReply({ target: { projectPath: project } }), timeoutReply({ errors: ['unknown'] }),
        timeoutReply({ stdout: '{unknown JSON' }), timeoutReply({ stdout: JSON.stringify({ projectPath: project }) }),
        timeoutReply({ stdout: networkGap().stdout })])
        assert.equal(editorStatusProcessTimeout(reply, 'editor_status', project), null, 'Unknown/error/target/JSON evidence remains terminal');
});
for (const type of ['DocumentReloadTests', 'GuideReloadTests']) for (const objectResult of [false, true])
test(type + ' observed compile -> 8s empty status timeout -> 4s reload interval -> original native proof: ' + objectResult, async () => {
    const { result, calls, time } = await mock({ type, objectResult, busyStates: ['compiling', 'idle'], injectReplies: [
        { phase: 'editor_status', occurrence: 4, durationMs: 8000, value: timeoutReply() },
        { phase: 'editor_status', occurrence: 5, durationMs: 4000, value: envelope({ projectPath: project,
            compiling: false, domainReloadInProgress: true, playMode: 'stopped' }) }
    ] });
    assert.equal(result.status, 'passed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(result.facts.observedCompilation, true);
    assert.equal(result.facts.readOnlyStatusTimeouts.length, 1);
    assert.ok(time >= 12000 && time < 45000, 'Original deadline, not an extended native-operation budget');
    assert.ok(phases(calls).slice(4, phases(calls).indexOf('ReloadPoll')).every(phase => phase === 'editor_status'));
    for (const phase of ['Begin', 'Trigger', 'ReloadPoll', 'Verify', 'Cleanup'])
        assert.equal(phases(calls).filter(p => p === phase).length, 1, 'No native/mutating retry: ' + phase);
    assert.equal(result.facts.ReloadPoll.testResult.beforeReload, 1);
    assert.equal(result.facts.ReloadPoll.testResult.oldDomainGone, true);
});
test('persistent observed-compilation status process timeout exhausts original deadline, no proof/cleanup retry', async () => {
    const { result, calls, time } = await mock({ busyStates: ['compiling'], statusTimeoutForeverAfter: 4 });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.ok(time >= 40000 && time <= 45000);
    assert.ok(result.facts.readOnlyStatusTimeouts.length > 1);
    assert.ok(phases(calls).slice(4).every(phase => phase === 'editor_status'));
    assert.equal(phases(calls).filter(p => p === 'Trigger').length, 1);
});
for (const options of [
    { injectReplies: [{ phase: 'editor_status', occurrence: 1, durationMs: 8000, value: timeoutReply() }] },
    { injectReplies: [{ phase: 'editor_status', occurrence: 2, durationMs: 8000, value: timeoutReply() }] },
    { injectReplies: [{ phase: 'editor_status', occurrence: 3, durationMs: 8000, value: timeoutReply() }] },
    { busyStates: ['reloading'], injectReplies: [{ phase: 'editor_status', occurrence: 4, durationMs: 8000, value: timeoutReply() }] }
]) test('timeout before Trigger/without actual compiling flag never reconnects: ' + JSON.stringify(options), async () => {
    const { result, calls } = await mock(options);
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(result.facts.readOnlyStatusTimeouts, undefined);
    assert.equal(result.facts.observedCompilation, undefined);
    assert.equal(phases(calls).includes('ReloadPoll'), false);
    assert.equal(phases(calls).includes('Cleanup'), false);
});
for (const phase of ['Begin', 'Trigger', 'ReloadPoll', 'Verify', 'Cleanup'])
test(phase + ' timeout cannot use read-only reconnect exemption', async () => {
    const { result, calls } = await mock({ busyStates: ['compiling', 'idle'],
        injectReplies: [{ phase, occurrence: 1, durationMs: 8000, value: timeoutReply() }] });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(result.facts.readOnlyStatusTimeouts, undefined);
    assert.equal(phases(calls).filter(p => p === phase).length, 1);
    assert.ok(phases(calls).filter(p => p === 'Trigger').length <= 1);
});
for (const value of [timeoutReply(), networkGap()])
test('no reconnect after full native markers, even before Verify: ' + JSON.stringify(value), async () => {
    const { result, calls } = await mock({ busyStates: ['compiling', 'idle'],
        injectReplies: [{ phase: 'editor_status', occurrence: 5, value }] });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(result.facts.readOnlyStatusTimeouts, undefined);
    assert.equal(result.facts.readOnlyReconnects, undefined);
    assert.equal(phases(calls).includes('Verify'), false);
    assert.equal(phases(calls).includes('Cleanup'), false);
});
for (const value of [{ code: 0, stdout: 'not JSON' }, envelope({ projectPath: path.join(project, 'different-project'),
    compiling: false, domainReloadInProgress: false, playMode: 'stopped' }),
    envelope({ projectPath: project, compiling: 'false', domainReloadInProgress: false, playMode: 'stopped' }),
    envelope({ projectPath: project, compiling: false, domainReloadInProgress: false, playMode: 'playing' })])
test('malformed/mismatched/play status after timeout stays terminal: ' + JSON.stringify(value), async () => {
    const { result, calls } = await mock({ busyStates: ['compiling'], injectReplies: [
        { phase: 'editor_status', occurrence: 4, durationMs: 8000, value: timeoutReply() },
        { phase: 'editor_status', occurrence: 5, value }
    ] });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(result.facts.readOnlyStatusTimeouts.length, 1);
    assert.equal(phases(calls).includes('ReloadPoll'), false);
    assert.equal(phases(calls).includes('Cleanup'), false);
});
test('generic status exception after observed compilation is not own process-timeout evidence', async () => {
    const { result, calls } = await mock({ busyStates: ['compiling'],
        injectReplies: [{ phase: 'editor_status', occurrence: 4, error: 'status timeout' }] });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(result.facts.readOnlyStatusTimeouts, undefined);
    assert.equal(phases(calls).includes('ReloadPoll'), false);
});
test('reconnected idle still cannot Verify before the owned markers', async () => {
    const { result, calls } = await mock({ networkGaps: 2, busyStates: ['compiling'], pollStates: ['issued', 'before', 'done'] });
    assert.equal(result.status, 'passed');
    assert.equal(phases(calls).filter(p => p === 'ReloadPoll').length, 3);
    assert.equal(phases(calls).indexOf('Verify') > phases(calls).lastIndexOf('ReloadPoll'), true);
});
for (const options of [
    { replaceReply: { phase: 'editor_status', value: networkGap() } },
    { lostPhase: 'Trigger', networkGaps: 3 },
    { lostPhase: 'ReloadPoll', networkGaps: 2, busyStates: ['compiling'] },
    { networkGaps: 1, busyStates: ['compiling'], wrongProjectAfterReconnect: true },
    { networkGaps: 1, busyStates: ['compiling'], unknownAfterReconnect: true },
    { networkGaps: 1, busyStates: ['compiling'], badReconnectReply: networkGap({ errors: [{ code: 'COMMAND_FAILED', message: 'Other native failure' }] }) },
    { networkGaps: 1, busyStates: ['compiling'], badReconnectReply: networkGap({ command: 'unity command run_script' }) },
    { networkGaps: 1, busyStates: ['compiling'], badReconnectReply: { code: 6, stdout: 'not JSON' } }
]) test('network gap is NOT a broad command/C# retry permission: ' + JSON.stringify(options), async () => {
    const { result, calls } = await mock(options);
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(phases(calls).includes('Verify'), false);
    assert.equal(phases(calls).includes('Cleanup'), false);
    assert.ok(phases(calls).filter(p => p === 'Trigger').length <= 1);
    assert.ok(phases(calls).filter(p => p === 'ReloadPoll').length <= 1);
});
test('unexecuted callback is cancelled by known-idle Cleanup at the bounded deadline', async () => {
    const { result, calls, nativeRequests, callbackQueued, time } = await mock({ neverCallback: true, slow: true });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(phases(calls).includes('Verify'), false);
    assert.equal(phases(calls).at(-1), 'Cleanup');
    assert.equal(nativeRequests, 0, 'No compilation after cancellation/advancing the update');
    assert.equal(callbackQueued, false);
    assert.ok(time < 55000);
    assert.match(result.failures[0].message, /callback did not execute/);
});
for (const options of [{ neverReload: true, slow: true }, { busyForever: true, slow: true }])
test('issued/busy timeout retains lock without pretending native cancellation: ' + JSON.stringify(options), async () => {
    const { result, calls } = await mock(options);
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(phases(calls).includes('Verify'), false);
    assert.equal(phases(calls).includes('Cleanup'), false);
    assert.equal(phases(calls).filter(p => p === 'Trigger').length, 1);
});
for (const options of [{ failedRequest: true }, { compilerErrors: true }])
test('explicit idle native request/compiler failure is failed and cleans ownership: ' + JSON.stringify(options), async () => {
    const { result, calls } = await mock(options);
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(phases(calls).includes('Verify'), false);
    assert.equal(phases(calls).at(-1), 'Cleanup');
});
test('carrier-only green fallback remains explicitly partial', async () => {
    const { result } = await mock({ carrierOnly: true });
    assert.equal(result.status, 'passed');
    assert.equal(result.facts.documentReloadCoverage.partial, true);
    assert.equal(result.facts.documentReloadCoverage.branch, 'carrier-only');
});
for (const phase of ['Begin', 'Trigger', 'Verify'])
test('known ' + phase + ' assertion failure is not erased by zero-check Cleanup', async () => {
    const { result, calls } = await mock({ failedPhase: phase });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(result.facts[phase].testResult.status, 'failed');
    assert.equal(result.facts.Cleanup.testResult.status, 'passed');
    assert.equal(phases(calls).at(-1), 'Cleanup');
    if (phase === 'Begin') assert.equal(phases(calls).includes('Trigger'), false);
});
test('cleanup failure preserves the primary assertion failure and requests recovery', async () => {
    const { result } = await mock({ failedPhase: 'Verify', failedCleanup: true });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.equal(result.facts.Verify.testResult.status, 'failed');
    assert.equal(result.facts.Cleanup.testResult.status, 'failed');
    assert.match(result.failures[0].message, /Primary verdict and cleanup failure/);
    assert.equal(result.facts.recovery.cleanupEntry, 'DocumentReloadTests.Cleanup');
});
for (const options of [
    ...['Begin', 'Trigger', 'ReloadPoll', 'Verify', 'Cleanup'].map(lostPhase => ({ lostPhase })),
    { unknownEditor: true }, { wrongProject: true }, { busyBeforeBegin: true }, { playing: true },
    { replaceReply: { phase: 'Trigger', value: null } },
    { replaceReply: { phase: 'Trigger', value: { code: 0, stdout: 'null' } } },
    { replaceReply: { phase: 'Trigger', value: { code: 0, stdout: 'not JSON' } } },
    { replaceReply: { phase: 'Trigger', error: 'transport disconnected' } },
    ...['Begin', 'Trigger', 'ReloadPoll'].map(phase => ({ replaceReply: { phase, value: structured('passed', 0, 'empty oracle') } })),
    { replaceReply: { phase: 'ReloadPoll', value: structured('passed', 5, 'forged status', {
        requestQueued: false, requestCount: 1, beforeReload: 0, oldDomainGone: false, compilationErrors: [] }) } },
    { replaceReply: { phase: 'ReloadPoll', value: structured('running', 5, 'bad markers', {
        requestQueued: true, requestCount: 1, beforeReload: 0, oldDomainGone: false, compilationErrors: [] }) } },
    { replaceReply: { phase: 'ReloadPoll', value: structured('running', 5, 'missing markers') } },
    { replaceReply: { phase: 'ReloadPoll', value: structured('running', 0, 'no real assertions', {
        requestQueued: true, requestCount: 0, beforeReload: 0, oldDomainGone: false, compilationErrors: [] }) } },
    { replaceReply: { phase: 'ReloadPoll', value: structured('running', 5, 'no callback', {
        requestQueued: false, requestCount: 0, beforeReload: 0, oldDomainGone: false, compilationErrors: [] }) } },
    { replaceReply: { phase: 'ReloadPoll', value: structured('running', 5, 'domain gone without before event', {
        requestQueued: false, requestCount: 1, beforeReload: 0, oldDomainGone: true, compilationErrors: [] }) } },
    { replaceReply: { phase: 'ReloadPoll', value: structured('passed', 5, 'malformed compiler diagnostics', {
        requestQueued: false, requestCount: 1, beforeReload: 1, oldDomainGone: true, compilationErrors: 'none' }) } }
]) test('transport/unknown Editor or marker uncertainty: ' + JSON.stringify(options), async () => {
    const { result, calls } = await mock(options);
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, true);
    assert.ok(phases(calls).filter(p => p === 'Trigger').length <= 1);
    if (options.lostPhase !== 'Cleanup') assert.equal(phases(calls).includes('Cleanup'), false);
    if (options.wrongProject || options.busyBeforeBegin) assert.equal(phases(calls).includes('Begin'), false);
    const classified = classifyReply({ code: 1, stdout: resultMarker + JSON.stringify(result) },
        { runner: 'node', requiresUnity: true, result: { kind: 'structured' } });
    assert.equal(classified.status, 'assertion-failed');
    assert.equal(classified.uncertain, true, 'Outer runner retains the exact lock after uncertain child completion');
});
test('known API rejection is non-green but can run independently acknowledged cleanup', async () => {
    const { result, calls } = await mock({ replaceReply: { phase: 'Verify',
        value: { code: 0, stdout: JSON.stringify({ success: true, data: { success: false } }) } } });
    assert.equal(result.status, 'failed');
    assert.equal(result.recoveryRequired, undefined);
    assert.equal(result.facts.Verify.status, 'api-failed');
    assert.equal(phases(calls).at(-1), 'Cleanup');
});
test('temporary source cleanup rejects redirected/unowned paths without recursive deletion', () => {
    const directory = path.join(project, 'Temp/WhimTex/test-runs');
    const file = path.join(directory, 'unity-b-reload-' + '0'.repeat(32) + '.cs');
    const removed = [];
    const io = { lstatSync: () => ({ isFile: () => true, isSymbolicLink: () => false }),
        realpathSync: value => value, unlinkSync: value => removed.push(value) };
    removeOwnedSource(file, directory, directory, io);
    assert.deepEqual(removed, [file]);
    for (const bad of [
        { ...io, realpathSync: () => path.join(project, 'outside') },
        { ...io, lstatSync: () => ({ isFile: () => true, isSymbolicLink: () => true }) },
        { ...io, lstatSync: () => ({ isFile: () => false, isSymbolicLink: () => false }) }
    ]) assert.throws(() => removeOwnedSource(file, directory, directory, bad), /Refusing/);
    assert.throws(() => removeOwnedSource(path.join(directory, 'user.cs'), directory, directory, io), /Refusing/);
    assert.throws(() => removeOwnedSource(file, path.dirname(directory), directory, io), /Refusing/);
    assert.deepEqual(removed, [file]);
});
test('source bundle contains only supported native trigger, real markers and cancellable deferred work', () => {
    for (const type of ['DocumentReloadTests', 'GuideReloadTests']) {
        const sources = ['Tests~/Framework/TestApi.cs', 'Tests~/Cases/UnityB/UnityBSupport.cs',
            'Tests~/Cases/UnityB/ReloadSupport.cs', 'Tests~/Cases/UnityB/' + type + '.cs'];
        const bundled = bundleSources(sources);
        for (const source of sources) assert.ok(bundled.includes('#line 1 "' + source + '"'));
        for (const entry of ['Begin', 'Trigger', 'ReloadPoll', 'Verify', 'Cleanup'])
            assert.ok(bundled.includes('public static string ' + entry + '(string runId)'));
        assert.ok(bundled.includes('AssemblyReloadEvents.AssemblyReloadCallback callback = null'));
        assert.ok(bundled.includes('as UnityEditor.AssemblyReloadEvents.AssemblyReloadCallback'));
        assert.ok(bundled.includes('AssemblyReloadEvents.beforeAssemblyReload += callback'));
        assert.ok(bundled.includes('System.AppDomain.CurrentDomain.GetData(key) == null'));
        assert.doesNotMatch(bundled, /UnityEditorInternal|Tests~\/Legacy\/|DocumentReloadSmoke\.|GuideReloadSmoke\./);
    }
    const support = fs.readFileSync(path.join(root, 'Tests~/Cases/UnityB/ReloadSupport.cs'), 'utf8');
    const nativeSupport = support.replace(/#if UNITY_6000_4_OR_NEWER\s*([\s\S]*?)#else[\s\S]*?#endif/g, '$1');
    assert.ok(nativeSupport.includes('GetEntityId().ToString()'));
    assert.doesNotMatch(nativeSupport, /GetInstanceID|activeInstanceID|InstanceIDToObject/);
    assert.ok(support.includes('snapshot.version != 3'));
    assert.equal(support.split('CompilationPipeline.RequestScriptCompilation();').length - 1, 1, 'Exactly one supported native request call site');
    assert.ok(support.includes('EditorApplication.CallbackFunction callback = null'));
    assert.ok(support.includes('EditorApplication.update -= request'));
    assert.ok(support.indexOf('EditorApplication.update -= request') < support.indexOf('try { fixtureCleanup(); }'));
    assert.ok(support.includes('assemblyCompilationFinished -= errors'));
    assert.ok(support.includes('state.requestCount == 0 && state.beforeReload == 0'));
    assert.ok(support.includes('!state.triggered && !state.requestQueued'), 'Even a failed deferred callback cannot permit a duplicate Trigger');
    assert.ok(support.includes('Native request already issued without a completed domain reload'), 'Cleanup detects the callback/request race while native work is still queued');
    assert.doesNotMatch(support, /AssetDatabase.Refresh|SaveAssets|ImportAsset|BuildPipeline/);
    const helper = fs.readFileSync(path.join(root, 'Tests~/Cases/UnityB/ReloadOrchestration.mjs'), 'utf8');
    assert.doesNotMatch(helper, /command\('recompile(?:_status)?'\)/);
});
test('two stable-ID Node scenarios validate against current catalog v2, timeout 60000', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/Batches/unity-b.json'), 'utf8'));
    const records = manifest.replacements.filter(r => ['DocumentReloadSmoke.cs', 'GuideReloadSmoke.cs'].includes(r.legacyFile));
    assert.equal(records.length, 2);
    const scenarios = records.flatMap(r => r.scenarios);
    assert.deepEqual(scenarios.map(s => s.id), ['document-reload-smoke-v2', 'guide-reload-smoke-v2']);
    validateCatalog({ version: 2, scenarios, profiles: { reload: scenarios.map(s => s.id) } });
    assert.ok(records.every(r => r.state === 'ported-unverified' && r.newFile.endsWith('.cs')));
    assert.ok(scenarios.every(s => s.timeoutMs === 60000 && s.requiresUnity && !s.supportFiles));
});

test('C# local recovery envelopes are uncertain, including object/string payloads', () => {
    const scenario = { runner: 'run_script', result: { kind: 'structured' } };
    for (const objectResult of [false, true]) {
        const failed = classifyReply(structured('failed', 3, 'primary plus manual cleanup failure', { recoveryRequired: true }, objectResult), scenario);
        assert.equal(failed.status, 'assertion-failed'); assert.equal(failed.uncertain, true);
        assert.equal(failed.testResult.recoveryRequired, true);
        const duplicate = classifyReply(structured('failed', 0, 'duplicate did not acquire journal', { recoveryRequired: false }, objectResult), scenario);
        assert.equal(duplicate.status, 'assertion-failed'); assert.equal(duplicate.uncertain, false);
    }
});
test('Contradictory C# passed plus retained journal is never green', () => {
    for (const objectResult of [false, true]) {
        const result = classifyReply(structured('passed', 2, 'bridge passed but manual journal remains', { recoveryRequired: true }, objectResult),
            { runner: 'run_script', result: { kind: 'structured' } });
        assert.equal(result.status, 'protocol-error'); assert.equal(result.uncertain, true);
    }
});
test('Nested manual cleanup failure preserves primary recovery without generic cleanup replay', async () => {
    const calls = [];
    const outcome = await runScenario({ id: 'mock-manual-cleanup', runner: 'run_script', result: { kind: 'structured' }, timeoutMs: 1000,
        entry: 'CaptureWorkflow', args: [], cleanup: { entry: 'CleanupRun', args: [] } }, async (_s, entry) => {
        calls.push(entry);
        return structured('failed', entry === 'CleanupRun' ? 0 : 2, entry === 'CleanupRun' ? 'manual journal retained after bridge cleanup' : 'body and cleanup AggregateException',
            { recoveryRequired: true });
    });
    assert.deepEqual(calls, ['CaptureWorkflow']); assert.equal(outcome.status, 'assertion-failed');
    assert.equal(outcome.uncertain, true); assert.equal(outcome.testResult.recoveryRequired, true);
    assert.deepEqual(outcome.testResult.failures, ['body and cleanup AggregateException']);
});
test('Export exact-journal absence recovery and acquired-only local envelope source guards', () => {
    const source = fs.readFileSync(path.join(root, 'Tests~/Cases/UnityB/ExportWindowTests.cs'), 'utf8');
    const recovery = source.slice(source.indexOf('public static string RecoverVisual'), source.indexOf('[Serializable] sealed class VisualResult'));
    assert.match(recovery, /journal\.owner, journal\.document, journal\.pixels, journal\.window, journal\.initialDocument/);
    assert.match(recovery, /ids\.Add\(id\)/); assert.match(recovery, /originals\.Add\(id\) && !ids\.Contains\(id\)/);
    assert.match(recovery, /Resources\.FindObjectsOfTypeAll<Object>\(\)/);
    assert.match(recovery, /ids\.Contains\(Identity\(value\)\)/);
    const absent = recovery.slice(recovery.indexOf('if (allAbsent)'), recovery.indexOf('// Validate every ordinary'));
    assert.match(absent, /SessionState\.EraseString\(key\)/);
    assert.doesNotMatch(absent, /DestroyImmediate|\.Close\(|\.Focus\(|\.name\s*=/);
    assert.ok(recovery.indexOf('if (allAbsent)') < recovery.indexOf('Owned<EditorWindow>'));
    const sequence = source.slice(source.indexOf('public static string VisualSequence'));
    assert.ok(sequence.indexOf('GUID already owns') < sequence.indexOf('var errors'));
    assert.match(sequence, /catch \(Exception error\) \{ errors\.Add\(error\); \}/);
    assert.match(sequence, /AggregateException\("Visual body and owned cleanup failures retained", errors\)/);
    assert.match(sequence, /bool recovery = acquired && SessionState/);
    assert.match(sequence, /new VisualResult \{ status = result\.status, checks = result\.checks, message = result\.message,/);
    assert.match(sequence, /failures = result\.failures, recoveryRequired = recovery/);
});
test('Gradient own acquisition receipt and terminal cleanup recovery source guards', () => {
    const source = fs.readFileSync(path.join(root, 'Tests~/Cases/UnityB/GradientHistoryTests.cs'), 'utf8');
    assert.match(source, /SaveCapture\(journal\);\s*if\(workflowToken!=null\)SessionState\.SetString\(WorkflowKey\(runId\),workflowToken\)/);
    assert.match(source, /ReadCapture\(runId\)\.workflowToken!=workflowToken/);
    assert.match(source, /AggregateException\("Capture body and owned cleanup failures retained",errors\)/);
    assert.match(source, /result\.status!="running"&&SessionState\.GetString\(WorkflowKey\(normalized\),""\)\.Length!=0&&SessionState\.GetString\(CaptureKey\(normalized\),""\)\.Length!=0/);
    assert.match(source, /failures=result\.failures,recoveryRequired=recovery/);
    assert.match(source, /Duplicate capture request did not acquire a journal or bridge/);
    assert.match(source, /recoveryRequired=false/);
    assert.match(source, /WorkflowEnvelope\(runId,UnityBRun\.Poll\(runId\)\)/);
    assert.match(source, /WorkflowEnvelope\(runId,await UnityBRun\.Cancel\(runId\)\)/);
    assert.match(source, /WorkflowEnvelope\(runId,UnityBRun\.Cleanup\(runId\)\)/);
});

await finish(protocol);
