// Read-only aggregate of retained evidence. Never runs tests, Unity, cleanup or deletion.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { root, reviewFingerprint, productionFingerprint, validateCatalog } from '../../Packages/com.dcfapixels.whimtex/Tests~/scripts/run-tests.mjs';
import { archiveMetadata, openLegacyArchive } from '../../Packages/com.dcfapixels.whimtex/Tests~/scripts/legacy.mjs';
import { buildInventory } from '../../Packages/com.dcfapixels.whimtex/Tests~/CoverageAudit/coverage-gate.mjs';

const project = path.resolve(root, '../..'), temp = path.join(project, 'Temp/WhimTex'), directory = path.join(temp, 'test-runs');
if (project !== path.resolve('D:/DCFA/Projects/Test6.6')) throw Error('Wrong project.');
const args = process.argv.slice(2), output = args.length ? path.resolve(args[1] ?? '') : null;
const outputPaths=['Tests~/ArchiveRetirementBeforeDeletion20261005.results.json','Tests~/ArchiveRetirementCompletion20261005.results.json'].map(file=>path.join(root,file));
if (args.length && (args.length !== 2 || args[0] !== '--output' || !outputPaths.includes(output))) throw Error('Only --output with an exact new retirement results path is allowed.');
const hash = bytes => createHash('sha256').update(bytes).digest('hex'), inputs = new Map();
const stat = file => { try { return fs.lstatSync(file); } catch (error) { if (error.code === 'ENOENT') return null; throw error; } };
const read = file => { const info = stat(file); if (!info?.isFile() || info.isSymbolicLink()) throw Error('Not a regular evidence file: ' + file); const bytes = fs.readFileSync(file), sha256 = hash(bytes); if (inputs.has(file) && inputs.get(file) !== sha256) throw Error('Evidence changed while aggregating: ' + file); inputs.set(file, sha256); return bytes; };
const json = file => JSON.parse(read(file)), artifact = file => ({ file, sha256: inputs.get(file) });
const optional = file => { if (!stat(file)) return { file, sha256: null, value: null }; const value = json(file); return { ...artifact(file), value }; };
const catalog = validateCatalog(json(path.join(root, 'Tests~/scripts/test-catalog.json'))), production = productionFingerprint();
const byId = new Map(catalog.scenarios.map(s => [s.id, s])), reports = [], events = new Map(), reportValidationIssues = [];
const reportNames = () => fs.readdirSync(directory).filter(name => /^final-20261005-.*\.json$/.test(name) && !name.endsWith('.provenance.json') || name === '2026-10-05T05-13-51.876Z-145576.json').sort();
const names = reportNames();
for (const name of names) {
    const file = path.join(directory, name), report = json(file), selected = (report.selected ?? []).map(id => byId.get(id));
    const shape = report.version === 2 && selected.length > 0 && selected.every(Boolean) && new Set(report.selected).size === selected.length && Array.isArray(report.results)
        && new Set(report.results.map(r => r.id)).size === report.results.length && report.results.every(r => report.selected.includes(r.id) && r.category === byId.get(r.id)?.category)
        && typeof report.projectPath === 'string' && path.resolve(report.projectPath) === project && Number.isFinite(Date.parse(report.startedAt)) && Number.isFinite(Date.parse(report.finishedAt));
    const currentSelectedReviewFingerprint = selected.length && selected.every(Boolean) ? reviewFingerprint(selected) : null;
    const current = shape && report.fingerprint === currentSelectedReviewFingerprint && report.productionFingerprint === production;
    if (!shape) reportValidationIssues.push({ file, issue: 'Malformed or unfinished runner receipt; retained without current credit.' });
    const receipt = { ...artifact(file), startedAt: report.startedAt, finishedAt: report.finishedAt, action: report.action, current,
        fingerprint: report.fingerprint, currentSelectedReviewFingerprint, productionFingerprint: report.productionFingerprint,
        success: report.success, recoveryRequired: report.recoveryRequired, selected: report.selected, notRun: report.notRun ?? [], error: report.error ?? null };
    reports.push(receipt);
    for (const row of report.results ?? []) {
        const event = { ...receipt, id: row.id, category: row.category, status: row.status, uncertain: row.uncertain === true,
            recoveryRequired: report.recoveryRequired !== false || row.recoveryRequired === true || row.testResult?.recoveryRequired === true,
            checks: row.testResult?.checks ?? null, detail: row.detail, failures: row.testResult?.failures ?? [], durationMs: row.durationMs, testResult: row.testResult ?? null };
        if (!events.has(row.id)) events.set(row.id, []);
        events.get(row.id).push(event);
    }
}
const latest = new Map(), cleanPass = e => !!e && e.status === 'passed' && !e.uncertain && !e.recoveryRequired && e.testResult?.status === 'passed' && Number.isSafeInteger(e.checks) && e.checks > 0 && Array.isArray(e.testResult.failures) && e.failures.length === 0;
const scenarios = catalog.scenarios.map(s => {
    const history = (events.get(s.id) ?? []).sort((a, b) => Date.parse(a.startedAt) - Date.parse(b.startedAt) || Date.parse(a.finishedAt) - Date.parse(b.finishedAt) || a.file.localeCompare(b.file));
    const chosen = history.filter(e => e.current && e.action === 'run').at(-1); latest.set(s.id, chosen);
    return { id: s.id, category: s.category, file: s.file, status: chosen?.status ?? 'not-run-in-final-pass', passed: cleanPass(chosen),
        report: chosen?.file ?? null, reportSha256: chosen?.sha256 ?? null, checks: chosen?.checks ?? null, uncertain: chosen?.uncertain ?? false, detail: chosen?.detail ?? null, failures: chosen?.failures ?? [],
        history: history.map(({ testResult, ...event }) => event) };
});
const expected = { regression: 402, 'source-guard': 56, runner: 12, diagnostic: 50 }, categories = {};
for (const [category, count] of Object.entries(expected)) {
    const rows = scenarios.filter(s => s.category === category);
    categories[category] = { total: rows.length, expected: count, passed: rows.filter(s => s.passed).length,
        notRun: rows.filter(s => s.status === 'not-run-in-final-pass').map(s => s.id), skipped: rows.filter(s => ['skip', 'skipped'].includes(s.status)).map(s => s.id),
        nonPassing: rows.filter(s => !s.passed).map(s => s.id), excludedFromCompletionGate: category === 'diagnostic' };
}
const archive = archiveMetadata(), physical = path.join(root, archive.descriptor.path), pinned = openLegacyArchive();
for (const entry of archive.manifest.files) { const bytes = pinned.read(entry.file); if (bytes.length !== entry.bytes || hash(bytes) !== entry.sha256) throw Error('Pinned bytes differ: ' + entry.file); }
function inspectTree(base) {
    const present = stat(base) !== null, issues = [], seen = new Set(), expectedFiles = new Map(archive.manifest.files.map(e => [e.file, e]));
    function visit(file, relative) {
        const info = stat(file);
        if (!info || info.isSymbolicLink()) { issues.push({ file: relative, issue: info ? 'symlink' : 'disappeared' }); return; }
        if (info.isDirectory()) { for (const name of fs.readdirSync(file).sort()) visit(path.join(file, name), relative ? relative + '/' + name : name); return; }
        if (!info.isFile()) { issues.push({ file: relative, issue: 'not-regular' }); return; }
        seen.add(relative); const entry = expectedFiles.get(relative);
        if (!entry) { issues.push({ file: relative, issue: 'extra-file' }); return; }
        const bytes = read(file); if (bytes.length !== entry.bytes || hash(bytes) !== entry.sha256) issues.push({ file: relative, issue: 'bytes-differ' });
    }
    if (present) { if (!stat(base).isDirectory()) issues.push({ file: '', issue: 'root-not-directory' }); visit(base, ''); for (const entry of archive.manifest.files) if (!seen.has(entry.file)) issues.push({ file: entry.file, issue: 'missing' }); }
    return { path: base, present, state: present ? 'present' : 'removed', regularFiles: seen.size, issues,
        note: present ? 'Exact frozen file set checked, including extra files and symlinks.' : 'Physical archive removed; recovery bytes authenticated from pinned Git; no missing-file errors.' };
}
const physicalArchive = inspectTree(physical), backup = optional(path.join(temp, 'legacy-retirement-backup-20261005.json'));
if (backup.value) {
    const value = backup.value, sorted = rows => [...rows].sort((a, b) => a.file.localeCompare(b.file)).map(({ file, bytes, sha256 }) => ({ file, bytes, sha256 }));
    const backupPath = typeof value.backup === 'string' ? path.resolve(value.backup) : '', rel = path.relative(temp, backupPath);
    backup.manifestMatches = value.verified === true && value.pinnedCommit === archive.descriptor.commit && value.source === physical && Array.isArray(value.files)
        && JSON.stringify(sorted(value.files)) === JSON.stringify(sorted(archive.manifest.files)) && rel.startsWith('legacy-retirement-backup-20261005-') && !rel.includes(path.sep);
    backup.tree = backup.manifestMatches ? inspectTree(backupPath) : null;
    backup.current = backup.manifestMatches && backup.tree.present && backup.tree.regularFiles === 466 && backup.tree.issues.length === 0;
}
const inventory = buildInventory({ projectPath: project }), settingsFile = path.join(temp, 'final-run-20261005.project-settings-before.json'), settings = json(settingsFile);
const sourceRuntime = new Map(inventory.files.flatMap(f => f.runtime).map(r => [r.id, r])), gateRuntime = {};
for (const category of ['regression', 'source-guard']) gateRuntime[category] = catalog.scenarios.filter(s => s.category === category).reduce((counts, s) => { const status = sourceRuntime.get(s.id)?.status ?? 'not-linked-to-original-source'; counts[status] = (counts[status] ?? 0) + 1; return counts; }, {});
const settingsChanges = settings.files.flatMap(e => { const file = path.join(project, e.file), actual = stat(file) ? hash(read(file)) : null; return actual === e.sha256 ? [] : [{ file: e.file, expected: e.sha256, actual }]; });
const settingsValid = inputs.get(settingsFile) === 'c6b3dbfe638566dcb47afe834ca457abab45de34781bf825b55ab63607d60fa9' && settings.files.length === 31 && settingsChanges.length === 0;
const verifierFile = path.join(directory, 'final-20261005-verifier-03.tap'), provenance = optional(verifierFile + '.provenance.json'), tapBytes = stat(verifierFile) ? read(verifierFile) : Buffer.alloc(0), tap = tapBytes.toString('utf8');
const count = key => Number(new RegExp('^# ' + key + ' ([0-9]+)\\r?$', 'm').exec(tap)?.[1] ?? NaN);
const dependencyPins = {
    'Tests~/CoverageAudit/coverage-gate.test.mjs': '1197813cd7946bd2144353f0f7404c82de63d2f23577e1f0f8afbcf4948b3ec5',
    'Tests~/CoverageAudit/reviewed-oracle-image-sourceguard.test.mjs': '9d7b172d2631a6f0f3798b9669d46cc79cc0cb29a705942f4912771ca2091e28',
    'Tests~/CoverageAudit/coverage-gate.mjs': '2f1957eb2949656e1470e4bb82483b60f3f14d2c25aba69882389181b53793b8',
    'Tests~/scripts/legacy.mjs': 'ae299fac143a7b5ab518dd13706e134f48c7deafa9a0fecee938c42e5d6c654d',
    'Tests~/Cases/UnityC/ReviewedOracle.cs': '0a7383618feea3c2a4ba780738bbc0e3831ef78b7199ea0887f4ca4f71dc68e5',
    'Tests~/Cases/UnityC/UnityCFixture.cs': '11fed83b70eebdf4cba08e3f6bed0f97f360b0720a72291d4c479dbaf134be7d'
};
const dependencyChanges = Object.entries(dependencyPins).flatMap(([file, sha256]) => hash(read(path.join(root, file))) === sha256 ? [] : [{ file, expected: sha256, actual: inputs.get(path.join(root, file)) }]);
const reviewFile = path.join(root, 'Tests~/CoverageAudit/retirement-source-review.json'), priorReview = json(reviewFile), pv = provenance.value;
const argv = ['--test', '--test-reporter=tap', '--test-isolation=none', 'Tests~/CoverageAudit/coverage-gate.test.mjs', 'Tests~/CoverageAudit/reviewed-oracle-image-sourceguard.test.mjs'];
const provenanceValid = pv?.version === 1 && provenance.sha256 === '71da20091fcaa4d55bba6cc03feeedfa41b7967493b5f5a5c0b524121aa0b921'
    && pv.cwd === root && pv.output === verifierFile && pv.exitCode === 0 && pv.error === null && pv.stderr === '' && pv.outputSha256 === hash(tapBytes)
    && JSON.stringify(pv.argv) === JSON.stringify(argv) && JSON.stringify(pv.sourceHashes) === JSON.stringify(argv.slice(3).map(file => ({ file, sha256: dependencyPins[file] })));
