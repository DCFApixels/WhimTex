// Read-only ACTIVE audit: explicit scenario metadata, no filename/classification heuristics.
// No Legacy source reads/execution. Categories are declared, never inferred from C# text.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readBatches } from '../../scripts/migration-inventory.mjs';
import { auditSources as auditHistoricalSources, auditOutput, option, tests, candidates }
    from '../../Cases/NodeA/AuditSources.mjs';
// Independent historical text/CLI contract alongside the additional ACTIVE inventory API.
export { auditHistoricalSources, auditOutput, option, tests, candidates };
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const walk = directory => fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw Error('Active audit does not follow symlinks: ' + full);
    return entry.isDirectory() ? walk(full) : [full];
});
const read = file => fs.readFileSync(file, 'utf8');
const relative = file => path.relative(root, file).replaceAll('\\', '/');

export function auditSources() {
    const batches = readBatches(root);
    const catalog = JSON.parse(read(path.join(root, 'Tests~/scripts/test-catalog.json')));
    const active = s => !s.legacy &&
        (s.file.startsWith('Tests~/Cases/') || s.file.startsWith('Tests~/Framework/'));
    const scenarios = new Map(catalog.scenarios.filter(active).map(s => [s.id, s]));
    const replacements = new Map();
    for (const batch of batches) for (const replacement of batch.replacements) {
        // Mapping identifiers only: accept the proposal spelling while the parent standardizes
        // all batches to immutable-manifest names. This string is never used as an IO path.
        const original = replacement.legacyFile.replace(/^Tests~\/Legacy\//, '');
        if (replacements.has(original)) throw Error('Duplicate active mapping: ' + original);
        replacements.set(original, { batch: batch.file, ...replacement, legacyFile: original });
        for (const scenario of replacement.scenarios) {
            if (!active(scenario)) throw Error('Replacement scenario is not ACTIVE: ' + scenario.id);
            scenarios.set(scenario.id, scenario); // Proposals are authoritative before catalog integration.
        }
    }
    const tests = [...scenarios.values()].map(scenario => ({
        file: scenario.file, primary: scenario.entry ?? null, family: scenario.category,
        lifecycle: scenario.async ? 'asynchronous' : 'synchronous', scenario,
    }));
    const activeCaseFiles = walk(path.join(root, 'Tests~/Cases')).map(relative);
    const sourceFiles = walk(path.join(root, 'src')).filter(file => file.endsWith('.cs'));
    // Historical textual candidate counts remain hints, based only on current code/docs/new tests.
    const allEvidence = [...sourceFiles,
        ...walk(path.join(root, 'Tests~/Cases')).filter(file => /\.(cs|mjs|md)$/.test(file)),
        ...walk(path.join(root, 'Tests~/Framework')).filter(file => /\.(cs|mjs|md)$/.test(file)),
        ...walk(path.join(root, 'Documentation~')).filter(file =>
            /\.(md|cs|mjs|json)$/.test(file) && !file.includes(path.sep + '_site' + path.sep))];
    const evidence = allEvidence.map(read).join('\n');
    const candidates = [];
    for (const file of sourceFiles) {
        const source = read(file);
        for (const match of source.matchAll(/^\s*(?:private|internal)\s+(?:static\s+)?(?:[\w<>\[\].,?]+\s+)+(\w+)\s*\(/gm)) {
            const name = match[1];
            if (/^(On\w+|Dispose|Equals|GetHashCode|GetEnumerator)$/.test(name)) continue;
            const count = [...evidence.matchAll(new RegExp('\\b' + name + '\\b', 'g'))].length;
            if (count === 1) candidates.push({ file: relative(file), name,
                line: source.slice(0, match.index).split('\n').length, occurrences: count });
        }
    }
    return { tests, replacements, activeCaseFiles, methodCandidates: candidates };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url))
    console.log(JSON.stringify(auditOutput({ tests, candidates }), null, 2));
