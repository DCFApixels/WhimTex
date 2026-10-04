import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyLegacy } from './legacy.mjs';
import { selectScenarios, reviewFingerprint } from './run-tests.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
export function compareMigration(report, registry = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/migration.json'), 'utf8'))) {
    const archive = verifyLegacy();
    if (report.version !== 2 || !report.productionFingerprint || report.archive?.manifestHash !== archive.manifestHash) throw Error('Report is not from the frozen-archive migration runner.');
    const catalog = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/scripts/test-catalog.json'), 'utf8'));
    if (!report.success || report.recoveryRequired || !Array.isArray(report.results) || !Array.isArray(report.selected)
        || report.results.length !== report.selected.length || new Set(report.results.map(result => result.id)).size !== report.results.length)
        throw Error('Report must be a completed unambiguous successful selected run.');
    const scenarios = selectScenarios(catalog, { ids: report.selected.join(',') });
    if (reviewFingerprint(scenarios) !== report.fingerprint) throw Error('Reviewed test sources/invocation/coverage mapping changed after this report. Rerun selected pairs.');
    const pairs = registry.pairs.map(pair => {
        const old = report.results.find(result => result.id === pair.legacyId);
        const current = report.results.find(result => result.id === pair.newId);
        const errors = [];
        if (!old || !current) errors.push('Both selected scenarios must appear in the SAME report.');
        if (old?.status !== 'passed' || current?.status !== 'passed' || old?.uncertain || current?.uncertain) errors.push('Both scenarios must pass without uncertainty.');
        if (current?.testResult?.checks !== pair.newChecks) errors.push('Unexpected replacement check count.');
        if (pair.oldChecksPattern && Number(new RegExp(pair.oldChecksPattern).exec(old?.detail ?? '')?.[1]) !== pair.newChecks) errors.push('Old and new check counts differ.');
        if (pair.oldPassPattern && !new RegExp(pair.oldPassPattern).test(old?.detail ?? '')) errors.push('Old fixture contract did not pass.');
        for (const [key, value] of Object.entries(pair.facts ?? {})) if (current?.testResult?.facts?.[key] !== value) errors.push('Replacement fact differs: ' + key);
        if (!pair.coverage?.length) errors.push('Missing assertion/input/cleanup coverage review.');
        return { legacyFile: pair.legacyFile, legacyId: pair.legacyId, newId: pair.newId,
            status: errors.length ? 'not-verified' : 'equivalent-tested', errors, coverage: pair.coverage };
    });
    return { success: pairs.every(pair => pair.status === 'equivalent-tested'), archive, pairs,
        archiveRemovalAllowed: false, note: 'Only the three explicit pilot pairs are compared; full source mappings do not prove all entry coverage or allow archive removal.' };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
    if (process.argv.length !== 3) throw Error('Usage: node Tests~/scripts/check-migration.mjs <paired-run-report.json>');
    const comparison = compareMigration(JSON.parse(fs.readFileSync(process.argv[2], 'utf8')));
    console.log(JSON.stringify(comparison, null, 2));
    process.exitCode = comparison.success ? 0 : 1;
}
