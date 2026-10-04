// Opt-in integration checks, run under the outer runner's project lock.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { runScenario, runProcess, commandArgs, root } from './scripts/run-tests.mjs';

const catalog = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/scripts/test-catalog.json'), 'utf8'));
const base = catalog.scenarios.find(s => s.id === 'protocol-pass');
const asyncCase = catalog.scenarios.find(s => s.id === 'protocol-async');
const project = path.resolve(root, '../..');
const invoke = (s, entry, args, budget) => runProcess('unity', commandArgs(s, entry, args, budget, project), { cwd: project, timeoutMs: budget });
const results = [];
try {
    for (const [entry, expected] of [
        ['RunnerProtocolSmoke.InnerFailure', 'api-failed'],
        ['RunnerProtocolSmoke.AssertionFailure', 'execution-failed'],
        ['RunnerProtocolSmoke.NeedsArgument', 'execution-failed'],
        ['RunnerProtocolSmoke.MissingEntry', 'execution-failed'],
        ['RunnerProtocolSmoke.Skip', 'skip']
    ]) {
        const result = await runScenario({ ...base, id: entry, entry }, invoke);
        results.push(result);
        assert.equal(result.status, expected, entry + ': ' + result.detail);
        assert.equal(result.uncertain, false, 'Do not continue while an Editor execution might still be running.');
    }
    const failure = await runScenario({ ...asyncCase, id: 'async-failure', entry: 'RunnerProtocolSmoke.StartFailure' }, invoke);
    results.push(failure);
    assert.equal(failure.status, 'assertion-failed');
    assert.equal(failure.uncertain, false);
    const clean = await runScenario({ ...base, id: 'cleanup-check', entry: 'RunnerProtocolSmoke.CheckCleanup', result: { kind: 'text', pass: '^PASS: runner fixture clean$' } }, invoke);
    results.push(clean);
    assert.equal(clean.status, 'passed');
    console.log(JSON.stringify({ success: true, expectedNegativeVerdicts: true, results }, null, 2));
} catch (error) {
    console.log(JSON.stringify({ success: false, results, error: String(error) }, null, 2));
    process.exitCode = 1;
}
