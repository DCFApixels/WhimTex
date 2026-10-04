import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { root, bundleSources, commandArgs, runProcess, classifyReply,
    productionFingerprint, reviewFingerprint } from '../../scripts/run-tests.mjs';

const caseFile = 'Tests~/Cases/UnityD/GradientReload.mjs';
const sources = ['Tests~/Cases/UnityD/WhimTexGradientReloadDiagnostic.cs', 'Tests~/Framework/TestApi.cs'];
const dependencies = [caseFile, ...sources, 'Tests~/Framework/test-api.mjs', 'Tests~/scripts/run-tests.mjs',
    'Tests~/Cases/UnityD/GradientReloadProtocol.test.mjs', 'src/UnityObjectID.cs'];
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const authorizedProject = path.resolve('D:/DCFA/Projects/Test6.6');

export class EditorStatusNetworkError extends Error {}
export class EditorStatusProcessTimeoutError extends Error {}

// Only our exact read-only status process, with no reply/error evidence, may identify
// a killed status probe. Eligibility after Trigger/observed compilation is separate.
export function editorStatusProcessTimeout(reply, command, project = authorizedProject) {
    if (command !== 'editor_status' || typeof project !== 'string' || path.resolve(project) !== authorizedProject
        || !reply || typeof reply !== 'object' || Array.isArray(reply)
        || Object.keys(reply).some(key => !['code', 'stdout', 'stderr', 'timedOut', 'error'].includes(key))
        || reply.timedOut !== true || reply.error !== undefined
        || !(reply.code === null || Number.isSafeInteger(reply.code))
        || typeof reply.stdout !== 'string' || reply.stdout.trim() !== ''
        || typeof reply.stderr !== 'string' || reply.stderr.trim() !== '') return null;
    return new EditorStatusProcessTimeoutError('Own read-only Editor status process timed out without a reply: '
        + JSON.stringify(reply));
}

// Only the observed CLI restart envelope is eligible here. Generic errors, process
// timeouts, malformed replies and mismatched targets are not network-error evidence.
export function editorStatusNetworkError(reply, project = authorizedProject) {
    if (reply.code !== 6 || reply.timedOut || reply.error) return null;
    let envelope;
    try { envelope = JSON.parse(reply.stdout); } catch { return null; }
    if (envelope?.success !== false || envelope.data !== null
        || !['unity command editor_status', 'command editor_status'].includes(envelope.command)
        || !Array.isArray(envelope.errors) || !envelope.errors.length
        || envelope.errors.some(error => error?.code !== 'COMMAND_FAILED' || typeof error.message !== 'string'
            || !/^Failed to execute command 'editor_status': Network error:/i.test(error.message))) return null;
    for (const target of [envelope.target?.projectPath, envelope.projectPath])
        if (target !== undefined && (typeof target !== 'string' || path.resolve(target) !== project)) return null;
    return new EditorStatusNetworkError('Read-only Editor status endpoint unavailable: ' + JSON.stringify(envelope));
}

export function normalizeEditorStatus(result, project = authorizedProject) {
    let payload = result;
    if (typeof payload === 'string') {
        try { payload = JSON.parse(payload); } catch { throw Error('Malformed Editor status JSON string'); }
    }
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)
        || typeof payload.projectPath !== 'string' || path.resolve(payload.projectPath) !== project
        || typeof payload.compiling !== 'boolean' || typeof payload.domainReloadInProgress !== 'boolean'
        || !['stopped', 'playing', 'paused'].includes(payload.playMode))
        throw Error('Unknown or mismatched Editor state: ' + JSON.stringify(payload));
    return payload;
}

export function normalizeReloadStatus(result) {
    let payload = result;
    if (typeof payload === 'string') {
        try { payload = JSON.parse(payload); }
        catch { throw Error('Malformed native reload status JSON string'); }
    }
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)
        || !['running', 'passed', 'failed'].includes(payload.status)
        || !Number.isSafeInteger(payload.checks) || payload.checks < 0 || typeof payload.message !== 'string'
        || !Array.isArray(payload.failures) || payload.failures.some(error => typeof error !== 'string')
        || (payload.status === 'failed') !== (payload.failures.length > 0)
        || ['queued', 'requested', 'reloaded', 'oldDomainCleared'].some(key => typeof payload[key] !== 'boolean')
        || !Number.isSafeInteger(payload.requests) || payload.requests < 0
        || !Array.isArray(payload.errors) || payload.errors.some(error => typeof error !== 'string'))
        throw Error('Unknown native reload status payload: ' + JSON.stringify(payload));
    return payload; // Never coerce flags or discard native error evidence from either wire shape.
}

