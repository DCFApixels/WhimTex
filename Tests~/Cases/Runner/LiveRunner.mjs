import fs from 'node:fs';
import path from 'node:path';
import { randomUUID, createHash } from 'node:crypto';
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { runScenario, runProcess, commandArgs, bundleSources, root } from '../../scripts/run-tests.mjs';

const context = new TestContext('Live negative verdicts, asynchronous failure and owned cleanup');
const assert = context.assert;
context.case('actual Editor replies retain each negative verdict and cleanup', async () => {
    const project = path.resolve(process.argv[2] ?? '');
    assert.equal(path.resolve(root, '../..'), project, 'Explicit matching project argument is required');
    const directory = path.join(project, 'Temp/WhimTex/test-runs');
    assert.ok(fs.existsSync(path.join(directory, 'runner.lock')), 'Only the locked outer runner may execute this integration');
    const runId = randomUUID();
    const file = path.join(directory, 'live-negative-' + runId + '.cs');
    const sources = ['Tests~/Cases/Runner/NegativeProtocol.cs', 'Tests~/Framework/TestApi.cs'];
    const source = bundleSources(sources);
    fs.writeFileSync(file, source, { flag: 'wx' });
    context.facts.input = { file, sources, sha256: createHash('sha256').update(source).digest('hex') };
    const base = { id: 'live-negative', file: path.relative(root, file), runner: 'run_script', args: [],
        category: 'runner', timeoutMs: 5000, result: { kind: 'structured' } };
    const results = context.facts.results = [];
    const invoke = (s, entry, args, budget) => runProcess('unity', commandArgs(s, entry, args, budget, project), { cwd: project, timeoutMs: budget });
    async function check(s, expected) {
        const result = await runScenario(s, invoke);
        results.push(result);
        if (result.uncertain) context.recoveryRequired = true;
        assert.equal(result.uncertain, false, 'Do not continue while execution might still be running');
        assert.equal(result.status, expected, s.entry + ': ' + result.detail);
    }
    // These are expected failures. No failed domain test is being turned into a pass.
    for (const [entry, expected] of [
        ['InnerFailure', 'api-failed'], ['AssertionFailure', 'assertion-failed'],
        ['ExecutionFailure', 'execution-failed'], ['NeedsArgument', 'execution-failed'],
        ['MissingEntry', 'execution-failed'], ['Skip', 'skip']
    ]) await check({ ...base, entry: 'NegativeProtocolTests.' + entry }, expected);
    await check({ ...base, entry: 'NegativeProtocolTests.StartFailure', args: [runId],
        async: { entry: 'NegativeProtocolTests.PollFailure', args: [runId], pollMs: 100 },
        cancel: { entry: 'NegativeProtocolTests.Cancel', args: [runId] },
        cleanup: { entry: 'NegativeProtocolTests.Cleanup', args: [runId] } }, 'assertion-failed');
    await check({ ...base, entry: 'NegativeProtocolTests.CheckCleanup', args: [runId] }, 'passed');
    assert.equal(results.length, 8, 'Every negative and cleanup case was executed');
    assert.equal(results[6].testResult.checks, 2, 'Async assertions actually ran');
    assert.equal(results[6].attempts.at(-1).phase, 'cleanup', 'Cleanup follows failed poll');
    assert.equal(results[7].testResult.checks, 1, 'Cleanup verified by a fresh compiled invocation');
});
await finish(context);
