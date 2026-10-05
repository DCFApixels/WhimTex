import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import { archiveMetadata } from './legacy.mjs';
import { resultMarker } from '../Framework/test-api.mjs';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const catalogPath = path.join(root, 'Tests~/scripts/test-catalog.json');
const verdict = (status, detail, uncertain = false) => ({ status, detail, uncertain });
const failText = /^(?:FAIL(?:ED)?|ERROR)\b/i;
const skipText = /^SKIP\b/i;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const sourceFiles = scenarios => [...new Set(scenarios.flatMap(s => [s.file, ...(s.supportFiles ?? []), ...(s.reviewFiles ?? [])]))].sort();
const inside = (base, candidate) => {
    const relative = path.relative(base, candidate);
    return relative !== '' && !relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative);
};

export function productionFingerprint(packageRoot = root) {
    const hash = createHash('sha256');
    const visit = directory => {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
            const file = path.join(directory, entry.name);
            if (entry.isSymbolicLink()) throw Error('Production snapshot does not follow symlinks.');
            if (entry.isDirectory()) visit(file);
            else hash.update(path.relative(packageRoot, file).replaceAll('\\', '/')).update(fs.readFileSync(file));
        }
    };
    visit(path.join(packageRoot, 'src'));
    return hash.digest('hex');
}