const priorReviewValid = inputs.get(reviewFile) === 'cc77a72756224f6fc680cdb183232f44297533f3b6fc986b5f9c30e5ae20b3db'
    && ['Tests~/CoverageAudit/coverage-gate.mjs', 'Tests~/scripts/legacy.mjs'].every(file => priorReview.sourceHashes[file] === dependencyPins[file]);
const pureVerifier = { file: verifierFile, sha256: hash(tapBytes), provenance, argv: pv?.argv ?? null, dependencyPins, dependencyChanges, priorReview: artifact(reviewFile),
    scope: 'Fresh verifier-03 TAP. Producer provenance records the two test files; their imported/read dependencies are separately pinned here, including prior-reviewed verifier/archive code. Verifier-01/02 get no completion credit.',
    tests: count('tests'), passed: count('pass'), failed: count('fail'), skipped: count('skipped'), cancelled: count('cancelled'), todo: count('todo'),
    history: [optional(path.join(root, 'Tests~/FinalRun20261005.results.json')), optional(path.join(directory, 'final-20261005-verifier-02.tap.provenance.json'))].map(({ value, ...receipt }) => ({ ...receipt, pureVerifier: value?.pureVerifier, recordedArgv: value?.argv, recordedOutputSha256: value?.outputSha256, completionCredit: false })) };
