import fs from 'node:fs';
import path from 'node:path';
import { randomUUID, createHash } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { TestContext } from '../../Framework/test-api.mjs';
import { bundleSources, commandArgs, runProcess, classifyReply, root } from '../../scripts/run-tests.mjs';

const authorizedProject = path.resolve('D:/DCFA/Projects/Test6.6');
export class EditorStatusProcessTimeoutError extends Error {}

// Same strict read-only timeout shape as the D helper; protocol eligibility is separate.
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

// Exported transport seam permits deterministic protocol tests WITHOUT a Unity process.
// This does not acquire, remove or replace the outer runner's lock.
export async function orchestrateReload(context, { type, project, file, runId }, dependencies = {}) {
    const invoke = dependencies.invoke ?? ((argv, budget) => runProcess('unity', argv, { cwd: project, timeoutMs: budget }));
    const now = dependencies.now ?? Date.now;
    const pause = dependencies.delay ?? delay;
    const started = now();
    const bodyDeadline = started + 45000;
    const cleanupDeadline = started + 55000; // Five seconds of outer 60s budget remain for result transport.
    const attempts = context.facts.attempts = [];
    const script = { runner: 'run_script', file: path.relative(root, file), args: [], result: { kind: 'structured' } };
    let mayOwnFixture = false;
    let primary;
    let cleanupError;
    let triggerCount = 0;
    let lastPoll;
    let triggerAcknowledged = false;
    let awaitingNativeReload = false;
    let observedCompilation = false;
    let nativeProofConfirmed = false;
    function uncertain(message) {
        context.recoveryRequired = true;
        context.facts.recovery = { runId, cleanupEntry: type + '.Cleanup', file,
            message, instruction: 'Keep the outer lock. Confirm this exact Editor is idle, then invoke public Cleanup with this GUID. Never retry the trigger blindly.' };
        return Error(message);
    }
    async function send(phase, argv, cleanup = false) {
        const budget = Math.min(cleanup ? 7000 : 8000, (cleanup ? cleanupDeadline : bodyDeadline) - now());
        if (budget < 1500) throw uncertain('Safe ' + phase + ' budget exhausted; completion/Editor state is not confirmed.');
        const attempt = { phase, argv, budgetMs: budget };
        attempts.push(attempt);
        let reply;
        try { reply = await invoke(argv, budget); }
        catch (error) { attempt.error = String(error); throw uncertain(phase + ' transport exception: ' + error); }
        attempt.reply = reply;
        if (!reply || typeof reply !== 'object') throw uncertain(phase + ' returned no transport reply.');
        if (!cleanup && triggerAcknowledged && observedCompilation && awaitingNativeReload && !nativeProofConfirmed
            && editorStatusProcessTimeout(reply, phase, project)) return reply;
        if (reply.timedOut || reply.error) throw uncertain(phase + ' transport timeout/error; Editor execution may continue.');
        return reply;
    }
    async function command(name, cleanup = false) {
        while (true) {
            const reply = await send(name, ['command', name, '--project-path', project, '--format', 'json'], cleanup);
            const statusTimeout = editorStatusProcessTimeout(reply, name, project);
            if (statusTimeout) {
                (context.facts.readOnlyStatusTimeouts ??= []).push({ atMs: now() - started, message: String(statusTimeout) });
                if (now() + 2250 >= bodyDeadline)
                    throw uncertain('Original native reload deadline exhausted during read-only status timeout reconnect.');
                await pause(250);
                continue; // Only this read-only status probe; no mutation retry or deadline extension.
            }
            let envelope;
            try { envelope = JSON.parse(reply.stdout); }
            catch { throw uncertain(name + ' returned unknown JSON/Editor state.'); }
            // The native domain reload temporarily restarts the Pipeline endpoint. Only
            // this observed CLI error envelope permits READ-ONLY reconnect, within the
            // original body deadline and only after an acknowledged owned Trigger.
            const expectedNetworkGap = name === 'editor_status' && !cleanup && triggerAcknowledged && awaitingNativeReload && !nativeProofConfirmed
                && reply.code === 6 && envelope?.success === false && envelope.command === 'unity command editor_status'
                && envelope.data === null && Array.isArray(envelope.errors) && envelope.errors.length > 0
                && envelope.errors.every(e => e?.code === 'COMMAND_FAILED' && typeof e.message === 'string'
                    && /^Failed to execute command 'editor_status': Network error:/i.test(e.message));
            if (expectedNetworkGap) {
                (context.facts.readOnlyReconnects ??= []).push({ atMs: now() - started, errors: envelope.errors });
                if (now() + 2250 >= bodyDeadline)
                    throw uncertain('Native reload endpoint remained disconnected through the bounded read-only reconnect deadline.');
                await pause(250);
                continue; // NEVER retry Trigger, ReloadPoll, Verify or any other C# execution.
            }
            if (reply.code !== 0 || envelope?.success !== true || envelope.data?.success !== true)
                throw uncertain(name + ' did not acknowledge success: ' + JSON.stringify(envelope));
            return envelope.data.result;
        }
    }
    function idle(value) {
        if (typeof value?.projectPath !== 'string' || path.resolve(value.projectPath) !== project
            || typeof value.compiling !== 'boolean' || typeof value.domainReloadInProgress !== 'boolean'
            || !['stopped', 'playing', 'paused'].includes(value.playMode))
            throw uncertain('Unknown or mismatched Editor state: ' + JSON.stringify(value));
        if (value.playMode !== 'stopped') throw uncertain('Editor entered play mode during reload orchestration.');
        if (triggerAcknowledged && !nativeProofConfirmed && value.compiling) {
            observedCompilation = true;
            context.facts.observedCompilation = true;
        }
        return !value.compiling && !value.domainReloadInProgress;
    }
    async function requireIdle(cleanup = false) {
        if (!idle(await command('editor_status', cleanup))) throw uncertain('Editor is not idle; no subsequent mutation is safe.');
    }
    async function scriptPhase(phase, cleanup = false) {
        const budget = Math.min(cleanup ? 7000 : 8000, (cleanup ? cleanupDeadline : bodyDeadline) - now());
        const reply = await send(phase, commandArgs(script, type + '.' + phase, [runId], budget, project), cleanup);
        let verdict;
        try { verdict = classifyReply(reply, phase === 'ReloadPoll' ? { ...script, async: { entry: type + '.ReloadPoll' } } : script,
            cleanup ? 'cleanup' : 'result'); }
        catch (error) { throw uncertain(phase + ': malformed structured transport reply: ' + error); }
        context.facts[phase] = verdict;
        if (type === 'DocumentReloadTests' && phase === 'Verify' && verdict.testResult)
            context.facts.documentReloadCoverage = {
                branch: verdict.testResult.coverageBranch ?? 'unreported',
                partial: verdict.testResult.partialCoverage !== false,
                note: 'A passed carrier-only fallback is NOT verification of the surviving-window oracle.'
            };
        if (verdict.uncertain || ['transport-error', 'protocol-error', 'timeout'].includes(verdict.status))
            throw uncertain(phase + ': ' + verdict.detail);
        if (verdict.testResult) context.checks += verdict.testResult.checks; // Actual counted C# context assertions, not parsed PASS text.
        if (phase !== 'ReloadPoll') context.assert.equal(verdict.status, 'passed', phase + ': ' + verdict.detail);
        return verdict;
    }
    function markers(verdict) {
        const value = verdict.testResult;
        if (!value || !Number.isSafeInteger(value.requestCount) || ![0, 1].includes(value.requestCount)
            || !Number.isSafeInteger(value.beforeReload) || ![0, 1].includes(value.beforeReload)
            || typeof value.requestQueued !== 'boolean' || typeof value.oldDomainGone !== 'boolean'
            || value.requestError != null && typeof value.requestError !== 'string'
            || !Array.isArray(value.compilationErrors) || !value.compilationErrors.every(e => typeof e === 'string')
            || value.checks === 0 && verdict.status !== 'assertion-failed')
            throw uncertain('Malformed/missing owned reload markers: ' + JSON.stringify(value));
        lastPoll = value;
        const realReload = value.requestCount === 1 && !value.requestQueued && value.beforeReload === 1 && value.oldDomainGone
            && !value.requestError && value.compilationErrors.length === 0;
        if (verdict.status === 'passed') {
            if (!realReload) throw uncertain('Passed ReloadPoll without actual owned reload markers; refusing Verify.');
            context.assert.ok(realReload, 'One native request, beforeAssemblyReload and loss of the old domain are confirmed');
            return true;
        }
        if (verdict.status === 'pending') {
            if (realReload || value.oldDomainGone && value.beforeReload !== 1 || value.requestQueued && value.requestCount !== 0
                || !value.requestQueued && value.requestCount === 0 || value.requestError || value.compilationErrors.length)
                throw uncertain('Inconsistent pending reload markers: ' + JSON.stringify(value));
            return false;
        }
        // An idle Editor plus an explicit request exception/compiler diagnostic is a known
        // failure. A request already issued with unexplained marker failure remains uncertain.
        if (value.requestCount === 1 && !value.requestError && value.compilationErrors.length === 0)
            throw uncertain('Issued native request has an unexplained failed reload marker.');
        context.assert.equal(verdict.status, 'passed', 'Owned reload poll failed: ' + verdict.detail);
        return false;
    }
    function exhausted() {
        if (lastPoll?.requestCount === 0 && lastPoll.requestQueued)
            throw Error('Deferred request callback did not execute within the bounded budget; cancel it in Cleanup.');
        throw uncertain('Issued native compilation/domain reload did not finish within the bounded budget.');
    }
    try {
        await requireIdle();
        mayOwnFixture = true; // Even a lost Begin response may have created its owned fixture.
        await scriptPhase('Begin');
        await requireIdle();
        // Start with the supported native request, NOT Refresh/recompile or a second-trigger fallback.
        triggerCount++;
        await scriptPhase('Trigger'); // ACK returns before the owned main-thread callback issues the native request.
        triggerAcknowledged = true;
        awaitingNativeReload = true;
        while (true) {
            if (now() + 2000 >= bodyDeadline) exhausted();
            // Never run fixture code while compilation/reload is observed. Stale idle alone
            // is insufficient: only the GUID's own persisted C# markers allow Verify.
            const editorIdle = idle(await command('editor_status'));
            if (now() + 2000 >= bodyDeadline) exhausted();
            if (editorIdle && markers(await scriptPhase('ReloadPoll'))) {
                nativeProofConfirmed = true;
                break;
            }
            await pause(250);
        }
        context.assert.equal(triggerCount, 1, 'Exactly one acknowledged deferred native compilation trigger');
        await requireIdle();
        awaitingNativeReload = false;
        await scriptPhase('Verify'); // C# requires beforeAssemblyReload AND removal of the old AppDomain slot.
    }
    catch (error) { primary = error; }
    finally {
        context.facts.triggerCount = triggerCount;
        if (mayOwnFixture && !context.recoveryRequired) {
            try { await requireIdle(true); await scriptPhase('Cleanup', true); }
            catch (error) { cleanupError = error; uncertain('Cleanup failed: ' + error); context.facts.cleanupError = String(error); }
        }
    }
    if (primary && cleanupError) throw new AggregateError([primary, cleanupError], 'Primary verdict and cleanup failure');
    if (primary) throw primary;
    if (cleanupError) throw cleanupError;
}

