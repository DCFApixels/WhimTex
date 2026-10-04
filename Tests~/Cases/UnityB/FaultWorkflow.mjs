// Standalone selectable fault workflow; imports have no native/filesystem effects.
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID, createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { bundleSources, commandArgs, classifyReply, runProcess, root } from '../../scripts/run-tests.mjs';
import { unwrapNative, decodeCompileStatus } from './PlayerWorkflow.mjs';

const exactProject = path.resolve('D:/DCFA/Projects/Test6.6');
const sources = ['Tests~/Cases/UnityB/FaultFixture.cs'];
export const catalogCandidate = {
    id: 'fault-release-workflow-v2', file: 'Tests~/Cases/UnityB/FaultWorkflow.mjs',
    runner: 'node', requiresUnity: true, workflow: 'native-fixture', groups: ['documents', 'document-faults'], category: 'regression',
    args: ['--project-path', '$projectPath', '--timeout-ms', '180000'], timeoutMs: 180000,
    effects: ['assets', 'temp-files', 'user-state'], result: { kind: 'structured' },
    reviewFiles: [...sources, 'Tests~/Cases/UnityB/PlayerWorkflow.mjs', 'Tests~/Cases/UnityB/PlayerRelease.mjs'],
    prerequisites: 'Connected idle Test6.6; no user live/recovery state; sole outer runner lock. Parent registers metadata. No manual native fixture required.',
    setup: 'Fresh GUID native postprocessor, automatic compilation/reload proof, independent Faults and deferred failure/retry.',
    teardown: 'Delete only GUID fixture, verify native removal reload and exact class absence, restore selection/focus; uncertainty retains lock/bundle/assets.'
};
function noLinks(file, io) {
    for (let p = path.resolve(file); ; p = path.dirname(p)) {
        if (io.existsSync(p) && (io.lstatSync(p).isSymbolicLink() || io.realpathSync(p) !== p)) throw Error('Redirected owned path: ' + p);
        if (path.dirname(p) === p) break;
    }
}
function session(context, { project, file, runId, deadline }, dependencies) {
    const io = dependencies.fs ?? fs, now = dependencies.now ?? Date.now, pause = dependencies.delay ?? delay;
    const invoke = dependencies.invoke ?? ((argv, budget) => runProcess('unity', argv, { cwd: project, timeoutMs: budget }));
    const lockPath = path.join(project, 'Temp/WhimTex/test-runs/runner.lock'); noLinks(lockPath, io);
    const lockText = io.readFileSync(lockPath, 'utf8');
    if (JSON.parse(lockText).pid !== process.ppid) throw Error('Outer runner must own the sole lock.');
    function uncertain(message) {
        context.recoveryRequired = true;
        context.facts.recovery = { runId, file, lockPath, message,
            instruction: 'Retain outer lock, exact GUID fixture and bundle. Confirm native/deferred completion before any mutation; never blindly repeat compile.' };
        return Error(message);
    }
    function budget() {
        if (io.readFileSync(lockPath, 'utf8') !== lockText) throw uncertain('Outer lock changed.');
        const left = deadline - now(); if (left < 1500) throw uncertain('Bounded workflow deadline exhausted; native work may continue.');
        return Math.min(left, 10000);
    }
    async function send(name, argv, readOnly = false, ownedCompileProof = null) {
        const limit = budget(); let reply;
        if (argv[0] === 'command' && argv[1] === name) argv = [...argv, '--timeout', String(Math.max(1, Math.ceil(limit / 1000)))];
        const attempt = { phase: name, argv, budgetMs: limit, startedAtMs: now() }; (context.facts.attempts ??= []).push(attempt);
        try { reply = await invoke(argv, limit); } catch (e) { throw uncertain(name + ' transport exception: ' + e); }
        attempt.reply = reply; attempt.elapsedMs = now() - attempt.startedAtMs;
        // Only own native compile waiting may tolerate an empty-output status timeout; never a mutation/pre-trigger read.
        if (reply !== null && typeof reply === 'object' && !Array.isArray(reply) && reply.timedOut === true &&
            ['code', 'stdout', 'stderr', 'timedOut'].every(key => Object.hasOwn(reply, key)) && !('error' in reply) &&
            Object.keys(reply).every(key => ['code', 'stdout', 'stderr', 'timedOut', 'error'].includes(key)) &&
            (reply.code === null || Number.isSafeInteger(reply.code)) && reply.stdout === '' && reply.stderr === '' && readOnly &&
            ['editor_status', 'recompile_status'].includes(name) && ownedCompileProof?.runId === runId &&
            ['install', 'cleanup'].includes(ownedCompileProof.kind) &&
            (ownedCompileProof.compilationStarted > 0 || ownedCompileProof.beforeReload > 0)) {
            const timeout = { phase: name, runId, kind: ownedCompileProof.kind, budgetMs: limit,
                remainingMs: deadline - now(), startedAtMs: attempt.startedAtMs, elapsedMs: attempt.elapsedMs, proof: { ...ownedCompileProof }, reply,
                reason: 'Read-only timeout during observed owned native compilation; wait within original deadline only.' };
            attempt.ownedCompileTimeout = timeout; (context.facts.nativeStatusTimeouts ??= []).push(timeout);
            return null;
        }
        if (!reply || reply.timedOut || reply.error) throw uncertain(name + ' timeout/error.');
        let envelope; try { envelope = JSON.parse(reply.stdout); } catch { throw uncertain(name + ' malformed transport JSON.'); }
        const gap = readOnly && ['editor_status', 'recompile_status'].includes(name) && reply.code === 6 &&
            envelope.success === false && envelope.command === 'unity command ' + name && envelope.data === null &&
            Array.isArray(envelope.errors) && envelope.errors.length > 0 && envelope.errors.every(e =>
                e?.code === 'COMMAND_FAILED' && typeof e.message === 'string' &&
                e.message.startsWith("Failed to execute command '" + name + "': Network error:"));
        if (gap) return null;
        if (reply.code !== 0 || envelope.success !== true || envelope.data?.success !== true) throw uncertain(name + ' rejected: ' + JSON.stringify(envelope));
        return { reply, value: envelope.data.result };
    }
    async function native(name, args = [], readOnly = false, ownedCompileProof = null) {
        const response = await send(name, ['command', name, ...args, '--project-path', project, '--format', 'json'], readOnly, ownedCompileProof);
        if (response === null) return null;
        try { return unwrapNative(response.value); } catch { throw uncertain(name + ' malformed nested JSON.'); }
    }
    function idle(value) {
        if (value === null) return false;
        if (typeof value.projectPath !== 'string' || path.resolve(value.projectPath) !== project ||
            typeof value.compiling !== 'boolean' || typeof value.domainReloadInProgress !== 'boolean' || value.playMode !== 'stopped')
            throw uncertain('Unknown/mismatched/playing Editor state.');
        return !value.compiling && !value.domainReloadInProgress;
    }
    async function noBuild() {
        const value = await native('build_status');
        if (!['idle', 'queued', 'building', 'completed'].includes(value?.status)) throw uncertain('Unknown native build status.');
        if (value.status === 'queued' || value.status === 'building') throw uncertain('Active native build forbids C#/asset mutation.');
    }
    async function phase(name, args = [runId], async = false) {
        const script = { runner: 'run_script', file: path.relative(root, file), result: { kind: 'structured' },
            ...(async ? { async: { entry: 'FaultFixture.PollDeferred', args: [runId], pollMs: 250 } } : {}) };
        const response = await send(name, commandArgs(script, 'FaultFixture.' + name, args, budget(), project));
        let verdict; try { verdict = classifyReply(response.reply, script, name === 'Cleanup' ? 'cleanup' : 'result'); }
        catch (e) { throw uncertain(name + ' protocol exception: ' + e); }
        context.facts[name] = verdict;
        if (verdict.uncertain || verdict.testResult?.recoveryRequired || !['passed', 'assertion-failed', ...(async ? ['pending'] : [])].includes(verdict.status))
            throw uncertain(name + ': ' + verdict.detail);
        context.checks += verdict.testResult.checks; return verdict;
    }
    return { io, now, pause, native, idle, noBuild, phase, uncertain, budget };
}