pureVerifier.current = provenanceValid && priorReviewValid && dependencyChanges.length === 0 && /^TAP version 13\r?$/m.test(tap) && /^1\.\.81\r?$/m.test(tap)
    && pureVerifier.tests === 81 && pureVerifier.passed === 81 && ['failed', 'skipped', 'cancelled', 'todo'].every(key => pureVerifier[key] === 0);
const guard = latest.get('final-legacy-audit-v2'), guardCases = guard?.testResult?.cases ?? [], guardNames = [
    'Fresh audit import never reads the physical archive', 'Retired tests and production helper removal', 'Soft range ACTIVE entry replaces former Main inference', 'Two-choice ACTIVE async entry replaces former Main inference',
    ...['CanvasViewFooterSmoke.cs', 'ContentFillUiSmoke.cs', 'UvUiSmoke.cs'].map(name => name + ': ACTIVE self-contained async UI regression'),
    ...['GradientClipboardCleanupSmoke.cs', 'RemainingLegacyCleanupSmoke.cs', 'UserSettingsCleanupSmoke.cs'].map(name => name + ': explicit ACTIVE regression classification'),
    'Shared Gaussian kernel capacity and upload paths', 'Original audit assertions on exact historical text inputs and current production',
    'Exact class/method regex grammar, Main preference and classification precedence', 'Candidate regex, exact word counts, recursive extensions and documentation site exclusion',
    'CLI output, full summary, slicing, numeric coercion and error semantics', 'Standalone CLI body/guard/output/errors in an isolated read-only JavaScript context'];