// Narrow deletion only; the caller owns this exact CreateNew file. Never recursively delete Temp.
export function removeOwnedSource(file, directory, canonicalDirectory, fileSystem = fs) {
    if (path.dirname(file) !== directory || !/^unity-b-reload-[0-9a-f]{32}\.cs$/.test(path.basename(file))
        || !fileSystem.lstatSync(file).isFile() || fileSystem.lstatSync(file).isSymbolicLink()
        || fileSystem.realpathSync(path.dirname(file)) !== canonicalDirectory
        || fileSystem.realpathSync(file) !== path.join(canonicalDirectory, path.basename(file)))
        throw Error('Refusing redirected/unowned temporary-source cleanup: ' + file);
    fileSystem.unlinkSync(file);
}

export function createReloadContext(type, project) {
    const context = new TestContext(type + ': real domain reload and original independent assertions');
    context.case('Begin -> one deferred native Trigger -> owned ReloadPoll + idle Editor -> Verify -> Cleanup', async () => {
        context.assert.ok(['DocumentReloadTests', 'GuideReloadTests'].includes(type), 'Only the two owned reload fixtures are supported');
        context.assert.equal(project, path.resolve(root, '../..'), 'Explicit project must be this package\'s exact Test6.6 project');
        const directory = path.join(project, 'Temp/WhimTex/test-runs');
        const lock = JSON.parse(fs.readFileSync(path.join(directory, 'runner.lock'), 'utf8'));
        context.assert.equal(lock.pid, process.ppid, 'Only the live locked outer runner may launch this child');
        const canonicalDirectory = fs.realpathSync(directory);
        context.assert.ok(canonicalDirectory.startsWith(fs.realpathSync(project) + path.sep), 'Runner temporary directory must resolve inside the project');
        const runId = randomUUID().replaceAll('-', ''); // N format shared by all fresh Pipeline assemblies.
        const sources = ['Tests~/Framework/TestApi.cs', 'Tests~/Cases/UnityB/UnityBSupport.cs',
            'Tests~/Cases/UnityB/ReloadSupport.cs', 'Tests~/Cases/UnityB/' + type + '.cs'];
        const source = bundleSources(sources);
        const file = path.join(directory, 'unity-b-reload-' + runId + '.cs');
        fs.writeFileSync(file, source, { flag: 'wx' }); // CreateNew: never overwrite a prior test or user source.
        context.facts.input = { file, runId, sources, sha256: createHash('sha256').update(source).digest('hex') };
        let primary;
        let sourceCleanupError;
        try { await orchestrateReload(context, { type, project, file, runId }); }
        catch (error) { primary = error; }
        try {
            if (!context.recoveryRequired) {
                removeOwnedSource(file, directory, canonicalDirectory);
                context.facts.input.removed = true;
            }
        }
        catch (error) { sourceCleanupError = error; context.facts.sourceCleanupError = String(error); }
        if (primary && sourceCleanupError) throw new AggregateError([primary, sourceCleanupError], 'Primary verdict and temporary-source cleanup failure');
        if (primary) throw primary;
        if (sourceCleanupError) throw sourceCleanupError;
    });
    return context;
}

export function projectArgument(argv) {
    if (argv.length !== 2 || argv[0] !== '--project-path' || !argv[1]) throw Error('Explicit --project-path <exact Test6.6 path> required.');
    return path.resolve(argv[1]);
}
