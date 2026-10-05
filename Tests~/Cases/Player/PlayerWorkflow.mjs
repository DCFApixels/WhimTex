// Standalone selectable Node workflow. No Unity command runs merely by importing this module.
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID, createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { bundleSources, commandArgs, classifyReply, runProcess, root } from '../../scripts/run-tests.mjs';
import { decodeBuildStatus, orchestratePlayerRelease } from './PlayerRelease.mjs';

const exactProject = path.resolve('D:/DCFA/Projects/Test6.6');
const sources = ['Tests~/Cases/Player/PlayerReleaseTests.cs'];
export function unwrapNative(value) { return typeof value === 'string' ? JSON.parse(value) : value; }
export function decodeCompileStatus(value) {
    value = unwrapNative(value);
    if (!['idle', 'triggered', 'compiling', 'completed', 'up_to_date'].includes(value?.status)) throw Error('Unknown recompile_status state/schema.');
    if (value.status === 'completed' || value.status === 'up_to_date') {
        if (typeof value.failed !== 'boolean' || !Array.isArray(value.errors)) throw Error('Native completion must contain failed and errors.');
    }
    return value;
}
function noLinks(file, io) {
    for (let current = path.resolve(file); ; current = path.dirname(current)) {
        if (io.existsSync(current) && (io.lstatSync(current).isSymbolicLink() || io.realpathSync(current) !== current)) throw Error('Redirected owned path: ' + current);
        if (path.dirname(current) === current) break;
    }
}
function session(context, { project, file, runId, deadline }, dependencies) {
    const io = dependencies.fs ?? fs, now = dependencies.now ?? Date.now;
    const pause = dependencies.delay ?? delay;
    const invoke = dependencies.invoke ?? ((argv, budget) => runProcess('unity', argv, { cwd: project, timeoutMs: budget }));
    const lockPath = path.join(project, 'Temp/WhimTex/test-runs/runner.lock');
    noLinks(lockPath, io); const lockText = io.readFileSync(lockPath, 'utf8');
    if (JSON.parse(lockText).pid !== process.ppid) throw Error('The outer uniform runner is the sole lock owner.');
    function uncertain(message) {
        context.recoveryRequired = true;
        context.facts.recovery = { runId, file, lockPath, message,
            instruction: 'Retain outer lock, bundle and GUID fixture. Confirm exact native compilation/build completion before any C# or cleanup. Never blindly repeat recompile/build.' };
        return Error(message);
    }
    function budget() {
        if (io.readFileSync(lockPath, 'utf8') !== lockText) throw uncertain('Outer lock changed.');
        const left = deadline - now();
        if (left < 1500) throw uncertain('Workflow deadline exhausted; native work may continue.');
        return Math.min(10000, left);
    }
    async function send(name, argv, allowReloadGap = false, ownedCompileProof = null) {
        const limit = budget(); let reply;
        if (argv[0] === 'command' && argv[1] === name) argv = [...argv, '--timeout', String(Math.max(1, Math.ceil(limit / 1000)))];
        const attempt = { phase: name, argv, budgetMs: limit }; (context.facts.workflowAttempts ??= []).push(attempt);
        try { reply = await invoke(argv, limit); } catch (error) { throw uncertain(name + ' transport exception: ' + error); }
        attempt.reply = reply;
        // Only finishNativeCompile supplies this validated GUID evidence. No timeout retry for mutations/pre-trigger reads.
        if (reply !== null && typeof reply === 'object' && !Array.isArray(reply) && reply.timedOut === true &&
            ['code', 'stdout', 'stderr', 'timedOut'].every(key => Object.hasOwn(reply, key)) && !('error' in reply) &&
            Object.keys(reply).every(key => ['code', 'stdout', 'stderr', 'timedOut', 'error'].includes(key)) &&
            (reply.code === null || Number.isSafeInteger(reply.code)) && reply.stdout === '' && reply.stderr === '' && allowReloadGap &&
            ['editor_status', 'recompile_status'].includes(name) && ownedCompileProof?.runId === runId &&
            ['install', 'cleanup'].includes(ownedCompileProof.kind) &&
            (ownedCompileProof.compilationStarted > 0 || ownedCompileProof.beforeReload > 0)) {
            const timeout = { phase: name, runId, kind: ownedCompileProof.kind, budgetMs: limit,
                remainingMs: deadline - now(), proof: { ...ownedCompileProof }, reply,
                reason: 'Read-only timeout during observed owned native compilation; wait within original deadline only.' };
            attempt.ownedCompileTimeout = timeout; (context.facts.nativeStatusTimeouts ??= []).push(timeout);
            return { gap: true };
        }
        if (!reply || reply.timedOut || reply.error) throw uncertain(name + ' timeout/error; execution is uncertain.');
        let envelope;
        try { envelope = JSON.parse(reply.stdout); } catch { throw uncertain(name + ' malformed transport JSON.'); }
        // Same proven, narrow READ-ONLY endpoint reconnect as ReloadOrchestration. Never repeat a mutation.
        const gap = allowReloadGap && ['editor_status', 'recompile_status'].includes(name) && reply.code === 6 &&
            envelope.success === false && envelope.command === 'unity command ' + name && envelope.data === null &&
            Array.isArray(envelope.errors) && envelope.errors.length > 0 && envelope.errors.every(error =>
                error?.code === 'COMMAND_FAILED' && typeof error.message === 'string' &&
                error.message.startsWith("Failed to execute command '" + name + "': Network error:"));
        if (gap) return { gap: true };
        if (reply.code !== 0 || envelope.success !== true || envelope.data?.success !== true) throw uncertain(name + ' rejected: ' + JSON.stringify(envelope));
        return { reply, value: envelope.data.result };
    }
    async function native(name, args = [], allowReloadGap = false, ownedCompileProof = null) {
        const result = await send(name, ['command', name, ...args, '--project-path', project, '--format', 'json'], allowReloadGap, ownedCompileProof);
        if (result.gap) return null;
        try { return unwrapNative(result.value); } catch { throw uncertain(name + ' returned malformed nested JSON.'); }
    }
    function idle(value) {
        if (value === null) return false;
        if (typeof value.projectPath !== 'string' || path.resolve(value.projectPath) !== project ||
            typeof value.compiling !== 'boolean' || typeof value.domainReloadInProgress !== 'boolean' || value.playMode !== 'stopped')
            throw uncertain('Unknown/mismatched/playing Editor state.');
        return !value.compiling && !value.domainReloadInProgress;
    }
    async function phase(name, args = [runId]) {
        const script = { runner: 'run_script', file: path.relative(root, file), result: { kind: 'structured' } };
        const response = await send(name, commandArgs(script, 'PlayerReleaseTests.' + name, args, budget(), project));
        let verdict;
        try { verdict = classifyReply(response.reply, script, name === 'Cleanup' ? 'cleanup' : 'result'); }
        catch (error) { throw uncertain(name + ' protocol exception: ' + error); }
        context.facts[name] = verdict;
        if (verdict.uncertain || verdict.testResult?.recoveryRequired || !['passed', 'assertion-failed'].includes(verdict.status))
            throw uncertain(name + ': ' + verdict.detail);
        context.checks += verdict.testResult.checks;
        return verdict;
    }
    return { io, now, pause, native, idle, phase, uncertain, budget };
}