const isolationProbe = { report: guard?.file ?? null, sha256: guard?.sha256 ?? null, checks: guard?.checks ?? null, cases: guardCases, facts: guard?.testResult?.facts ?? null,
    current: cleanPass(guard) && guardCases.length === 16 && new Set(guardCases.map(c => c.name)).size === 16 && guardNames.every(name => guardCases.some(c => c.name === name && c.status === 'passed')),
    historicalFailure: optional(path.join(temp, 'final-run-20261005.isolation-check.json')) };
const ownedRunId = '4817f7407dac483b94141d9e81bb6b17', ownedAssetFolder = path.join(project, 'Assets/WhimTexTestMigration', ownedRunId), ownedEvidenceFolder = path.join(temp, 'player-release', ownedRunId);
const journal = optional(path.join(ownedEvidenceFolder, 'state.json')), cleanupCompile = optional(path.join(ownedEvidenceFolder, 'cleanup-compile.json')), runnerLockExists = stat(path.join(directory, 'runner.lock')) !== null;
const nativeRecovery = { ownedRunId, ownedAssetFolder, ownedEvidenceFolder, journal, cleanupCompile, runnerLockExists,
    ownedFolderAbsent: !stat(ownedAssetFolder) && !stat(ownedAssetFolder + '.meta'), originalWorkflowPassed: false, scope: 'Recovery only; fresh Player/Fault PASS must come from current runner receipts.' };
