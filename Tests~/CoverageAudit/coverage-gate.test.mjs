// Pure in-memory filesystem/reports. No Unity, runner import, subprocess, or disk writes.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { test } from 'node:test';
import { auditNames, sha256, verifyFrozenLegacy, currentBundleSources, currentReviewFingerprint,
    splitGap, metadataGapEvidence, inspectInvocation, inspectRuntimeReport, inspectGradientWorkflowPhases,
    chooseNewestEvidence, buildInventory } from './coverage-gate.mjs';

function memory() {
    const project = path.resolve('coverage-gate-virtual'), root = path.join(project, 'Packages', 'sample');
    const bytes = new Map();
    const put = (file, value) => bytes.set(path.resolve(file), Buffer.isBuffer(value) ? value : Buffer.from(typeof value === 'string' ? value : JSON.stringify(value)));
    const add = (file, value) => put(path.join(root, file), value);
    const io = {
        readFileSync(file, encoding) {
            const value = bytes.get(path.resolve(file));
            if (!value) throw Error('Missing virtual file: ' + file);
            return encoding ? value.toString(encoding) : Buffer.from(value);
        },
        readdirSync(directory, options) {
            directory = path.resolve(directory);
            const names = new Map();
            for (const file of bytes.keys()) {
                const relative = path.relative(directory, file);
                if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) continue;
                const parts = relative.split(path.sep); names.set(parts[0], parts.length > 1 || names.get(parts[0]) === true);
            }
            if (!names.size && ![path.join(root, 'src'), path.join(project, 'Temp/WhimTex/test-runs')].includes(directory)) throw Error('Missing virtual directory: ' + directory);
            return [...names].map(([name, directory]) => options?.withFileTypes ? { name, isDirectory: () => directory, isSymbolicLink: () => false } : name);
        }
    };
    for (const file of ['Tests~/scripts/run-tests.mjs', 'Tests~/scripts/legacy.mjs', 'Tests~/Framework/test-api.mjs',
        'Tests~/Framework/TestApi.cs', 'Tests~/Framework/EntryContract.cs', 'Tests~/archive-descriptor.json',
        'Tests~/migration.json', 'Tests~/scripts/check-migration.mjs']) add(file, 'receipt-' + file);
    add('src/Current.cs', 'class Production {}');
    const scenario = { id: 'example-v2', file: 'Tests~/Cases/Example.cs', runner: 'run_script', category: 'regression',
        entry: 'Example.Run', args: ['$runId', 2, true], supportFiles: ['Tests~/Framework/TestApi.cs'] };
    add(scenario.file, 'using System;\r\nclass Example {}\r\n');
    const catalog = { version: 2, scenarios: [scenario] };
    const legacy = new Map([['Example.cs', Buffer.from('original\r\n')]]);
    const rows = new Map(auditNames.map(name => [name, []]));
    function row(file = 'Example.cs', newFile = scenario.file, ids = [scenario.id]) {
        return { legacyFile: file, newFile, scenarioIds: ids, legacyEntries: ['Run()'],
            coverage: ['Original strict tolerance/input/branch/cleanup reviewed independently; receipt binds this claim, not check counts.'],
            gaps: [], reviewStatus: 'source-reviewed', runtimeStatus: 'pending-parent-validation',
            sourceHashes: { legacy: sha256(legacy.get(file)), replacement: sha256(io.readFileSync(path.join(root, newFile))) } };
    }
    rows.get('unity-a.json').push(row());
    function flush() {
        const manifest = { version: 1, files: [...legacy].map(([file, raw]) => ({ file, bytes: raw.length, sha256: sha256(raw) })) };
        add('Tests~/legacy-manifest.json', manifest);
        for (const [file, raw] of legacy) add('Tests~/Legacy/' + file, raw);
        for (const [name, files] of rows) add('Tests~/CoverageAudit/' + name, { version: 1, files });
        add('Tests~/scripts/test-catalog.json', catalog);
        return sha256(io.readFileSync(path.join(root, 'Tests~/legacy-manifest.json')));
    }
    const pin = flush();
    const options = () => ({ root, projectPath: project, io, expectedManifestSha: pin });
    const nativeResult = (status = 'passed', sourceScenario = scenario) => {
        const runId = 'owned-run', file = path.join(project, 'Temp/WhimTex/test-runs/input-owned.cs');
        return { id: sourceScenario.id, file: sourceScenario.file, runner: sourceScenario.runner, category: sourceScenario.category, status,
            testResult: { status: status === 'passed' ? 'passed' : 'failed', checks: 1, failures: status === 'passed' ? [] : ['real failure'] },
            attempts: [{ phase: 'result', entry: sourceScenario.entry, reply: { stdout: JSON.stringify({ data: {
                parameters: { file: path.relative(project, file), entry: sourceScenario.entry, args: JSON.stringify([runId, 2, true]) },
                target: { projectPath: project } } }) } }] };
    };
    const report = (result = nativeResult(), time = '2026-01-01T00:00:00Z') => ({ action: 'run', startedAt: time, projectPath: project,
        selected: [scenario.id], fingerprint: currentReviewFingerprint([scenario], root, io), results: [result], inputs: [{ id: scenario.id,
            file: path.join(project, 'Temp/WhimTex/test-runs/input-owned.cs'), sources: [scenario.file, ...scenario.supportFiles],
            sha256: sha256(currentBundleSources([scenario.file, ...scenario.supportFiles], root, io)) }] });
    const context = () => ({ root, projectPath: project, io, catalog, productionFingerprint: 'current-production',
        scenarioReviewBindings: new Map([[scenario.id, { current: true }]]),
        fingerprint: scenarios => currentReviewFingerprint(scenarios, root, io) });
    return { project, root, bytes, add, put, io, catalog, scenario, legacy, rows, row, flush, pin, options, nativeResult, report, context };
}

test('Frozen manifest and every raw byte: CRLF, missing/extra files and manifest mutation', () => {
    const m = memory(); assert.equal(verifyFrozenLegacy(m.root, m.io, m.pin).verified, true);
    m.add('Tests~/Legacy/Example.cs', 'original\n');
    assert.equal(verifyFrozenLegacy(m.root, m.io, m.pin).issues[0].kind, 'legacy-raw-bytes-mismatch');
    m.add('Tests~/Legacy/Example.cs', 'original\r\n'); m.add('Tests~/Legacy/Extra.cs', 'extra');
    assert.ok(verifyFrozenLegacy(m.root, m.io, m.pin).issues.some(i => i.kind === 'unexpected-legacy-file'));
    m.bytes.delete(path.join(m.root, 'Tests~/Legacy/Example.cs'));
    assert.ok(verifyFrozenLegacy(m.root, m.io, m.pin).issues.some(i => i.kind === 'missing-legacy-file'));
    m.add('Tests~/legacy-manifest.json', '{"files":[]}');
    assert.ok(verifyFrozenLegacy(m.root, m.io, m.pin).issues.some(i => i.kind === 'manifest-raw-sha-mismatch'));
});

test('Bundle directive grammar, deduplication, file order, line preservation and raw receipt sensitivity', () => {
    const m = memory(); m.add('one.cs', 'using System;\r\nusing static System.Math;\r\n    using Local;\r\nbody1');
    m.add('two.cs', 'using System;\nusing Alias = System.Text;\nbody2');
    assert.equal(currentBundleSources(['one.cs', 'two.cs'], m.root, m.io),
        'using System;\nusing static System.Math;\nusing Alias = System.Text;\n#line 1 "one.cs"\n\n\n    using Local;\r\nbody1\n#line 1 "two.cs"\n\n\nbody2');
    const first = currentReviewFingerprint([m.scenario], m.root, m.io);
    m.scenario.args[1] = 3; assert.notEqual(currentReviewFingerprint([m.scenario], m.root, m.io), first);
    m.scenario.args[1] = 2; m.add('Tests~/Framework/TestApi.cs', 'changed');
    assert.notEqual(currentReviewFingerprint([m.scenario], m.root, m.io), first);
    assert.throws(() => currentBundleSources(['../outside.cs'], m.root, m.io), /outside package/);
});