export async function finishNativeCompile(context, { project, file, runId, kind, deadline }, dependencies = {}) {
    const s = session(context, { project, file, runId, deadline }, dependencies);
    const evidencePath = path.join(project, 'Temp/WhimTex/player-release', runId, kind + '-compile.json');
    const started = s.now(); let explicitRequests = 0, observedBusy = false;
    const read = () => {
        noLinks(evidencePath, s.io);
        const e = JSON.parse(s.io.readFileSync(evidencePath, 'utf8'));
        if (e.version !== 1 || e.runId !== runId || e.kind !== kind || !Array.isArray(e.errors) ||
            ![e.beforeReload, e.compilationStarted, e.compilationFinished].every(x => Number.isSafeInteger(x) && x >= 0))
            throw s.uncertain('Malformed owned compilation evidence.');
        return e;
    };
    while (true) {
        s.budget();
        const e = read(), editor = await s.native('editor_status', [], true, e);
        const ready = s.idle(editor); if (!ready) observedBusy = true;
        if (ready && e.errors.length) throw Error('Native compilation failed: ' + e.errors.join('\n'));
        if (ready && e.compilationStarted > 0 && e.compilationFinished > 0 && e.beforeReload > 0) {
            const verdict = await s.phase('VerifyNativeCompile', [runId, kind]);
            context.assert.equal(verdict.status, 'passed', 'Actual owned native reload and probe class verification');
            const proof = verdict.testResult;
            context.assert.ok(proof.nativeCompilationComplete && proof.oldDomainGone && proof.beforeReload > 0, 'Native completion is event/class evidence, never idle/up_to_date alone');
            const result = { status: 'passed', projectPath: project, runId, nativeCompilationComplete: true,
                kind, beforeReload: proof.beforeReload, observedBusy, explicitRequests };
            (context.facts.nativeCompilations ??= []).push(result); return result;
        }
        // Autoreload gets priority. A started compiler, even one not yet reloading, never permits a duplicate request.
        if (ready && e.compilationStarted === 0 && e.beforeReload === 0 && explicitRequests === 0 && s.now() - started >= 5000) {
            let status;
            const value = await s.native('recompile_status', [], true);
            if (value === null) { await s.pause(250); continue; }
            try { status = decodeCompileStatus(value); }
            catch (error) { if (context.recoveryRequired) throw error; throw s.uncertain('Unknown native recompile status: ' + error); }
            if (!['triggered', 'compiling'].includes(status.status)) {
                // Recheck owned callbacks and Editor immediately before the one supported native command.
                const latest = read();
                if (latest.compilationStarted === 0 && latest.beforeReload === 0 && s.idle(await s.native('editor_status', [], true))) {
                    explicitRequests = 1; // Persist intent BEFORE sending; never resend after timeout/reload gap.
                    const requestPath = path.join(path.dirname(evidencePath), kind + '-recompile-request.json');
                    s.io.writeFileSync(requestPath, JSON.stringify({ version: 1, runId, kind, explicitRequests }), { flag: 'wx' });
                    await s.native('recompile', ['--focus', 'false']);
                }
            }
        }
        if (explicitRequests === 1 && ready) {
            const statusValue = await s.native('recompile_status', [], true, read());
            if (statusValue !== null) {
                let status;
                try { status = decodeCompileStatus(statusValue); } catch (error) { throw s.uncertain('Unknown native recompile completion: ' + error); }
                if (status.failed === true || status.errors?.length) throw Error('Explicit native compilation failed: ' + JSON.stringify(status));
                // completed/up_to_date without this GUID's events is still pending, never a substitute for actual proof.
            }
        }
        await s.pause(250);
    }
}