// Generic owned fixture verifier: defaults to FaultFixture; never counts idle/up_to_date as proof.
export async function finishNativeCompile(context, options, dependencies = {}) {
    const { project, runId, kind } = options;
    if (!['install', 'cleanup'].includes(kind)) throw Error('Expected install/cleanup compile phase.');
    const s = session(context, options, dependencies);
    const evidencePath = path.join(project, 'Temp/WhimTex/fault-release', runId, kind + '-compile.json');
    const started = s.now(); let requests = 0;
    const read = () => {
        noLinks(evidencePath, s.io); let e;
        try { e = JSON.parse(s.io.readFileSync(evidencePath, 'utf8')); } catch (error) { throw s.uncertain('Missing/malformed own compile evidence: ' + error); }
        if (e.version !== 1 || e.runId !== runId || e.kind !== kind || !Array.isArray(e.errors) ||
            ![e.beforeReload, e.compilationStarted, e.compilationFinished].every(x => Number.isSafeInteger(x) && x >= 0))
            throw s.uncertain('Malformed owned native evidence.');
        return e;
    };
    const status = async () => {
        const value = await s.native('recompile_status', [], true, read()); if (value === null) return null;
        try { return decodeCompileStatus(value); } catch (e) { throw s.uncertain('Unknown compile status: ' + e); }
    };
    while (true) {
        s.budget(); const e = read(), ready = s.idle(await s.native('editor_status', [], true, e));
        if (ready && e.errors.length) throw Error('Native compilation errors: ' + e.errors.join('\n'));
        if (ready && e.compilationStarted > 0 && e.compilationFinished > 0 && e.beforeReload > 0) {
            const result = await s.phase('VerifyNativeCompile', [runId, kind]);
            context.assert.equal(result.status, 'passed', 'Actual native own-class and old-domain proof');
            context.assert.ok(result.testResult.nativeCompilationComplete && result.testResult.oldDomainGone && result.testResult.beforeReload > 0);
            const proof = { kind, runId, nativeCompilationComplete: true, explicitRequests: requests };
            (context.facts.nativeCompilations ??= []).push(proof); return proof;
        }
        if (ready && e.compilationStarted === 0 && e.beforeReload === 0 && requests === 0 && s.now() - started >= 5000) {
            const value = await status();
            if (value && !['triggered', 'compiling'].includes(value.status)) {
                const latest = read();
                if (latest.compilationStarted === 0 && latest.beforeReload === 0 && s.idle(await s.native('editor_status', [], true))) {
                    requests = 1;
                    s.io.writeFileSync(path.join(path.dirname(evidencePath), kind + '-recompile-request.json'),
                        JSON.stringify({ version: 1, runId, kind, explicitRequests: 1 }), { flag: 'wx' });
                    await s.native('recompile', ['--focus', 'false']);
                }
            }
        }
        if (requests && ready) {
            const value = await status();
            if (value && (value.failed === true || value.errors?.length)) throw Error('Native compile failed: ' + JSON.stringify(value));
        }
        await s.pause(250);
    }
}

