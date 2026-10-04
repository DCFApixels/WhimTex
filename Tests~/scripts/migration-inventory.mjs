// Read-only integration of reviewed batch mappings. Never edits or regenerates Legacy.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyLegacy } from './legacy.mjs';
import { validateCatalog } from './run-tests.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

export function readBatches(packageRoot = root) {
    const directory = path.join(packageRoot, 'Tests~/Batches');
    if (!fs.existsSync(directory)) return [];
    return fs.readdirSync(directory).filter(name => name.endsWith('.json')).sort().map(name => {
        const file = 'Tests~/Batches/' + name;
        const data = JSON.parse(fs.readFileSync(path.join(packageRoot, file), 'utf8'));
        if (data.version !== 1 || !Array.isArray(data.replacements) || !Array.isArray(data.exclusions)) throw Error('Invalid batch: ' + file);
        return { file, ...data };
    });
}

export function inspectMappings(batches, manifest, packageRoot = root) {
    const known = new Set(manifest.files.map(file => file.file));
    const mapped = new Map();
    const scenarios = [];
    const scenarioIds = new Set();
    for (const batch of batches) {
        for (const entry of [...batch.replacements, ...batch.exclusions]) {
            if (!known.has(entry.legacyFile) || mapped.has(entry.legacyFile)) throw Error('Unknown or duplicate original: ' + entry.legacyFile);
            mapped.set(entry.legacyFile, { batch: batch.file, ...entry });
        }
        for (const entry of batch.replacements) {
            if (!['ported-unverified', 'blocked', 'coverage-reviewed'].includes(entry.state)
                || !Array.isArray(entry.coverage) || !entry.coverage.length || !Array.isArray(entry.scenarios)) throw Error('Missing coverage/state/scenarios: ' + entry.legacyFile);
            if (entry.state !== 'blocked' && (!entry.newFile || !fs.statSync(path.join(packageRoot, entry.newFile)).isFile())) throw Error('Missing independent source: ' + entry.legacyFile);
            for (const scenario of entry.scenarios) {
                if (scenarioIds.has(scenario.id)) throw Error('Duplicate batch scenario: ' + scenario.id);
                scenarioIds.add(scenario.id);
                scenarios.push(scenario);
            }
        }
        for (const entry of batch.exclusions) if (!entry.kind || !entry.reason) throw Error('Unexplained exclusion: ' + entry.legacyFile);
    }
    return { mapped, scenarios };
}

export function proposals(packageRoot = root) {
    const manifest = JSON.parse(fs.readFileSync(path.join(packageRoot, 'Tests~/legacy-manifest.json'), 'utf8'));
    const catalog = JSON.parse(fs.readFileSync(path.join(packageRoot, 'Tests~/scripts/test-catalog.json'), 'utf8'));
    const registry = JSON.parse(fs.readFileSync(path.join(packageRoot, 'Tests~/migration.json'), 'utf8'));
    const batches = readBatches(packageRoot);
    const { mapped, scenarios } = inspectMappings(batches, manifest, packageRoot);
    const retired = batches.flatMap(b => b.replacements.flatMap(r => {
        if (!r.retiredScenarioIds) return [];
        if (r.legacyFile !== 'DocumentReleaseValidation.cs' || !r.scenarios.some(s => s.id === 'fault-release-workflow-v2') ||
            r.retiredScenarioIds.some(id => !['document-release-validation-faults-v2', 'document-release-validation-deferred-v2'].includes(id)))
            throw Error('Unapproved scenario retirement: ' + r.legacyFile);
        return r.retiredScenarioIds;
    }));
    catalog.scenarios = catalog.scenarios.filter(s => !retired.includes(s.id));
    for (const name of Object.keys(catalog.profiles)) catalog.profiles[name] = catalog.profiles[name].filter(id => !retired.includes(id));
    const originalIds = new Set(catalog.scenarios.map(s => s.id));
    for (const scenario of scenarios) {
        const previous = catalog.scenarios.find(s => s.id === scenario.id);
        // A reviewed batch owns metadata for its independent source. Re-integration
        // may update dependencies or lifecycle notes, but must never hijack an ID.
        if (previous && (previous.file !== scenario.file || previous.runner !== scenario.runner || previous.legacy))
            throw Error('Conflicting scenario ownership: ' + scenario.id);
        if (previous) catalog.scenarios[catalog.scenarios.indexOf(previous)] = scenario;
        if (!originalIds.has(scenario.id)) { catalog.scenarios.push(scenario); originalIds.add(scenario.id); }
    }
    for (const batch of batches) {
        const ids = batch.replacements.flatMap(entry => entry.scenarios.map(s => s.id));
        if (ids.length) catalog.profiles['new-' + path.basename(batch.file, '.json')] = ids;
        const regressions = batch.replacements.flatMap(entry => entry.scenarios.filter(s => s.category !== 'diagnostic').map(s => s.id));
        if (regressions.length) catalog.profiles['new-' + path.basename(batch.file, '.json') + '-regressions'] = regressions;
    }
    const newScenarios = catalog.scenarios.filter(s => !s.legacy);
    const profile = (name, predicate) => {
        const ids = newScenarios.filter(predicate).map(s => s.id);
        if (ids.length) catalog.profiles[name] = ids;
    };
    profile('new-node', s => s.runner === 'node' && !s.requiresUnity);
    profile('new-unity-memory', s => s.runner === 'run_script' && s.category !== 'diagnostic' && !s.effects.length);
    profile('new-unity-assets', s => s.runner === 'run_script' && s.category !== 'diagnostic' && s.effects.includes('assets'));
    profile('new-regressions', s => s.category !== 'diagnostic');
    validateCatalog(catalog, packageRoot);
    registry.batches = batches.map(batch => batch.file);
    for (const entry of registry.files) {
        const mapping = mapped.get(entry.file);
        if (!mapping) continue;
        entry.state = mapping.state ?? 'classified-non-test';
        entry.replacementIds = (mapping.scenarios ?? []).map(s => s.id);
        entry.newFile = mapping.newFile ?? mapping.replacement ?? null;
        entry.batch = mapping.batch;
        entry.coverage = mapping.coverage ?? [];
        if (mapping.reason) entry.reason = mapping.reason;
        if (mapping.kind) entry.kind = mapping.kind;
    }
    const sources = manifest.files.filter(entry => /\.(cs|mjs|cjs)$/.test(entry.file));
    const outstanding = registry.files.filter(entry => /\.(cs|mjs|cjs)$/.test(entry.file) && entry.state === 'unreviewed').map(entry => entry.file);
    return { catalog, registry, summary: { originalSourceFiles: sources.length,
        mappedSourceFiles: sources.length - outstanding.length, outstanding,
        independentScenarios: newScenarios.length, batches: batches.map(batch => batch.file),
        archiveRemovalAllowed: false, note: 'A ported source is not a verified run or proof of complete entry coverage.' } };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
    verifyLegacy();
    const value = proposals();
    const field = process.argv[2] ?? 'summary';
    if (!['summary', 'catalog', 'registry'].includes(field)) throw Error('Choose summary, catalog or registry');
    console.log(JSON.stringify(value[field], null, 2));
}
