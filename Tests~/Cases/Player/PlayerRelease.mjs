// Parent first executes Setup, completes ONE native compilation/reload, then PreparePlayer.
// This Node tail is opt-in workflow:'player-build', requiresUnity:true, effects: assets,temp-files,player-build.
// It requires the existing outer runner lock; it never acquires/removes that lock or installs dependencies.
import fs from 'node:fs';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { TestContext } from '../../Framework/test-api.mjs';
import { commandArgs, runProcess, classifyReply, root } from '../../scripts/run-tests.mjs';

const exactProject = path.resolve('D:/DCFA/Projects/Test6.6');
const supported = new Map([
    ['StandaloneWindows', 'win32'], ['StandaloneWindows64', 'win32'],
    ['StandaloneLinux64', 'linux'], ['StandaloneOSX', 'darwin']
]);
const nativeStates = new Set(['idle', 'queued', 'building', 'completed']);

function noLinks(file, io = fs) {
    for (let current = path.resolve(file); ; current = path.dirname(current)) {
        if (io.existsSync(current) && io.lstatSync(current).isSymbolicLink()) throw Error('Redirected owned path: ' + current);
        if (io.existsSync(current) && io.realpathSync(current) !== current) throw Error('Noncanonical owned path: ' + current);
        if (path.dirname(current) === current) break;
    }
}
export function decodeBuildStatus(value) {
    if (typeof value === 'string') value = JSON.parse(value);
    // Transport returns data.result. Parent may supply an exact decoder if its Pipeline uses a different field name.
    const state = value?.status ?? value?.state;
    if (!nativeStates.has(state)) throw Error('Unknown native build_status schema/state: ' + JSON.stringify(value));
    return state;
}
export function playerBinary(target, output, host = process.platform, io = fs) {
    if (!supported.has(target) || supported.get(target) !== host) throw Error('Current Standalone target cannot execute on this host; never switch target/install modules.');
    let binary = output;
    if (target === 'StandaloneOSX') {
        const plist = io.readFileSync(path.join(output, 'Contents/Info.plist'), 'utf8');
        const matches = [...plist.matchAll(/<key>CFBundleExecutable<\/key>\s*<string>([^<]+)<\/string>/g)];
        if (matches.length !== 1 || !/^[A-Za-z0-9 _.-]+$/.test(matches[0][1]) || ['.', '..'].includes(matches[0][1]))
            throw Error('Cannot resolve the exact built macOS executable from its public bundle metadata.');
        binary = path.join(output, 'Contents/MacOS', matches[0][1]);
    }
    noLinks(binary, io);
    if (!io.lstatSync(binary).isFile()) throw Error('Player binary must be the exact owned regular file.');
    return binary;
}