export async function orchestrateFaultWorkflow(context, { project, timeoutMs = 180000 }, dependencies = {}) {
    project = path.resolve(project);
    if (project !== exactProject || !Number.isSafeInteger(timeoutMs) || timeoutMs < 15000 || timeoutMs > 180000)
        throw Error('Exact Test6.6 and bounded 15000..180000ms native-fixture budget required.');
    const io = dependencies.fs ?? fs, now = dependencies.now ?? Date.now;
    const runId = (dependencies.guid ?? (() => randomUUID().replaceAll('-', '')))();
    if (!/^[0-9a-f]{32}$/.test(runId)) throw Error('Lowercase N GUID required.');
    const directory = path.join(project, 'Temp/WhimTex/test-runs'), statePath = path.join(project, 'Temp/WhimTex/fault-release', runId, 'state.json');
    noLinks(directory, io); noLinks(statePath, io);
    const lockPath = path.join(directory, 'runner.lock'); noLinks(lockPath, io);
    if (JSON.parse(io.readFileSync(lockPath, 'utf8')).pid !== process.ppid) throw Error('Outer runner must own the sole lock.');
    if (io.existsSync(statePath)) throw Error('Cannot reuse an ownership GUID.');
    const file = path.join(directory, 'fault-workflow-' + runId + '.cs'); noLinks(file, io);
    const source = (dependencies.bundle ?? bundleSources)(sources); io.writeFileSync(file, source, { flag: 'wx' });
    context.facts.input = { runId, file, sources, sha256: createHash('sha256').update(source).digest('hex') };
    const deadline = now() + timeoutMs - 5000, options = { project, file, runId, deadline };
    context.facts.nativeFixtureBudget = { workflow: 'native-fixture', timeoutMs, deadline, processCeilingMs: 10000, reserveMs: 5000 };
    const s = session(context, options, dependencies); let mayOwn = false, primary, cleanupError;
    try {
        context.assert.ok(s.idle(await s.native('editor_status')), 'Direct matching Editor is ready'); await s.noBuild();
        mayOwn = true;
        const setup = await s.phase('Setup'); context.assert.equal(setup.status, 'passed', 'Own native fixture installed');
        await finishNativeCompile(context, { ...options, kind: 'install' }, dependencies);
        const faults = await s.phase('Faults'); context.assert.equal(faults.status, 'passed', 'Independent release fault assertions');
        let deferred = await s.phase('StartDeferred', [runId], true);
        while (deferred.status === 'pending') { await s.pause(250); s.budget(); deferred = await s.phase('PollDeferred', [runId], true); }
        context.assert.equal(deferred.status, 'passed', 'Native deferred import failure/retry assertions');
    }
    catch (e) { primary = e; }
    finally {
        if (mayOwn && !context.recoveryRequired && io.existsSync(statePath)) {
            try {
                // Never enqueue cleanup to discover whether native/deferred work is still active.
                noLinks(statePath, io); const state = JSON.parse(io.readFileSync(statePath, 'utf8'));
                if (state.runId !== runId || ['faults-running', 'deferred-running'].includes(state.phase)) throw s.uncertain('Running/unknown owned test cannot be cleaned.');
                if (!s.idle(await s.native('editor_status'))) throw s.uncertain('Native compilation/reload still active.');
                await s.noBuild(); const result = await s.phase('Cleanup');
                context.assert.equal(result.status, 'passed', 'Exact GUID fixture removed');
                context.assert.equal(result.testResult.phase, 'cleaned');
                if (result.testResult.nativeCompileRequired === false) {
                    context.assert.ok(s.idle(await s.native('editor_status')), 'Partial Setup installed no native source');
                    context.facts.cleanupNoNativeSource = true;
                } else await finishNativeCompile(context, { ...options, kind: 'cleanup' }, dependencies);
            }
            catch (e) { cleanupError = e; s.uncertain('Cleanup/native removal needs recovery: ' + e); }
        }
        if (!context.recoveryRequired) {
            try {
                noLinks(file, io);
                if (path.dirname(file) !== directory || !/^fault-workflow-[0-9a-f]{32}\.cs$/.test(path.basename(file)) || !io.lstatSync(file).isFile()) throw Error('Unexpected owned bundle path.');
                io.unlinkSync(file); context.facts.input.removed = true;
            } catch (e) { cleanupError = e; s.uncertain('Exact bundle cleanup failed: ' + e); }
        }
    }
    if (primary && cleanupError) throw new AggregateError([primary, cleanupError], 'Fault workflow/cleanup failed');
    if (primary) throw primary; if (cleanupError) throw cleanupError;
}
export function createFaultWorkflowContext(project, timeoutMs = 180000, dependencies = {}) {
    const context = new TestContext('Owned native import fixture: install/reload, Faults, Deferred, remove/reload');
    context.case('Standalone exact-GUID native fault lifecycle', () => orchestrateFaultWorkflow(context, { project, timeoutMs }, dependencies));
    return context;
}
export function workflowArguments(argv) {
    if (![2, 4].includes(argv.length) || argv[0] !== '--project-path' || !argv[1] || argv.length === 4 && argv[2] !== '--timeout-ms')
        throw Error('Expected --project-path <Test6.6> [--timeout-ms <15000..180000>].');
    const timeoutMs = argv.length === 4 ? Number(argv[3]) : 180000;
    if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 15000 || timeoutMs > 180000) throw Error('Bounded native-fixture timeout required.');
    return { project: path.resolve(argv[1]), timeoutMs };
}