test('Current C# pass binds exact input order/SHA and invocation typed args; compile/entries never count', () => {
    const m = memory(), r = m.report();
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'current-regression-run');
    for (const action of ['compile', 'entries']) assert.equal(inspectRuntimeReport({ ...r, action }, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
    const bad = structuredClone(r); bad.inputs[0].sources.reverse();
    assert.equal(inspectRuntimeReport(bad, bad.results[0], m.scenario, m.context()).sourceCurrent, false);
    const args = structuredClone(r.results[0]); const envelope = JSON.parse(args.attempts[0].reply.stdout);
    envelope.data.parameters.args = '["owned-run","2",true]'; args.attempts[0].reply.stdout = JSON.stringify(envelope);
    assert.equal(inspectRuntimeReport(r, args, m.scenario, m.context()).proof, 'not-current-proof');
    const wrongEntry = structuredClone(r.results[0]); wrongEntry.attempts[0].entry = 'Other.Run';
    assert.equal(inspectRuntimeReport(r, wrongEntry, m.scenario, m.context()).invocation.verified, false);
    const uncertain = { ...r.results[0], uncertain: true };
    assert.equal(inspectRuntimeReport(r, uncertain, m.scenario, m.context()).proof, 'not-current-proof');
    assert.equal(inspectRuntimeReport({ ...r, productionFingerprint: 'old' }, r.results[0], m.scenario, m.context()).sourceCurrent, false);
});

test('Receipt hashes exact independent-dispatcher dependencies; migration tools are not execution globals', () => {
    const m = memory();
    const digest = createHash('sha256').update(JSON.stringify([m.scenario]));
    for (const file of [m.scenario.file, ...m.scenario.supportFiles].sort())
        digest.update(file).update(m.io.readFileSync(path.join(m.root, file)));
    digest.update(m.io.readFileSync(path.join(m.root, 'Tests~/scripts/run-tests.mjs')));
    for (const file of ['Tests~/scripts/legacy.mjs', 'Tests~/Framework/test-api.mjs', 'Tests~/Framework/TestApi.cs',
        'Tests~/Framework/EntryContract.cs', 'Tests~/legacy-manifest.json', 'Tests~/archive-descriptor.json'])
        digest.update(m.io.readFileSync(path.join(m.root, file)));
    const fingerprint = currentReviewFingerprint([m.scenario], m.root, m.io);
    assert.equal(fingerprint, digest.digest('hex'));
    for (const file of ['Tests~/migration.json', 'Tests~/scripts/check-migration.mjs']) {
        m.add(file, 'changed migration-only metadata');
        assert.equal(currentReviewFingerprint([m.scenario], m.root, m.io), fingerprint);
    }
    m.add('Tests~/archive-descriptor.json', 'changed pinned archive identity');
    assert.notEqual(currentReviewFingerprint([m.scenario], m.root, m.io), fingerprint);
});

test('Native receipt with unchanged bundle is historical after dispatcher or pinned archive changes', () => {
    for (const file of ['Tests~/scripts/run-tests.mjs', 'Tests~/archive-descriptor.json']) {
        const m = memory(), r = m.report();
        const reportFile = path.join(m.project, 'Temp/WhimTex/test-runs/pre-retirement.json');
        m.put(reportFile, r);
        const before = m.io.readFileSync(reportFile);
        m.add(file, 'new independent dispatcher/archive contract');
        const evidence = inspectRuntimeReport(r, r.results[0], m.scenario, m.context());
        assert.equal(evidence.input.verified, true);
        assert.equal(evidence.receiptMatches, false);
        assert.equal(evidence.sourceCurrent, false);
        assert.equal(evidence.proof, 'not-current-proof');
        assert.match(evidence.issues.join(' '), /Native receipt is historical/);
        const runtime = buildInventory(m.options()).files[0].runtime[0];
        assert.equal(runtime.status, 'pending-no-current-runtime-proof');
        assert.equal(runtime.latest, null);
        assert.equal(runtime.history.length, 1);
        assert.equal(runtime.history[0].status, 'passed');
        assert.equal(runtime.history[0].sourceCurrent, false);
        assert.equal(runtime.history[0].proof, 'not-current-proof');
        assert.deepEqual(m.io.readFileSync(reportFile), before, 'No historical receipt re-signing');
    }
});

test('Native selected IDs missing from current catalog never bypass the receipt through a matching bundle', () => {
    const m = memory(), r = m.report();
    r.selected.push('retired-legacy-id');
    const evidence = inspectRuntimeReport(r, r.results[0], m.scenario, m.context());
    assert.equal(evidence.input.verified, true);
    assert.equal(evidence.sourceCurrent, false);
    assert.equal(evidence.proof, 'not-current-proof');
    assert.match(evidence.issues.join(' '), /Selected ID absent from current catalog/);
});

test('Removed catalog results keep raw receipt identity and status as historical evidence only', () => {
    const m = memory(), r = m.report();
    const retired = { ...structuredClone(r.results[0]), id: 'retired-legacy-id', legacy: true };
    r.results.push(retired); r.selected.push(retired.id);
    const file = path.join(m.project, 'Temp/WhimTex/test-runs/retired.json'); m.put(file, r);
    const raw = m.io.readFileSync(file), out = buildInventory(m.options());
    assert.equal(out.retiredRuntimeEvidence.length, 1);
    const history = out.retiredRuntimeEvidence[0];
    assert.equal(history.id, retired.id); assert.equal(history.status, 'passed');
    assert.equal(history.recordedFingerprint, r.fingerprint);
    assert.equal(history.reportRawSha256, sha256(raw));
    assert.equal(history.sourceCurrent, false); assert.equal(history.proof, 'not-current-proof');
    assert.equal(out.reportInputs[0].rawSha256, sha256(raw));
    assert.equal(out.files[0].runtime[0].status, 'pending-no-current-runtime-proof');
    assert.deepEqual(m.io.readFileSync(file), raw);
});

test('Unlinked infrastructure receipts remain in history without granting original-source coverage', () => {
    const m = memory();
    const unlinked = { ...m.scenario, id: 'unlinked-runner', category: 'runner' };
    m.catalog.scenarios.push(unlinked); m.flush();
    const r = m.report(m.nativeResult('passed', unlinked)); r.selected = [unlinked.id];
    r.inputs[0].id = unlinked.id;
    r.fingerprint = currentReviewFingerprint([unlinked], m.root, m.io);
    m.put(path.join(m.project, 'Temp/WhimTex/test-runs/unlinked.json'), r);
    m.add('Tests~/archive-descriptor.json', 'retired archive contract');
    const out = buildInventory(m.options()), retained = out.runtimeHistory.find(item => item.id === unlinked.id);
    assert.equal(retained.category, 'runner'); assert.equal(retained.history.length, 1);
    assert.equal(retained.history[0].status, 'passed'); assert.equal(retained.history[0].sourceCurrent, false);
    assert.equal(retained.history[0].proof, 'not-current-proof');
    assert.equal(out.files[0].runtime.some(item => item.id === unlinked.id), false);
    assert.equal(out.summary.currentRegressionReportFiles, 0);
});

test('Stored framework source review becomes stale after runner edits; its hashes are never rebound', () => {
    const m = memory(), runnerFile = 'Tests~/scripts/run-tests.mjs';
    const row = m.row('Example.cs', runnerFile); m.rows.get('framework-and-auxiliary.json').push(row); m.flush();
    const auditFile = path.join(m.root, 'Tests~/CoverageAudit/framework-and-auxiliary.json');
    const before = m.io.readFileSync(auditFile);
    const r = m.report(); m.put(path.join(m.project, 'Temp/WhimTex/test-runs/prior.json'), r);
    m.add(runnerFile, 'changed dispatcher');
    const out = buildInventory(m.options()), source = out.files[0];
    assert.equal(source.sourceReviewStatus, 'stale-or-incomplete-source-review');
    assert.ok(source.stale.some(issue => issue.auditFile === 'framework-and-auxiliary.json' && issue.file === runnerFile));
    assert.equal(source.runtime[0].status, 'pending-no-current-runtime-proof');
    assert.equal(source.runtime[0].history[0].status, 'passed');
    assert.deepEqual(m.io.readFileSync(auditFile), before, 'Read-only gate cannot re-certify changed infrastructure');
});

test('Async phases enforce same run identity, final poll, required cleanup and project', () => {
    const m = memory(); const s = { ...m.scenario, async: { entry: 'Example.Poll', args: ['$runId'] }, cleanup: { entry: 'Example.Cleanup', args: ['$runId'] } };
    const r = m.nativeResult(); r.attempts[0].phase = 'start';
    const poll = structuredClone(r.attempts[0]); poll.phase = 'result'; poll.entry = 'Example.Poll';
    const cleanup = structuredClone(poll); cleanup.phase = 'cleanup'; cleanup.entry = 'Example.Cleanup';
    for (const a of [poll, cleanup]) { const e = JSON.parse(a.reply.stdout); e.data.parameters.entry = a.entry; e.data.parameters.args = '["owned-run"]'; a.reply.stdout = JSON.stringify(e); }
    r.attempts.push(poll, cleanup); const input = path.join(m.project, 'Temp/WhimTex/test-runs/input-owned.cs');
    assert.equal(inspectInvocation(r, s, m.project, input).verified, true);
    const e = JSON.parse(cleanup.reply.stdout); e.data.parameters.args = '["different-run"]'; cleanup.reply.stdout = JSON.stringify(e);
    assert.equal(inspectInvocation(r, s, m.project, input).verified, false);
    r.attempts.pop(); assert.ok(inspectInvocation(r, s, m.project, input).issues.some(i => /cleanup/.test(i)));
    r.attempts.pop(); assert.ok(inspectInvocation(r, s, m.project, input).issues.some(i => /final poll/.test(i)));
});

test('Node current selected receipt required; historical pass and diagnostics never certify regression', () => {
    const m = memory(); m.scenario.runner = 'node'; m.scenario.file = 'Tests~/Cases/Example.mjs'; m.scenario.entry = null;
    m.add(m.scenario.file, 'node fixture'); m.flush();
    const r = m.report(); r.results[0].attempts = [{ phase: 'result', entry: null }]; r.results[0].testResult.checks = 1000000;
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'current-regression-run');
    assert.equal(inspectRuntimeReport({ ...r, fingerprint: 'old' }, r.results[0], m.scenario, m.context()).sourceCurrent, false);
    assert.equal(inspectRuntimeReport({ ...r, selected: ['missing'] }, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
    m.scenario.category = 'diagnostic'; const d = m.report(); d.results[0].attempts = [{ phase: 'result', entry: null }];
    assert.equal(inspectRuntimeReport(d, d.results[0], m.scenario, m.context()).proof, 'current-diagnostic-run-not-regression');
});

test('Latest same-source failure/SKIP/uncertain overrides green, historical failure does not', () => {
    const green = { action: 'run', sourceCurrent: true, status: 'passed', proof: 'current-regression-run', timestamp: 1, report: 'old' };
    for (const status of ['skip', 'assertion-failed', 'timeout', 'cleanup-failed']) {
        const newer = { ...green, status, proof: 'not-current-proof', timestamp: 2, report: 'new' };
        const selected = chooseNewestEvidence([green, newer]); assert.equal(selected.latest.status, status); assert.equal(selected.earlierGreenOverridden, true);
    }
    const compile = { ...green, action: 'compile', timestamp: 3 };
    const historical = { ...green, status: 'assertion-failed', sourceCurrent: false, timestamp: 4 };
    assert.equal(chooseNewestEvidence([green, compile, historical]).latest.report, 'old');
    assert.equal(chooseNewestEvidence([green, { ...green, status: 'skip', proof: 'not-current-proof' }]).latest.status, 'skip');
});

test('Custom Player facts require both exact native bundle and global receipt; SKIP before input overrides', () => {
    const m = memory(); Object.assign(m.scenario, { runner: 'node', requiresUnity: true, workflow: 'player-build', file: 'Tests~/Cases/PlayerWorkflow.mjs', entry: null,
        reviewFiles: ['Tests~/Cases/UnityB/PlayerReleaseTests.cs'] });
    m.add(m.scenario.file, 'wrapper'); m.add(m.scenario.reviewFiles[0], 'native player'); m.flush();
    const r = m.report(); r.results[0].attempts = [{ phase: 'result', entry: null }];
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
    r.results[0].testResult.facts = { input: { sources: m.scenario.reviewFiles, sha256: sha256(currentBundleSources(m.scenario.reviewFiles, m.root, m.io)) } };
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'current-regression-run');
    const noReview = m.context(); noReview.scenarioReviewBindings = new Map();
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, noReview).proof, 'not-current-proof');
    assert.equal(inspectRuntimeReport({ ...r, fingerprint: 'old' }, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
    r.results[0].testResult.facts.input.sha256 = 'a'.repeat(64);
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
    r.results[0].status = 'skip'; delete r.results[0].testResult.facts;
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).sourceCurrent, true);
});