const recoveryDirectory = path.join(ownedEvidenceFolder, 'attempt-001/recovery-20261005-cancel'), continuation = path.join(recoveryDirectory, 'continuation-existing-assets');
nativeRecovery.readyReceipt = optional(path.join(continuation, 'ready-to-release.json'));
nativeRecovery.lockReleaseReceipt = optional(path.join(continuation, 'lock-release-authorization.json'));
nativeRecovery.historicalFailure = optional(path.join(recoveryDirectory, 'recovery-failure.json'));
const recoveryProof = nativeRecovery.readyReceipt.value, releaseProof = nativeRecovery.lockReleaseReceipt.value, oldFolder = 'Assets/WhimTexTestMigration/' + ownedRunId;
const cachedPaths = [oldFolder, oldFolder + '/WhimTexMigrationPlayerProbe.cs', oldFolder + '/Probe.unity', oldFolder + '/Resources/WhimTexPlayer_' + ownedRunId + '/Document.tiff'];
nativeRecovery.continuationVerified = recoveryProof?.version === 1 && recoveryProof.runId === ownedRunId && recoveryProof.recoveryProofComplete === true && recoveryProof.originalWorkflowPassed === false
    && ['cleanup', 'verify'].every(k => recoveryProof[k]?.runId === ownedRunId && recoveryProof[k].status === 'passed' && recoveryProof[k].phase === 'cleaned' && recoveryProof[k].recoveryRequired === false && recoveryProof[k].failures?.length === 0)
    && recoveryProof.verify.nativeCompilationComplete === true && recoveryProof.verify.oldDomainGone === true && recoveryProof.verify.beforeReload > 0
    && ['runId', 'kind', 'compilationStarted', 'compilationFinished', 'beforeReload'].every(k => recoveryProof.compile?.[k] === cleanupCompile.value?.[k]) && recoveryProof.compile.errors?.length === 0
    && recoveryProof.absence?.runId === ownedRunId && ['probeClassAbsent', 'folderAbsent', 'metaAbsent', 'sceneAbsent', 'probeAssetAbsent'].every(k => recoveryProof.absence[k] === true) && recoveryProof.absence.assembliesScanned > 0
    && JSON.stringify(recoveryProof.absence.cachedGuidPaths) === JSON.stringify(cachedPaths) && JSON.stringify(recoveryProof.absence.existingPathGuids) === JSON.stringify(['', '', '', '']) && JSON.stringify(recoveryProof.absence.cachedPathsHaveExistingAssets) === JSON.stringify([false, false, false, false])
    && releaseProof?.runId === ownedRunId && releaseProof.nativeCompilationComplete === true && releaseProof.ownedProbeAndAssetsAbsent === true && releaseProof.originalWorkflowPassed === false && JSON.stringify(releaseProof.retainedGuidCachePaths) === JSON.stringify(cachedPaths);
nativeRecovery.complete = !runnerLockExists && nativeRecovery.ownedFolderAbsent && journal.value?.version === 1 && journal.value?.runId === ownedRunId && journal.value?.folder === 'Assets/WhimTexTestMigration/' + ownedRunId && journal.value?.phase === 'cleaned'
    && cleanupCompile.value?.version === 1 && cleanupCompile.value?.runId === ownedRunId && cleanupCompile.value?.kind === 'cleanup' && ['compilationStarted', 'compilationFinished', 'beforeReload'].every(k => Number.isSafeInteger(cleanupCompile.value[k]) && cleanupCompile.value[k] > 0)
    && Array.isArray(cleanupCompile.value.errors) && cleanupCompile.value.errors.length === 0 && (!(recoveryProof || releaseProof) || nativeRecovery.continuationVerified);