export function validateCatalog(catalog, packageRoot = root) {
    if (catalog.version !== 2 || !Array.isArray(catalog.scenarios) || !catalog.profiles) throw Error('Invalid catalog version/shape.');
    const ids = new Set();
    for (const s of catalog.scenarios) {
        if (!/^[a-z0-9-]+$/.test(s.id) || ids.has(s.id)) throw Error('Invalid/duplicate scenario id: ' + s.id);
        ids.add(s.id);
        if (s.reviewFiles !== undefined && !Array.isArray(s.reviewFiles) || s.requiresUnity !== undefined && typeof s.requiresUnity !== 'boolean') throw Error('Invalid review/prerequisite metadata: ' + s.id);
        if (!Array.isArray(s.groups) || !s.groups.length || s.groups.some(group => !/^[a-z0-9-]+$/.test(group))) throw Error('Missing/invalid groups: ' + s.id);
        if (s.legacy !== undefined || s.file.startsWith('Tests~/Legacy/')) throw Error('Retired archive scenarios are not executable: ' + s.id);
        if (s.supportFiles !== undefined && (!Array.isArray(s.supportFiles) || s.runner !== 'run_script' || s.supportFiles.some(file => !file.endsWith('.cs')))) throw Error('Invalid support files: ' + s.id);
        for (const file of [s.file, ...(s.supportFiles ?? []), ...(s.reviewFiles ?? [])]) {
            if (typeof file === 'string') {
                const normalized = path.relative(packageRoot, path.resolve(packageRoot, file)).replaceAll('\\', '/');
                if (normalized === 'Tests~/Legacy' || normalized.startsWith('Tests~/Legacy/'))
                    throw Error('Retired archive dependencies are not executable: ' + s.id);
            }
            const dependency = (s.reviewFiles ?? []).includes(file);
            // The portable format writer is compiled from CURRENT source for its internal unit tests.
            const formatWriter = (s.supportFiles ?? []).includes(file) && file === 'src/PsdWriter.cs';
            const reviewedImplementation = dependency && /^(?:src|Documentation~|Context~|ExternalTools~|Samples~)\//.test(file);
            if (typeof file !== 'string' || !(file.startsWith('Tests~/') || reviewedImplementation || formatWriter) || !inside(packageRoot, path.resolve(packageRoot, file)) || !fs.statSync(path.resolve(packageRoot, file)).isFile() || !inside(fs.realpathSync(packageRoot), fs.realpathSync(path.resolve(packageRoot, file)))) throw Error('Invalid scenario file: ' + s.id);
        }
        if (!['node', 'run_script', 'eval_file'].includes(s.runner) || !Array.isArray(s.args) || !Array.isArray(s.effects)) throw Error('Invalid invocation: ' + s.id);
        if (s.workflow !== undefined && !['player-build', 'native-fixture', 'diagnostic'].includes(s.workflow)) throw Error('Unknown workflow: ' + s.id);
        const playerWorkflow = s.workflow === 'player-build';
        const nativeFixtureWorkflow = s.workflow === 'native-fixture';
        const diagnosticWorkflow = s.workflow === 'diagnostic';
        if (diagnosticWorkflow && (s.category !== 'diagnostic' || s.runner !== 'run_script'))
            throw Error('Long diagnostic workflow must be an independent native diagnostic: ' + s.id);
        if (playerWorkflow && (s.runner !== 'node' || s.requiresUnity !== true ||
            !['assets', 'temp-files', 'player-build'].every(effect => s.effects.includes(effect))))
            throw Error('Player workflow must be independent, Unity-backed and explicitly declare asset/build effects: ' + s.id);
        if (nativeFixtureWorkflow && (s.runner !== 'node' || s.requiresUnity !== true || s.category !== 'regression' ||
            !['assets', 'temp-files'].every(effect => s.effects.includes(effect)) || s.effects.includes('player-build')))
            throw Error('Native fixture workflow requires an independent Unity regression and explicit fixture effects: ' + s.id);
        // Builds can legitimately exceed a short script budget. Only the explicit,
        // separately authorized workflow receives this bounded exception.
        if (!Number.isSafeInteger(s.timeoutMs) || s.timeoutMs < 1000 || s.timeoutMs > (playerWorkflow ? 1800000 : nativeFixtureWorkflow ? 180000 : diagnosticWorkflow ? 300000 : 60000)) throw Error('Invalid timeout: ' + s.id);
        if (!['regression', 'source-guard', 'runner', 'diagnostic'].includes(s.category)) throw Error('Invalid category: ' + s.id);
        for (const name of ['prerequisites', 'setup', 'teardown']) if (typeof s[name] !== 'string' || !s[name].trim()) throw Error('Missing ' + name + ': ' + s.id);
        for (const effect of s.effects) if (!['assets', 'temp-files', 'windows', 'user-state', 'player-build'].includes(effect)) throw Error('Unknown effect: ' + effect);
        if (s.effects.includes('player-build') && !playerWorkflow) throw Error('Player build effect requires its explicit workflow: ' + s.id);
        const validEntry = entry => typeof entry === 'string' && /^[\w.]+$/.test(entry);
        if (s.runner === 'run_script' && !validEntry(s.entry) || s.runner === 'eval_file' && (s.entry || s.args.length)) throw Error('Invalid entry: ' + s.id);
        if (s.result?.kind !== 'structured') throw Error('Active scenarios require structured results: ' + s.id);
        if (s.runner !== 'node' && !s.file.endsWith('.cs') || s.runner === 'node' && !s.file.endsWith('.mjs')) throw Error('Runner/file mismatch: ' + s.id);
        if (s.async) {
            if (s.runner !== 'run_script' || !validEntry(s.async.entry) || !Array.isArray(s.async.args) || !Number.isSafeInteger(s.async.pollMs) || s.async.pollMs < 100 || s.async.pollMs > 5000) throw Error('Invalid async protocol: ' + s.id);
        }
        if (s.cleanup) {
            if (s.runner !== 'run_script' || !validEntry(s.cleanup.entry) || !Array.isArray(s.cleanup.args)) throw Error('Invalid cleanup: ' + s.id);
        }
        if (s.cancel && (!s.async || s.result.kind !== 'structured' || !validEntry(s.cancel.entry) || !Array.isArray(s.cancel.args))) throw Error('Invalid cancellation: ' + s.id);
    }
    for (const [name, profile] of Object.entries(catalog.profiles)) {
        if (!Array.isArray(profile) || profile.length === 0 || new Set(profile).size !== profile.length || profile.some(id => !ids.has(id))) throw Error('Invalid profile: ' + name);
    }
    return catalog;
}