test('All declared support hash formats, additionalDependenciesSha, source gaps and pending separated', () => {
    const m = memory(); const row = m.rows.get('unity-a.json')[0];
    const helper = 'Tests~/Helper.cs'; m.add(helper, 'helper'); const h = sha256(m.io.readFileSync(path.join(m.root, helper)));
    row.sourceHashes.support = { [helper]: h }; row.additionalDependenciesSha = { [helper]: h }; row.supportSources = [{ file: helper, sha256: h }];
    row.gaps = ['Actual runtime pending parent execution.', 'Missing installed independent PSD decoder.', 'No exact original oracle predicate match.']; m.flush();
    let result = buildInventory(m.options()); assert.equal(result.files[0].pending.length, 1); assert.equal(result.files[0].externalGaps.length, 1); assert.equal(result.files[0].sourceGaps.length, 1);
    m.add(helper, 'new helper'); result = buildInventory(m.options());
    assert.equal(result.files[0].stale.filter(i => i.file === helper).length, 3);
    assert.equal(result.archiveRemovalAllowed, false); assert.equal(result.automaticFullCoverage, false);
    assert.equal(splitGap('Missing original oracle; pending parent runtime.'), 'source');
});

test('ONLY four restored B files superseded; DocumentRelease distinct subsets and gaps retained', () => {
    const m = memory(); m.rows.get('unity-a.json').length = 0;
    for (const file of ['DocumentPreparationSmoke.cs', 'DocumentReleaseValidation.cs', 'Other.cs']) {
        m.legacy.set(file, Buffer.from('old ' + file)); const stale = m.row(file), parent = m.row(file);
        stale.sourceHashes.replacement = 'a'.repeat(64); stale.gaps = ['No exact original oracle match'];
        m.rows.get('unity-b-public.json').push(stale); m.rows.get('framework-and-auxiliary.json').push(parent);
    }
    m.rows.get('player.json').push(m.row('DocumentReleaseValidation.cs'));
    const pin = m.flush(), out = buildInventory({ ...m.options(), expectedManifestSha: pin });
    const restored = out.files.find(f => f.legacyFile === 'DocumentPreparationSmoke.cs'); assert.equal(restored.supersededReviews.length, 1); assert.equal(restored.stale.length, 0);
    const release = out.files.find(f => f.legacyFile === 'DocumentReleaseValidation.cs'); assert.equal(release.supersededReviews.length, 0); assert.equal(release.reviews.length, 3); assert.ok(release.sourceGaps.length);
    assert.ok(out.files.find(f => f.legacyFile === 'Other.cs').stale.length);
    assert.ok(out.files.find(f => f.legacyFile === 'Example.cs').missing.length);
});

test('Missing parent audit/report races never grant coverage; inventory emits concrete current runtime', () => {
    const m = memory(); m.bytes.delete(path.join(m.root, 'Tests~/CoverageAudit/framework-and-auxiliary.json'));
    m.put(path.join(m.project, 'Temp/WhimTex/test-runs/active.json'), '');
    m.put(path.join(m.project, 'Temp/WhimTex/test-runs/green.json'), m.report());
    m.put(path.join(m.project, 'Temp/WhimTex/test-runs/new-skip.json'), m.report(m.nativeResult('skip'), '2026-01-02T00:00:00Z'));
    const out = buildInventory(m.options()); assert.ok(out.pending.some(i => /framework-and-auxiliary/.test(i.file)));
    assert.ok(out.reportIssues.some(i => i.kind === 'unreadable-or-in-progress-report'));
    assert.equal(out.files[0].runtime[0].status, 'current-skipped'); assert.equal(out.files[0].runtime[0].earlierGreenOverridden, true);
    assert.equal(out.summary.automaticFullCoverage, false); assert.equal(out.summary.archiveRemovalAllowed, false);
});

test('PSD custom input SHA, global receipt, producer/decoder receipts and installed module bytes required', () => {
    const m = memory(); Object.assign(m.scenario, { id: 'psd-reader-roundtrip-v2', runner: 'node', requiresUnity: true,
        file: 'Tests~/Cases/Export/PsdReaderWorkflow.mjs', entry: null,
        reviewFiles: ['Tests~/Cases/Export/PsdWriter.cs', 'Tests~/Framework/TestApi.cs', 'src/PsdWriter.cs'] });
    m.add(m.scenario.file, 'wrapper'); m.add(m.scenario.reviewFiles[0], 'encoder'); m.add('src/PsdWriter.cs', 'writer'); m.flush();
    const modulePath = path.join(m.project, 'known-node_modules/decoder/index.js'); m.put(modulePath, 'installed decoder');
    const r = m.report(); r.results[0].attempts = [{ phase: 'result', entry: null }];
    r.results[0].testResult.facts = { input: { sha256: sha256(currentBundleSources(m.scenario.reviewFiles, m.root, m.io)) },
        decoder: { modulePath, sha256: sha256(m.io.readFileSync(modulePath)) }, producer: { status: 'passed' }, decoderResult: { status: 'passed' } };
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'current-regression-run');
    m.put(modulePath, 'different decoder');
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
    r.results[0].testResult.facts.decoderResult.status = 'skip';
    assert.equal(inspectRuntimeReport(r, r.results[0], m.scenario, m.context()).proof, 'not-current-proof');
});