const nativeFresh = ['player-release-workflow-v2', 'fault-release-workflow-v2'].map(id => { const e = latest.get(id); return { id, report: e?.file ?? null, sha256: e?.sha256 ?? null, passed: cleanPass(e), recoveryRequired: e?.recoveryRequired ?? null }; });
const documentRecoveryId='0162673e3749421e9ada097bfdba2a40';
const documentRecoveryDirectory=path.join(temp,'document-reload-recovery-'+documentRecoveryId);
const documentRecoveryReady=optional(path.join(documentRecoveryDirectory,'ready-to-release.json'));
const documentRecoveryRelease=optional(path.join(documentRecoveryDirectory,'lock-release-authorization.json'));
const documentRecoveryProof=documentRecoveryReady.value;
const documentRecovery={ready:documentRecoveryReady,release:documentRecoveryRelease,originalWorkflowPassed:false};
documentRecovery.complete=documentRecoveryProof?.runId===documentRecoveryId&&documentRecoveryProof.recoveryComplete===true
    &&documentRecoveryProof.originalWorkflowPassed===false&&documentRecoveryProof.poll?.status==='passed'
    &&documentRecoveryProof.poll.requestCount===1&&documentRecoveryProof.poll.beforeReload===1&&documentRecoveryProof.poll.oldDomainGone===true
    &&documentRecoveryProof.poll.failures?.length===0&&documentRecoveryProof.cleanup?.status==='passed'&&documentRecoveryProof.cleanup.failures?.length===0
    &&documentRecoveryProof.absence?.runId===documentRecoveryId&&documentRecoveryProof.absence.ownedStateAbsent===true
    &&documentRecoveryRelease.value?.runId===documentRecoveryId&&documentRecoveryRelease.value?.ownedStateAbsent===true
    &&!stat(path.join(project,'Assets/WhimTexTestMigration',documentRecoveryId))
    &&!stat(path.join(project,'Assets/WhimTexTestMigration',documentRecoveryId)+'.meta')
    &&!stat(path.join(directory,'unity-b-reload-'+documentRecoveryId+'.cs'));
const isolationId='8455f557201b4988a18773741448050d';
const sceneJournal=optional(path.join(temp,'player-scene-isolation-'+isolationId+'.json'));
const sceneRestore=optional(path.join(temp,'player-scene-isolation-evidence-'+isolationId,'Restore/complete.json'));
const sceneRestoration={journal:sceneJournal,restore:sceneRestore,scope:'Explicitly approved SampleScene save/temporary close/restore; no coverage credit'};
sceneRestoration.complete=sceneJournal.value?.id===isolationId&&sceneJournal.value?.phase==='restored'
    &&sceneRestore.value?.action==='Restore'&&sceneRestore.value?.result?.id===isolationId&&sceneRestore.value?.result?.phase==='restored'
    &&sceneRestore.value?.sourceHash===hash(read(path.join(temp,'PlayerSceneIsolation.cs')))
    &&!stat(path.join(project,'Assets/WhimTexTestMigration',isolationId))&&!stat(path.join(project,'Assets/WhimTexTestMigration',isolationId)+'.meta');
const documentFresh=latest.get('document-reload-smoke-v2');
const documentFullBranch=cleanPass(documentFresh)&&documentFresh.testResult?.facts?.documentReloadCoverage?.branch==='surviving-window'
    &&documentFresh.testResult?.facts?.documentReloadCoverage?.partial===false;
const archiveSnapshotStable = JSON.stringify(inspectTree(physical)) === JSON.stringify(physicalArchive) && (!backup.tree || JSON.stringify(inspectTree(backup.tree.path)) === JSON.stringify(backup.tree));
const nativeSnapshotStable = runnerLockExists === (stat(path.join(directory, 'runner.lock')) !== null) && nativeRecovery.ownedFolderAbsent === (!stat(ownedAssetFolder) && !stat(ownedAssetFolder + '.meta'));
const snapshotChanges = [...inputs].flatMap(([file, sha256]) => stat(file)?.isFile() && hash(fs.readFileSync(file)) === sha256 ? [] : [{ file, issue: 'Evidence changed while aggregating.' }]);
const sourceSnapshotStable = productionFingerprint() === production && reports.every(r => !r.current || r.currentSelectedReviewFingerprint === reviewFingerprint(r.selected.map(id => byId.get(id))));
const evidenceSnapshotStable = snapshotChanges.length === 0 && sourceSnapshotStable && archiveSnapshotStable && nativeSnapshotStable && JSON.stringify(names) === JSON.stringify(reportNames());
const gates = { requiredCategories: ['regression', 'source-guard', 'runner'].every(c => categories[c].total === expected[c] && categories[c].passed === expected[c]),
    diagnosticsExcluded: categories.diagnostic.total === 50, sourceReview: inventory.summary.snapshotStable && inventory.summary.expectedSourceCountMatches360 && inventory.summary.currentReviewedSourceClaims === 360 && inventory.summary.sourceGapFiles === 0 && inventory.summary.externalGapFiles === 0,
    pureVerifier: pureVerifier.current, isolationProbe: isolationProbe.current, nativeRecovery: nativeRecovery.complete, nativeFresh: nativeFresh.every(r => r.passed && r.recoveryRequired === false),
    documentRecovery:documentRecovery.complete,documentFullBranch,sceneRestoration:sceneRestoration.complete,
    projectSettings: settingsValid, archive: pinned.summary.files === 466 && (!physicalArchive.present || physicalArchive.regularFiles === 466 && physicalArchive.issues.length === 0),
    backup: backup.value !== null && backup.current === true, evidenceSnapshotStable, reportReceipts: reportValidationIssues.length === 0 };
