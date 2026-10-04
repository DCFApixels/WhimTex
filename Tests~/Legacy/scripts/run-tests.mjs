import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const catalogPath = path.join(root, 'Tests~/scripts/test-catalog.json');
const verdict = (status, detail, uncertain = false) => ({ status, detail, uncertain });
const failText = /^(?:FAIL(?:ED)?|ERROR)\b/i;
const skipText = /^SKIP\b/i;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const sourceFiles = scenarios => [...new Set(scenarios.flatMap(s => [s.file, ...(s.reviewFiles ?? [])]))].sort();
const inside = (base, candidate) => {
    const relative = path.relative(base, candidate);
    return relative !== '' && !relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative);
};

export function validateCatalog(catalog, packageRoot = root) {
    if (catalog.version !== 1 || !Array.isArray(catalog.scenarios) || !catalog.profiles) throw Error('Invalid catalog version/shape.');
    const ids = new Set();
    for (const s of catalog.scenarios) {
        if (!/^[a-z0-9-]+$/.test(s.id) || ids.has(s.id)) throw Error('Invalid/duplicate scenario id: ' + s.id);
        ids.add(s.id);
        if (s.reviewFiles !== undefined && !Array.isArray(s.reviewFiles) || s.requiresUnity !== undefined && typeof s.requiresUnity !== 'boolean') throw Error('Invalid review/prerequisite metadata: ' + s.id);
        for (const file of [s.file, ...(s.reviewFiles ?? [])]) {
            if (typeof file !== 'string' || !file.startsWith('Tests~/') || !inside(path.join(packageRoot, 'Tests~'), path.resolve(packageRoot, file)) || !fs.statSync(path.resolve(packageRoot, file)).isFile() || !inside(fs.realpathSync(path.join(packageRoot, 'Tests~')), fs.realpathSync(path.resolve(packageRoot, file)))) throw Error('Invalid scenario file: ' + s.id);
        }
        if (!['node', 'run_script', 'eval_file'].includes(s.runner) || !Array.isArray(s.args) || !Array.isArray(s.effects)) throw Error('Invalid invocation: ' + s.id);
        if (!Number.isSafeInteger(s.timeoutMs) || s.timeoutMs < 1000 || s.timeoutMs > 60000) throw Error('Invalid timeout: ' + s.id);
        if (!['regression', 'source-guard', 'runner', 'diagnostic'].includes(s.category)) throw Error('Invalid category: ' + s.id);
        for (const name of ['prerequisites', 'setup', 'teardown']) if (typeof s[name] !== 'string' || !s[name].trim()) throw Error('Missing ' + name + ': ' + s.id);
        for (const effect of s.effects) if (!['assets', 'temp-files', 'windows', 'user-state'].includes(effect)) throw Error('Unknown effect: ' + effect);
        const validEntry = entry => typeof entry === 'string' && /^[\w.]+$/.test(entry);
        if (s.runner === 'run_script' && !validEntry(s.entry) || s.runner === 'eval_file' && (s.entry || s.args.length)) throw Error('Invalid entry: ' + s.id);
        if (s.runner === 'node' && s.result?.kind !== 'exit-code' || s.runner !== 'node' && !['text', 'json-success'].includes(s.result?.kind)) throw Error('Invalid result protocol: ' + s.id);
        if (s.runner !== 'node' && !s.file.endsWith('.cs') || s.runner === 'node' && !s.file.endsWith('.mjs')) throw Error('Runner/file mismatch: ' + s.id);
        if (s.result.kind === 'text') {
            if (typeof s.result.pass !== 'string' || !s.result.pass.startsWith('^')) throw Error('Missing anchored pass: ' + s.id);
            new RegExp(s.result.pass);
        }
        if (s.async) {
            if (s.runner !== 'run_script' || !validEntry(s.async.entry) || !Array.isArray(s.async.args) || !Number.isSafeInteger(s.async.pollMs) || s.async.pollMs < 100 || s.async.pollMs > 5000) throw Error('Invalid async protocol: ' + s.id);
            for (const pattern of [s.async.started, s.result.pending]) {
                if (typeof pattern !== 'string' || !pattern.startsWith('^')) throw Error('Missing anchored async pattern: ' + s.id);
                new RegExp(pattern);
            }
        }
        if (s.cleanup) {
            if (s.runner !== 'run_script' || !validEntry(s.cleanup.entry) || !Array.isArray(s.cleanup.args) || typeof s.cleanup.pass !== 'string' || !s.cleanup.pass.startsWith('^')) throw Error('Invalid cleanup: ' + s.id);
            new RegExp(s.cleanup.pass);
        }
    }
    for (const [name, profile] of Object.entries(catalog.profiles)) {
        if (!Array.isArray(profile) || profile.length === 0 || new Set(profile).size !== profile.length || profile.some(id => !ids.has(id))) throw Error('Invalid profile: ' + name);
    }
    return catalog;
}