test('Fault native GUID fixture supersedes registered global import fixture after install/both bodies/removal proof', () => {
    const m = memory(); Object.assign(m.scenario, { id: 'fault-release-workflow-v2', runner: 'node', requiresUnity: true,
        file: 'Tests~/Cases/UnityB/FaultWorkflow.mjs', entry: null, reviewFiles: ['Tests~/Cases/UnityB/FaultFixture.cs'] });
    m.add(m.scenario.file, 'fault wrapper'); m.add(m.scenario.reviewFiles[0], 'native fixture');
    const oldFixture = 'Tests~/Cases/Support/ImportFailureProbe.cs'; m.add(oldFixture, 'old uniform global fixture');
    const fixture = 'Fixtures/WhimTexImportFailureProbe.cs'; m.legacy.set(fixture, Buffer.from('old frozen import fixture'));
    m.legacy.set('DocumentReleaseValidation.cs', Buffer.from('old release subsets'));
    m.rows.get('framework-and-auxiliary.json').push(m.row(fixture, oldFixture, ['old-uniform-faults-v2']));
    m.catalog.scenarios.push({ ...m.scenario, id: 'old-uniform-faults-v2', file: oldFixture, requiresUnity: false });
    m.rows.get('fault.json').push(m.row(fixture, m.scenario.reviewFiles[0]), m.row('DocumentReleaseValidation.cs', m.scenario.reviewFiles[0]));
    const pin = m.flush(); const options = { ...m.options(), expectedManifestSha: pin };
    assert.equal(buildInventory(options).files.find(f => f.legacyFile === fixture).supersededReviews.length, 0);
    const r = m.report(); r.results[0].attempts = [{ phase: 'result', entry: null }]; const runId = '0123456789abcdef0123456789abcdef';
    r.results[0].testResult.facts = { input: { runId, sources: m.scenario.reviewFiles, sha256: sha256(currentBundleSources(m.scenario.reviewFiles, m.root, m.io)) },
        Setup: { status: 'passed' }, Faults: { status: 'passed' }, PollDeferred: { status: 'passed' }, Cleanup: { status: 'passed' },
        nativeCompilations: [{ kind: 'install', nativeCompilationComplete: true, runId }] };
    const reportFile = path.join(m.project, 'Temp/WhimTex/test-runs/fault.json'); m.put(reportFile, r);
    assert.equal(buildInventory(options).files.find(f => f.legacyFile === fixture).supersededReviews.length, 0);
    r.results[0].testResult.facts.nativeCompilations.push({ kind: 'cleanup', nativeCompilationComplete: true, runId }); m.put(reportFile, r);
    const out = buildInventory(options); assert.equal(out.files.find(f => f.legacyFile === fixture).supersededReviews.length, 1);
    assert.equal(out.files.find(f => f.legacyFile === 'DocumentReleaseValidation.cs').supersededReviews.length, 0);
    r.fingerprint = 'historical'; m.put(reportFile, r);
    assert.equal(buildInventory(options).files.find(f => f.legacyFile === fixture).supersededReviews.length, 0);
});

test('Recovery-only JSON never promotes a failed current run to runtime pass', () => {
    const failure = { action: 'run', sourceCurrent: true, status: 'timeout', proof: 'not-current-proof', timestamp: 1, report: 'timeout' };
    const recovery = { ...failure, action: 'recovery', status: 'passed', proof: 'current-regression-run', timestamp: 2, report: 'recovery' };
    assert.equal(chooseNewestEvidence([failure, recovery]).status, 'current-not-passed');
});

test('Generic public-helper entry labels do not reclassify real regressions; explicit manual setup stays separate', () => {
    const m = memory(); const row = m.rows.get('unity-a.json')[0];
    row.legacyEntries = [{ name: 'Run', kind: 'public-entry-or-public-helper', scheduledEntries: [m.scenario.id] }, { name: 'Check', kind: 'helper-or-top-level-local-function' }];
    m.flush(); assert.equal(buildInventory(m.options()).files[0].classification, 'regression-linked');
    row.disposition = { kind: 'integrated-helper' }; m.flush();
    assert.equal(buildInventory(m.options()).files[0].runtimeStatus, 'not-regression-equivalence');
});

test('Source mutation during binding, even reverted, requires a freeze instead of fresh green', () => {
    const m = memory(); const ordinaryRead = m.io.readFileSync; const source = path.join(m.root, m.scenario.file); let reads = 0;
    const io = { ...m.io, readFileSync(file, encoding) {
        if (path.resolve(file) === source && ++reads === 2) return encoding ? 'changed during binding' : Buffer.from('changed during binding');
        return ordinaryRead(file, encoding);
    } };
    const out = buildInventory({ ...m.options(), io });
    assert.equal(out.summary.snapshotStable, false);
    assert.ok(out.pending.some(p => p.kind === 'source-freeze-required-before-runtime-binding'));
    assert.equal(out.files[0].runtimeStatus, 'pending-source-freeze');
});

test('Archive descriptor race revokes source freeze even without a runtime report', () => {
    const m = memory(), read = m.io.readFileSync;
    const descriptor = path.join(m.root, 'Tests~/archive-descriptor.json'); let reads = 0;
    m.io.readFileSync = (file, encoding) => path.resolve(file) === descriptor && ++reads > 1
        ? encoding ? 'mutated archive contract' : Buffer.from('mutated archive contract') : read(file, encoding);
    const out = buildInventory(m.options());
    assert.equal(out.summary.snapshotStable, false);
    assert.ok(out.snapshotChanges.some(change => change.file === descriptor));
    assert.equal(out.summary.currentRegressionReportFiles, 0);
    assert.equal(out.files[0].runtimeStatus, 'pending-source-freeze');
});

test('Unordered current evidence is conservative, not a reason to resurrect an older green', () => {
    const green = { action: 'run', sourceCurrent: true, status: 'passed', proof: 'current-regression-run', timestamp: 10, report: 'green', chronologyKnown: true };
    const unknown = { ...green, status: 'timeout', proof: 'not-current-proof', timestamp: 0, chronologyKnown: false, report: 'undated' };
    assert.equal(chooseNewestEvidence([green, unknown]).latest.report, 'undated');
});

test('Unavailable named historical input artefacts are external, not missing code', () => {
    for (const gap of [
        'Capture: original live_4.bin unavailable; restored inputs await original input.',
        'Preview: original copy-blend-lab/00_source.png unavailable; no direct visual assertion.',
        'Preview: original screened-poisson-lab/00_original.png unavailable; output restored.',
        'Compare/Audit: 252 pre-change float snapshots unavailable. Original 2e-5 tolerance retained.'
    ]) assert.equal(splitGap(gap), 'external');
});

test('Unverified manual screenshot/state and current-receipt binding are pending, never code omission', () => {
    assert.equal(splitGap('RUNTIME: PreparePlayer failed because an untitled user scene is unsaved. Build assertions remain pending.'), 'pending');
    assert.equal(splitGap('RUNTIME SUBSET: Faults/Deferred bodies passed; this does not cover Player entries.'), 'pending');
    assert.equal(splitGap('METADATA: parent owns final catalog/profile/batch supersession and fresh review fingerprints.'), 'pending');
    assert.equal(splitGap('Native postprocessor activation/removal passed for the exact recorded GUID; no cross-project/platform or concurrent-user-operation claim. Final metadata remains parent-owned.'), 'pending');
    assert.equal(splitGap('Manual NativeSetup/NativeInspect screenshot/state equivalence is not verified. Its closure cause is unestablished.'), 'pending');
    assert.equal(splitGap('Parent-reported real-original reports require evidence binding/current-fingerprint validation; carrier-only branch is not full original coverage.'), 'pending');
    assert.equal(splitGap('Parent-authored profiling=true require current-fingerprint runtime proof; no-profiler pass is not raw-capture equivalence.'), 'pending');
});

test('Already-integrated append metadata remains an owner-update pending claim with original provenance', () => {
    const m = memory(), row = m.rows.get('unity-a.json')[0];
    row.gaps = ['Public visual ability restored, but parent must append VisualSequence and preserve manual GUID workflow; no original screenshot/layout oracle has been executed here.'];
    row.reviewStatus = 'gap'; m.scenario.entry = 'Example.VisualSequence'; m.flush();
    assert.equal(splitGap(row.gaps[0]), 'metadata');
    const out = buildInventory(m.options()), file = out.files[0];
    assert.equal(file.sourceGaps.length, 0); assert.equal(file.reviews[0].originalGaps[0], row.gaps[0]);
    assert.equal(file.pending[0].integrationState, 'present-in-current-catalog; audit-owner-must-update-stale-claim');
    assert.equal(file.pending[0].observedMetadata[0].entry, 'Example.VisualSequence');
    const capture = 'Supported public repaint Capture restored; append async CaptureWorkflow metadata and validate actual framebuffer PNG.';
    assert.equal(splitGap(capture), 'metadata');
    assert.equal(metadataGapEvidence(capture, row, m.catalog).integrationState, 'not-fully-observed-in-current-catalog');
});