export function selectScenarios(catalog, { profile, id, ids, group, all } = {}) {
    if ([profile, id, ids, group, all].filter(Boolean).length > 1) throw Error('Choose one selector: --profile, --id, --ids, --group or --all.');
    if (profile && !Object.hasOwn(catalog.profiles, profile)) throw Error('Unknown profile: ' + profile);
    const selectedIds = profile ? catalog.profiles[profile] : id ? [id] : ids ? ids.split(',')
        : group ? catalog.scenarios.filter(s => s.groups.includes(group)).map(s => s.id) : catalog.scenarios.map(s => s.id);
    if (!selectedIds.length || new Set(selectedIds).size !== selectedIds.length || selectedIds.some(value => !value)) throw Error('Empty/duplicate selection.');
    return selectedIds.map(value => {
        const s = catalog.scenarios.find(scenario => scenario.id === value);
        if (!s) throw Error('Unknown scenario: ' + value);
        return s;
    });
}

export function reviewFingerprint(scenarios, packageRoot = root) {
    const hash = createHash('sha256').update(JSON.stringify(scenarios));
    const files = sourceFiles(scenarios);
    for (const file of files) hash.update(file).update(fs.readFileSync(path.join(packageRoot, file)));
    // The dispatcher contract participates in every review receipt.
    hash.update(fs.readFileSync(path.join(packageRoot, 'Tests~/scripts/run-tests.mjs')));
    for (const file of ['Tests~/scripts/legacy.mjs', 'Tests~/Framework/test-api.mjs', 'Tests~/Framework/TestApi.cs', 'Tests~/Framework/EntryContract.cs', 'Tests~/legacy-manifest.json', 'Tests~/archive-descriptor.json']) hash.update(fs.readFileSync(path.join(packageRoot, file)));
    return hash.digest('hex');
}

export function bundleSources(files, packageRoot = root) {
    const imports = new Set();
    const bodies = files.map(relative => {
        const source = fs.readFileSync(path.join(packageRoot, relative), 'utf8');
        // Only unindented global directives, not using statements or namespace-local imports.
        // Blank replacement preserves the original line numbers in every #line section.
        const body = source.replace(/^using[ \t]+(?:static[ \t]+)?[\w.]+(?:[ \t]*=[ \t]*[\w.]+)?;[ \t]*\r?$/gm,
            directive => { imports.add(directive.trim()); return ''; });
        return '#line 1 "' + relative + '"\n' + body;
    });
    return [...imports].join('\n') + '\n' + bodies.join('\n');
}

export function entryContracts(scenario) {
    return { phases: [scenario, scenario.async, scenario.cancel, scenario.cleanup].filter(Boolean)
        .map(phase => ({ entry: phase.entry, arguments: phase.args.length })) };
}

export function classifyStructured(value, scenario, phase = 'result') {
    const recoveryDebt = value && typeof value === 'object' && Object.hasOwn(value, 'recoveryRequired') && value.recoveryRequired !== false;
    const invalid = detail => ({ ...verdict('protocol-error', detail, Boolean(recoveryDebt)),
        ...(recoveryDebt ? { recoveryRequired: true } : {}) });
    if (!value || !['running', 'passed', 'failed', 'skipped', 'cancelled'].includes(value.status)
        || !Number.isSafeInteger(value.checks) || value.checks < 0 || typeof value.message !== 'string'
        || !Array.isArray(value.failures) || value.status === 'failed' && !value.failures.length
        || value.status !== 'failed' && value.failures.length) return invalid('Malformed structured test result: ' + JSON.stringify(value));
    if (value.status === 'passed' && value.checks === 0 && phase !== 'cleanup')
        return invalid('Passed test did not execute any assertions.');
    const status = { running: 'pending', passed: 'passed', failed: 'assertion-failed', skipped: 'skip', cancelled: 'cancelled' }[value.status];
    if (value.recoveryRequired !== undefined && typeof value.recoveryRequired !== 'boolean')
        return { ...verdict('protocol-error', 'Invalid recoveryRequired flag.', true), recoveryRequired: true };
    if (value.recoveryRequired === true && status === 'passed')
        return { ...verdict('protocol-error', 'Passed result still requires owned cleanup/recovery.', true), testResult: value, recoveryRequired: true };
    if (status === 'pending' && (!scenario.async || phase === 'cleanup' || phase === 'cancel')) return verdict('protocol-error', 'Running is not a final result.', true);
    if (phase === 'cancel' && status !== 'cancelled') return verdict('protocol-error', 'Cancellation did not confirm cancelled.', true);
    return { ...verdict(status, value.message, value.recoveryRequired === true), testResult: value };
}

