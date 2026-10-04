import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { classifyReply, runScenario, commandArgs, validateCatalog, selectScenarios, reviewFingerprint, root } from './scripts/run-tests.mjs';

const catalog = JSON.parse(fs.readFileSync(new URL('./scripts/test-catalog.json', import.meta.url), 'utf8'));
const base = catalog.scenarios.find(s => s.id === 'protocol-pass');
const asyncCase = catalog.scenarios.find(s => s.id === 'protocol-async');
const nodeCase = catalog.scenarios.find(s => s.id === 'runner-unit');
const envelope = value => ({ code: 0, stdout: JSON.stringify({ success: true, data: { success: true, result: { success: true, diagnostics: [], result: value } } }), stderr: '' });
const clone = value => JSON.parse(JSON.stringify(value));

test('catalog has explicit invocations and valid bounded protocols', () => {
    assert.equal(validateCatalog(catalog), catalog);
    assert.equal(selectScenarios(catalog, { profile: 'core' }).length, 6);
    assert.equal(selectScenarios(catalog, { id: base.id })[0], base);
    assert.throws(() => selectScenarios(catalog, { profile: 'missing' }), /Unknown profile/);
    assert.throws(() => selectScenarios(catalog, { id: 'missing' }), /Unknown scenario/);
    assert.throws(() => selectScenarios(catalog, { id: base.id, profile: 'core' }), /Choose/);
});

for (const [name, mutate] of [
    ['duplicate id', c => c.scenarios.push(c.scenarios[0])],
    ['escaped path', c => { c.scenarios[0].file = 'Tests~/../../elsewhere.mjs'; }],
    ['missing entry', c => { delete c.scenarios.find(s => s.runner === 'run_script').entry; }],
    ['missing setup', c => { c.scenarios[0].setup = ''; }],
    ['unbounded timeout', c => { c.scenarios[0].timeoutMs = 0; }],
    ['unknown profile member', c => { c.profiles.core.push('missing'); }],
    ['unanchored pass', c => { c.scenarios.find(s => s.result.kind === 'text').result.pass = 'PASS'; }],
    ['invalid regex', c => { c.scenarios.find(s => s.result.kind === 'text').result.pass = '^['; }],
    ['unknown effects', c => { c.scenarios[0].effects = ['anything']; }],
    ['missing async completion', c => { delete c.scenarios.find(s => s.async).async.entry; }],
    ['missing cleanup verdict', c => { delete c.scenarios.find(s => s.cleanup).cleanup.pass; }]
]) test('rejects ' + name, () => { const c = clone(catalog); mutate(c); assert.throws(() => validateCatalog(c)); });

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