test('Actual missing predicates/branches/cleanup/public modes cannot fall through softer pending/external/manual labels', () => {
    for (const gap of [
        'Missing original assertion; screenshot/state equivalence is unverified.',
        'RUNTIME: Missing original assertion; parent execution pending.',
        'Missing original screenshot assertion; screenshot/state equivalence is unverified.',
        'Screenshot predicate is not implemented; screenshot/state equivalence is unverified.',
        'Required cleanup branch is absent; runtime pending parent execution.',
        'Original predicate not retained; live_4.bin unavailable.',
        'No exact loop/switch token match at Legacy:260; pending parent execution.',
        'Unresolved original cleanup; append async CaptureWorkflow metadata.',
        'Assertion weakened; 252 pre-change float snapshots unavailable.',
        'Missing public entry; runtime not executed.',
        'No individual Faults-only/Deferred-only CLI modes are implemented; bodies run serially.'
    ]) assert.equal(splitGap(gap), 'source');
});

test('Retained original ContextTools failure and unreached tail remain explicit runtime blockers, not missing code', () => {
    const m = memory(), row = m.rows.get('unity-a.json')[0];
    row.reviewStatus = 'gap';
    row.gaps = [
        'Unresolved genuine original-oracle failure: minimum640x420 fit is red. Source fixture and100ms wait are faithful; preserve regression classification and the original assertion.',
        'Cause remains bounded but not proven: the receipt cannot distinguish a delayed reveal from stable product scroll/layout failure.',
        'Validation gap: original assertions after the minimum-window failure were not executed in this run. They remain independently ported in source; other passes do not prove equivalence.'
    ];
    for (const gap of row.gaps) assert.equal(splitGap(gap), 'runtime-blocker');
    // Any actual code loss still wins over the runtime-specific wording.
    assert.equal(splitGap(row.gaps[0] + ' Missing original assertion.'), 'source');
    m.flush();
    const file = buildInventory(m.options()).files[0];
    assert.equal(file.sourceGaps.length, 0);
    assert.deepEqual(file.reviews[0].originalGaps, row.gaps);
    assert.equal(file.pending.filter(p => p.kind === 'unresolved-regression-runtime-blocker').length, 3);
    assert.notEqual(file.runtimeStatus, 'current-regression-reports-present');
    assert.equal(file.runtime[0].category, 'regression');
});

function combinedReleaseMemory() {
    const m = memory(); m.rows.get('unity-a.json').length = 0;
    const doc = 'DocumentReleaseValidation.cs', oldFile = 'Tests~/Cases/UnityB/DocumentReleaseValidationTests.cs';
    m.legacy.set(doc, Buffer.from('frozen whole document-release source'));
    m.add(oldFile, 'obsolete port');
    Object.assign(m.scenario, { id: 'fault-release-workflow-v2', file: 'Tests~/Cases/UnityB/FaultWorkflow.mjs',
        runner: 'node', requiresUnity: true, workflow: 'native-fixture', entry: null, supportFiles: [],
        reviewFiles: ['Tests~/Cases/UnityB/FaultFixture.cs'] });
    const player = { ...m.scenario, id: 'player-release-workflow-v2', file: 'Tests~/Cases/UnityB/PlayerWorkflow.mjs',
        workflow: 'player-build', reviewFiles: ['Tests~/Cases/UnityB/PlayerReleaseTests.cs'] };
    m.catalog.scenarios.push(player);
    for (const s of [m.scenario, player]) { m.add(s.file, 'wrapper ' + s.id); m.add(s.reviewFiles[0], 'native ' + s.id); }
    const old = m.row(doc, oldFile, ['document-release-validation-faults-v2', 'document-release-validation-deferred-v2']);
    old.reviewStatus = 'gap'; old.gaps = ['No exact original predicate/helper match.'];
    m.rows.get('unity-b-public.json').push(old);
    for (const [s, audit] of [[m.scenario, 'fault.json'], [player, 'player.json']]) {
        const r = m.row(doc, s.reviewFiles[0], [s.id]);
        r.sourceHashes.support = { [s.file]: sha256(m.io.readFileSync(path.join(m.root, s.file))) };
        m.rows.get(audit).push(r);
    }
    const parent = m.row(doc, m.scenario.reviewFiles[0], [m.scenario.id, player.id]);
    parent.legacyEntries = ['Install()', 'Faults()', 'PrepareDeferredFailure()', 'VerifyDeferredFailure()',
        'PreparePlayer()', 'RestartPlayerLive()', 'InspectBuild()', 'Cleanup()'];
    parent.sourceHashes.support = Object.fromEntries([m.scenario.file, player.file, player.reviewFiles[0]]
        .map(file => [file, sha256(m.io.readFileSync(path.join(m.root, file)))]));
    parent.disposition = { kind: 'combined-release-source-handoff' };
    parent.supersedesAudits = ['unity-b-public.json'];
    m.rows.get('framework-and-auxiliary.json').push(parent);
    const pin = m.flush();
    const inventory = () => buildInventory({ ...m.options(), expectedManifestSha: pin });
    const release = () => inventory().files.find(f => f.legacyFile === doc);
    const workflowReport = (s, status = 'passed') => {
        const r = m.report(m.nativeResult(status, s)); r.selected = [s.id];
        r.fingerprint = currentReviewFingerprint([s], m.root, m.io);
        r.results[0].attempts = [{ phase: 'result', entry: null }];
        const runId = 'owned-native';
        r.results[0].testResult.facts = { input: { runId, sources: s.reviewFiles,
            sha256: sha256(currentBundleSources(s.reviewFiles, m.root, m.io)) },
            Setup: { status: 'passed' }, Faults: { status: 'passed' }, PollDeferred: { status: 'passed' }, Cleanup: { status: 'passed' },
            nativeCompilations: ['install', 'cleanup'].map(kind => ({ kind, nativeCompilationComplete: true, runId })) };
        return r;
    };
    return { ...m, doc, old, parent, player, inventory, release, workflowReport };
}

test('Explicit current parent combined source review supersedes ONLY exact obsolete B row, with no runtime inference', () => {
    const m = combinedReleaseMemory(), f = m.release();
    assert.equal(f.parentCombinedDocRelease.approved, true);
    assert.equal(f.supersededReviews.length, 1);
    assert.equal(f.supersededReviews[0].gaps[0], m.old.gaps[0]);
    assert.equal(f.sourceReviewStatus, 'current-reviewed-claims');
    assert.equal(f.sourceGaps.length, 0);
    assert.deepEqual(f.scenarioIds.sort(), ['fault-release-workflow-v2', 'player-release-workflow-v2']);
    assert.equal(f.runtime.length, 2); assert.equal(f.runtimeStatus, 'pending-or-not-passed');
    assert.equal(m.inventory().archiveRemovalAllowed, false);
    assert.equal(m.inventory().automaticFullCoverage, false);
    // The parent may explicitly rebind that B review to the native Fault source.
    m.old.newFile = m.scenario.reviewFiles[0]; m.old.sourceHashes.replacement = m.parent.sourceHashes.replacement;
    m.flush(); assert.equal(m.release().parentCombinedDocRelease.approved, true);
});

test('Combined source policy rejects absent/malformed/partial/stale/wrong-owner/overbroad handoffs without relaxing source gaps', () => {
    for (const change of [
        m => { delete m.parent.disposition; },
        m => { m.parent.disposition.kind = 'ordinary-review'; },
        m => { delete m.parent.supersedesAudits; },
        m => { m.parent.supersedesAudits = 'unity-b-public.json'; },
        m => { m.parent.reviewStatus = 'gap'; },
        m => { m.parent.legacyEntries.pop(); },
        m => { m.parent.gaps = ['Missing original assertion; runtime pending parent.']; },
        m => { m.parent.sourceHashes.legacy = 'a'.repeat(64); },
        m => { delete m.parent.sourceHashes.support[m.player.file]; },
        m => { m.parent.sourceHashes.support[m.player.file] = 'b'.repeat(64); },
        m => { m.old.sourceHashes.legacy = 'c'.repeat(64); },
        m => { m.parent.supersedesAudits = ['fault.json']; },
        m => { m.parent.supersedesAudits = ['unity-b-public.json', 'player.json']; },
        m => { m.parent.newFile = m.player.reviewFiles[0]; },
        m => { m.catalog.scenarios.pop(); },
        m => { m.parent.scenarioIds = [m.scenario.id]; },
        m => { m.rows.get('framework-and-auxiliary.json').length = 0; m.rows.get('player.json').push(m.parent); },
        m => { m.rows.get('framework-and-auxiliary.json').push(structuredClone(m.parent)); }
    ]) {
        const m = combinedReleaseMemory(); change(m); m.flush(); const f = m.release();
        assert.equal(f.parentCombinedDocRelease.approved, false);
        assert.equal(f.supersededReviews.length, 0); assert.ok(f.sourceGaps.length);
        assert.notEqual(f.runtimeStatus, 'current-regression-reports-present');
    }
});