export function classifyReply(reply, scenario, phase = 'result') {
    if (reply.timedOut) return verdict('timeout', 'CLI wait expired; Editor execution may still be running.', scenario.runner !== 'node' || Boolean(scenario.requiresUnity));
    if (reply.error) return verdict('transport-error', reply.error, Boolean(reply.uncertain));
    let value;
    if (scenario.runner === 'node') {
        if (scenario.result.kind === 'structured') {
            const lines = (reply.stdout ?? '').split(/\r?\n/).filter(line => line.startsWith(resultMarker));
            if (lines.length !== 1) return verdict('protocol-error', 'Node must emit exactly one structured result marker.');
            try { value = JSON.parse(lines[0].slice(resultMarker.length)); } catch { return verdict('protocol-error', 'Invalid Node result JSON.'); }
            const result = classifyStructured(value, scenario, phase);
            if (reply.code !== 0 && result.status === 'passed') return verdict('protocol-error', 'Passed payload with unsuccessful Node exit.');
            // A nested live runner can finish while an Editor task is still uncertain.
            // Never release the outer lock merely because the Node process exited.
            if (scenario.requiresUnity && value.recoveryRequired === true) result.uncertain = true;
            return result;
        }
        if (reply.code !== 0) return verdict('assertion-failed', reply.stderr || reply.stdout || 'Node exit ' + reply.code, Boolean(scenario.requiresUnity));
        value = (reply.stdout ?? '').trim();
        if (/^(?:FAIL(?:ED)?|ERROR)\b/im.test(value)) return verdict('assertion-failed', value);
        if (/^SKIP\b/im.test(value)) return verdict('skip', value);
        if (/^(?:ℹ |# )?skipped [1-9][0-9]*\b/im.test(value)) return verdict('skip', value);
        return verdict('passed', value);
    }
    let envelope;
    try { envelope = JSON.parse(reply.stdout); } catch { return verdict('transport-error', 'CLI did not return a JSON envelope.', true); }
    if (reply.code !== 0 || envelope.success !== true) {
        const detail = JSON.stringify(envelope.errors ?? envelope);
        const timeout = /timed?\s*out|timeout/i.test(detail);
        return verdict(timeout ? 'timeout' : 'transport-error', detail, timeout);
    }
    if (envelope.data?.success !== true) return verdict('api-failed', JSON.stringify(envelope.data));
    value = envelope.data.result;
    // Both current Pipeline commands wrap their compile/evaluate verdict around the test payload.
    if (!value || value.success !== true) {
        const detail = JSON.stringify(value);
        const timeout = /timed?[\s_-]*out|timeout/i.test(detail ?? '');
        return verdict(timeout ? 'timeout' : 'execution-failed', detail, timeout);
    }
    if (value.diagnostics?.some(d => /error/i.test(d.severity ?? '')) || value.errorDetails?.length) return verdict('execution-failed', JSON.stringify(value));
    value = value.result;
    if (typeof value === 'string' && value.trim().startsWith('{')) {
        try { value = JSON.parse(value); } catch { /* An ordinary result string is checked below. */ }
    }
    if (value?.success === false) return verdict('api-failed', JSON.stringify(value));
    if (scenario.result.kind === 'structured') return classifyStructured(value, scenario, phase);
    if (typeof value === 'string') {
        const text = value.trim();
        if (failText.test(text)) return verdict('assertion-failed', text);
        if (skipText.test(text)) return verdict('skip', text);
        if (phase === 'start') return new RegExp(scenario.async.started).test(text)
            ? verdict('pending', text) : verdict('protocol-error', 'Unexpected start result: ' + text, true);
        if (scenario.result.kind === 'text' && new RegExp(scenario.result.pass).test(text)) return verdict('passed', text);
        if (scenario.async && new RegExp(scenario.result.pending).test(text)) return verdict('pending', text);
    }
    if (phase === 'result' && scenario.result.kind === 'json-success' && value?.success === true) return verdict('passed', JSON.stringify(value));
    return verdict('protocol-error', 'No declared final verdict: ' + JSON.stringify(value));
}

export function classifyCompileReply(reply, scenario) {
    if (reply.timedOut) return verdict('timeout', 'Compilation wait expired; completion is unknown.', scenario.runner !== 'node');
    if (reply.error) return verdict('transport-error', reply.error, Boolean(reply.uncertain));
    if (scenario.runner === 'node') return reply.code === 0 ? verdict('compiled', 'Node syntax checked; assertions NOT executed.')
        : verdict('compile-failed', reply.stderr || reply.stdout || 'Node syntax check failed.');
    let envelope;
    try { envelope = JSON.parse(reply.stdout); } catch { return verdict('transport-error', 'Compilation returned no JSON envelope.', true); }
    if (reply.code !== 0 || envelope.success !== true || envelope.data?.success !== true)
        return verdict('transport-error', JSON.stringify(envelope.errors ?? envelope.data), /timeout/i.test(JSON.stringify(envelope)));
    const result = envelope.data.result;
    if (result?.success !== true || result.diagnostics?.some(d => /error/i.test(d.severity ?? '')))
        return verdict('compile-failed', JSON.stringify(result));
    return verdict('compiled', 'Unity dry_run compilation succeeded; assembly NOT loaded and assertions NOT executed.');
}

export async function runScenario(scenario, invoke, { now = Date.now, delay = sleep } = {}) {
    const started = now();
    const attempts = [];
    const deadline = started + scenario.timeoutMs;
    let result;
    let asyncStarted = false;
    const call = async (entry, args, phase) => {
        let reply;
        try { reply = await invoke(scenario, entry, args, phase === 'cancel' ? 10000 : Math.max(1, deadline - now())); }
        catch (error) { reply = { error: String(error), uncertain: scenario.runner !== 'node' || Boolean(scenario.requiresUnity) }; }
        attempts.push({ phase, entry: entry ?? null, reply });
        return classifyReply(reply, scenario, phase);
    };
    try {
        result = await call(scenario.entry, scenario.args, scenario.async ? 'start' : 'result');
        asyncStarted = Boolean(scenario.async && result.status === 'pending');
        while (scenario.async && result.status === 'pending') {
            if (now() >= deadline) { result = verdict('timeout', 'Async final result was not reached.', true); break; }
            await delay(Math.min(scenario.async.pollMs, deadline - now()));
            if (now() >= deadline) { result = verdict('timeout', 'Async final result was not reached.', true); break; }
            result = await call(scenario.async.entry, scenario.async.args, 'result');
        }
    } catch (error) { result = verdict('transport-error', String(error), scenario.runner !== 'node' || Boolean(scenario.requiresUnity)); }
    finally {
        // An inner lifecycle retained its ownership journal. Generic bridge cleanup
        // cannot prove that this separate lifecycle stopped or safely replay it.
        const innerRecovery = result.recoveryRequired === true || result.testResult?.recoveryRequired === true;
        if (asyncStarted && ['transport-error', 'execution-failed', 'api-failed', 'protocol-error'].includes(result.status)) result.uncertain = true;
        if (!innerRecovery && scenario.cancel && (result.uncertain || result.status === 'pending' || result.status === 'timeout')) {
            const cancellation = await call(scenario.cancel.entry, scenario.cancel.args, 'cancel');
            if (cancellation.status !== 'cancelled') result = { ...result, uncertain: true, cancellation };
            // Cancellation acknowledgement never erases earlier transport uncertainty.
        }
        if (!innerRecovery && scenario.cleanup) {
            let cleanup;
            try { cleanup = await invoke(scenario, scenario.cleanup.entry, scenario.cleanup.args, 10000); }
            catch (error) { cleanup = { error: String(error) }; }
            attempts.push({ phase: 'cleanup', entry: scenario.cleanup.entry, reply: cleanup });
            const cleanupScenario = { ...scenario, async: undefined, result: scenario.result.kind === 'structured' ? { kind: 'structured' } : { kind: 'text', pass: scenario.cleanup.pass } };
            const cleanupResult = classifyReply(cleanup, cleanupScenario, 'cleanup');
            if (cleanupResult.status !== 'passed') result = { ...verdict('cleanup-failed', cleanupResult.detail, true), primaryResult: result, cleanupResult };
            // A timeout remains uncertain: successful cleanup does not prove a detached task stopped.
        }
    }
    return { id: scenario.id, file: scenario.file, runner: scenario.runner, category: scenario.category,
        ...result, durationMs: now() - started, attempts };
}

export function commandArgs(scenario, entry, args, budgetMs, projectPath, packageRoot = root) {
    const file = path.relative(projectPath, path.join(packageRoot, scenario.file)).replaceAll('\\', '/');
    const argv = ['command', scenario.runner, '--file', file];
    if (scenario.runner === 'run_script') argv.push('--entry', entry, '--args', JSON.stringify(args), '--timeout_ms', String(Math.max(1, Math.min(45000, budgetMs - 1000))));
    // --timeout is the CLI's seconds option, not eval_file's milliseconds parameter.
    argv.push('--project-path', projectPath, '--timeout', String(Math.max(1, Math.ceil(budgetMs / 1000))), '--format', 'json');
    return argv;
}

export function runProcess(binary, args, { cwd = root, timeoutMs = 50000 } = {}) {
    return new Promise(resolve => {
        let stdout = '', stderr = '', timedOut = false, error;
        const child = spawn(binary, args, { cwd, shell: false, windowsHide: true });
        const timer = setTimeout(() => { timedOut = true; child.kill(); }, timeoutMs);
        child.stdout.on('data', chunk => { stdout += chunk; });
        child.stderr.on('data', chunk => { stderr += chunk; });
        child.on('error', cause => { error = String(cause); });
        child.on('close', code => { clearTimeout(timer); resolve({ code, stdout, stderr, timedOut, ...(error ? { error } : {}) }); });
    });
}

function options(argv) {
    const result = {};
    const flags = new Set(['list', 'review', 'run', 'compile', 'check-entries', 'all', 'keep-going']);
    const values = new Set(['profile', 'id', 'ids', 'group', 'project-path', 'reviewed', 'allow-effects', 'output']);
    for (let i = 0; i < argv.length; i++) {
        const key = argv[i].replace(/^--/, '');
        if (!argv[i].startsWith('--') || Object.hasOwn(result, key) || !flags.has(key) && !values.has(key)) throw Error('Unknown/duplicate option: ' + argv[i]);
        result[key] = flags.has(key) ? true : argv[++i];
        if (result[key] === undefined || typeof result[key] === 'string' && result[key].startsWith('--')) throw Error('Missing value: ' + key);
    }
    if (['list', 'review', 'run', 'compile', 'check-entries'].filter(key => result[key]).length > 1) throw Error('Choose one action: --list, --review, --compile, --check-entries or --run.');
    if (!result.run && !result.compile && !result['check-entries'] && (result.reviewed || result['allow-effects'] || result.output)) throw Error('Execution options require --run, --compile or --check-entries.');
    if (result['keep-going'] && !result.run) throw Error('--keep-going requires --run.');
    return result;
}

export async function main(argv = process.argv.slice(2)) {
    const opt = options(argv);
    const metadata = archiveMetadata();
    const archive = { files: metadata.descriptor.files, baselineCommit: metadata.manifest.baselineCommit,
        manifestHash: metadata.manifestHash, source: 'pinned-git-reference',
        recoveryCommit: metadata.descriptor.commit, contentsVerified: false };
    const catalog = validateCatalog(JSON.parse(fs.readFileSync(catalogPath, 'utf8')));
    const scenarios = selectScenarios(catalog, opt);
    const fingerprint = reviewFingerprint(scenarios);
    if (opt.review) {
        console.log(JSON.stringify({ scenarios, fingerprint }, null, 2));
        for (const file of sourceFiles(scenarios)) console.log('\n--- ' + file + ' ---\n' + fs.readFileSync(path.join(root, file), 'utf8'));
        return 0;
    }
    if (!opt.run && !opt.compile && !opt['check-entries']) {
        console.log(JSON.stringify({ scope: catalog.scope, profiles: catalog.profiles, scenarios, archive,
            note: 'Only independent registered scenarios are executable. Archive contents are verified separately by archive-integrity from the pinned Git snapshot.' }, null, 2));
        return 0;
    }
    if (!opt.profile && !opt.id && !opt.ids && !opt.group && !opt.all) throw Error('Verification requires an explicit selector.');
    if (opt.reviewed !== fingerprint) throw Error('Read --review output first, then pass its current fingerprint with --reviewed.');
    const allowed = new Set((opt['allow-effects'] ?? '').split(',').filter(Boolean));
    if (opt.run) for (const s of scenarios) for (const effect of s.effects) if (!allowed.has(effect)) throw Error(s.id + ' requires --allow-effects ' + effect + ' and existing user authority.');
    if (opt.compile && scenarios.some(s => s.runner === 'eval_file')) throw Error('Compile-only does not accept executable snippets.');
    if (opt['check-entries'] && scenarios.some(s => s.runner !== 'run_script')) throw Error('Entry validation accepts independent C# cases only, never runs domain bodies.');
    const hasUnity = scenarios.some(s => s.runner !== 'node' || !opt.compile && s.requiresUnity);
    const inferredProject = path.resolve(root, '../..');
    const projectPath = opt['project-path'] ? path.resolve(opt['project-path'])
        : inside(path.join(inferredProject, 'Packages'), root) ? inferredProject : root;
    if (hasUnity && !opt['project-path']) throw Error('Unity scenarios require explicit --project-path.');
    if (hasUnity && !inside(path.join(projectPath, 'Packages'), root)) throw Error('This package is not inside the requested project Packages directory.');
    const directory = path.join(projectPath, 'Temp/WhimTex/test-runs');
    fs.mkdirSync(directory, { recursive: true });
    if (!inside(fs.realpathSync(projectPath), fs.realpathSync(directory))) throw Error('Report directory resolves outside the requested project.');
    const output = path.resolve(projectPath, opt.output ?? 'Temp/WhimTex/test-runs/' + new Date().toISOString().replaceAll(':', '-') + '-' + process.pid + '.json');
    if (!inside(directory, output) || path.dirname(output) !== directory || !output.endsWith('.json')) throw Error('--output must be a new JSON file directly under Temp/WhimTex/test-runs.');
    const lock = path.join(directory, 'runner.lock');
    const lockHandle = fs.openSync(lock, 'wx');
    let outputHandle;
    let keepLock = false;
    const report = { version: 2, action: opt.compile ? 'compile' : opt['check-entries'] ? 'entries' : 'run', startedAt: new Date().toISOString(), projectPath, fingerprint, archive, productionFingerprint: productionFingerprint(),
        selected: scenarios.map(s => s.id), environment: null, results: [], success: false };
    const save = () => { fs.ftruncateSync(outputHandle, 0); fs.writeSync(outputHandle, JSON.stringify(report, null, 2) + '\n', 0, 'utf8'); };
    try {
        fs.writeFileSync(lockHandle, JSON.stringify({ pid: process.pid, output, startedAt: report.startedAt }));
        outputHandle = fs.openSync(output, 'wx');
        save();
        if (hasUnity) {
            const reply = await runProcess('unity', ['command', 'editor_status', '--project-path', projectPath, '--format', 'json'], { cwd: projectPath, timeoutMs: 15000 });
            let envelope;
            try { envelope = JSON.parse(reply.stdout); } catch { throw Error('Editor preflight returned no JSON: ' + reply.stderr); }
            const state = envelope.data?.result;
            if (reply.code !== 0 || envelope.success !== true || envelope.data?.success !== true || path.resolve(state?.projectPath ?? '.') !== projectPath || state.compiling || state.domainReloadInProgress || state.playMode !== 'stopped') throw Error('Requested Editor is unavailable, compiling, reloading, playing or mismatched.');
            report.environment = state;
        }
        const runId = randomUUID();
        const bundles = new Map();
        const invoke = async (s, entry, args, budgetMs) => {
            args = args.map(value => value === '$runId' ? runId : value === '$projectPath' ? projectPath : value);
            if (s.runner === 'node') {
                return runProcess(process.execPath, [...(opt.compile ? ['--check'] : []), path.join(root, s.file), ...args], { timeoutMs: budgetMs });
            }
            let invocation = s;
            if (s.supportFiles?.length || opt['check-entries']) {
                if (!bundles.has(s.id)) {
                    const file = path.join(directory, 'input-' + runId + '-' + s.id + '.cs');
                    const sources = [...new Set([s.file, ...(s.supportFiles ?? []), ...(opt['check-entries'] ? ['Tests~/Framework/TestApi.cs', 'Tests~/Framework/EntryContract.cs'] : [])])];
                    const source = bundleSources(sources);
                    fs.writeFileSync(file, source, { flag: 'wx' }); bundles.set(s.id, file);
                    (report.inputs ??= []).push({ id: s.id, file, sources, sha256: createHash('sha256').update(source).digest('hex') });
                }
                invocation = { ...s, file: path.relative(root, bundles.get(s.id)) };
            }
            const argv = commandArgs(invocation, opt['check-entries'] ? 'WhimTex.Tests.EntryContract.Verify' : entry,
                opt['check-entries'] ? [entryContracts(s).phases.map(phase => phase.entry + '|' + phase.arguments).join('\n')] : args, budgetMs, projectPath);
            if (opt.compile) argv.push('--dry_run', 'true');
            return runProcess('unity', argv, { cwd: projectPath, timeoutMs: budgetMs });
        };
        for (const s of scenarios) {
            if (reviewFingerprint(scenarios) !== fingerprint) throw Error('Reviewed source changed during the run; stopping before next scenario.');
            archiveMetadata();
            if (productionFingerprint() !== report.productionFingerprint) throw Error('Production sources changed during the run; stopping before next scenario.');
            const compileStarted = Date.now();
            const compileReply = opt.compile || opt['check-entries'] ? await invoke(s, s.entry, s.args, s.timeoutMs) : null;
            let verification = opt['check-entries'] ? classifyReply(compileReply, s) : opt.compile ? classifyCompileReply(compileReply, s) : null;
            if (opt['check-entries'] && verification.status === 'passed') verification = { ...verification, status: 'entry-validated' };
            const result = verification ? { id: s.id, file: s.file, runner: s.runner, category: s.category,
                ...verification, durationMs: Date.now() - compileStarted,
                attempts: [{ phase: opt.compile ? 'compile' : 'entries', entry: opt.compile ? s.entry ?? null : 'WhimTex.Tests.EntryContract.Verify', reply: compileReply }] } : await runScenario(s, invoke);
            report.results.push(result);
            keepLock ||= result.uncertain;
            save();
            console.log(s.id + ': ' + result.status + ' (' + result.durationMs + 'ms)');
            // Explicit broad verification may collect known failures, but never
            // start more work after uncertain execution/cancellation/cleanup.
            if (result.uncertain || opt.run && !opt['keep-going'] && result.status !== 'passed') break;
        }
        report.notRun = scenarios.slice(report.results.length).map(s => s.id);
        report.success = report.results.length === scenarios.length && report.results.every(r => r.status === (opt.compile ? 'compiled' : opt['check-entries'] ? 'entry-validated' : 'passed'));
        report.recoveryRequired = keepLock;
        return report.success ? 0 : 1;
    } catch (error) {
        report.error = String(error);
        console.error(report.error);
        return 1;
    } finally {
        report.finishedAt = new Date().toISOString();
        if (outputHandle !== undefined) { save(); fs.closeSync(outputHandle); }
        fs.closeSync(lockHandle);
        if (!keepLock) fs.unlinkSync(lock);
        if (outputHandle !== undefined) console.log('Report: ' + output);
        if (keepLock) console.error('Runner lock retained: inspect Editor execution/cleanup before removing ' + lock + '. Never blindly retry.');
    }
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
    main().then(code => { process.exitCode = code; }).catch(error => { console.error(String(error)); process.exitCode = 2; });
}