// Exported to test the stop/cleanup protocol without launching Unity or writing fixtures.
export async function driveReload(context, { request, now = Date.now, delay = sleep, project = authorizedProject }) {
    const assert = context.assert;
    const started = now(), operationDeadline = started + 45000, cleanupDeadline = started + 55000;
    let fixtureAttempted = false, canCleanup = false, primaryError, cleanupError;
    let triggerAcknowledged = false, observedCompilation = false, nativeProofConfirmed = false;
    let reconnectPending = false;
    context.facts.nativeTriggerRequests = 0;
    function unknown(message) {
        context.recoveryRequired = true;
        throw Error(message);
    }
    async function call(name, cleanup = false) {
        for (;;) {
            if (context.recoveryRequired) throw Error('No further Unity commands after uncertain execution');
            const remaining = (cleanup ? cleanupDeadline : operationDeadline) - now();
            if (remaining < 1100) unknown('Bounded deadline exhausted; completion/recovery must be inspected externally');
            try {
                let result = await request(name, Math.min(cleanup ? 10000 : 8000, remaining));
                if (name === 'editor_status') {
                    result = normalizeEditorStatus(result, project);
                    if (triggerAcknowledged && result.compiling && result.playMode === 'stopped') {
                        observedCompilation = true;
                        context.facts.observedCompilation = true;
                    }
                    if (reconnectPending) {
                        (context.facts.editorReconnectStatusReplies ??= []).push({ elapsedMs: now() - started, state: result });
                        // Merely reconnecting does not constitute native proof or authorize End.
                        if (!result.compiling && !result.domainReloadInProgress && result.playMode === 'stopped')
                            reconnectPending = false;
                    }
                }
                return result;
            }
            catch (error) {
                if (name === 'editor_status' && triggerAcknowledged && observedCompilation && !nativeProofConfirmed
                    && !cleanup && (error instanceof EditorStatusNetworkError || error instanceof EditorStatusProcessTimeoutError)) {
                    (context.facts.editorReconnectErrors ??= []).push({ elapsedMs: now() - started, message: String(error) });
                    if (error instanceof EditorStatusProcessTimeoutError)
                        (context.facts.editorReconnectTimeouts ??= []).push({ elapsedMs: now() - started, message: String(error) });
                    reconnectPending = true;
                    const pause = Math.min(500, operationDeadline - now() - 1100);
                    if (pause <= 0) unknown('Original operation deadline exhausted during read-only reconnect');
                    await delay(pause);
                    continue; // Read-only status only, within the original deadline. Never retry C#.
                }
                unknown(name + ': unknown transport/execution result: ' + error);
            }
        }
    }
    async function waitIdle(initial) {
        let state = initial ?? await call('editor_status');
        for (;;) {
            if (state.playMode !== 'stopped') unknown('Editor entered play mode; no automatic cleanup/retry');
            if (!state.compiling && !state.domainReloadInProgress) return state;
            await delay(500);
            state = await call('editor_status');
        }
    }
    function countNative(result, phase) {
        (context.facts.nativeResults ??= {})[phase] = result;
        context.checks += result.checks; // Actual structured C# assertions, including failed assertions.
    }
    try {
        const before = await call('editor_status');
        assert.equal(before.compiling, false, 'Initial Editor must not already be compiling');
        assert.equal(before.domainReloadInProgress, false, 'Initial Editor must not already be reloading/importing');
        assert.equal(before.playMode, 'stopped', 'Initial Editor must be stopped');
        canCleanup = true;
        fixtureAttempted = true;
        const begin = await call('Begin');
        countNative(begin, 'begin');
        assert.equal(begin.status, 'skipped', 'Begin is fixture setup, not a regression pass: ' + JSON.stringify(begin));
        assert.equal(begin.checks, 0, 'Setup must not fabricate regression assertions');

        canCleanup = false;
        context.facts.nativeTriggerRequests++;
        const trigger = normalizeReloadStatus(await call('Trigger'));
        countNative(trigger, 'trigger');
        if (trigger.status !== 'running' || !trigger.queued || trigger.requested || trigger.requests !== 0
            || trigger.reloaded || trigger.oldDomainCleared || trigger.errors.length || trigger.checks !== 0)
            unknown('Deferred Trigger did not acknowledge a fresh queued request: ' + JSON.stringify(trigger));
        triggerAcknowledged = true;
        let reload;
        for (;;) {
            await delay(500);
            // editor_status is the supported non-main-thread probe. Never dispatch run_script
            // while compilation/reload/import is busy; it may queue across a dying AppDomain.
            const editor = await call('editor_status');
            (context.facts.editorPollStates ??= []).push(editor);
            if (editor.playMode !== 'stopped') unknown('Editor entered play mode while native request was pending');
            if (editor.compiling || editor.domainReloadInProgress) continue;
            // No CLI recompile/refresh fallback. A no-op cannot satisfy both native proof fields.
            try { reload = normalizeReloadStatus(await call('ReloadPoll')); }
            catch (error) { unknown('Invalid native ReloadPoll evidence: ' + error); }
            (context.facts.reloadStates ??= []).push(reload);
            if (reload.status === 'failed' || reload.errors.length || reload.requests > 1)
                unknown('Native request/reload failure; completion uncertain: ' + JSON.stringify(reload));
            if (reload.status === 'passed') {
                if (!reload.requested || reload.requests !== 1 || !reload.reloaded || !reload.oldDomainCleared
                    || reload.queued || reload.checks !== 3)
                    unknown('Terminal native reload lacks real marker, old-domain release or single-request proof: ' + JSON.stringify(reload));
                countNative(reload, 'reload');
                nativeProofConfirmed = true;
                break;
            }
            if (reload.checks !== 0) unknown('Pending native reload must not invent assertion counts');
        }
        context.facts.editorAfterReload = await waitIdle();
        canCleanup = true;
        const end = await call('End');
        countNative(end, 'end');
        assert.equal(end.status, 'passed', 'Actual reload assertion failed: ' + JSON.stringify(end));
        assert.equal(end.checks, 5, 'Real reload marker, restored owner, HDR4, alpha .3 and midpoint .23 all asserted');
        assert.equal(context.facts.nativeTriggerRequests, 1, 'Only one native Trigger invocation was issued');
    }
    catch (error) {
        // No malformed reply/assertion may accidentally release the lock after Trigger.
        if (fixtureAttempted && !canCleanup) context.recoveryRequired = true;
        primaryError = error;
    }
    finally {
        if (fixtureAttempted && canCleanup && !context.recoveryRequired) {
            try {
                const cleanup = await call('Cleanup', true);
                countNative(cleanup, 'cleanup');
                if (cleanup.status !== 'passed' || cleanup.checks === 0) {
                    context.recoveryRequired = true;
                    throw Error('Owned fixture cleanup failed: ' + JSON.stringify(cleanup));
                }
                assert.ok(cleanup.checks > 0, 'Fresh invocation asserts that owned SessionState was erased');
            }
            catch (error) { cleanupError = error; }
        }
        context.facts.cleanupCommandSuppressed = fixtureAttempted && (!canCleanup || context.recoveryRequired)
            && !context.facts.nativeResults?.cleanup;
    }
    if (primaryError && cleanupError)
        throw new AggregateError([primaryError, cleanupError], String(primaryError) + '; ' + String(cleanupError));
    if (primaryError || cleanupError) throw primaryError ?? cleanupError;
}