// In-memory protocol verification: never invokes Unity, creates assets or spawns processes.
export async function runWorkflowProtocolTests() {
    const tests = new TestContext('Standalone fault workflow injected protocol');
    tests.case('native-fixture-timeout-contract', async () => {
        tests.assert.equal(catalogCandidate.workflow, 'native-fixture'); tests.assert.equal(catalogCandidate.runner, 'node');
        tests.assert.equal(catalogCandidate.requiresUnity, true); tests.assert.equal(catalogCandidate.timeoutMs, 180000);
        tests.assert.ok(catalogCandidate.effects.includes('assets') && catalogCandidate.effects.includes('temp-files'));
        tests.assert.equal(workflowArguments(['--project-path', exactProject]).timeoutMs, 180000);
        tests.assert.equal(workflowArguments(['--project-path', exactProject, '--timeout-ms', '180000']).timeoutMs, 180000);
        for (const value of ['180001', '14999', 'NaN', 'Infinity', '180000.5'])
            tests.assert.throws(() => workflowArguments(['--project-path', exactProject, '--timeout-ms', value]));
        await tests.assert.rejects(() => orchestrateFaultWorkflow(new TestContext('invalid'), { project: exactProject, timeoutMs: 180001 },
            { fs: { readFileSync: () => { throw Error('Must not touch files'); } } }), /bounded/);
    });
    for (const mode of ['auto-reload', 'explicit-recompile', 'setup-failed-no-script', 'setup-failed-with-script',
        'compiler-errors', 'deferred-failed', 'deferred-timeout', 'no-reload-proof', 'unknown-compile-status', 'existing-build',
        'compile-status-timeout', 'recompile-status-timeout', 'pretrigger-timeout', 'mutation-timeout', 'recompile-timeout',
        'malformed-status', 'project-mismatch', 'status-timeout-deadline', 'timeout-malformed-stderr', 'timeout-missing-stdout',
        'timeout-empty-error', 'timeout-target', 'timeout-invalid-code', 'timeout-missing-stderr']) {
        tests.case(mode, async () => {
            const project = exactProject, runId = '0123456789abcdef0123456789abcdef';
            const output = path.join(project, 'Temp/WhimTex/fault-release', runId);
            const statePath = path.join(output, 'state.json'), lock = path.join(project, 'Temp/WhimTex/test-runs/runner.lock');
            const file = path.join(project, 'Temp/WhimTex/test-runs/fault-workflow-' + runId + '.cs');
            const memory = new Map([[lock, JSON.stringify({ pid: process.ppid })]]), calls = []; let tick = 0, requests = 0, editorGaps = 0, statusTimeouts = 0;
            const state = { runId, phase: 'installed' };
            const compileFile = kind => path.join(output, kind + '-compile.json');
            const evidence = (kind, complete = true, errors = []) => JSON.stringify({ version: 1, runId, kind,
                compilationStarted: complete ? 1 : 0, compilationFinished: complete ? 1 : 0, beforeReload: complete && !errors.length ? 1 : 0, errors });
            const io = {
                existsSync: p => memory.has(p) || !/\.(cs|json)$/.test(p), realpathSync: p => p,
                lstatSync: () => ({ isSymbolicLink: () => false, isFile: () => true }),
                readFileSync: p => { if (!memory.has(p)) throw Error('Unknown mock file: ' + p); return memory.get(p); },
                writeFileSync: (p, text, options) => { tests.assert.equal(options.flag, 'wx'); tests.assert.ok(!memory.has(p)); memory.set(p, text); },
                unlinkSync: p => { tests.assert.equal(p, file); memory.delete(p); }
            };
            const envelope = result => ({ code: 0, stdout: JSON.stringify({ success: true, data: { success: true, result } }) });
            const reply = result => envelope({ success: true, result: JSON.stringify({ checks: 1, status: 'passed', failures: [], message: 'mock', ...result }) });
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
                if (command === 'build_status') return envelope(JSON.stringify({ status: mode === 'existing-build' ? 'building' : 'idle' }));
                if (command === 'recompile_status') {
                    if (mode === 'recompile-status-timeout' && requests > 0 && statusTimeouts++ === 0) {
                        tick += 8000; return { timedOut: true, code: 143, stdout: '', stderr: '' };
                    }
                    return envelope(JSON.stringify({ status: mode === 'unknown-compile-status' ? 'mystery' : 'completed', failed: false, errors: [] }));
                }
                if (command === 'recompile') {
                    requests++; tests.assert.deepEqual(argv.slice(2, argv.indexOf('--project-path')), ['--focus', 'false']);
                    if (mode !== 'no-reload-proof') memory.set(compileFile('install'), evidence('install'));
                    if (mode === 'recompile-timeout') return { timedOut: true, code: null, stdout: '', stderr: '' };
                    return envelope({ status: 'triggered' });
                }
                tests.assert.equal(command, 'run_script');
                const name = argv[argv.indexOf('--entry') + 1].split('.').at(-1); calls.push(name);
                const args = JSON.parse(argv[argv.indexOf('--args') + 1]); tests.assert.equal(args[0], runId);
                if (name === 'Setup') {
                    memory.set(statePath, JSON.stringify(state));
                    if (mode !== 'setup-failed-no-script') memory.set(compileFile('install'), evidence('install',
                        !['explicit-recompile', 'no-reload-proof', 'unknown-compile-status', 'pretrigger-timeout', 'recompile-timeout', 'recompile-status-timeout'].includes(mode), mode === 'compiler-errors' ? ['CS injected'] : []));
                    if (mode === 'mutation-timeout') return { timedOut: true, code: null, stdout: '', stderr: '' };
                    return reply({ status: mode.startsWith('setup-failed') ? 'failed' : 'passed', failures: mode.startsWith('setup-failed') ? ['Setup fault'] : [] });
                }
                if (name === 'VerifyNativeCompile') {
                    const e = JSON.parse(memory.get(compileFile(args[1]))); tests.assert.ok(e.beforeReload > 0 && !e.errors.length);
                    return reply({ nativeCompilationComplete: true, oldDomainGone: true, beforeReload: 1 });
                }
                if (name === 'Faults') { state.phase = 'faults-completed'; memory.set(statePath, JSON.stringify(state)); return reply(); }
                if (name === 'StartDeferred') { state.phase = 'deferred-running'; memory.set(statePath, JSON.stringify(state)); return reply({ status: 'running' }); }
                if (name === 'PollDeferred') {
                    if (mode === 'deferred-timeout') return reply({ status: 'running' });
                    state.phase = 'deferred-completed'; memory.set(statePath, JSON.stringify(state));
                    return reply({ status: mode === 'deferred-failed' ? 'failed' : 'passed', failures: mode === 'deferred-failed' ? ['retry assertion'] : [] });
                }
                if (name === 'Cleanup') {
                    state.phase = 'cleaned'; memory.set(statePath, JSON.stringify(state));
                    const required = mode !== 'setup-failed-no-script'; if (required) memory.set(compileFile('cleanup'), evidence('cleanup'));
                    return reply({ phase: 'cleaned', nativeCompileRequired: required });
                }
                throw Error('Unexpected entry: ' + name);
            };
            const context = new TestContext(mode); let error;
            try { await orchestrateFaultWorkflow(context, { project }, {
                fs: io, invoke, now: () => tick, delay: async ms => { tick += ms; }, guid: () => runId, bundle: () => '// mock'
            }); } catch (e) { error = e; }
            const uncertain = ['deferred-timeout', 'no-reload-proof', 'unknown-compile-status', 'existing-build', 'pretrigger-timeout',
                'mutation-timeout', 'recompile-timeout', 'malformed-status', 'project-mismatch', 'status-timeout-deadline'].includes(mode) || mode.startsWith('timeout-');
            tests.assert.equal(Boolean(context.recoveryRequired), uncertain);
            tests.assert.equal(Boolean(error), !['auto-reload', 'explicit-recompile', 'compile-status-timeout', 'recompile-status-timeout'].includes(mode));
            tests.assert.equal(calls.filter(x => x === 'Cleanup').length, uncertain ? 0 : 1);
            tests.assert.equal(memory.has(file), uncertain); tests.assert.ok(memory.has(lock));
            tests.assert.equal(requests, ['explicit-recompile', 'no-reload-proof', 'recompile-timeout', 'recompile-status-timeout'].includes(mode) ? 1 : 0);
            if (!uncertain && mode !== 'setup-failed-no-script') tests.assert.ok(context.facts.nativeCompilations.some(x => x.kind === 'cleanup'));
            if (['auto-reload', 'explicit-recompile'].includes(mode)) tests.assert.ok(calls.includes('Faults') && calls.includes('PollDeferred'));
            tests.assert.equal(calls.filter(x => x === 'Setup').length, mode === 'existing-build' ? 0 : 1, 'No Setup replay');
            if (['compile-status-timeout', 'recompile-status-timeout', 'status-timeout-deadline'].includes(mode)) {
                tests.assert.ok(context.facts.nativeStatusTimeouts.length > 0, 'Timeout plus owned proof retained');
                for (const t of context.facts.nativeStatusTimeouts) tests.assert.ok(t.proof.runId === runId &&
                    (t.proof.compilationStarted > 0 || t.proof.beforeReload > 0) && t.reply.timedOut && t.remainingMs < 175000 && t.elapsedMs === 8000);
            } else tests.assert.equal(context.facts.nativeStatusTimeouts, undefined, 'No scoped retries outside observed compile wait');
        });
    }
    return tests.run();
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const args = workflowArguments(process.argv.slice(2)); await finish(createFaultWorkflowContext(args.project, args.timeoutMs));
}
