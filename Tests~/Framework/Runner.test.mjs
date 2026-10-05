import { TestContext, finish } from './test-api.mjs';
const context = new TestContext('Runner contracts and migration infrastructure');
const assert = context.assert;
const test = (name, body) => context.case(name, body);
import fs from 'node:fs';
import { compareHistoricalPilot } from '../scripts/check-migration.mjs';
import { textCase as base, asyncTextCase as asyncCase, exitCodeCase as nodeCase, validateHistoricalProtocol } from './Fixtures/HistoricalProtocol.mjs';
import { classifyReply, classifyCompileReply, runScenario, commandArgs, validateCatalog, selectScenarios, reviewFingerprint, bundleSources, entryContracts, root } from '../scripts/run-tests.mjs';

const catalog = JSON.parse(fs.readFileSync(new URL('../scripts/test-catalog.json', import.meta.url), 'utf8'));
const activeCase = catalog.scenarios.find(s => s.id === 'canonical-reader-v2');
const envelope = value => ({ code: 0, stdout: JSON.stringify({ success: true, data: { success: true, result: { success: true, diagnostics: [], result: value } } }), stderr: '' });
const clone = value => JSON.parse(JSON.stringify(value));

test('catalog has explicit invocations and valid bounded protocols', () => {
    assert.equal(validateCatalog(catalog), catalog);
    assert.deepEqual(selectScenarios(catalog, { profile: 'core' }).map(s => s.id), ['display-channels-v2', 'canonical-reader-v2', 'frozen-files-v2']);
    assert.equal(selectScenarios(catalog, { id: activeCase.id })[0], activeCase);
    assert.throws(() => selectScenarios(catalog, { profile: 'missing' }), /Unknown profile/);
    assert.throws(() => selectScenarios(catalog, { id: 'missing' }), /Unknown scenario/);
    assert.throws(() => selectScenarios(catalog, { id: activeCase.id, profile: 'core' }), /Choose/);
});

for (const [name, mutate] of [
    ['duplicate id', c => c.scenarios.push(c.scenarios[0])],
    ['escaped path', c => { c.scenarios[0].file = 'Tests~/../../elsewhere.mjs'; }],
    ['missing entry', c => { delete c.scenarios.find(s => s.runner === 'run_script').entry; }],
    ['missing setup', c => { c.scenarios[0].setup = ''; }],
    ['unbounded timeout', c => { c.scenarios[0].timeoutMs = 0; }],
    ['unknown profile member', c => { c.profiles.core.push('missing'); }],
    ['unknown effects', c => { c.scenarios[0].effects = ['anything']; }],
    ['missing async completion', c => { delete c.scenarios.find(s => s.async).async.entry; }],
    ['missing cleanup entry', c => { delete c.scenarios.find(s => s.cleanup).cleanup.entry; }]
]) test('rejects ' + name, () => { const c = clone(catalog); mutate(c); assert.throws(() => validateCatalog(c)); });

test('active dispatch cannot re-enable retired scenarios or historical result protocols', () => {
    assert.ok(catalog.scenarios.every(s => s.legacy === undefined && !s.file.startsWith('Tests~/Legacy/') && s.result.kind === 'structured'));
    for (const flag of [true, false, null]) {
        const value = clone(catalog); value.scenarios[0].legacy = flag;
        assert.throws(() => validateCatalog(value), /Retired archive scenarios/);
    }
    const archivePath = clone(catalog); archivePath.scenarios[0].file = 'Tests~/Legacy/TestRunner.test.mjs';
    assert.throws(() => validateCatalog(archivePath), /Retired archive scenarios/);
    for (const field of ['supportFiles', 'reviewFiles']) for (const file of [
        'Tests~/Legacy/DisplayChannelsSmoke.cs', 'Tests~/Cases/../Legacy/DisplayChannelsSmoke.cs']) {
        const value = clone(catalog), scenario = value.scenarios.find(s => s.runner === 'run_script');
        scenario[field] = [file];
        assert.throws(() => validateCatalog(value), /Retired archive dependencies/);
    }
    for (const kind of ['text', 'json-success', 'exit-code']) {
        const value = clone(catalog);
        const scenario = value.scenarios.find(s => kind === 'exit-code' ? s.runner === 'node' : s.runner === 'run_script');
        scenario.result = { kind, pass: base.result.pass };
        assert.throws(() => validateCatalog(value), /Active scenarios require structured results/);
    }
    for (const profile of ['legacy-core', 'migration-pilot']) assert.throws(() => selectScenarios(catalog, { profile }), /Unknown profile/);
    for (const id of ['protocol-pass', 'protocol-async', 'runner-unit']) assert.throws(() => selectScenarios(catalog, { id }), /Unknown scenario/);
});