test('Combined DocRelease source review never substitutes Fault PASS or cleanup-only Player for actual Player PASS', () => {
    const m = combinedReleaseMemory(), reportDir = path.join(m.project, 'Temp/WhimTex/test-runs');
    m.put(path.join(reportDir, 'fault.json'), m.workflowReport(m.scenario));
    assert.equal(m.release().runtime.find(r => r.id === m.scenario.id).status, 'current-passed');
    assert.equal(m.release().runtimeStatus, 'pending-or-not-passed');
    for (const status of ['skip', 'assertion-failed']) {
        m.put(path.join(reportDir, 'player.json'), m.workflowReport(m.player, status));
        assert.notEqual(m.release().runtimeStatus, 'current-regression-reports-present');
    }
    m.put(path.join(reportDir, 'player.json'), m.workflowReport(m.player));
    assert.equal(m.release().runtimeStatus, 'current-regression-reports-present');
    const historical = m.workflowReport(m.player); historical.fingerprint = 'old';
    m.put(path.join(reportDir, 'player.json'), historical);
    assert.equal(m.release().runtimeStatus, 'pending-or-not-passed');
});

test('Retired absent-catalog global fixture IDs require explicit combined source handoff plus current native Fault proof', () => {
    const m = combinedReleaseMemory(), fixture = 'Fixtures/WhimTexImportFailureProbe.cs', oldFile = 'Tests~/Cases/Support/ImportFailureProbe.cs';
    m.legacy.set(fixture, Buffer.from('frozen import source')); m.add(oldFile, 'old fixture');
    m.rows.get('framework-and-auxiliary.json').push(m.row(fixture, oldFile, ['document-release-validation-faults-v2']));
    const native = m.row(fixture, m.scenario.reviewFiles[0], [m.scenario.id]);
    native.sourceHashes.support = { [m.scenario.file]: sha256(m.io.readFileSync(path.join(m.root, m.scenario.file))) };
    m.rows.get('fault.json').push(native); const pin = m.flush();
    const fixtureRow = () => buildInventory({ ...m.options(), expectedManifestSha: pin }).files.find(f => f.legacyFile === fixture);
    assert.equal(fixtureRow().supersededReviews.length, 0);
    m.put(path.join(m.project, 'Temp/WhimTex/test-runs/fault.json'), m.workflowReport(m.scenario));
    assert.equal(fixtureRow().supersededReviews.length, 1);
    m.parent.disposition.kind = 'ordinary-review'; m.flush();
    assert.equal(fixtureRow().supersededReviews.length, 0);
    assert.ok(fixtureRow().pending.some(p => p.kind === 'scenario-not-in-current-catalog'));
});

test('Already-integrated structured parent combined declaration requires explicit full read and exact obsolete B SHA', () => {
    const m = combinedReleaseMemory(); delete m.parent.disposition; delete m.parent.supersedesAudits;
    m.parent.parentCombinedDocRelease = { version: 1, fullOriginalRead: true, sourceReplacementReviewed: true,
        handoffScenarioIds: [m.scenario.id, m.player.id], supersedes: { auditFile: 'unity-b-public.json',
            newFile: m.old.newFile, sourceHashes: { ...m.old.sourceHashes } } };
    m.flush(); assert.equal(m.release().parentCombinedDocRelease.approved, true);
    assert.equal(m.release().runtimeStatus, 'pending-or-not-passed');
    for (const change of [
        d => { d.fullOriginalRead = false; },
        d => { d.sourceReplacementReviewed = false; },
        d => { d.handoffScenarioIds = [m.scenario.id]; },
        d => { d.supersedes.sourceHashes.replacement = 'a'.repeat(64); },
        d => { d.supersedes.newFile = m.player.reviewFiles[0]; },
        d => { d.supersedes.auditFile = 'fault.json'; },
        d => { delete d.supersedes; }
    ]) {
        const valid = structuredClone(m.parent.parentCombinedDocRelease); change(m.parent.parentCombinedDocRelease); m.flush();
        const f = m.release(); assert.equal(f.parentCombinedDocRelease.approved, false); assert.equal(f.supersededReviews.length, 0);
        m.parent.parentCombinedDocRelease = valid;
    }
});

// Synthetic raw receipts exercise the verifier, not the Unity oracles themselves.
function gradientPhaseMemory() {
    const m = memory(), legacyFile = 'WhimTexGradientReloadSmoke.cs';
    const nativeFile = 'Tests~/Cases/UnityD/WhimTexGradientReloadDiagnostic.cs';
    const sources = [nativeFile, 'Tests~/Framework/TestApi.cs'];
    const dependencies = ['Tests~/Cases/UnityD/GradientReload.mjs', ...sources, 'Tests~/Framework/test-api.mjs',
        'Tests~/scripts/run-tests.mjs', 'Tests~/Cases/UnityD/GradientReloadProtocol.test.mjs', 'src/UnityObjectID.cs'];
    for (const file of dependencies) m.add(file, 'reviewed source ' + file);
    m.legacy.clear(); m.legacy.set(legacyFile, Buffer.from('frozen original Begin/End\r\n'));
    m.bytes.delete(path.join(m.root, 'Tests~/Legacy/Example.cs'));
    for (const rows of m.rows.values()) rows.length = 0;
    Object.assign(m.scenario, { id: 'whimtex-gradient-reload-v2', file: dependencies[0], runner: 'node',
        category: 'regression', entry: null, args: ['$projectPath'], result: { kind: 'structured' },
        supportFiles: [], reviewFiles: dependencies.slice(1), requiresUnity: true });
    const helpers = ['Begin', 'End'].map(phase => ({ id: 'whimtex-gradient-reload-' + phase.toLowerCase() + '-v2',
        file: nativeFile, runner: 'run_script', category: 'diagnostic', entry: 'WhimTexGradientReloadDiagnostic.' + phase,
        args: ['$runId'], supportFiles: sources.slice(1), reviewFiles: ['src/UnityObjectID.cs'], result: { kind: 'structured' },
        cleanup: { entry: 'WhimTexGradientReloadDiagnostic.Cleanup', args: ['$runId'] } }));
    m.catalog.scenarios.push(...helpers);
    const row = m.row(legacyFile, nativeFile, [m.scenario.id, ...helpers.map(h => h.id)]);
    row.legacyEntries = ['Begin', 'End'].map(name => ({ name, signature: 'public static string ' + name + '()',
        replacementMethods: ['public static string ' + name + '(string runId)'] }));
    row.sourceHashes.support = Object.fromEntries(dependencies.map(file => [file, sha256(m.io.readFileSync(path.join(m.root, file)))]));
    m.rows.get('unity-cd.json').push(row);
    const pin = m.flush();
    const production = sha256(Buffer.concat(['src/Current.cs', 'src/UnityObjectID.cs'].flatMap(file =>
        [Buffer.from(file), m.io.readFileSync(path.join(m.root, file))])));
    const context = () => ({ ...m.context(), productionFingerprint: production,
        scenarioReviewBindings: new Map(m.catalog.scenarios.map(s => [s.id, { current: true }])), gradientReview: { current: true } });
    const runId = '01234567-89ab-4cde-8012-3456789abcde';
    const input = { runId, file: path.join(m.project, 'Temp/WhimTex/test-runs/gradient-reload-' + runId, 'input.cs'), sources,
        sha256: sha256(currentBundleSources(sources, m.root, m.io)) };
    const payload = (status, checks, extra = {}) => ({ status, checks, message: 'native ' + status, failures: [], ...extra });
    const queued = { queued: true, requested: false, requests: 0, reloaded: false, oldDomainCleared: false, errors: [] };
    const terminal = { queued: false, requested: true, requests: 1, reloaded: true, oldDomainCleared: true, errors: [] };
    const native = { begin: payload('skipped', 0), trigger: payload('running', 0, queued),
        reload: payload('passed', 3, terminal), end: payload('passed', 5), cleanup: payload('passed', 1) };
    const idle = { projectPath: m.project, status: 'ready', compiling: false, domainReloadInProgress: false, playMode: 'stopped' };
    function call(phase, value) {
        const status = phase === 'editor_status', entry = 'WhimTexGradientReloadDiagnostic.' + phase;
        const file = path.relative(m.project, input.file).replaceAll('\\', '/');
        const argv = ['command', status ? phase : 'run_script'];
        if (!status) argv.push('--file', file, '--entry', entry, '--args', JSON.stringify([runId]), '--timeout_ms', '7000');
        argv.push('--project-path', m.project, '--timeout', '8', '--format', 'json');
        const envelope = { success: true, command: 'command ' + (status ? phase : 'run_script'), errors: [], data: {
            command: status ? phase : 'run_script', success: true, target: { projectPath: m.project },
            parameters: status ? {} : { file, entry, args: JSON.stringify([runId]), timeout_ms: 7000 },
            result: status ? value : { success: true, diagnostics: [], result: JSON.stringify(value) } } };
        return { phase, argv, budgetMs: 8000, durationMs: 10, reply: { code: 0, timedOut: false, stderr: '', stdout: JSON.stringify(envelope) } };
    }
    function report(time = '2026-01-01T00:00:00Z') {
        const fingerprint = currentReviewFingerprint([m.scenario], m.root, m.io);
        const current = { reviewed: currentReviewFingerprint([{ file: m.scenario.file, reviewFiles: dependencies.slice(1) }], m.root, m.io), production };
        const pending = payload('running', 0, queued);
        const testResult = { status: 'passed', checks: 28, message: 'workflow', failures: [], facts: {
            projectPath: m.project, input: structuredClone(input), fingerprint: { before: current, after: current, outer: fingerprint, outerProduction: production },
            cliEvidence: [call('editor_status', idle), call('Begin', native.begin), call('Trigger', native.trigger),
                call('editor_status', idle), call('ReloadPoll', pending),
                call('editor_status', { ...idle, compiling: true, status: 'compiling' }), call('editor_status', idle),
                call('ReloadPoll', native.reload), call('editor_status', idle), call('End', native.end), call('Cleanup', native.cleanup)],
            nativeTriggerRequests: 1, nativeResults: structuredClone(native), reloadStates: [pending, native.reload],
            editorAfterReload: idle, tempInputRemoved: true, cleanupCommandSuppressed: false } };
        const result = { id: m.scenario.id, file: m.scenario.file, runner: 'node', category: 'regression', status: 'passed', uncertain: false,
            testResult, attempts: [{ phase: 'result', entry: null, reply: { code: 0, timedOut: false, stdout: '', stderr: '' } }] };
        const r = { action: 'run', startedAt: time, finishedAt: new Date(Date.parse(time) + 1000).toISOString(), recoveryRequired: false,
            projectPath: m.project, selected: [m.scenario.id], fingerprint, productionFingerprint: production, results: [result] };
        sync(r); return r;
    }
    function sync(r) { r.results[0].attempts[0].reply.stdout = 'WHIMTEX_TEST_RESULT ' + JSON.stringify(r.results[0].testResult) + '\n'; }
    function editNative(r, phase, edit) {
        const c = r.results[0].testResult.facts.cliEvidence.find(c => c.phase === phase);
        const e = JSON.parse(c.reply.stdout); edit(e, c); c.reply.stdout = JSON.stringify(e);
    }
    function editTerminal(r, edit) {
        const c = r.results[0].testResult.facts.cliEvidence.filter(c => c.phase === 'ReloadPoll').at(-1);
        const e = JSON.parse(c.reply.stdout), value = JSON.parse(e.data.result.result);
        edit(value); e.data.result.result = JSON.stringify(value); c.reply.stdout = JSON.stringify(e);
    }
    const inspect = r => inspectGradientWorkflowPhases(r, r.results[0], context());
    const inventory = () => buildInventory({ ...m.options(), expectedManifestSha: pin });
    const save = (r, name = 'gradient.json') => m.put(path.join(m.project, 'Temp/WhimTex/test-runs', name), r);
    return { ...m, helpers, legacyFile, row, sources, dependencies, context, call, idle, report, sync, editNative, editTerminal, inspect, inventory, save };
}