export async function runGradientReload(context, projectArgument) {
    const assert = context.assert;
    assert.equal(typeof projectArgument, 'string', 'An explicit project path is required');
    const project = path.resolve(projectArgument);
    assert.equal(project.toLowerCase(), path.resolve('D:/DCFA/Projects/Test6.6').toLowerCase(), 'Only Test6.6 is authorized');
    assert.equal(project, path.resolve(root, '../..'), 'Package must belong to the exact requested project');
    const directory = path.join(project, 'Temp/WhimTex/test-runs');
    assert.equal(fs.realpathSync(directory), directory, 'Outer report directory must not redirect outside the project');
    const lockFile = path.join(directory, 'runner.lock');
    const lock = JSON.parse(fs.readFileSync(lockFile, 'utf8'));
    assert.equal(lock.pid, process.ppid, 'This integration must be the child of the locked outer runner');
    assert.equal(path.dirname(path.resolve(lock.output)), directory, 'Outer evidence report is inside its owned report directory');
    const outerReport = JSON.parse(fs.readFileSync(lock.output, 'utf8'));
    assert.equal(path.resolve(outerReport.projectPath), project, 'Outer lock/report targets the same project');
    assert.ok(outerReport.selected.includes('whimtex-gradient-reload-v2'), 'Outer runner explicitly selected this integration');
    const lockBytes = fs.readFileSync(lockFile);
    const runId = randomUUID();
    const folder = path.join(directory, 'gradient-reload-' + runId);
    const file = path.join(folder, 'input.cs');
    const source = bundleSources(sources);
    const fingerprint = () => ({ reviewed: reviewFingerprint([{ file: caseFile, reviewFiles: dependencies.slice(1) }]),
        production: productionFingerprint(root) });
    const before = fingerprint();
    assert.equal(before.production, outerReport.productionFingerprint, 'Production sources still match the outer locked review');
    context.facts.input = { runId, file, sources, sha256: createHash('sha256').update(source).digest('hex') };
    context.facts.fingerprint = { before, outer: outerReport.fingerprint, outerProduction: outerReport.productionFingerprint };
    context.facts.projectPath = project;
    const attempts = context.facts.cliEvidence = [];
    const scenario = { id: 'reload-invocation', file: path.relative(root, file), runner: 'run_script',
        args: [runId], result: { kind: 'structured' },
        async: { entry: 'WhimTexGradientReloadDiagnostic.ReloadPoll', args: [runId], pollMs: 500 } };
    async function request(name, budget) {
        const script = ['Begin', 'Trigger', 'ReloadPoll', 'End', 'Cleanup'].includes(name);
        if (!script && name !== 'editor_status') throw Error('Command outside the owned native reload protocol: ' + name);
        const entry = 'WhimTexGradientReloadDiagnostic.' + name;
        const argv = script ? commandArgs(scenario, entry, [runId], budget, project)
            : ['command', name, '--project-path', project, '--timeout', String(Math.ceil(budget / 1000)), '--format', 'json'];
        // Check the still-held outer lock without taking or deleting a second lock.
        if (!fs.readFileSync(lockFile).equals(lockBytes)) throw Error('Outer lock changed; execution ownership is uncertain');
        const started = Date.now();
        let reply;
        try { reply = await runProcess('unity', argv, { cwd: project, timeoutMs: budget }); }
        catch (error) { reply = { code: null, stdout: '', stderr: '', error: String(error), timedOut: false }; }
        attempts.push({ phase: name, argv, budgetMs: budget, durationMs: Date.now() - started, reply });
        const statusTimeout = editorStatusProcessTimeout(reply, name, project);
        if (statusTimeout) throw statusTimeout;
        if (reply.timedOut || reply.error) throw Error(JSON.stringify(reply));
        let envelope;
        try { envelope = JSON.parse(reply.stdout); } catch { throw Error('Missing CLI JSON envelope'); }
        if (name === 'editor_status') {
            const networkError = editorStatusNetworkError(reply, project);
            if (networkError) throw networkError;
        }
        if (reply.code !== 0 || envelope.success !== true || envelope.data?.success !== true)
            throw Error('CLI did not acknowledge successful command completion: ' + JSON.stringify(envelope));
        if (path.resolve(envelope.data?.target?.projectPath ?? '') !== project)
            throw Error('CLI target did not confirm the exact requested project');
        if (script) {
            const verdict = classifyReply(reply, scenario, name === 'Cleanup' ? 'cleanup' : 'result');
            const allowed = ['Trigger', 'ReloadPoll'].includes(name)
                ? ['pending', 'passed', 'assertion-failed'] : ['passed', 'assertion-failed', 'skip'];
            if (!allowed.includes(verdict.status) || !verdict.testResult)
                throw Error('No known structured native result: ' + JSON.stringify(verdict));
            return verdict.testResult;
        }
        return normalizeEditorStatus(envelope.data.result, project);
    }
    let failure, folderCreated = false;
    const localErrors = [];
    try {
        fs.mkdirSync(folder); // Exclusive GUID folder; never writes production or asset source.
        folderCreated = true;
        fs.writeFileSync(file, source, { flag: 'wx' });
        await driveReload(context, { request, project });
    }
    catch (error) { failure = error; }
    finally {
        try { context.facts.fingerprint.after = fingerprint(); }
        catch (error) { localErrors.push(error); }
        if (context.recoveryRequired && folderCreated) context.facts.retainedInputForRecovery = file;
        else if (folderCreated) {
            // Only this newly-created file/folder, nonrecursive; raw CLI evidence remains in the outer report.
            try { if (fs.existsSync(file)) fs.unlinkSync(file); } catch (error) { localErrors.push(error); }
            try { fs.rmdirSync(folder); } catch (error) { localErrors.push(error); }
            context.facts.tempInputRemoved = !fs.existsSync(folder);
        }
    }
    if (localErrors.length) {
        context.facts.localEvidenceCleanupErrors = localErrors.map(String);
        const errors = failure ? [failure, ...localErrors] : localErrors;
        throw new AggregateError(errors, errors.map(String).join('; '));
    }
    if (failure) throw failure;
    assert.deepEqual(context.facts.fingerprint.after, before, 'Reviewed helper and production source fingerprints unchanged');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const context = new TestContext('Actual native domain reload preserves stored gradient keys and restores owned fixture state');
    context.case('Begin -> deferred native Trigger -> real marker/old-domain release and idle -> asserted End -> Cleanup',
        () => runGradientReload(context, process.argv[2]));
    await finish(context);
}