export async function orchestratePlayerWorkflow(context, { project, timeoutMs = 600000 }, dependencies = {}) {
    project = path.resolve(project);
    if (project !== exactProject || !Number.isSafeInteger(timeoutMs) || timeoutMs < 120000 || timeoutMs > 1800000) throw Error('Exact Test6.6 project and bounded 120000..1800000ms workflow required.');
    const io = dependencies.fs ?? fs, now = dependencies.now ?? Date.now;
    const runId = (dependencies.guid ?? (() => randomUUID().replaceAll('-', '')))();
    if (!/^[0-9a-f]{32}$/.test(runId)) throw Error('Fresh lowercase N-format GUID required.');
    const directory = path.join(project, 'Temp/WhimTex/test-runs'); noLinks(directory, io);
    const lockPath = path.join(directory, 'runner.lock'); noLinks(lockPath, io);
    if (JSON.parse(io.readFileSync(lockPath, 'utf8')).pid !== process.ppid) throw Error('Parent runner owns the sole lock.');
    const statePath = path.join(project, 'Temp/WhimTex/player-release', runId, 'state.json');
    noLinks(statePath, io);
    if (io.existsSync(statePath)) throw Error('Fresh GUID cannot reuse an existing ownership journal, even a cleaned one.');
    const file = path.join(directory, 'player-workflow-' + runId + '.cs'); noLinks(file, io);
    const source = (dependencies.bundle ?? bundleSources)(sources); io.writeFileSync(file, source, { flag: 'wx' });
    context.facts.input = { runId, file, sources, sha256: createHash('sha256').update(source).digest('hex') };
    const deadline = now() + timeoutMs - 10000;
    const s = session(context, { project, file, runId, deadline }, dependencies);
    let mayOwnFixture = false, tailStarted = false, finalized = false, primary, cleanupError;
    const finalize = async ({ cleanupResult }) => {
        if (cleanupResult.nativeCompileRequired === false) {
            // Partial Setup failed before a native script existed. Validate the explicit C# deletion receipt and idle state.
            context.assert.equal(cleanupResult.phase, 'cleaned');
            context.assert.ok(s.idle(await s.native('editor_status')), 'No native source was installed and Editor is idle');
            finalized = true;
            return { status: 'passed', runId, projectPath: project, nativeCompilationComplete: true, nativeCompilationRequired: false };
        }
        const proof = await finishNativeCompile(context, { project, file, runId, kind: 'cleanup', deadline }, dependencies);
        finalized = true; return proof;
    };
    try {
        context.assert.ok(s.idle(await s.native('editor_status')), 'Direct matching editor_status is ready; discovery instance counts are irrelevant');
        const build = decodeBuildStatus(await s.native('build_status'));
        if (build === 'queued' || build === 'building') throw s.uncertain('Existing native build prevents Setup/C# mutation.');
        mayOwnFixture = true;
        const setup = await s.phase('Setup'); context.assert.equal(setup.status, 'passed', 'Owned native probe installation');
        await finishNativeCompile(context, { project, file, runId, kind: 'install', deadline }, dependencies);
        const prepare = await s.phase('PreparePlayer'); context.assert.equal(prepare.status, 'passed', 'Independent green TIFF/red live fixture');
        tailStarted = true;
        const remaining = deadline - now() + 10000;
        if (remaining < 120000) throw s.uncertain('Too little bounded budget remains for native Player build/runtime/cleanup.');
        await orchestratePlayerRelease(context, { project, file, runId, timeoutMs: remaining }, { ...dependencies, finishNativeCleanup: finalize });
    }
    catch (error) { primary = error; }
    finally {
        // Setup/Prepare failure with KNOWN idle native state still owns a cleanup obligation.
        if (mayOwnFixture && !finalized && !context.recoveryRequired && !tailStarted && io.existsSync(statePath)) {
            try {
                if (!s.idle(await s.native('editor_status'))) throw s.uncertain('Partial fixture Editor still compiling/reloading.');
                const build = decodeBuildStatus(await s.native('build_status'));
                if (build === 'queued' || build === 'building') throw s.uncertain('Cannot clean partial fixture during native build.');
                const cleanup = await s.phase('Cleanup'); context.assert.equal(cleanup.status, 'passed', 'Partial Setup/Prepare cleanup');
                await finalize({ cleanupResult: cleanup.testResult });
            }
            catch (error) { cleanupError = error; s.uncertain('Partial fixture cleanup/native completion needs recovery: ' + error); }
        }
        if (!context.recoveryRequired) {
            try {
                noLinks(file, io);
                if (path.dirname(file) !== directory || !/^player-workflow-[0-9a-f]{32}\.cs$/.test(path.basename(file)) || !io.lstatSync(file).isFile()) throw Error('Unexpected owned bundle cleanup path.');
                io.unlinkSync(file); context.facts.input.removed = true;
            }
            catch (error) { cleanupError = error; s.uncertain('Exact temporary bundle cleanup failed: ' + error); }
        }
    }
    if (primary && cleanupError) throw new AggregateError([primary, cleanupError], 'Player workflow and cleanup failed');
    if (primary) throw primary;
    if (cleanupError) throw cleanupError;
}