const finalSuiteComplete = Object.values(gates).every(Boolean), result = { version: 1, generatedAt: new Date().toISOString(),
    scope: 'Current selected final-run receipts plus standard CI 145576; 50 diagnostics excluded. Source claims and runtime PASS are separate; no automatic equivalence or deletion authority.',
    productionFingerprint: production, categories, reports, scenarios, sourceReview: inventory.summary, gateRuntime, sourceIssues: inventory.sourceIssues, reportIssues: inventory.reportIssues, reportValidationIssues,
    workflowHelperPhases: inventory.files.flatMap(f => f.runtime).filter(r => r.phaseCoverage).map(r => ({ id: r.id, ...r.phaseCoverage })),
    archive: { descriptor: archive.descriptor, authenticatePinnedGit: { ...pinned.summary, bytesChecked: 466 }, physicalArchive, backup },
    projectSettings: { ...artifact(settingsFile), checkedFiles: settings.files.length, changes: settingsChanges, authorizedSampleSceneSave: { path: 'Assets/Scenes/SampleScene.unity', scope: 'Explicit user-authorized save, outside the settings baseline; no scene hash preservation claim.' } },
    pureVerifier, isolationProbe, nativeRecovery, nativeFresh, documentRecovery,sceneRestoration,gates, snapshotIssues: [...inventory.snapshotChanges, ...snapshotChanges], evidenceSnapshotStable, finalSuiteComplete,
    readyForAuthorizedPhysicalDeletion: finalSuiteComplete && physicalArchive.present, physicalArchiveRemoved: !physicalArchive.present,
    runnerLockExists, archiveRemovalAllowed: false, archiveRemovalAuthorizedByThisReport: false, commitOrPushPerformed: false };
if (output) fs.writeFileSync(output, JSON.stringify(result, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ output, finalSuiteComplete, readyForAuthorizedPhysicalDeletion: result.readyForAuthorizedPhysicalDeletion, gates,
    categories: Object.fromEntries(Object.entries(categories).map(([name, c]) => [name, { total: c.total, expected: c.expected, passed: c.passed, nonPassing: c.nonPassing.length, notRun: c.notRun.length, skipped: c.skipped.length, excludedFromCompletionGate: c.excludedFromCompletionGate }])),
    sourceReview: inventory.summary, gateRuntime, reports: { total: reports.length, current: reports.filter(r => r.current).length, staleOrUnfinished: reports.filter(r => !r.current).length, validationIssues: reportValidationIssues.length },
    archive: { pinnedFiles: pinned.summary.files, physicalState: physicalArchive.state, physicalFiles: physicalArchive.regularFiles, issues: physicalArchive.issues.length, backupVerified: backup.current === true },
    projectSettings: { checkedFiles: settings.files.length, changes: settingsChanges.length }, pureVerifier: { file: verifierFile, sha256: pureVerifier.sha256, provenanceSha256: provenance.sha256, current: pureVerifier.current, tests: pureVerifier.tests, passed: pureVerifier.passed, failed: pureVerifier.failed, dependencyChanges: dependencyChanges.length },
    isolationProbe: { current: isolationProbe.current, report: isolationProbe.report, cases: guardCases.length, checks: isolationProbe.checks }, nativeRecovery: { complete: nativeRecovery.complete, continuationVerified: nativeRecovery.continuationVerified, runnerLockExists }, nativeFresh,
    snapshotIssueCount: result.snapshotIssues.length, archiveRemovalAllowed: false }, null, 2));
process.exitCode = finalSuiteComplete ? 0 : 1;