export async function orchestratePlayerRelease(context, { project, file, runId, timeoutMs = 600000 }, dependencies = {}) {
    const io = dependencies.fs ?? fs;
    const invoke = dependencies.invoke ?? ((argv, budget) => runProcess('unity', argv, { cwd: project, timeoutMs: budget }));
    const launch = dependencies.launch ?? ((binary, argv, budget) => runProcess(binary, argv, { cwd: project, timeoutMs: budget }));
    const now = dependencies.now ?? Date.now, pause = dependencies.delay ?? delay;
    const decodeStatus = dependencies.decodeBuildStatus ?? decodeBuildStatus;
    const host = dependencies.platform ?? process.platform;
    // Parent supplies its already-supported, serial native compilation wait. This module never guesses a recompile schema.
    const finishNativeCleanup = dependencies.finishNativeCleanup;
    if (typeof finishNativeCleanup !== 'function') throw Error('Parent-owned finishNativeCleanup callback is required before any Unity execution.');
    if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 120000 || timeoutMs > 1800000) throw Error('Explicit bounded Player workflow budget must be 120000..1800000ms.');
    if (path.resolve(project) !== exactProject || !/^[0-9a-f]{32}$/.test(runId)) throw Error('Exact Test6.6 project and lowercase GUID required.');
    const lockPath = path.join(project, 'Temp/WhimTex/test-runs/runner.lock');
    noLinks(lockPath, io);
    const lock = JSON.parse(io.readFileSync(lockPath, 'utf8'));
    if (lock.pid !== process.ppid) throw Error('Only a child of the live locked outer runner may execute the Player workflow.');
    noLinks(file, io);
    const directory = path.join(project, 'Temp/WhimTex/player-release', runId);
    noLinks(directory, io);
    const state = JSON.parse(io.readFileSync(path.join(directory, 'state.json'), 'utf8'));
    if (state.version !== 1 || state.runId !== runId || !['prepared', 'completed'].includes(state.phase) ||
        path.resolve(state.output) !== directory || state.folder !== 'Assets/WhimTexTestMigration/' + runId ||
        state.scene !== state.folder + '/Probe.unity' || !supported.has(state.target)) throw Error('Parent must supply its durable prepared GUID fixture.');
    const initialLock = JSON.stringify(lock);
    const deadline = now() + timeoutMs - 10000; // Leave outer deadline room for result delivery.
    const attempts = context.facts.attempts = [];
    context.facts.input = { ...context.facts.input, runId, file, directory, parentOwnsNativeCompile: true };
    let armed, nativeCompleted = false, cleanupAllowed = true, primary, cleanupError;
    const script = { runner: 'run_script', file: path.relative(root, file), result: { kind: 'structured' } };

    function uncertain(message) {
        context.recoveryRequired = true; cleanupAllowed = false;
        context.facts.recovery = { runId, file, lock: lockPath, nativeMarker: armed?.nativeMarker,
            instruction: 'Retain outer lock and fixture. Read connected build_status; no run_script/compilation/mutation while queued/building. After confirmed completed, persist native marker and InspectBuild before Cleanup.', message };
        return Error(message);
    }
    function budget(ceiling = 10000) {
        const left = deadline - now();
        if (left < 1500) throw uncertain('Player workflow deadline expired; timeout never cancels native Editor work.');
        return Math.min(ceiling, left);
    }
    function assertLock() {
        if (JSON.stringify(JSON.parse(io.readFileSync(lockPath, 'utf8'))) !== initialLock) throw uncertain('Outer lock changed during Player workflow.');
    }
    async function send(name, argv) {
        assertLock(); const limit = budget();
        if (argv[0] === 'command' && argv[1] === name) argv = [...argv, '--timeout', String(Math.max(1, Math.ceil(limit / 1000)))];
        const attempt = { phase: name, argv, timeoutMs: limit }; attempts.push(attempt);
        let reply;
        try { reply = await invoke(argv, limit); } catch (error) { throw uncertain(name + ' transport exception: ' + error); }
        attempt.reply = reply;
        if (!reply || reply.timedOut || reply.error) throw uncertain(name + ' transport timeout/error; do not retry an armed build.');
        return reply;
    }
    async function native(name, params = []) {
        const reply = await send(name, ['command', name, ...params, '--project-path', project, '--format', 'json']);
        let envelope;
        try { envelope = JSON.parse(reply.stdout); } catch { throw uncertain(name + ' malformed JSON.'); }
        if (reply.code !== 0 || envelope?.success !== true || envelope.data?.success !== true) throw uncertain(name + ' failed: ' + JSON.stringify(envelope));
        return envelope.data.result;
    }
    async function idle() {
        const value = await native('editor_status');
        if (typeof value?.projectPath !== 'string' || path.resolve(value.projectPath) !== project ||
            typeof value.compiling !== 'boolean' || typeof value.domainReloadInProgress !== 'boolean' || value.playMode !== 'stopped')
            throw uncertain('Unknown/mismatched/playing Editor state.');
        return !value.compiling && !value.domainReloadInProgress;
    }
    async function requireIdle() { if (!await idle()) throw uncertain('No new C# may execute while Editor is compiling/reloading.'); }
    async function phase(name, pending = false) {
        const request = pending ? { ...script, async: { entry: 'PlayerReleaseTests.' + name } } : script;
        const reply = await send(name, commandArgs(script, 'PlayerReleaseTests.' + name, [runId], budget(), project));
        let verdict;
        try { verdict = classifyReply(reply, request, name === 'Cleanup' ? 'cleanup' : 'result'); }
        catch (error) { throw uncertain(name + ' structured protocol exception: ' + error); }
        context.facts[name] = verdict;
        if (verdict.uncertain || verdict.testResult?.recoveryRequired || ['timeout', 'transport-error', 'protocol-error', 'execution-failed'].includes(verdict.status))
            throw uncertain(name + ': ' + verdict.detail);
        if (verdict.testResult) context.checks += verdict.testResult.checks;
        context.assert.equal(verdict.status, pending ? 'pending' : 'passed', name + ': ' + verdict.detail);
        return verdict.testResult;
    }
    async function buildState() {
        try { return decodeStatus(await native('build_status')); }
        catch (error) { if (context.recoveryRequired) throw error; throw uncertain('Native build status schema is unknown: ' + error); }
    }
    try {
        await requireIdle();
        const before = await buildState();
        if (before !== 'idle' && before !== 'completed') throw uncertain('An existing native build is queued/building; no fixture C# or cleanup may run.');
        context.assert.ok(true, 'Connected Pipeline has no queued/building operation');
        context.assert.equal(supported.get(state.target), host, 'Current supported Standalone target can run on this host');
        await phase('RestartPlayerLive');
        await requireIdle();
        // Treat even a lost Trigger reply as armed. A timeout must leave durable fixture/outer lock intact.
        armed = await phase('Trigger', true); cleanupAllowed = false;
        const attempt = 'attempt-' + String(armed.attempt).padStart(3, '0');
        const attemptDirectory = path.join(directory, attempt);
        const extension = state.target === 'StandaloneOSX' ? '.app' : state.target === 'StandaloneLinux64' ? '' : '.exe';
        context.assert.ok(Number.isSafeInteger(armed.attempt) && armed.attempt > 0 && armed.runId === runId && armed.target === state.target &&
            armed.phase === 'queued' && armed.scene === state.scene && path.resolve(armed.output) === directory &&
            armed.executable === path.join(attemptDirectory, 'Player' + extension) &&
            armed.nativeMarker === path.join(attemptDirectory, 'native-result.json') &&
            armed.buildMarker === path.join(attemptDirectory, 'build-result.json') &&
            armed.playerReport === path.join(attemptDirectory, 'player-result.json'), 'Trigger returned exact GUID attempt paths');
        // Purpose-built connected Pipeline command, NEVER cold `unity build` or profileName/options/settings changes.
        const acknowledgement = await native('build', ['--target', state.target, '--outputPath', armed.executable,
            '--scenes', JSON.stringify([state.scene]), '--confirm', 'true', '--dry_run', 'false']);
        context.facts.nativeBuildAcknowledgement = acknowledgement;
        let observed;
        try { observed = decodeStatus(acknowledgement); } catch (error) { throw uncertain('Unknown native build acknowledgement: ' + error); }
        if (!['queued', 'building', 'completed'].includes(observed)) throw uncertain('Native build was not acknowledged as queued/building/completed.');
        observed = await buildState(); // Require an explicit build_status completion, even for a fast acknowledgement.
        while (observed !== 'completed') {
            budget(); await pause(500);
            observed = await buildState();
            if (observed === 'idle') throw uncertain('Armed native build became idle without a completed report.');
        }
        nativeCompleted = true;
        const receipt = { version: 1, runId, attempt: armed.attempt, target: armed.target,
            outputPath: armed.executable, status: 'completed', completed: true };
        // Status polling is mainThreadRequired:false; no C# or compilation occurred while native build was queued/building.
        noLinks(armed.nativeMarker, io);
        io.writeFileSync(armed.nativeMarker, JSON.stringify(receipt, null, 2), { flag: 'wx' });
        context.facts.nativeCompletion = receipt;
        while (!await idle()) { budget(); await pause(500); }
        const inspect = await phase('InspectBuild'); // Native BuildReport output identity is the final completion oracle.
        cleanupAllowed = true;
        context.assert.ok(inspect.buildReturned && inspect.phase === 'completed', 'Native report confirmed the owned terminal build');
        const binary = playerBinary(armed.target, armed.executable, host, io);
        const argv = ['-batchmode', '-logFile', path.join(path.dirname(armed.playerReport), 'player.log'),
            '--whimtex-probe-output', armed.playerReport, '--whimtex-probe-run-id', runId, '--whimtex-probe-attempt', String(armed.attempt)];
        // Graphics remain enabled: runtime readback is the actual saved-green oracle.
        assertLock(); const limit = budget(90000);
        let reply;
        try { reply = await launch(binary, argv, limit); } catch (error) { throw uncertain('Player launch exception: ' + error); }
        context.facts.player = { binary, argv, reply };
        if (!reply || reply.timedOut || reply.error) throw uncertain('Player process completion is uncertain; retain fixture and outer lock.');
        context.assert.equal(reply.code, 0, 'Built native Player exits successfully');
        await requireIdle(); await phase('InspectPlayer');
    }
    catch (error) {
        primary = error;
        // If InspectBuild confirmed a terminal report but failed a packing/assertion oracle, cleanup is safe.
        if (nativeCompleted && !context.recoveryRequired) {
            const current = JSON.parse(io.readFileSync(path.join(directory, 'state.json'), 'utf8'));
            cleanupAllowed = current.runId === runId && current.phase === 'completed';
        }
        if (!cleanupAllowed && !context.recoveryRequired) uncertain('Armed build did not reach a confirmed owned report: ' + error);
    }
    finally {
        if (cleanupAllowed && !context.recoveryRequired) {
            try {
                await requireIdle(); const cleanupResult = await phase('Cleanup');
                assertLock();
                const finalized = await finishNativeCleanup({ project, runId, file, deadline, lockPath, cleanupResult });
                if (finalized?.status !== 'passed' || finalized.runId !== runId ||
                    path.resolve(finalized.projectPath ?? '.') !== project || finalized.nativeCompilationComplete !== true)
                    throw uncertain('Parent native cleanup finalizer did not provide completion evidence.');
                assertLock(); await requireIdle();
                context.assert.ok(true, 'Parent completed the final serial native script-removal compilation/reload before releasing lock');
            }
            catch (error) { cleanupError = error; uncertain('Cleanup/native script removal needs recovery: ' + error); }
        }
        context.facts.parentFinalStep = 'After Cleanup deletes the native probe script, wait for its single native compilation/reload to finish before releasing outer lock or any further Unity mutation.';
    }
    if (primary && cleanupError) throw new AggregateError([primary, cleanupError], 'Player assertion and cleanup failed');
    if (primary) throw primary;
    if (cleanupError) throw cleanupError;
}