test('synthetic historical protocols are independent of the active registry', () => {
    for (const scenario of [base, asyncCase, nodeCase]) {
        assert.equal(validateHistoricalProtocol(scenario), scenario);
        assert.ok(!catalog.scenarios.some(s => s.id === scenario.id));
        assert.ok(!scenario.file.startsWith('Tests~/Legacy/'));
    }
});
for (const [name, mutate, error] of [
    ['unanchored pass', s => { s.result.pass = 'PASS'; }, /anchored pass/],
    ['invalid regex', s => { s.result.pass = '^['; }, SyntaxError],
    ['missing async completion', s => { delete s.async.entry; }, /Invalid async protocol/],
    ['missing cleanup verdict', s => { delete s.cleanup.pass; }, /anchored cleanup/],
    ['unanchored async start', s => { s.async.started = 'Started'; }, /anchored async start/],
    ['invalid async start regex', s => { s.async.started = '^['; }, SyntaxError],
    ['unanchored pending', s => { s.result.pending = 'Running'; }, /anchored async pending/],
    ['invalid pending regex', s => { s.result.pending = '^['; }, SyntaxError],
    ['unanchored cleanup', s => { s.cleanup.pass = 'PASS'; }, /anchored cleanup/],
    ['invalid cleanup regex', s => { s.cleanup.pass = '^['; }, SyntaxError]
]) test('historical protocol rejects ' + name + ' directly', () => {
    const scenario = clone(asyncCase); mutate(scenario);
    assert.throws(() => validateHistoricalProtocol(scenario), error);
});

test('review receipt includes source and invocation, not just scenario names', () => {
    const first = reviewFingerprint([base]);
    assert.equal(first, reviewFingerprint([base]));
    assert.notEqual(first, reviewFingerprint([{ ...base, entry: 'Changed.Entry' }]));
    assert.notEqual(first, reviewFingerprint([{ ...base, result: { kind: 'text', pass: '^Different' } }]));
});

test('transport, API, execution and assertion verdicts remain distinct', () => {
    assert.equal(classifyReply(envelope('PASS: runner protocol fixture'), base).status, 'passed');
    assert.equal(classifyReply({ code: 1, stdout: '{"success":false,"errors":[{"code":"FAILED"}]}' }, base).status, 'transport-error');
    assert.equal(classifyReply({ code: 0, stdout: '{"success":true,"data":{"success":false}}' }, base).status, 'api-failed');
    assert.equal(classifyReply(envelope({ success: false, error: 'broken' }), base).status, 'api-failed');
    assert.equal(classifyReply(envelope('{"success":false,"error":"broken"}'), base).status, 'api-failed');
    const inner = JSON.parse(envelope('ignored').stdout); inner.data.result.success = false;
    assert.equal(classifyReply({ code: 0, stdout: JSON.stringify(inner) }, base).status, 'execution-failed');
    inner.data.result.error = 'ExecutionTimeout';
    assert.equal(classifyReply({ code: 0, stdout: JSON.stringify(inner) }, base).status, 'timeout');
    assert.equal(classifyReply({ code: 0, stdout: JSON.stringify(inner) }, base).uncertain, true);
    delete inner.data.result.error;
    inner.data.result.success = true; inner.data.result.diagnostics = [{ severity: 'Error' }];
    assert.equal(classifyReply({ code: 0, stdout: JSON.stringify(inner) }, base).status, 'execution-failed');
    assert.equal(classifyReply(envelope('FAILED: PASS: runner protocol fixture'), base).status, 'assertion-failed');
    assert.equal(classifyReply(envelope('SKIP: fixture unavailable'), base).status, 'skip');
    assert.equal(classifyReply(envelope('Started; call Result.'), base).status, 'protocol-error');
    assert.equal(classifyReply(envelope('Running'), base).status, 'protocol-error');
    assert.equal(classifyReply({ code: 0, stdout: 'not JSON' }, base).uncertain, true);
});

