// Pure in-memory filesystem/reports. No Unity, runner import, subprocess, or disk writes.
import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import { auditNames, sha256, verifyFrozenLegacy, currentBundleSources, currentReviewFingerprint,
    splitGap, metadataGapEvidence, inspectInvocation, inspectRuntimeReport, chooseNewestEvidence, buildInventory } from './coverage-gate.mjs';

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
        'Tests~/Framework/TestApi.cs', 'Tests~/Framework/EntryContract.cs', 'Tests~/migration.json', 'Tests~/scripts/check-migration.mjs']) add(file, 'receipt-' + file);
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
