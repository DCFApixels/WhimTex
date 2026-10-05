import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { verifyLegacy } from './legacy.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
// This is a historical receipt, not a fingerprint recomputed from today's runner.
// Updating dispatch/catalog code must neither invalidate nor re-sign the old run.
const historicalPilot = Object.freeze({
    fingerprint: '23f69281bd46fb12d10a5140c153c846376923babbed6f183f4a0377964676e1',
    productionFingerprint: 'a3da14871f6e0bf5c49a6f077b1026720e0fb2d3c28a49213dd715f3dd2b7738',
    manifestHash: 'c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194',
    reportFile: 'Temp/WhimTex/test-runs/2026-10-04T18-02-02.749Z-120056.json',
    reportSha256: '1aa2b5ca988b4ec66fd74a54e4942d2fc012e095684c06f49486187ff6b97cb8',
    catalogSha256: '5af017f0a42f31164376fe17aa5ec1ef697c60de42d96b780ed90afa8d66039e',
    pairsSha256: '0c291903b05e947131c89e9265fd3fad5c323a33b14f0eabc434afb459882ab9'
});

// Pure comparison for synthetic negative tests. It deliberately does NOT claim
// receipt authenticity or current runtime equivalence; compareMigration owns the
// fixed raw receipt/catalog hashes and pinned Git archive verification.
export function compareHistoricalPilot(report, registry, catalog, expected = historicalPilot) {
    if (report.version !== 2 || report.action !== 'run' || report.productionFingerprint !== expected.productionFingerprint
        || report.archive?.manifestHash !== expected.manifestHash) throw Error('Report is not from the frozen-archive historical pilot runner.');
    if (report.success !== true || report.recoveryRequired !== false || !Array.isArray(report.results) || !Array.isArray(report.selected)
        || report.results.length !== report.selected.length || new Set(report.results.map(result => result.id)).size !== report.results.length
        || new Set(report.selected).size !== report.selected.length || report.notRun?.length !== 0
        || !Number.isFinite(Date.parse(report.finishedAt)) || !Number.isFinite(Date.parse(report.startedAt))
        || Date.parse(report.finishedAt) < Date.parse(report.startedAt))
        throw Error('Report must be a completed unambiguous successful selected run.');
    const selected = catalog.profiles?.['migration-pilot'];
    const pairIds = registry.pairs.flatMap(pair => [pair.legacyId, pair.newId]);
    if (!Array.isArray(selected) || !selected.length || new Set(selected).size !== selected.length
        || selected.some(id => !catalog.scenarios.some(scenario => scenario.id === id))
        || JSON.stringify(selected) !== JSON.stringify(pairIds)
        || JSON.stringify(report.selected) !== JSON.stringify(selected)
        || JSON.stringify(report.results.map(result => result.id)) !== JSON.stringify(selected))
        throw Error('Historical selected scenarios must match the frozen pilot catalog and pair order.');
    if (report.fingerprint !== expected.fingerprint) throw Error('Historical reviewed fingerprint changed; do not re-sign the old report with current sources.');
    const pairs = registry.pairs.map(pair => {
        const old = report.results.find(result => result.id === pair.legacyId);
        const current = report.results.find(result => result.id === pair.newId);
        const errors = [];
        if (!old || !current) errors.push('Both selected scenarios must appear in the SAME report.');
        if (old?.status !== 'passed' || current?.status !== 'passed' || old?.uncertain || current?.uncertain) errors.push('Both scenarios must pass without uncertainty.');
        if (current?.testResult?.status !== 'passed' || !Array.isArray(current?.testResult?.failures)
            || current.testResult.failures.length) errors.push('Replacement structured payload did not pass.');
        if (current?.testResult?.checks !== pair.newChecks) errors.push('Unexpected replacement check count.');
        if (pair.oldChecksPattern && Number(new RegExp(pair.oldChecksPattern).exec(old?.detail ?? '')?.[1]) !== pair.newChecks) errors.push('Old and new check counts differ.');
        if (pair.oldPassPattern && !new RegExp(pair.oldPassPattern).test(old?.detail ?? '')) errors.push('Old fixture contract did not pass.');
        for (const [key, value] of Object.entries(pair.facts ?? {})) if (current?.testResult?.facts?.[key] !== value) errors.push('Replacement fact differs: ' + key);
        if (!pair.coverage?.length) errors.push('Missing assertion/input/cleanup coverage review.');
        return { legacyFile: pair.legacyFile, legacyId: pair.legacyId, newId: pair.newId,
            status: errors.length ? 'not-verified' : 'historical-pair-matched', errors, coverage: pair.coverage };
    });
    return { success: pairs.every(pair => pair.status === 'historical-pair-matched'), pairs,
        scope: 'historical-pilot-only', receiptVerified: false, currentRuntimeEquivalence: false,
        archiveRemovalAllowed: false, note: 'Pure historical contract comparison only. Counts do not authenticate a run, prove full coverage or authorize archive removal.' };
}

export function compareMigration(report, registry = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/migration.json'), 'utf8'))) {
    const archive = verifyLegacy(root);
    if (archive.manifestHash !== historicalPilot.manifestHash) throw Error('Historical manifest identity changed.');
    const baseline = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/retirement-baseline.json')));
    if (baseline.version !== 1 || baseline.retiredCatalog !== 'Tests~/retired-catalog.json'
        || baseline.retiredCatalogSha256 !== historicalPilot.catalogSha256
        || baseline.pilot?.fingerprint !== historicalPilot.fingerprint
        || baseline.pilot?.report !== historicalPilot.reportFile || baseline.pilot?.reportSha256 !== historicalPilot.reportSha256)
        throw Error('Fixed historical pilot descriptor changed.');
    const catalogBytes = fs.readFileSync(path.join(root, baseline.retiredCatalog));
    if (hash(catalogBytes) !== historicalPilot.catalogSha256) throw Error('Frozen retired catalog changed.');
    if (hash(JSON.stringify(registry.pairs)) !== historicalPilot.pairsSha256) throw Error('Historical pair coverage/expectations changed.');
    const receiptBytes = fs.readFileSync(path.resolve(root, '../..', historicalPilot.reportFile));
    if (hash(receiptBytes) !== historicalPilot.reportSha256) throw Error('Original historical pilot raw receipt changed.');
    const receipt = JSON.parse(receiptBytes);
    if (JSON.stringify(report) !== JSON.stringify(receipt)) throw Error('Input differs from the pinned original historical pilot receipt.');
    const comparison = compareHistoricalPilot(report, registry, JSON.parse(catalogBytes));
    return { ...comparison, archive, receiptVerified: true,
        evidence: { reportFile: historicalPilot.reportFile, reportSha256: historicalPilot.reportSha256,
            fingerprint: historicalPilot.fingerprint, catalogSha256: historicalPilot.catalogSha256 },
        note: 'Authenticated historical evidence for only the three explicit pilot pairs. The current runner is not compared or re-signed; no current runtime equivalence or archive-removal permission is inferred.' };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
    if (process.argv.length !== 3) throw Error('Usage: node Tests~/scripts/check-migration.mjs <paired-run-report.json>');
    const comparison = compareMigration(JSON.parse(fs.readFileSync(process.argv[2], 'utf8')));
    console.log(JSON.stringify(comparison, null, 2));
    process.exitCode = comparison.success ? 0 : 1;
}