test('eval_file unwraps evaluation verdict separately from failing inner API', () => {
    const s = { ...base, runner: 'eval_file' };
    assert.equal(classifyReply(envelope('PASS: runner protocol fixture'), s).status, 'passed');
    assert.equal(classifyReply(envelope({ success: false }), s).status, 'api-failed');
});

test('JSON success requires an explicit result contract', () => {
    assert.equal(classifyReply(envelope({ success: true }), base).status, 'protocol-error');
    assert.equal(classifyReply(envelope({ success: true }), { ...base, result: { kind: 'json-success' } }).status, 'passed');
});

test('Node exit, skip, timeout and spawn error are not all pass', () => {
    assert.equal(classifyReply({ code: 0, stdout: 'assertions done' }, nodeCase).status, 'passed');
    assert.equal(classifyReply({ code: 1, stderr: 'AssertionError' }, nodeCase).status, 'assertion-failed');
    assert.equal(classifyReply({ code: 0, stdout: 'SKIP: intentional' }, nodeCase).status, 'skip');
    assert.equal(classifyReply({ code: 0, stdout: 'ℹ skipped 1' }, nodeCase).status, 'skip');
    assert.equal(classifyReply({ code: 0, stdout: 'FAILED: deliberate' }, nodeCase).status, 'assertion-failed');
    assert.equal(classifyReply({ timedOut: true }, nodeCase).status, 'timeout');
    assert.equal(classifyReply({ timedOut: true }, { ...nodeCase, requiresUnity: true }).uncertain, true);
    assert.equal(classifyReply({ code: 1, stderr: 'Disconnected' }, { ...nodeCase, requiresUnity: true }).uncertain, true);
    assert.equal(classifyReply({ error: 'ENOENT' }, nodeCase).status, 'transport-error');
});

test('CLI milliseconds and seconds are not confused; project path is explicit', () => {
    const args = commandArgs(base, base.entry, ["a'b", 'a b'], 50000, 'D:/Fixture', root);
    assert.equal(args[args.indexOf('--timeout') + 1], '50');
    assert.equal(args[args.indexOf('--timeout_ms') + 1], '45000');
    assert.equal(args[args.indexOf('--project-path') + 1], 'D:/Fixture');
    assert.equal(args[args.indexOf('--args') + 1], JSON.stringify(["a'b", 'a b']));
    assert.ok(!commandArgs({ ...base, runner: 'eval_file' }, null, [], 50000, 'D:/Fixture').includes('--timeout_ms'));
});

test('async start and Running are not final; cleanup runs once', async () => {
    const entries = []; let tick = 0;
    const results = ['Started; call Result.', 'Running', 'Passed: runner async fixture', 'PASS: runner fixture cleanup'];
    const result = await runScenario(asyncCase, async (s, entry) => { entries.push(entry); return envelope(results.shift()); }, { now: () => tick, delay: async ms => { tick += ms; } });
    assert.equal(result.status, 'passed');
    assert.deepEqual(entries, [asyncCase.entry, asyncCase.async.entry, asyncCase.async.entry, asyncCase.cleanup.entry]);
    assert.equal(result.attempts.length, 4);
});

test('async failure runs cleanup without overwriting the failed verdict', async () => {
    const results = ['Started; call Result.', 'FAILED: deliberate', 'PASS: runner fixture cleanup'];
    const result = await runScenario(asyncCase, async () => envelope(results.shift()), { delay: async () => {} });
    assert.equal(result.status, 'assertion-failed');
    assert.equal(result.attempts.at(-1).phase, 'cleanup');
});

test('async timeout cannot be passed by a successful cleanup', async () => {
    let tick = 0;
    const s = { ...asyncCase, timeoutMs: 1000 };
    const result = await runScenario(s, async (s, entry) => envelope(entry === s.entry ? 'Started; call Result.' : entry === s.cleanup.entry ? 'PASS: runner fixture cleanup' : 'Running'), { now: () => tick, delay: async ms => { tick += ms; } });
    assert.equal(result.status, 'timeout'); assert.equal(result.uncertain, true);
    assert.equal(result.attempts.at(-1).phase, 'cleanup');
});

test('failed poll transport is uncertain; cleanup still runs', async () => {
    let count = 0;
    const result = await runScenario(asyncCase, async () => {
        count++; if (count === 2) throw Error('Disconnected');
        return envelope(count === 1 ? 'Started; call Result.' : 'PASS: runner fixture cleanup');
    }, { delay: async () => {} });
    assert.equal(result.status, 'transport-error'); assert.equal(result.uncertain, true); assert.equal(count, 3);
});

