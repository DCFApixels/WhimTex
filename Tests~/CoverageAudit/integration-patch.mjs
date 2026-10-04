// Read-only patch proposal. The caller applies it with apply_patch; no filesystem writes here.
import fs from 'node:fs';
import { proposals } from '../scripts/migration-inventory.mjs';
const draft = proposals();
const mode = process.argv[2] ?? 'summary';
const specs = {
    catalog: ['Tests~/scripts/test-catalog.json', draft.catalog, 'scenarios', 'id'],
    registry: ['Tests~/migration.json', draft.registry, 'files', 'file']
};
if (mode === 'native-workflows') {
    const file = 'Tests~/Batches/unity-b.json';
    const batch = JSON.parse(fs.readFileSync(file));
    const record = batch.replacements.find(r => r.legacyFile === 'DocumentReleaseValidation.cs');
    const player = JSON.parse(fs.readFileSync('Tests~/CoverageAudit/player.json')).standaloneWorkflow.catalogCandidate;
    const { catalogCandidate: faults } = await import('../Cases/UnityB/FaultWorkflow.mjs');
    for (const candidate of [player, faults]) {
        const index = record.scenarios.findIndex(s => s.id === candidate.id);
        if (index < 0) record.scenarios.push(candidate); else record.scenarios[index] = candidate;
    }
    specs[mode] = [file, batch, 'replacements', 'legacyFile'];
}
if (mode === 'retire-native-fault-ports') {
    const file = 'Tests~/Batches/unity-b.json';
    const batch = JSON.parse(fs.readFileSync(file));
    const record = batch.replacements.find(r => r.legacyFile === 'DocumentReleaseValidation.cs');
    record.retiredScenarioIds = ['document-release-validation-faults-v2', 'document-release-validation-deferred-v2'];
    record.scenarios = record.scenarios.filter(s => !record.retiredScenarioIds.includes(s.id));
    record.newFile = 'Tests~/Cases/UnityB/FaultFixture.cs';
    record.reason = 'Explicit reviewed source handoff: FaultFixture retains Faults/Deferred and native probe lifecycle; PlayerReleaseTests retains the Player subset. Selectable native-fixture and explicitly authorized player-build workflows have separate current runtime receipts. Retired ephemeral-probe ports are not active coverage evidence. Legacy remains frozen.';
    specs[mode] = [file, batch, 'replacements', 'legacyFile'];
}
if (mode === 'unity-b-proposal') {
    const file = 'Tests~/Batches/unity-b.json';
    const batch = JSON.parse(fs.readFileSync(file));
    const proposal = JSON.parse(fs.readFileSync('Tests~/CoverageAudit/unity-b-public.proposal.json'));
    for (const scenario of proposal.proposedScenarios) {
        const record = batch.replacements.find(r => r.newFile === scenario.file);
        if (!record) throw Error('No owner for proposed scenario: ' + scenario.id);
        if (scenario.timeoutMs > 60000) scenario.workflow = 'diagnostic';
        const existing = record.scenarios.findIndex(s => s.id === scenario.id);
        if (existing >= 0) record.scenarios[existing] = scenario; else record.scenarios.push(scenario);
    }
    for (const update of proposal.requiredBatchUpdates) {
        const record = batch.replacements.find(r => r.legacyFile === update.legacyFile);
        record.state = 'ported-unverified';
        record.reason = update.change + ' Runtime verification of current input bundle is pending.';
        const entries = {
            'DocumentSaveCostProbe.cs': ['Container', 'CacheSafety'],
            'HistogramArithmeticAudit.cs': ['Main'],
            'ExportWindowSmoke.cs': ['ShowVisual', 'ShowExrVisual', 'CloseVisual'],
            'GradientHistorySmoke.cs': ['Capture'],
            'HistogramSeamlessSmoke.cs': ['Preview', 'Benchmark', 'ReferenceFixtures']
        }[update.legacyFile];
        if (!entries) throw Error('Unreviewed entry update: ' + update.legacyFile);
        record.entryDispositions = (record.entryDispositions ?? []).filter(d =>
            d.kind === 'combined-regression' || !entries.some(e => (d.entries ?? []).includes(e) || d.entry === e || d.reason?.includes(e)));
        record.entryDispositions.push({kind:'restored-entry', entries, reason:update.change});
        if (!record.coverage.includes(update.change)) record.coverage.push(update.change);
    }
    for (const update of proposal.requiredExistingEffectsUpdates ?? []) {
        for (const id of update.scenarioIds) {
            const scenario = batch.replacements.flatMap(r => r.scenarios).find(s => s.id === id);
            if (!scenario || scenario.file !== update.file) throw Error('Effect update has no exact owner: ' + id);
            scenario.effects = [...new Set([...scenario.effects, ...update.addEffects])];
        }
    }
    specs[mode] = [file,batch,'replacements','legacyFile'];
}
if (!specs[mode]) {
    console.log(JSON.stringify(draft.summary));
} else {
    const [file, proposed, collection, key] = specs[mode];
    const source = fs.readFileSync(file, 'utf8').replaceAll('\r\n', '\n');
    const old = JSON.parse(source), hunks = [];
    const block = (value, indent) => JSON.stringify(value, null, 2).split('\n').map(line => ' '.repeat(indent) + line).join('\n');
    function replace(before, after) {
        const index = source.indexOf(before);
        if (index < 0 || source.indexOf(before, index + 1) >= 0) throw Error('Nonunique patch context: ' + before.slice(0, 80));
        if (source[index + before.length] === ',') { before += ','; after += ','; }
        hunks.push({ index, text:'@@\n' + before.split('\n').map(line => '-' + line).join('\n') + '\n' + after.split('\n').map(line => '+' + line).join('\n') });
    }
    for (const property of Object.keys(proposed).filter(name => name !== collection)) {
        if (property === 'profiles') {
            const addedNames = Object.keys(proposed.profiles).filter(name => !(name in old.profiles));
            const lastName = Object.keys(old.profiles).at(-1);
            const field = (name, value) => '    ' + JSON.stringify(name) + ': ' + block(value, 4).trimStart();
            for (const name of Object.keys(old.profiles))
                if (JSON.stringify(old.profiles[name]) !== JSON.stringify(proposed.profiles[name]) || name === lastName && addedNames.length)
                    replace(field(name, old.profiles[name]), field(name, proposed.profiles[name]) +
                        (name === lastName && addedNames.length ? ',\n' + addedNames.map(key => field(key, proposed.profiles[key])).join(',\n') : ''));
        } else if (JSON.stringify(old[property]) !== JSON.stringify(proposed[property]))
            replace('  ' + JSON.stringify(property) + ': ' + block(old[property], 2).trimStart(), '  ' + JSON.stringify(property) + ': ' + block(proposed[property], 2).trimStart());
    }
    const added = proposed[collection].filter(entry => !old[collection].some(previous => previous[key] === entry[key]));
    const last = old[collection].at(-1);
    for (const before of old[collection]) {
        const after = proposed[collection].find(entry => entry[key] === before[key]);
        if (!after) {
            const retired = before.id && draft.registry.files.some(r => r.file === 'DocumentReleaseValidation.cs' &&
                r.replacementIds.includes('fault-release-workflow-v2')) &&
                ['document-release-validation-faults-v2', 'document-release-validation-deferred-v2'].includes(before.id) &&
                before.file === 'Tests~/Cases/UnityB/DocumentReleaseValidationTests.cs' && !before.legacy;
            if (!retired || mode !== 'catalog') throw Error('Integration may not delete existing invocations/mappings.');
            const snippet = block(before, 4) + ',';
            replace(snippet, '');
            continue;
        }
        if (JSON.stringify(before) !== JSON.stringify(after) || before === last && added.length)
            replace(block(before, 4), block(after, 4) + (before === last && added.length ? ',\n' + added.map(value => block(value, 4)).join(',\n') : ''));
    }
    const ordered = hunks.sort((a,b) => a.index-b.index);
    const count = Number(process.argv[4] ?? 8), start = Number(process.argv[3] ?? 0);
    console.log(JSON.stringify({ file, total:ordered.length, selected:Math.min(count,ordered.length-start),
        patch:ordered.length ? '*** Begin Patch\n*** Update File: ' + file + '\n' + ordered.slice(start,start+count).map(h => h.text).join('\n') + '\n*** End Patch' : null }));
}