test('Gradient helpers receive raw current same-GUID phase coverage, NEVER standalone/regression PASS credit', () => {
    const m = gradientPhaseMemory(), r = m.report();
    assert.equal(m.inspect(r).verified, true);
    m.save(r); const out = m.inventory(), f = out.files.find(f => f.legacyFile === m.legacyFile);
    assert.equal(out.summary.verifiedWorkflowHelperPhases, 2);
    assert.equal(out.summary.pendingWorkflowHelperPhases, 0);
    assert.equal(out.summary.currentRegressionReportFiles, 1);
    for (const helper of f.runtime.filter(i => i.phaseCoverage)) {
        assert.equal(helper.status, 'pending-no-current-runtime-proof');
        assert.equal(helper.phaseCoverage.status, 'current-workflow-phase-verified');
        assert.equal(helper.phaseCoverage.standalonePassCredited, false);
        assert.equal(helper.phaseCoverage.evidence.phases[0].status, 'skipped');
        assert.equal(helper.phaseCoverage.evidence.phases[0].checks, 0);
        assert.equal(helper.phaseCoverage.reportRawSha256, sha256(m.io.readFileSync(path.join(m.project, helper.phaseCoverage.report))));
    }
    assert.equal(out.archiveRemovalAllowed, false); assert.equal(out.automaticFullCoverage, false);
});

const gradientNegativeCases = [
    ['sidecar/metadata alone', (m, r) => { r.results[0].testResult.facts.cliEvidence = []; }],
    ['wrong typed GUID', (m, r) => m.editNative(r, 'End', e => { e.data.parameters.args = '[123]'; })],
    ['different End GUID', (m, r) => m.editNative(r, 'End', e => { e.data.parameters.args = '["ffffffff-ffff-ffff-ffff-ffffffffffff"]'; })],
    ['wrong entry', (m, r) => m.editNative(r, 'Begin', e => { e.data.parameters.entry = 'Other.Begin'; })],
    ['wrong argv', (m, r) => m.editNative(r, 'End', (e, c) => { c.argv[c.argv.indexOf('--args') + 1] = '[]'; })],
    ['wrong native input', (m, r) => m.editNative(r, 'End', e => { e.data.parameters.file = 'other.cs'; })],
    ['wrong project', (m, r) => m.editNative(r, 'End', e => { e.data.target.projectPath = path.join(m.project, 'other'); })],
    ['native transport timeout', (m, r) => m.editNative(r, 'End', (e, c) => { c.reply.timedOut = true; })],
    ['compile error', (m, r) => m.editNative(r, 'End', e => { e.data.result.diagnostics = [{ severity: 'Error' }]; })],
    ['Begin fake pass', (m, r) => m.editNative(r, 'Begin', e => { e.data.result.result = JSON.stringify({ status: 'passed', checks: 1, message: 'fake', failures: [] }); })],
    ['no real reload marker', (m, r) => m.editNative(r, 'ReloadPoll', e => { const v = JSON.parse(e.data.result.result); v.status = 'passed'; v.checks = 3; e.data.result.result = JSON.stringify(v); })],
    ['old AppDomain callback retained', (m, r) => m.editTerminal(r, v => { v.oldDomainCleared = false; })],
    ['two native requests', (m, r) => m.editTerminal(r, v => { v.requests = 2; })],
    ['no requested compilation', (m, r) => m.editTerminal(r, v => { v.requested = false; })],
    ['terminal native compiler error', (m, r) => m.editTerminal(r, v => { v.errors = ['CS actual error']; })],
    ['coerced native marker type', (m, r) => m.editTerminal(r, v => { v.reloaded = 'true'; })],
    ['native call during observed compilation', (m, r) => { r.results[0].testResult.facts.cliEvidence.splice(6, 1); }],
    ['repeated Trigger', (m, r) => { const calls = r.results[0].testResult.facts.cliEvidence; calls.splice(3, 0, structuredClone(calls[2])); }],
    ['no terminal reload', (m, r) => { const calls = r.results[0].testResult.facts.cliEvidence; calls.splice(7, 1); }],
    ['End before reload', (m, r) => { const calls = r.results[0].testResult.facts.cliEvidence; [calls[2], calls[9]] = [calls[9], calls[2]]; }],
    ['missing final Cleanup', (m, r) => { r.results[0].testResult.facts.cliEvidence.pop(); }],
    ['cleanup failure', (m, r) => m.editNative(r, 'Cleanup', e => { const v = JSON.parse(e.data.result.result); v.status = 'failed'; v.failures = ['actual cleanup failure']; e.data.result.result = JSON.stringify(v); })],
    ['native summary contradicts raw reply', (m, r) => { r.results[0].testResult.facts.nativeResults.end.checks = 5000; }],
    ['retained temporary input', (m, r) => { r.results[0].testResult.facts.tempInputRemoved = false; }],
    ['suppressed cleanup flag', (m, r) => { r.results[0].testResult.facts.cleanupCommandSuppressed = true; }],
    ['malformed cleanup flag', (m, r) => { r.results[0].testResult.facts.cleanupCommandSuppressed = ''; }],
    ['malformed native recovery flag', (m, r) => m.editNative(r, 'End', e => { const v = JSON.parse(e.data.result.result); v.recoveryRequired = ''; e.data.result.result = JSON.stringify(v); })],
    ['stale input SHA', (m, r) => { r.results[0].testResult.facts.input.sha256 = 'f'.repeat(64); }],
    ['reordered bundle sources', (m, r) => { r.results[0].testResult.facts.input.sources.reverse(); }],
    ['stale global selected receipt', (m, r) => { r.fingerprint = 'f'.repeat(64); }],
    ['stale within-workflow review', (m, r) => { r.results[0].testResult.facts.fingerprint.after.reviewed = 'f'.repeat(64); }],
    ['recovery debt', (m, r) => { r.recoveryRequired = true; }],
    ['duplicate wrapper result', (m, r) => { r.results.push(structuredClone(r.results[0])); }],
    ['incomplete report', (m, r) => { delete r.finishedAt; }],
    ['compile action', (m, r) => { r.action = 'compile'; }],
    ['missing End oracle assertion', (m, r) => m.editNative(r, 'End', e => { const v = JSON.parse(e.data.result.result); v.checks = 4; e.data.result.result = JSON.stringify(v); })]
];
for (const [name, mutate] of gradientNegativeCases) test('Gradient phase proof rejects ' + name, () => {
    const m = gradientPhaseMemory(), r = m.report();
    mutate(m, r); m.sync(r);
    assert.equal(m.inspect(r).verified, false, name);
    m.add('Tests~/CoverageAudit/gradient-reload-helper-phases.json', { verified: true, status: 'passed', checks: 100000 });
    m.save(r);
    assert.equal(m.inventory().summary.verifiedWorkflowHelperPhases, 0, 'No sidecar rescue: ' + name);
});