test('cleanup failure is visible even after passed assertions', async () => {
    const results = ['Started; call Result.', 'Passed: runner async fixture', 'FAILED: cleanup'];
    const result = await runScenario(asyncCase, async () => envelope(results.shift()), { delay: async () => {} });
    assert.equal(result.status, 'cleanup-failed'); assert.equal(result.uncertain, true);
});

test('sync thrown invoker returns infrastructure failure, not pass', async () => {
    const result = await runScenario(base, async () => { throw Error('Transport died'); });
    assert.equal(result.status, 'transport-error'); assert.equal(result.uncertain, true);
});
test('bounded selectors preserve order without adding implicit dependencies', () => {
    const selected = selectScenarios(catalog, { ids: 'canonical-reader-v2,display-channels-v2' });
    assert.deepEqual(selected.map(s => s.id), ['canonical-reader-v2', 'display-channels-v2']);
    assert.ok(selectScenarios(catalog, { group: 'rendering' }).every(s => s.groups.includes('rendering')));
    assert.throws(() => selectScenarios(catalog, { ids: 'framework,framework' }));
    assert.throws(() => selectScenarios(catalog, { group: 'missing' }));
    assert.throws(() => selectScenarios(catalog, { all: true, id: 'framework' }));
    assert.equal(selectScenarios(catalog, { all: true }).length, catalog.scenarios.length);
});