export function createPlayerWorkflowContext(project, timeoutMs = 600000, dependencies = {}) {
    const context = new TestContext('Standalone Player release workflow: native install/reload, saved TIFF build/runtime, native cleanup/reload');
    context.case('Setup -> native compilation -> Prepare -> connected build -> Player -> Cleanup -> native compilation',
        () => orchestratePlayerWorkflow(context, { project, timeoutMs }, dependencies));
    return context;
}
export function workflowArguments(argv) {
    if ((argv.length !== 2 && argv.length !== 4) || argv[0] !== '--project-path' || !argv[1] ||
        (argv.length === 4 && argv[2] !== '--timeout-ms')) throw Error('Expected --project-path <Test6.6> [--timeout-ms <120000..1800000>].');
    return { project: path.resolve(argv[1]), timeoutMs: argv.length === 4 ? Number(argv[3]) : 600000 };
}

// No real filesystem/CLI/Player effects: exercises the standalone lifecycle and native evidence gates.
export async function runWorkflowProtocolTests() {
    const tests = new TestContext('Standalone Player workflow protocol with injected native/asset/process effects');
    for (const mode of ['auto-reload', 'explicit-recompile', 'setup-failed-no-script', 'setup-failed-with-script',
        'compiler-errors', 'no-reload-proof', 'existing-build', 'compile-status-timeout', 'recompile-status-timeout',
        'pretrigger-timeout', 'mutation-timeout', 'recompile-timeout', 'malformed-status', 'project-mismatch', 'status-timeout-deadline',
        'timeout-malformed-stderr', 'timeout-missing-stdout', 'timeout-empty-error', 'timeout-target', 'timeout-invalid-code', 'timeout-missing-stderr']) {
        tests.case(mode, async () => {
            const project = exactProject, runId = 'fedcba0987654321fedcba0987654321';
            const directory = path.join(project, 'Temp/WhimTex/player-release', runId), attempt = path.join(directory, 'attempt-001');
            const lockPath = path.join(project, 'Temp/WhimTex/test-runs/runner.lock'), statePath = path.join(directory, 'state.json');
            const file = path.join(project, 'Temp/WhimTex/test-runs/player-workflow-' + runId + '.cs');
            const state = { version: 1, runId, phase: 'installed', output: directory, folder: 'Assets/WhimTexTestMigration/' + runId,
                scene: 'Assets/WhimTexTestMigration/' + runId + '/Probe.unity', target: 'StandaloneWindows64' };
            const memory = new Map([[lockPath, JSON.stringify({ pid: process.ppid })]]); const calls = [];
            let tick = 0, built = false, nativeRequests = 0, editorGaps = 0, statusTimeouts = 0;
            const compileFile = kind => path.join(directory, kind + '-compile.json');
            const evidence = (kind, complete = true, errors = []) => ({ version: 1, runId, kind,
                compilationStarted: complete ? 1 : 0, compilationFinished: complete ? 1 : 0, beforeReload: complete && errors.length === 0 ? 1 : 0, errors });
            const io = {
                existsSync: value => memory.has(value) || !/\.(?:cs|json)$/.test(value),
                lstatSync: () => ({ isSymbolicLink: () => false, isFile: () => true }), realpathSync: value => value,
                readFileSync: value => { if (!memory.has(value)) throw Error('Unknown mock file: ' + value); return memory.get(value); },
                writeFileSync: (value, text, options) => {
                    tests.assert.equal(options.flag, 'wx'); tests.assert.ok(!memory.has(value), 'CreateNew outputs only'); memory.set(value, text);
                },
                unlinkSync: value => { tests.assert.equal(value, file, 'Remove only exact owned bundle'); memory.delete(value); }
            };
            const envelope = value => ({ code: 0, stdout: JSON.stringify({ success: true, data: { success: true, result: value } }) });
            const scriptReply = (value = {}) => envelope({ success: true, result: JSON.stringify({ status: 'passed', checks: 1, failures: [], message: 'mock', ...value }) });
            const invoke = async (argv, budgetMs) => {
                tests.assert.equal(argv[argv.indexOf('--project-path') + 1], project);
                const command = argv[1];
                if (command !== 'run_script') {
                    tests.assert.equal(argv.filter(x => x === '--timeout').length, 1, 'Exactly one global native CLI timeout');
                    tests.assert.equal(Number(argv[argv.indexOf('--timeout') + 1]), Math.ceil(budgetMs / 1000), 'Native CLI timeout matches remaining process budget');
                }
                if (command === 'editor_status') {
                    if (memory.has(compileFile('install')) && !calls.includes('VerifyNativeCompile')) {
                        if (mode.startsWith('timeout-')) {
                            const bad = { timedOut: true, code: null, stdout: '', stderr: '' };
                            if (mode === 'timeout-malformed-stderr') bad.stderr = 'wrong target or malformed diagnostic';
                            if (mode === 'timeout-missing-stdout') delete bad.stdout;
                            if (mode === 'timeout-empty-error') bad.error = '';
                            if (mode === 'timeout-target') bad.target = project + '/wrong';
                            if (mode === 'timeout-invalid-code') bad.code = 0.5;
                            if (mode === 'timeout-missing-stderr') delete bad.stderr;
                            return bad;
                        }
                        if (mode === 'compile-status-timeout' && editorGaps++ === 0) return { code: 6, stdout: JSON.stringify({ success: false,
                            command: 'unity command editor_status', data: null, errors: [{ code: 'COMMAND_FAILED', message: "Failed to execute command 'editor_status': Network error: reload" }] }) };
                        if (mode === 'status-timeout-deadline' || mode === 'pretrigger-timeout' || mode === 'compile-status-timeout' && statusTimeouts === 0) {
                            statusTimeouts++; tick += 8000; return { timedOut: true, code: null, stdout: '', stderr: '' };
                        }
                        if (mode === 'malformed-status') return { code: 0, stdout: '{bad JSON' };
                        if (mode === 'project-mismatch') return envelope({ projectPath: project + '/wrong', compiling: false, domainReloadInProgress: false, playMode: 'stopped' });
                    }
                    return envelope({ projectPath: project, compiling: false, domainReloadInProgress: false, playMode: 'stopped' });
                }
                if (command === 'recompile_status') {
                    if (mode === 'recompile-status-timeout' && nativeRequests > 0 && statusTimeouts++ === 0) {
                        tick += 8000; return { timedOut: true, code: 143, stdout: '', stderr: '' };
                    }
                    return envelope(JSON.stringify({ status: 'completed', failed: false, errors: [] }));
                }
                if (command === 'recompile') {
                    calls.push(command); nativeRequests++;
                    tests.assert.deepEqual(argv.slice(2, argv.indexOf('--project-path')), ['--focus', 'false']);
                    if (mode !== 'no-reload-proof') memory.set(compileFile('install'), JSON.stringify(evidence('install')));
                    if (mode === 'recompile-timeout') return { timedOut: true, code: null, stdout: '', stderr: '' };
                    return envelope(JSON.stringify({ status: 'triggered' }));
                }
                if (command === 'build_status') return envelope(JSON.stringify({ status: mode === 'existing-build' ? 'building' : built ? 'completed' : 'idle' }));
                if (command === 'build') { calls.push(command); built = true; return envelope(JSON.stringify({ status: 'queued' })); }
                tests.assert.equal(command, 'run_script');
                const name = argv[argv.indexOf('--entry') + 1].split('.').at(-1); calls.push(name);
                const args = JSON.parse(argv[argv.indexOf('--args') + 1]); tests.assert.equal(args[0], runId);
                if (name === 'Setup') {
                    memory.set(statePath, JSON.stringify(state));
                    if (mode !== 'setup-failed-no-script') memory.set(compileFile('install'), JSON.stringify(evidence('install',
                        !['explicit-recompile', 'no-reload-proof', 'pretrigger-timeout', 'recompile-timeout', 'recompile-status-timeout'].includes(mode), mode === 'compiler-errors' ? ['CS fixture error'] : [])));
                    if (mode === 'mutation-timeout') return { timedOut: true, code: null, stdout: '', stderr: '' };
                    return scriptReply({ status: mode.startsWith('setup-failed') ? 'failed' : 'passed',
                        failures: mode.startsWith('setup-failed') ? ['injected Setup failure'] : [] });
                }
                if (name === 'VerifyNativeCompile') {
                    tests.assert.ok(['install', 'cleanup'].includes(args[1]));
                    const e = JSON.parse(memory.get(compileFile(args[1])));
                    tests.assert.ok(e.beforeReload > 0 && e.errors.length === 0, 'Verification follows real owned event evidence in the protocol');
                    return scriptReply({ nativeCompilationComplete: true, oldDomainGone: true, beforeReload: e.beforeReload });
                }
                if (name === 'PreparePlayer') { state.phase = 'prepared'; memory.set(statePath, JSON.stringify(state)); }
                if (name === 'Trigger') {
                    state.phase = 'queued'; memory.set(statePath, JSON.stringify(state));
                    return scriptReply({ status: 'running', runId, attempt: 1, phase: 'queued', target: state.target,
                        output: directory, scene: state.scene, executable: path.join(attempt, 'Player.exe'),
                        nativeMarker: path.join(attempt, 'native-result.json'), buildMarker: path.join(attempt, 'build-result.json'),
                        playerReport: path.join(attempt, 'player-result.json') });
                }
                if (name === 'InspectBuild') { state.phase = 'completed'; memory.set(statePath, JSON.stringify(state)); return scriptReply({ phase: 'completed', buildReturned: true }); }
                if (name === 'Cleanup') {
                    state.phase = 'cleaned'; memory.set(statePath, JSON.stringify(state));
                    const required = mode !== 'setup-failed-no-script';
                    if (required) memory.set(compileFile('cleanup'), JSON.stringify(evidence('cleanup')));
                    return scriptReply({ phase: 'cleaned', nativeCompileRequired: required });
                }
                return scriptReply();
            };
            const context = new TestContext(mode); let error;
            try { await orchestratePlayerWorkflow(context, { project, timeoutMs: 240000 }, {
                fs: io, invoke, guid: () => runId, bundle: () => '// mock reviewed source', now: () => tick,
                delay: async ms => { tick += ms; }, platform: 'win32',
                launch: async () => { calls.push('Player'); return { code: 0, stdout: '', stderr: '' }; }
            }); } catch (cause) { error = cause; }
            if (['auto-reload', 'explicit-recompile', 'compile-status-timeout', 'recompile-status-timeout'].includes(mode)) {
                tests.assert.equal(error, undefined); tests.assert.ok(calls.includes('Player') && calls.includes('Cleanup'));
                tests.assert.equal(context.facts.input.sha256, createHash('sha256').update('// mock reviewed source').digest('hex'), 'Build/runtime tail preserves actual input source SHA');
                tests.assert.deepEqual(context.facts.input.sources, sources, 'Build/runtime tail preserves bundle source list');
                tests.assert.ok(context.facts.nativeCompilations.some(x => x.kind === 'cleanup'));
                tests.assert.equal(context.recoveryRequired, undefined);
            }
            else if (mode.startsWith('setup-failed') || mode === 'compiler-errors') {
                tests.assert.ok(error); tests.assert.ok(calls.includes('Cleanup'), 'Known quiescent partial installation is cleaned');
                tests.assert.ok(!calls.includes('PreparePlayer') && !calls.includes('build') && !calls.includes('Player'));
                tests.assert.equal(context.recoveryRequired, undefined);
            }
            else {
                tests.assert.ok(error); tests.assert.equal(context.recoveryRequired, true);
                tests.assert.ok(!calls.includes('Cleanup') && !calls.includes('build'), 'Unknown reload/existing build cannot cause mutations');
                tests.assert.ok(memory.has(file), 'Uncertain execution retains source and lock');
            }
            tests.assert.equal(nativeRequests, ['explicit-recompile', 'no-reload-proof', 'recompile-timeout', 'recompile-status-timeout'].includes(mode) ? 1 : 0,
                'Autoreload never gets an extra compile request; fallback is at most once');
            tests.assert.ok(memory.has(lockPath), 'Child never deletes parent lock');
            if (!context.recoveryRequired) tests.assert.ok(!memory.has(file), 'Quiescent completion removes only its bundle');
            tests.assert.equal(calls.filter(x => x === 'Cleanup').length, context.recoveryRequired ? 0 : 1, 'No duplicate cleanup');
            tests.assert.equal(calls.filter(x => x === 'Setup').length, mode === 'existing-build' ? 0 : 1, 'No Setup replay');
            if (['compile-status-timeout', 'recompile-status-timeout', 'status-timeout-deadline'].includes(mode)) {
                tests.assert.ok(context.facts.nativeStatusTimeouts.length > 0, 'Timeout plus owned proof retained in facts');
                for (const t of context.facts.nativeStatusTimeouts) tests.assert.ok(t.proof.runId === runId &&
                    (t.proof.compilationStarted > 0 || t.proof.beforeReload > 0) && t.reply.timedOut && t.remainingMs < 230000);
            } else tests.assert.equal(context.facts.nativeStatusTimeouts, undefined, 'No scoped retry facts outside observed read-only compile wait');
        });
    }
    return tests.run();
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const args = workflowArguments(process.argv.slice(2));
    await finish(createPlayerWorkflowContext(args.project, args.timeoutMs));
}