test('Gradient raw Node marker is mandatory; changing copied facts cannot promote helper phases', () => {
    const m = gradientPhaseMemory(), r = m.report();
    r.results[0].testResult.facts.nativeTriggerRequests = 20;
    assert.match(m.inspect(r).issues.join(' '), /raw Node result marker/);
    r.results[0].testResult.facts.nativeTriggerRequests = 1;
    r.results[0].attempts[0].reply.stdout += r.results[0].attempts[0].reply.stdout;
    assert.equal(m.inspect(r).verified, false);
});

test('Gradient omitted suppression flag matches native successful wire shape, but NEVER substitutes actual Cleanup', () => {
    const m = gradientPhaseMemory(), r = m.report();
    delete r.results[0].testResult.facts.cleanupCommandSuppressed; m.sync(r);
    assert.equal(m.inspect(r).verified, true);
    m.save(r); assert.equal(m.inventory().summary.verifiedWorkflowHelperPhases, 2);
    r.results[0].testResult.facts.cliEvidence.pop(); m.sync(r);
    assert.equal(m.inspect(r).verified, false);
});

test('Gradient exact post-trigger observed-compilation read-only reconnect is not native proof by itself', () => {
    const m = gradientPhaseMemory(), r = m.report(), calls = r.results[0].testResult.facts.cliEvidence;
    const network = m.call('editor_status', m.idle);
    network.reply = { code: 6, timedOut: false, stderr: '', stdout: JSON.stringify({ success: false, command: 'unity command editor_status', data: null,
        errors: [{ code: 'COMMAND_FAILED', message: "Failed to execute command 'editor_status': Network error: connection reset" }] }) };
    const timeout = m.call('editor_status', m.idle); timeout.reply = { code: null, timedOut: true, stdout: '', stderr: '' };
    calls.splice(6, 0, network, timeout); m.sync(r);
    assert.equal(m.inspect(r).verified, true);
    for (const mutate of [
        c => { c.splice(5, 1); }, // No raw observed compilation; a summary claim cannot grant retries.
        c => { c[7].reply.stderr = 'error'; },
        c => { c[7].reply.extra = true; },
        c => { const e = JSON.parse(c[6].reply.stdout); e.errors[0].message = 'generic network error'; c[6].reply.stdout = JSON.stringify(e); },
        c => { c.splice(6, 0, c.splice(7, 1)[0]); c.splice(5, 1); },
        c => { c.splice(10, 0, structuredClone(timeout)); } // After terminal marker, before End.
    ]) {
        const bad = structuredClone(r); mutate(bad.results[0].testResult.facts.cliEvidence); m.sync(bad);
        assert.equal(m.inspect(bad).verified, false);
    }
});

test('Gradient latest current FAIL/SKIP/uncertain/incomplete phases block earlier green, including timestamp ties', () => {
    for (const negative of ['skip', 'assertion-failed', 'timeout', 'incomplete', 'tie']) {
        const m = gradientPhaseMemory(), green = m.report(); m.save(green, 'z-green.json');
        const newer = m.report(negative === 'tie' ? green.startedAt : '2026-01-02T00:00:00Z');
        if (['incomplete', 'tie'].includes(negative)) newer.results[0].testResult.facts.cliEvidence.pop();
        else { newer.results[0].status = negative; newer.results[0].testResult.status = negative === 'skip' ? 'skipped' : 'failed'; }
        if (negative === 'timeout') newer.results[0].uncertain = true;
        m.sync(newer); m.save(newer, 'a-new.json');
        const out = m.inventory(), helpers = out.files[0].runtime.filter(i => i.phaseCoverage);
        assert.equal(out.summary.verifiedWorkflowHelperPhases, 0, negative);
        assert.ok(helpers.every(h => h.phaseCoverage.earlierGreenOverridden), negative);
        assert.ok(helpers.every(h => h.phaseCoverage.report.endsWith('a-new.json')), negative);
    }
});

test('Gradient phase credit needs complete current source review and unchanged paired catalog contract', () => {
    for (const mutate of [
        m => { m.row.sourceHashes.support[m.dependencies[0]] = 'f'.repeat(64); },
        m => { delete m.row.sourceHashes.support[m.dependencies[0]]; },
        m => { m.row.legacyEntries.pop(); },
        m => { m.row.gaps = ['Missing original assertion, even though pending runtime.']; },
        m => { m.row.reviewStatus = 'gap'; },
        m => { m.helpers[0].args = [123]; },
        m => { m.helpers[1].category = 'regression'; },
        m => { m.helpers[0].cleanup = null; },
        m => { m.scenario.reviewFiles.reverse(); },
        m => { m.add(m.sources[0], 'changed native body'); }
    ]) {
        const m = gradientPhaseMemory(), r = m.report(); mutate(m); m.flush(); m.save(r);
        assert.equal(m.inventory().summary.verifiedWorkflowHelperPhases, 0);
    }
});

test('Gradient source/report races revoke phase coverage; diagnostics and unrelated rows remain distinct', () => {
    for (const mode of ['source', 'report']) {
        const m = gradientPhaseMemory(), r = m.report(); m.save(r);
        const originalRead = m.io.readFileSync; let reads = 0;
        const target = mode === 'source' ? path.join(m.root, m.sources[0]) : path.join(m.project, 'Temp/WhimTex/test-runs/gradient.json');
        m.io.readFileSync = (file, encoding) => {
            if (path.resolve(file) === target && ++reads > 1) return encoding ? 'changed' : Buffer.from('changed');
            return originalRead(file, encoding);
        };
        const out = m.inventory(); assert.equal(out.summary.verifiedWorkflowHelperPhases, 0, mode);
        if (mode === 'source') assert.equal(out.summary.snapshotStable, false);
    }
    const m = gradientPhaseMemory(), r = m.report();
    const standalone = m.nativeResult('passed', m.helpers[1]);
    const e = JSON.parse(standalone.attempts[0].reply.stdout); e.data.parameters.args = '["owned-run"]';
    standalone.attempts[0].reply.stdout = JSON.stringify(e);
    standalone.attempts.push({ ...structuredClone(standalone.attempts[0]), phase: 'cleanup', entry: m.helpers[1].cleanup.entry });
    const cleanup = JSON.parse(standalone.attempts[1].reply.stdout); cleanup.data.parameters.entry = m.helpers[1].cleanup.entry;
    standalone.attempts[1].reply.stdout = JSON.stringify(cleanup);
    r.results.push(standalone); r.selected.push(standalone.id); r.fingerprint = currentReviewFingerprint([m.scenario, m.helpers[1]], m.root, m.io);
    r.inputs = [{ id: standalone.id, file: path.join(m.project, 'Temp/WhimTex/test-runs/input-owned.cs'),
        sources: m.sources, sha256: sha256(currentBundleSources(m.sources, m.root, m.io)) }];
    r.results[0].testResult.facts.fingerprint.outer = r.fingerprint; m.sync(r); m.save(r);
    const out = m.inventory(), end = out.files[0].runtime.find(i => i.id === standalone.id);
    assert.equal(end.status, 'diagnostic-only'); assert.equal(end.phaseCoverage.verified, true);
    assert.equal(end.phaseCoverage.standalonePassCredited, false);
    assert.equal(out.summary.currentRegressionReportFiles, 1);
});