export function createPlayerReleaseContext(project, runId, dependencies) {
    const context = new TestContext('Independent Player release: native build, TIFF packing, runtime pixels and assembly boundary');
    context.case('Prepared GUID -> native build/build_status -> InspectBuild -> Player -> InspectPlayer -> owned Cleanup', async () => {
        const file = path.join(root, 'Tests~/Cases/Player/PlayerReleaseTests.cs');
        await orchestratePlayerRelease(context, { project: path.resolve(project), file, runId }, dependencies);
    });
    return context;
}

// Export-only adapter. Parent owns the catalogued Node entry, Setup/native-compile/PreparePlayer phases,
// exact build_status decoder and finishNativeCleanup. Importing this module never invokes Unity or a Player.

// Pure protocol tests. All filesystem, CLI, Player and native-finalizer effects below are injected in memory.
export async function runProtocolTests() {
    const tests = new TestContext('Player protocol fault tests without Unity, assets, builds or child processes');
    for (const mode of ['passed', 'build-status-timeout', 'build-status-unknown', 'existing-build', 'trigger-timeout', 'failed-pack', 'player-timeout']) {
        tests.case(mode, async () => {
            const runId = '1234567890abcdef1234567890abcdef';
            const project = exactProject, file = path.join(root, 'Tests~/Cases/Player/PlayerReleaseTests.cs');
            const directory = path.join(project, 'Temp/WhimTex/player-release', runId);
            const attempt = path.join(directory, 'attempt-001');
            const lockPath = path.join(project, 'Temp/WhimTex/test-runs/runner.lock');
            const statePath = path.join(directory, 'state.json');
            const state = { version: 1, runId, phase: 'prepared', output: directory, folder: 'Assets/WhimTexTestMigration/' + runId,
                scene: 'Assets/WhimTexTestMigration/' + runId + '/Probe.unity', target: 'StandaloneWindows64' };
            const files = new Map([[lockPath, JSON.stringify({ pid: process.ppid })], [statePath, JSON.stringify(state)]]);
            const armed = { version: 1, checks: 1, status: 'running', message: 'Arm native build', failures: [], runId, attempt: 1, phase: 'queued', target: state.target,
                scene: state.scene, output: directory, executable: path.join(attempt, 'Player.exe'),
                nativeMarker: path.join(attempt, 'native-result.json'), buildMarker: path.join(attempt, 'build-result.json'),
                playerReport: path.join(attempt, 'player-result.json') };
            const io = {
                existsSync: () => true, realpathSync: value => value,
                lstatSync: () => ({ isSymbolicLink: () => false, isFile: () => true }),
                readFileSync: value => { if (!files.has(value)) throw Error('Unknown in-memory file: ' + value); return files.get(value); },
                writeFileSync: (value, text, options) => {
                    tests.assert.equal(options.flag, 'wx', 'Marker never overwrites existing evidence');
                    tests.assert.ok(!files.has(value)); files.set(value, text);
                }
            };
            const calls = []; let statusCount = 0, tick = 0, nativeFinalized = false;
            const envelope = value => ({ code: 0, stdout: JSON.stringify({ success: true, data: { success: true, result: value } }) });
            const scriptEnvelope = value => envelope({ success: true, result: JSON.stringify(value) });
            const invoke = async (argv, budgetMs) => {
                const command = argv[1];
                if (command !== 'run_script') {
                    tests.assert.equal(argv.filter(x => x === '--timeout').length, 1, 'Exactly one global native CLI timeout');
                    tests.assert.equal(Number(argv[argv.indexOf('--timeout') + 1]), Math.ceil(budgetMs / 1000), 'Build/status CLI timeout matches process budget');
                }
                tests.assert.equal(argv[argv.indexOf('--project-path') + 1], project, 'Every CLI command targets exact connected project');
                if (command === 'editor_status') { calls.push(command); return envelope({ projectPath: project, compiling: false, domainReloadInProgress: false, playMode: 'stopped' }); }
                if (command === 'build_status') {
                    calls.push(command); statusCount++;
                    if (statusCount === 1) return envelope({ status: mode === 'existing-build' ? 'building' : 'idle' });
                    if (mode === 'build-status-timeout') return { timedOut: true, code: null, stdout: '' };
                    if (mode === 'build-status-unknown') return envelope({ status: 'unrecognized' });
                    return envelope({ status: statusCount === 2 ? 'building' : 'completed' });
                }
                if (command === 'build') {
                    calls.push(command);
                    tests.assert.deepEqual(JSON.parse(argv[argv.indexOf('--scenes') + 1]), [state.scene], 'Exactly one owned scene');
                    tests.assert.equal(argv[argv.indexOf('--outputPath') + 1], armed.executable);
                    tests.assert.equal(argv[argv.indexOf('--target') + 1], state.target);
                    tests.assert.equal(argv[argv.indexOf('--confirm') + 1], 'true');
                    tests.assert.ok(!argv.includes('--profileName') && !argv.includes('--options'), 'Default DetailedBuildReport; no profile/settings changes');
                    return envelope({ status: 'queued' });
                }
                tests.assert.equal(command, 'run_script');
                const entry = argv[argv.indexOf('--entry') + 1].split('.').at(-1); calls.push(entry);
                tests.assert.deepEqual(JSON.parse(argv[argv.indexOf('--args') + 1]), [runId], 'One GUID argument for fresh C# entries');
                if (entry === 'Trigger') {
                    state.phase = 'queued'; files.set(statePath, JSON.stringify(state));
                    return mode === 'trigger-timeout' ? { timedOut: true } : scriptEnvelope(armed);
                }
                if (entry === 'InspectBuild') {
                    tests.assert.equal(statusCount, 3, 'No C# until native status is completed');
                    state.phase = 'completed'; files.set(statePath, JSON.stringify(state));
                    return scriptEnvelope({ status: mode === 'failed-pack' ? 'failed' : 'passed', checks: 2, phase: 'completed',
                        buildReturned: true, failures: mode === 'failed-pack' ? ['packing oracle failed'] : [], message: 'pack' });
                }
                return scriptEnvelope({ status: 'passed', checks: 1, failures: [], message: entry });
            };
            const context = new TestContext(mode); let error;
            try {
                await orchestratePlayerRelease(context, { project, file, runId, timeoutMs: 120000 }, {
                    fs: io, invoke, now: () => tick, delay: async ms => { tick += ms; }, platform: 'win32',
                    finishNativeCleanup: async () => { calls.push('nativeFinalizer'); nativeFinalized = true;
                        return { status: 'passed', runId, projectPath: project, nativeCompilationComplete: true }; },
                    launch: async (binary, argv) => {
                        calls.push('Player'); tests.assert.equal(binary, armed.executable);
                        tests.assert.ok(!argv.includes('-nographics')); tests.assert.equal(argv[argv.indexOf('--whimtex-probe-output') + 1], armed.playerReport);
                        return mode === 'player-timeout' ? { timedOut: true } : { code: 0, stdout: '', stderr: '' };
                    }
                });
            }
            catch (cause) { error = cause; }
            if (mode === 'passed') {
                tests.assert.equal(error, undefined); tests.assert.ok(nativeFinalized);
                tests.assert.ok(calls.indexOf('InspectBuild') < calls.indexOf('Player') && calls.indexOf('InspectPlayer') < calls.indexOf('Cleanup'));
                tests.assert.equal(context.recoveryRequired, undefined);
            }
            else if (mode === 'failed-pack') {
                tests.assert.ok(error); tests.assert.ok(calls.includes('Cleanup') && nativeFinalized);
                tests.assert.ok(!calls.includes('Player')); tests.assert.equal(context.recoveryRequired, undefined);
            }
            else {
                tests.assert.ok(error); tests.assert.equal(context.recoveryRequired, true, 'Uncertainty keeps outer lock');
                tests.assert.ok(!calls.includes('Cleanup') && !nativeFinalized, 'Never clean while build/Player completion is uncertain');
                if (mode !== 'player-timeout') tests.assert.ok(!calls.includes('InspectBuild') && !calls.includes('Player'));
                if (mode === 'existing-build') tests.assert.ok(!calls.includes('RestartPlayerLive') && !calls.includes('Trigger'));
                tests.assert.ok(files.has(lockPath), 'No lock deletion');
            }
            tests.assert.equal(calls.filter(value => value === 'build').length, ['existing-build', 'trigger-timeout'].includes(mode) ? 0 : 1, 'Build is never retried');
        });
    }
    return tests.run();
}