export function selectScenarios(catalog, { profile, id } = {}) {
    if (profile && id) throw Error('Choose --profile OR --id.');
    if (profile && !Object.hasOwn(catalog.profiles, profile)) throw Error('Unknown profile: ' + profile);
    const ids = profile ? catalog.profiles[profile] : id ? [id] : catalog.scenarios.map(s => s.id);
    return ids.map(value => {
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
    return hash.digest('hex');
}

export function classifyReply(reply, scenario, phase = 'result') {
    if (reply.timedOut) return verdict('timeout', 'CLI wait expired; Editor execution may still be running.', scenario.runner !== 'node' || Boolean(scenario.requiresUnity));
    if (reply.error) return verdict('transport-error', reply.error, Boolean(reply.uncertain));
    let value;
    if (scenario.runner === 'node') {
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

export async function runScenario(scenario, invoke, { now = Date.now, delay = sleep } = {}) {
    const started = now();
    const attempts = [];
    const deadline = started + scenario.timeoutMs;
    let result;
    let asyncStarted = false;
    const call = async (entry, args, phase) => {
        let reply;
        try { reply = await invoke(scenario, entry, args, Math.max(1, deadline - now())); }
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
        if (asyncStarted && ['transport-error', 'execution-failed', 'api-failed', 'protocol-error'].includes(result.status)) result.uncertain = true;
        if (scenario.cleanup) {
            let cleanup;
            try { cleanup = await invoke(scenario, scenario.cleanup.entry, scenario.cleanup.args, 10000); }
            catch (error) { cleanup = { error: String(error) }; }
            attempts.push({ phase: 'cleanup', entry: scenario.cleanup.entry, reply: cleanup });
            const cleanupScenario = { ...scenario, async: undefined, result: { kind: 'text', pass: scenario.cleanup.pass } };
            const cleanupResult = classifyReply(cleanup, cleanupScenario);
            if (cleanupResult.status !== 'passed') result = verdict('cleanup-failed', cleanupResult.detail, true);
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
    const flags = new Set(['list', 'review', 'run']);
    const values = new Set(['profile', 'id', 'project-path', 'reviewed', 'allow-effects', 'output']);
    for (let i = 0; i < argv.length; i++) {
        const key = argv[i].replace(/^--/, '');
        if (!argv[i].startsWith('--') || Object.hasOwn(result, key) || !flags.has(key) && !values.has(key)) throw Error('Unknown/duplicate option: ' + argv[i]);
        result[key] = flags.has(key) ? true : argv[++i];
        if (result[key] === undefined || typeof result[key] === 'string' && result[key].startsWith('--')) throw Error('Missing value: ' + key);
    }
    if (['list', 'review', 'run'].filter(key => result[key]).length > 1) throw Error('Choose one action: --list, --review or --run.');
    if (!result.run && (result.reviewed || result['allow-effects'] || result.output)) throw Error('Execution options require --run.');
    return result;
}

export async function main(argv = process.argv.slice(2)) {
    const opt = options(argv);
    const catalog = validateCatalog(JSON.parse(fs.readFileSync(catalogPath, 'utf8')));
    const scenarios = selectScenarios(catalog, opt);
    const fingerprint = reviewFingerprint(scenarios);
    if (opt.review) {
        console.log(JSON.stringify({ scenarios, fingerprint }, null, 2));
        for (const file of sourceFiles(scenarios)) console.log('\n--- ' + file + ' ---\n' + fs.readFileSync(path.join(root, file), 'utf8'));
        return 0;
    }
    if (!opt.run) {
        const known = new Set(catalog.scenarios.map(s => s.file));
        const uncatalogued = fs.readdirSync(path.join(root, 'Tests~')).filter(file => /\.(cs|mjs)$/.test(file) && !known.has('Tests~/' + file)).sort();
        console.log(JSON.stringify({ scope: catalog.scope, profiles: catalog.profiles, scenarios, uncatalogued,
            note: 'Uncatalogued files may be regressions, diagnostics or manual helpers; no runner is inferred.' }, null, 2));
        return 0;
    }
    if (!opt.profile && !opt.id) throw Error('--run requires an explicit --profile or --id.');
    if (opt.reviewed !== fingerprint) throw Error('Read --review output first, then pass its current fingerprint with --reviewed.');
    const allowed = new Set((opt['allow-effects'] ?? '').split(',').filter(Boolean));
    for (const s of scenarios) for (const effect of s.effects) if (!allowed.has(effect)) throw Error(s.id + ' requires --allow-effects ' + effect + ' and existing user authority.');
    const hasUnity = scenarios.some(s => s.runner !== 'node' || s.requiresUnity);
    const projectPath = opt['project-path'] ? path.resolve(opt['project-path']) : path.resolve(root, '../..');
    if (hasUnity && !opt['project-path']) throw Error('Unity scenarios require explicit --project-path.');
    if (!inside(path.join(projectPath, 'Packages'), root)) throw Error('This package is not inside the requested project Packages directory.');
    const directory = path.join(projectPath, 'Temp/WhimTex/test-runs');
    fs.mkdirSync(directory, { recursive: true });
    if (!inside(fs.realpathSync(projectPath), fs.realpathSync(directory))) throw Error('Report directory resolves outside the requested project.');
    const output = path.resolve(projectPath, opt.output ?? 'Temp/WhimTex/test-runs/' + new Date().toISOString().replaceAll(':', '-') + '-' + process.pid + '.json');
    if (!inside(directory, output) || path.dirname(output) !== directory || !output.endsWith('.json')) throw Error('--output must be a new JSON file directly under Temp/WhimTex/test-runs.');
    const lock = path.join(directory, 'runner.lock');
    const lockHandle = fs.openSync(lock, 'wx');
    let outputHandle;
    let keepLock = false;
    const report = { version: 1, startedAt: new Date().toISOString(), projectPath, fingerprint,
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
        const invoke = (s, entry, args, budgetMs) => s.runner === 'node'
            ? runProcess(process.execPath, [path.join(root, s.file), ...args], { timeoutMs: budgetMs })
            : runProcess('unity', commandArgs(s, entry, args, budgetMs, projectPath), { cwd: projectPath, timeoutMs: budgetMs });
        for (const s of scenarios) {
            if (reviewFingerprint(scenarios) !== fingerprint) throw Error('Reviewed source changed during the run; stopping before next scenario.');
            const result = await runScenario(s, invoke);
            report.results.push(result);
            keepLock ||= result.uncertain;
            save();
            console.log(s.id + ': ' + result.status + ' (' + result.durationMs + 'ms)');
            if (result.status !== 'passed') break;
        }
        report.notRun = scenarios.slice(report.results.length).map(s => s.id);
        report.success = report.results.length === scenarios.length && report.results.every(r => r.status === 'passed');
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
