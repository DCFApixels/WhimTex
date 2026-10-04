// Read-only final snapshot emitter. The parent saves its output with apply_patch.
// Source review, successful execution and full equivalence remain distinct.
import fs from 'node:fs';
import path from 'node:path';
import { buildInventory, sha256, defaultRoot } from './coverage-gate.mjs';
import { productionFingerprint } from '../scripts/run-tests.mjs';

export function finalInventory() {
    const root = defaultRoot, project = path.resolve(root, '../..');
    const gate = buildInventory({ root, projectPath: project });
    const catalog = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/scripts/test-catalog.json')));
    const manifestBytes = fs.readFileSync(path.join(root, 'Tests~/legacy-manifest.json'));
    if (sha256(manifestBytes) !== gate.archive.manifestRawSha256) throw Error('Frozen manifest changed during snapshot.');
    const archive = new Map(JSON.parse(manifestBytes).files.map(f => [f.file, f.sha256]));
    const runtime = new Map();
    for (const f of gate.files) for (const r of f.runtime) runtime.set(r.id, r);
    const scenarios = catalog.scenarios.filter(s => !s.legacy);
    const latestReports = new Set([...runtime.values()].map(r => r.latest?.report).filter(Boolean));
    const reports = [...latestReports].sort().map(file => ({ file,
        sha256: sha256(fs.readFileSync(path.join(project, file))) }));
    const reportIndex = new Map(reports.map((r, i) => [r.file, i]));
    const count = values => Object.fromEntries([...new Set(values)].sort().map(v => [v, values.filter(x => x === v).length]));
    const actualProduction = productionFingerprint(root);
    return {
        version: 2, generatedAt: new Date().toISOString(),
        scope: 'Final source-by-source Legacy inventory plus current native/Node evidence. Diagnostic outputs and mapping counts are not regression equivalence.',
        production: { current: actualProduction,
            expectedUnchanged: 'a3da14871f6e0bf5c49a6f077b1026720e0fb2d3c28a49213dd715f3dd2b7738',
            unchanged: actualProduction === 'a3da14871f6e0bf5c49a6f077b1026720e0fb2d3c28a49213dd715f3dd2b7738' },
        archive: { files: gate.archive.files, verified: gate.archive.verified,
            manifestRawSha256: gate.archive.manifestRawSha256, issues: gate.archive.issues },
        summary: { ...gate.summary, independentScenarios: scenarios.length,
            categories: count(scenarios.map(s => s.category)),
            categoryRuntime: Object.fromEntries([...new Set(scenarios.map(s => s.category))].sort().map(category =>
                [category, count(scenarios.filter(s => s.category === category).map(s => runtime.get(s.id)?.status ?? 'not-linked-to-original-source'))])),
            linkedRuntimeVerdicts: count([...runtime.values()].map(r => r.status)) },
        auditInputs: gate.auditInputs,
        fileColumns: ['legacyFile', 'legacyRawSha256', 'currentSourceReview', 'runtimeStatus', 'scenarioIds'],
        files: gate.files.filter(f => f.sourceFile).map(f =>
            [f.legacyFile, archive.get(f.legacyFile), f.sourceReviewStatus, f.runtimeStatus, f.scenarioIds]),
        scenarioColumns: ['id', 'category', 'currentRuntimeStatus', 'latestResult', 'reportIndex', 'proof', 'inputSha256', 'invocationVerified', 'receiptMatches'],
        scenarios: scenarios.map(s => {
            const r = runtime.get(s.id), e = r?.latest;
            return [s.id, s.category, r?.status ?? 'not-linked-to-original-source', e?.status ?? null,
                e ? reportIndex.get(e.report) : null, e?.proof ?? null,
                e?.input?.recordedSha256 ?? null, e?.invocation?.verified ?? null, e?.receiptMatches ?? null];
        }),
        reports,
        issues: gate.files.filter(f => f.sourceGaps.length || f.externalGaps.length || f.pending.length || f.stale.length || f.missing.length ||
            f.runtime.some(r => r.status === 'current-not-passed')).map(f => ({
            legacyFile: f.legacyFile, sourceGaps: f.sourceGaps, externalGaps: f.externalGaps,
            stale: f.stale, missing: f.missing, runtimePending: f.pending,
            nonPassed: f.runtime.filter(r => r.status === 'current-not-passed').map(r => ({ id: r.id,
                report: r.latest?.report, result: r.latest?.status, input: r.latest?.input, invocation: r.latest?.invocation })) })),
        pending: gate.pending, sourceIssues: gate.sourceIssues, reportIssues: gate.reportIssues,
        snapshotStable: gate.summary.snapshotStable,
        archiveRemovalAllowed: false, automaticFullCoverage: false,
        limitations: [
            'All original source responsibilities were reviewed independently; audit receipts bind those claims, not an automatic semantic proof.',
            'Only three explicitly reviewed pilot pairs use same-report Legacy/new runtime comparison.',
            'Diagnostic/manual producers, performance observations and missing historical inputs are not certified by regression PASS counts.',
            'This is a snapshot; rerun the read-only gate after any source, invocation, dependency or evidence change.'
        ]
    };
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.join(defaultRoot, 'Tests~/CoverageAudit/final-inventory.mjs'))
    console.log(JSON.stringify(finalInventory()));