const structuredCase = catalog.scenarios.find(s => s.id === 'canonical-reader-v2');
const structuredAsync = catalog.scenarios.find(s => s.id === 'async-protocol-v2');
const payload = (status = 'passed', checks = 1, failures = []) => ({ status, checks, failures, message: 'Fixture' });
test('structured verdicts reject missing, contradictory and malformed data', () => {
    assert.equal(classifyReply(envelope(payload()), structuredCase).status, 'passed');
    for (const value of [payload('passed', -1), payload('passed', 1, ['error']), payload('failed'), { success: true }])
        assert.equal(classifyReply(envelope(value), structuredCase).status, 'protocol-error');
    assert.equal(classifyReply(envelope(payload('failed', 1, ['assertion'])), structuredCase).status, 'assertion-failed');
    assert.equal(classifyReply(envelope(payload('skipped')), structuredCase).status, 'skip');
    assert.equal(classifyReply(envelope(payload('running')), structuredCase).status, 'protocol-error');
});
test('Node structured contract requires exactly one result and consistent exit', () => {
    const s = catalog.scenarios.find(s => s.id === 'frozen-files-v2');
    const line = 'WHIMTEX_TEST_RESULT ' + JSON.stringify(payload());
    assert.equal(classifyReply({ code: 0, stdout: line }, s).status, 'passed');
    for (const reply of [{ code: 0, stdout: '' }, { code: 1, stdout: line }, { code: 0, stdout: line + '\n' + line }])
        assert.equal(classifyReply(reply, s).status, 'protocol-error');
});
test('structured async uses Start/Poll/Cleanup and retains final check count', async () => {
    let tick = 0;
    const values = [payload('running', 0), payload('running', 0), payload('passed', 12), payload('passed', 1)];
    const result = await runScenario(structuredAsync, async () => envelope(values.shift()), { now: () => tick, delay: async ms => { tick += ms; } });
    assert.equal(result.status, 'passed'); assert.equal(result.testResult.checks, 12);
    assert.deepEqual(result.attempts.map(a => a.phase), ['start', 'result', 'result', 'cleanup']);
});
test('timed-out structured async requests cancellation with its own budget and then cleanup', async () => {
    let tick = 0; const calls = [];
    const result = await runScenario({ ...structuredAsync, timeoutMs: 1000 }, async (s, entry, args, budget) => {
        calls.push({ entry, budget });
        return envelope(payload(entry === s.cancel.entry ? 'cancelled' : entry === s.cleanup.entry ? 'passed' : 'running', 0));
    }, { now: () => tick, delay: async ms => { tick += ms; } });
    assert.equal(result.status, 'timeout'); assert.equal(result.uncertain, true);
    assert.equal(calls.at(-2).entry, structuredAsync.cancel.entry); assert.equal(calls.at(-2).budget, 10000);
    assert.equal(calls.at(-1).entry, structuredAsync.cleanup.entry);
});
test('structured cleanup failure preserves the primary assertion failure', async () => {
    const values = [payload('running', 0), payload('failed', 2, ['assertion']), payload('failed', 1, ['cleanup'])];
    const result = await runScenario(structuredAsync, async () => envelope(values.shift()), { delay: async () => {} });
    assert.equal(result.status, 'cleanup-failed'); assert.equal(result.primaryResult.status, 'assertion-failed');
});
test('support source changes participate in review receipts', () => {
    assert.notEqual(reviewFingerprint([structuredCase]), reviewFingerprint([{ ...structuredCase, supportFiles: [] }]));
});
test('common Node API executes cases serially and reports assertion failures', async () => {
    const inner = new TestContext('Deliberate self-check'); const order = [];
    inner.case('first', async () => { order.push(1); inner.assert.equal(1, 1); });
    inner.case('second', () => { order.push(2); inner.assert.equal(1, 2); });
    const result = await inner.run();
    assert.deepEqual(order, [1, 2]); assert.equal(result.status, 'failed');
    assert.equal(result.checks, 2); assert.equal(result.failures.length, 1);
});
test('pure historical comparison rejects stale, duplicate, missing and weakened results without re-signing', () => {
    const registry = JSON.parse(fs.readFileSync(new URL('../migration.json', import.meta.url), 'utf8'));
    const manifestHash = 'c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194';
    const selected = registry.pairs.flatMap(pair => [pair.legacyId, pair.newId]);
    const results = registry.pairs.flatMap(pair => [
        { id: pair.legacyId, status: 'passed', uncertain: false,
            detail: pair.legacyId === 'display-channels' ? 'Display channel checks passed: 96 across all 16 masks.'
                : pair.legacyId === 'canonical-reader' ? 'PASS: 11 canonical name-resolution and unknown-data guards.'
                : 'PASS: 30 frozen 0.12.5 file hashes, seven supported TIFF/JSON documents' },
        { id: pair.newId, status: 'passed', uncertain: false, testResult: { status: 'passed', failures: [], checks: pair.newChecks, facts: pair.facts } }
    ]);
    const expected = { fingerprint: 'synthetic-historical-review', productionFingerprint: 'mock-production', manifestHash };
    const historicalCatalog = { profiles: { 'migration-pilot': selected }, scenarios: selected.map(id => ({ id })) };
    const report = { version: 2, action: 'run', success: true, recoveryRequired: false, productionFingerprint: expected.productionFingerprint,
        archive: { manifestHash }, selected, results, fingerprint: expected.fingerprint, notRun: [],
        startedAt: '2026-10-04T18:02:02.749Z', finishedAt: '2026-10-04T18:04:00.000Z' };
    const compare = (value, mapping = registry) => compareHistoricalPilot(value, mapping, historicalCatalog, expected);
    assert.equal(compare(report).success, true);
    assert.equal(compare(report).archiveRemovalAllowed, false);
    assert.equal(compare(report).receiptVerified, false, 'Synthetic inputs cannot authenticate runtime evidence');
    assert.equal(compare(report).currentRuntimeEquivalence, false);
    assert.equal(compare(report).scope, 'historical-pilot-only');
    const weaker = clone(report); weaker.results[1].testResult.checks--;
    assert.equal(compare(weaker).success, false);
    assert.throws(() => compare({ ...report, fingerprint: 'stale' }), /changed/);
    assert.throws(() => compare({ ...report, results: [report.results[0], ...report.results.slice(0, -1)] }), /completed/);
    assert.throws(() => compare({ ...report, results: report.results.slice(1) }), /completed/);
    assert.throws(() => compare({ ...report, selected: [report.selected[0], ...report.selected.slice(0, -1)] }), /completed/);
    assert.throws(() => compare({ ...report, selected: [...report.selected].reverse() }), /frozen pilot catalog/);
    assert.throws(() => compare({ ...report, selected: report.selected.map((id, i) => i === 0 ? 'unknown' : id) }), /frozen pilot catalog/);
    assert.throws(() => compare({ ...report, notRun: ['missing'] }), /completed/);
    assert.throws(() => compare({ ...report, recoveryRequired: true }), /completed/);
    assert.throws(() => compare({ ...report, action: 'compile' }), /historical pilot runner/);
    assert.throws(() => compare({ ...report, productionFingerprint: 'different-production' }), /historical pilot runner/);
    assert.throws(() => compare({ ...report, archive: { manifestHash: 'changed' } }), /historical pilot runner/);
    const failedOld = clone(report); failedOld.results[0].detail = 'FAILED: ' + failedOld.results[0].detail;
    assert.equal(compare(failedOld).success, false, 'Anchored original count pattern still rejects prefixed failure');
    const wrongOldCount = clone(report); wrongOldCount.results[0].detail = 'Display channel checks passed: 95 across all 16 masks.';
    assert.equal(compare(wrongOldCount).success, false);
    const wrongFacts = clone(report); wrongFacts.results.at(-1).testResult.facts.fileHashes--;
    assert.equal(compare(wrongFacts).success, false);
    const missingCoverage = clone(registry); missingCoverage.pairs[0].coverage = [];
    assert.equal(compare(report, missingCoverage).success, false);
    const uncertain = clone(report); uncertain.results[0].uncertain = true;
    assert.equal(compare(uncertain).success, false);
    const incomplete = clone(report); incomplete.results[0].status = 'skip';
    assert.equal(compare(incomplete).success, false);
    const malformed = clone(report); malformed.results[1].testResult.status = 'failed';
    assert.equal(compare(malformed).success, false);
});
test('empty Node cases and empty case lists cannot pass', async () => {
    const empty = new TestContext('Empty');
    assert.equal((await empty.run()).status, 'failed');
    empty.case('no assertions', () => {});
    const result = await empty.run();
    assert.equal(result.status, 'failed');
    assert.match(result.failures[0].message, /No assertions/);
});
test('passing structured domain results require actual assertions', () => {
    assert.equal(classifyReply(envelope(payload('passed', 0)), structuredCase).status, 'protocol-error');
    assert.equal(classifyReply(envelope(payload('passed', 0)), structuredCase, 'cleanup').status, 'passed');
});
test('global support imports precede types while source line mappings remain', () => {
    const files = ['Tests~/Cases/Export/PsdWriter.cs', 'Tests~/Framework/TestApi.cs', 'src/PsdWriter.cs'];
    const source = bundleSources(files);
    assert.ok(source.indexOf('using System;') < source.indexOf('public static class PsdWriterTests'));
    assert.equal([...source.matchAll(/^using System;$/gm)].length, 1);
    for (const file of files) assert.ok(source.includes('#line 1 "' + file + '"'));
    assert.ok(source.includes('using var rejectedStream'), 'Resource using statements are not moved');
    assert.ok(source.includes('internal static class PsdWriter'), 'Current encoder implementation is compiled unchanged');
});
test('nested live integration cannot erase uncertain Editor execution', () => {
    const s = { ...catalog.scenarios.find(s => s.id === 'frozen-files-v2'), requiresUnity: true };
    const value = { ...payload('failed', 1, ['nested execution timeout']), recoveryRequired: true };
    const result = classifyReply({ code: 1, stdout: 'WHIMTEX_TEST_RESULT ' + JSON.stringify(value) }, s);
    assert.equal(result.status, 'assertion-failed');
    assert.equal(result.uncertain, true);
    const inner = new TestContext('Recovery'); inner.recoveryRequired = true;
    inner.case('assert', () => inner.assert.ok(true));
    return inner.run().then(value => assert.equal(value.recoveryRequired, true));
});
test('structured C# cleanup debt retains lock and cannot be erased by bridge cleanup', async () => {
    const failed = { ...payload('failed', 2, ['primary and owned cleanup failed']), recoveryRequired: true };
    const direct = classifyReply(envelope(failed), structuredCase);
    assert.equal(direct.status, 'assertion-failed'); assert.equal(direct.uncertain, true);
    for (const value of [{ ...payload(), recoveryRequired: true }, { ...failed, recoveryRequired: 'true' },
        { ...payload('passed', 0), recoveryRequired: true }, { ...payload('failed', 1, []), recoveryRequired: true }]) {
        const result = classifyReply(envelope(value), structuredCase);
        assert.equal(result.status, 'protocol-error'); assert.equal(result.uncertain, true);
        const phases = [];
        const run = await runScenario(structuredAsync, async (s, entry) => {
            phases.push(entry); return envelope(value);
        });
        assert.equal(run.uncertain, true);
        assert.deepEqual(phases, [structuredAsync.entry], 'Contradictory/malformed cleanup debt is retained');
    }
    const calls = [];
    const result = await runScenario(structuredAsync, async (s, entry) => {
        calls.push(entry); return envelope(failed);
    });
    assert.equal(result.uncertain, true);
    assert.deepEqual(calls, [structuredAsync.entry], 'No blind cleanup after unresolved inner ownership');
});
test('compile-only verdict is separate from executing assertions', () => {
    assert.equal(classifyCompileReply(envelope(null), structuredCase).status, 'compiled');
    assert.equal(classifyReply(envelope(null), structuredCase).status, 'protocol-error');
    const error = JSON.parse(envelope(null).stdout); error.data.result.success = false;
    assert.equal(classifyCompileReply({ code: 0, stdout: JSON.stringify(error) }, structuredCase).status, 'compile-failed');
    assert.equal(classifyCompileReply({ timedOut: true }, structuredCase).uncertain, true);
    assert.equal(classifyCompileReply({ code: 0, stdout: '' }, { runner: 'node' }).status, 'compiled');
    assert.equal(classifyCompileReply({ code: 1, stderr: 'SyntaxError' }, { runner: 'node' }).status, 'compile-failed');
});
test('entry validation explicitly includes all lifecycle phases without running bodies', () => {
    const scenario = { entry: 'Case.Start', args: ['$runId'], async: { entry: 'Case.Poll', args: ['$runId'] },
        cancel: { entry: 'Case.Cancel', args: ['$runId'] }, cleanup: { entry: 'Case.Cleanup', args: ['$runId'] } };
    assert.deepEqual(entryContracts(scenario).phases.map(s => s.entry), ['Case.Start', 'Case.Poll', 'Case.Cancel', 'Case.Cleanup']);
    assert.ok(entryContracts(scenario).phases.every(s => s.arguments === 1));
    assert.deepEqual(entryContracts({ entry: 'Case.Run', args: [] }), { phases: [{ entry: 'Case.Run', arguments: 0 }] });
});
test('only explicit authorized Player workflows receive a long budget', () => {
    const value = clone(catalog);
    const source = value.scenarios.find(s => s.id === 'runner-live-v2');
    source.workflow = 'player-build';
    source.timeoutMs = 900000;
    source.effects = ['assets', 'temp-files', 'user-state', 'player-build'];
    assert.equal(validateCatalog(value), value);
    for (const mutate of [
        s => { delete s.workflow; },
        s => { s.workflow = 'arbitrary'; },
        s => { s.requiresUnity = false; },
        s => { s.effects = ['temp-files', 'player-build']; },
        s => { s.timeoutMs = 1800001; }
    ]) {
        const invalid = clone(value);
        mutate(invalid.scenarios.find(s => s.id === source.id));
        assert.throws(() => validateCatalog(invalid));
    }
});
test('native fixture install and removal reloads receive only an explicit bounded workflow budget', () => {
    const value = clone(catalog), source = value.scenarios.find(s => s.id === 'runner-live-v2');
    source.workflow = 'native-fixture'; source.category = 'regression'; source.timeoutMs = 180000;
    source.effects = ['assets', 'temp-files', 'user-state'];
    assert.equal(validateCatalog(value), value);
    for (const mutate of [
        s => { delete s.workflow; }, s => { s.category = 'diagnostic'; },
        s => { s.requiresUnity = false; }, s => { s.runner = 'run_script'; s.entry = 'Case.Run'; },
        s => { s.effects = ['temp-files']; }, s => { s.effects.push('player-build'); },
        s => { s.legacy = true; }, s => { s.timeoutMs = 180001; }
    ]) {
        const invalid = clone(value); mutate(invalid.scenarios.find(s => s.id === source.id));
        assert.throws(() => validateCatalog(invalid));
    }
});
test('large native diagnostics have a separate bounded budget, not a regression waiver', () => {
    const value = clone(catalog);
    const source = value.scenarios.find(s => s.id === 'psd-writer-v2');
    source.workflow = 'diagnostic'; source.category = 'diagnostic'; source.timeoutMs = 240000;
    assert.equal(validateCatalog(value), value);
    for (const mutate of [
        s => { delete s.workflow; },
        s => { s.category = 'regression'; },
        s => { s.timeoutMs = 300001; },
        s => { s.runner = 'node'; },
        s => { s.legacy = true; }
    ]) {
        const invalid = clone(value); mutate(invalid.scenarios.find(s => s.id === source.id));
        assert.throws(() => validateCatalog(invalid));
    }
});
await finish(context);
