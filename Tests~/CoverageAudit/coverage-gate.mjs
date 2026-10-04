// Read-only evidence inventory. Never runs tests, imports a runner, writes receipts, or permits removal.
// Source-review claims are preserved with provenance; a hash/count/pattern is not an oracle review.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

export const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
export const auditNames = ['unity-a.json', 'unity-b-public.json', 'unity-cd.json', 'node.json',
    'player.json', 'framework-and-auxiliary.json', 'fault.json'];
const frozenManifestSha = 'c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194';
const restoredB = new Set(['DocumentPreparationSmoke.cs', 'EyedropperSmoke.cs',
    'FileNavigationSmoke.cs', 'DocumentSaveTailProbe.cs']);
const sourceExtension = /\.(cs|mjs|cjs)$/;
const shaPattern = /^[a-f0-9]{64}$/;
export const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const unique = values => [...new Set(values)];
const slash = value => value.replaceAll('\\', '/');
const legacyName = value => slash(value).replace(/^Tests~\/Legacy\//, '');
const readJson = (io, file) => JSON.parse(io.readFileSync(file, 'utf8'));

function localFile(root, relative) {
    if (typeof relative !== 'string') throw Error('Expected package-relative path.');
    const file = path.resolve(root, relative), rel = path.relative(root, file);
    if (!rel || rel === '..' || rel.startsWith('..' + path.sep) || path.isAbsolute(rel))
        throw Error('Path outside package: ' + relative);
    return file;
}
function walk(io, directory) {
    return io.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        const file = path.join(directory, entry.name);
        if (entry.isSymbolicLink()) throw Error('Inventory does not follow symlinks: ' + file);
        return entry.isDirectory() ? walk(io, file) : [file];
    });
}

export function verifyFrozenLegacy(root, io = fs, expectedManifestSha = frozenManifestSha) {
    const bytes = io.readFileSync(path.join(root, 'Tests~/legacy-manifest.json'));
    const manifest = JSON.parse(bytes.toString('utf8'));
    const issues = [];
    if (sha256(bytes) !== expectedManifestSha) issues.push({ kind: 'manifest-raw-sha-mismatch', expected: expectedManifestSha, actual: sha256(bytes) });
    const directory = path.join(root, 'Tests~/Legacy');
    const actual = walk(io, directory).map(file => slash(path.relative(directory, file))).sort();
    const expected = manifest.files.map(f => f.file).sort();
    for (const file of actual.filter(file => !expected.includes(file))) issues.push({ kind: 'unexpected-legacy-file', file });
    for (const item of manifest.files) {
        try {
            const raw = io.readFileSync(localFile(directory, item.file));
            if (raw.length !== item.bytes || sha256(raw) !== item.sha256)
                issues.push({ kind: 'legacy-raw-bytes-mismatch', file: item.file, expected: item.sha256, actual: sha256(raw), expectedBytes: item.bytes, actualBytes: raw.length });
        } catch (error) { issues.push({ kind: 'missing-legacy-file', file: item.file, detail: String(error) }); }
    }
    return { manifest, verified: issues.length === 0, manifestRawSha256: sha256(bytes), files: manifest.files.length, issues };
}

// Independent copies of the inspected dispatcher's read-only receipt/bundle algorithms.
// No import/evaluation of scripts/run-tests.mjs or any selected test is needed.
export function currentReviewFingerprint(scenarios, root, io = fs) {
    const hash = createHash('sha256').update(JSON.stringify(scenarios));
    const files = unique(scenarios.flatMap(s => [s.file, ...(s.supportFiles ?? []), ...(s.reviewFiles ?? [])])).sort();
    for (const file of files) hash.update(file).update(io.readFileSync(localFile(root, file)));
    hash.update(io.readFileSync(localFile(root, 'Tests~/scripts/run-tests.mjs')));
    for (const file of ['Tests~/scripts/legacy.mjs', 'Tests~/Framework/test-api.mjs', 'Tests~/Framework/TestApi.cs',
        'Tests~/Framework/EntryContract.cs', 'Tests~/legacy-manifest.json', 'Tests~/migration.json', 'Tests~/scripts/check-migration.mjs'])
        hash.update(io.readFileSync(localFile(root, file)));
    return hash.digest('hex');
}
export function currentBundleSources(files, root, io = fs) {
    const imports = new Set();
    const bodies = files.map(relative => {
        const source = io.readFileSync(localFile(root, relative), 'utf8');
        const body = source.replace(/^using[ \t]+(?:static[ \t]+)?[\w.]+(?:[ \t]*=[ \t]*[\w.]+)?;[ \t]*\r?$/gm,
            directive => { imports.add(directive.trim()); return ''; });
        return '#line 1 "' + relative + '"\n' + body;
    });
    return [...imports].join('\n') + '\n' + bodies.join('\n');
}
function productionSha(root, io) {
    const digest = createHash('sha256');
    const visit = directory => {
        for (const entry of io.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
            const file = path.join(directory, entry.name);
            if (entry.isSymbolicLink()) throw Error('Production symlink: ' + file);
            if (entry.isDirectory()) visit(file);
            else digest.update(slash(path.relative(root, file))).update(io.readFileSync(file));
        }
    };
    visit(path.join(root, 'src')); return digest.digest('hex');
}

export function splitGap(text) {
    const value = typeof text === 'string' ? text : JSON.stringify(text);
    // Actual missing predicates/branches/public abilities outrank every softer classification.
    if (/No exact|omitted|not retained|missing (?:old |original )?(?:assertion|oracle|predicate)|uncovered|Unresolved original|assertions? (?:removed|weakened|missing|absent)|missing (?:public entry|branch|cleanup)|No individual .*CLI modes are implemented/i.test(value)) return 'source';
    if (/\b(?:missing|removed|weakened|absent)[^.;\n]{0,80}\b(?:assertions?|predicates?|branches?|cleanup|public entry)\b|\b(?:assertions?|predicates?|branches?|cleanup)\b[^.;\n]{0,80}\b(?:missing|omitted|removed|weakened|absent|not (?:implemented|retained|ported))\b/i.test(value)) return 'source';
    // ContextTools L239-L273 / replacement L243-L277 were inspected directly:
    // the red geometry predicate and the unreached tail remain in source. Keep
    // this failure as a runtime blocker, never as missing code or a diagnostic.
    if (/^Unresolved genuine original-oracle failure:.*Source fixture and100ms wait are faithful|^Cause remains bounded but not proven:.*receipt cannot distinguish.*layout failure|^Validation gap: original assertions .*not executed in this run\. They remain independently ported in source/i.test(value)) return 'runtime-blocker';
    if (/^(?:RUNTIME(?: SUBSET)?|METADATA):|^Native postprocessor activation\/removal passed for the exact recorded GUID; no cross-project\/platform or concurrent-user-operation claim\./i.test(value)) return 'pending';
    // Known historical input artefacts are external evidence, not missing replacement code.
    if (/live_4\.bin|00_source\.png|00_original\.png|252 (?:pre-change )?(?:float )?snapshots/i.test(value) &&
        /unavailable|missing|not available|await .*original input/i.test(value)) return 'external';
    if (/missing installed|external reader|external.*(?:module|prerequisite)|decoder|fixture.*(?:absent|missing)|SKIP without fixture/i.test(value)) return 'external';
    if (/outside this Player subset|belongs to the fault tests|outside .*subset|belong only to CoverageAudit\/player\.json/i.test(value)) return 'subset';
    // Keep stale metadata-append claims visible for the audit owner; do not silently clear them.
    if (/(?:must )?append (?:async )?(?:VisualSequence|CaptureWorkflow).*metadata|must append VisualSequence/i.test(value)) return 'metadata';
    if (/(?:screenshot\/state|screenshot\/layout|visual|state) equivalence (?:is )?(?:not verified|unverified)|no original screenshot\/layout oracle has been executed|reports require evidence binding\/current-fingerprint validation|require current-fingerprint runtime proof/i.test(value)) return 'pending';
    if (/Current ordinary Node timeout.*pure protocol PASS is not runtime coverage/i.test(value)) return 'pending';
    if (/pending parent|pending.*execution|await.*parent|must integrate|not yet integrated|registration.*metadata only|runtime.*not (?:run|executed)|implemented.*not executed|not executed.*host/i.test(value)) return 'pending';
    if (/^Fixture source is a native Assets script|^PARENT NOTE:/i.test(value)) return 'note';
    return 'source'; // Ambiguity never silently becomes a diagnostic or runtime-only debt.
}

export function metadataGapEvidence(text, row, catalog) {
    const requested = ['VisualSequence', 'CaptureWorkflow'].filter(name => text.includes(name));
    const observed = catalog.scenarios.filter(s => s.file === row.newFile && requested.some(name => s.entry?.endsWith('.' + name)))
        .map(s => ({ id: s.id, file: s.file, entry: s.entry, args: s.args, async: s.async ?? null }));
    return { kind: 'metadata-audit-claim-needs-owner-update', requestedEntries: requested, observedMetadata: observed,
        integrationState: requested.length && requested.every(name => observed.some(s => s.entry.endsWith('.' + name)))
            ? 'present-in-current-catalog; audit-owner-must-update-stale-claim' : 'not-fully-observed-in-current-catalog',
        proof: 'metadata-observation-only; no source/oracle/manual/runtime equivalence inferred' };
}

function argumentMatch(expected, actual, bindings, projectPath) {
    if (!Array.isArray(actual) || actual.length !== expected.length) return false;
    return expected.every((value, i) => {
        if (value === '$projectPath') return typeof actual[i] === 'string' && path.resolve(actual[i]) === path.resolve(projectPath);
        if (value !== '$runId') return same(value, actual[i]);
        if (typeof actual[i] !== 'string' || !actual[i]) return false;
        if (!bindings.runId) bindings.runId = actual[i];
        return bindings.runId === actual[i];
    });
}
export function inspectInvocation(result, scenario, projectPath, inputFile) {
    const issues = [], bindings = {}, seen = new Set();
    for (const attempt of result.attempts ?? []) {
        const phase = attempt.phase;
        const contract = phase === 'cleanup' ? scenario.cleanup : phase === 'cancel' ? scenario.cancel
            : phase === 'start' ? scenario : phase === 'result' ? scenario.async ?? scenario : null;
        if (!contract) { issues.push('Unknown/unexpected invocation phase: ' + phase); continue; }
        seen.add(phase);
        if (scenario.runner === 'node') {
            if ((attempt.entry ?? null) !== (contract.entry ?? null)) issues.push('Node entry differs from metadata.');
            continue; // Node args are not recorded: the exact selected-metadata receipt is mandatory instead.
        }
        let envelope;
        try { envelope = JSON.parse(attempt.reply?.stdout); } catch { issues.push('Invocation envelope unavailable: ' + phase); continue; }
        const parameters = envelope.data?.parameters;
        if (!parameters) { issues.push('Invocation parameters unavailable: ' + phase); continue; }
        if (scenario.runner === 'run_script') {
            if (attempt.entry !== contract.entry || parameters.entry !== contract.entry) issues.push('Entry mismatch: ' + phase);
            let args = parameters.args;
            try { if (typeof args === 'string') args = JSON.parse(args); } catch { args = null; }
            if (!argumentMatch(contract.args ?? [], args, bindings, projectPath)) issues.push('Argument/type/run identity mismatch: ' + phase);
        }
        if (typeof parameters.file !== 'string' || !inputFile || path.resolve(projectPath, parameters.file) !== path.resolve(inputFile))
            issues.push('Invoked source file mismatch: ' + phase);
        const target = envelope.data?.target?.projectPath;
        if (target && path.resolve(target) !== path.resolve(projectPath)) issues.push('Invoked project mismatch: ' + phase);
    }
    if (!seen.has(scenario.async ? 'start' : 'result')) issues.push('Main invocation is not recorded.');
    if (scenario.async && result.status === 'passed' && !seen.has('result')) issues.push('Async final poll is not recorded.');
    if (scenario.cleanup && !seen.has('cleanup')) issues.push('Required cleanup invocation is not recorded.');
    return { verified: issues.length === 0, issues, runId: bindings.runId ?? null,
        argsEvidence: scenario.runner === 'node' ? 'selected-catalog-fingerprint-required; process args not recorded' : 'attempt reply envelope parameters' };
}

function customInput(scenario, result, context) {
    const facts = result.testResult?.facts ?? {};
    const input = facts.input;
    const issues = [];
    let sources;
    if (scenario.id === 'psd-reader-roundtrip-v2') sources = ['Tests~/Cases/Export/PsdWriter.cs', 'Tests~/Framework/TestApi.cs', 'src/PsdWriter.cs'];
    else if (scenario.id === 'fault-release-workflow-v2') sources = ['Tests~/Cases/UnityB/FaultFixture.cs'];
    else if (scenario.workflow === 'player-build' || scenario.file.endsWith('/PlayerWorkflow.mjs')) sources = ['Tests~/Cases/UnityB/PlayerReleaseTests.cs'];
    else sources = input?.sources;
    if (!input || !Array.isArray(sources) || !sources.length || !shaPattern.test(input.sha256 ?? ''))
        return { verified: false, issues: ['Custom workflow has no exact native input SHA fact.'], sources: sources ?? [] };
    if (input.sources && !same(input.sources, sources)) issues.push('Custom input source order/list differs.');
    const declared = new Set([scenario.file, ...(scenario.supportFiles ?? []), ...(scenario.reviewFiles ?? [])]);
    if (sources.some(file => !declared.has(file))) issues.push('Custom native input not included in selected reviewed dependencies.');
    let currentSha;
    try { currentSha = sha256(currentBundleSources(sources, context.root, context.io)); }
    catch (error) { issues.push('Custom input cannot be read: ' + error); }
    if (input.sha256 !== currentSha) issues.push('Custom native input SHA differs from current bundle.');
    if (scenario.id === 'psd-reader-roundtrip-v2' && result.status === 'passed') {
        if (facts.producer?.status !== 'passed' || facts.decoderResult?.status !== 'passed') issues.push('PSD encode/decode final receipts unavailable.');
        try {
            if (!shaPattern.test(facts.decoder?.sha256 ?? '') || sha256(context.io.readFileSync(facts.decoder.modulePath)) !== facts.decoder.sha256)
                issues.push('Independent decoder SHA unavailable/stale.');
        } catch { issues.push('Independent decoder module is unavailable.'); }
    }
    return { verified: issues.length === 0, issues, sources, recordedSha256: input.sha256, currentSha256: currentSha,
        evidence: 'testResult.facts.input plus exact selected-catalog global receipt; never a synthetic report.inputs row' };
}

export function inspectRuntimeReport(report, result, scenario, context) {
    const issues = [];
    const selected = report.selected;
    let receiptMatches = false;
    try {
        if (!Array.isArray(selected) || !selected.length || unique(selected).length !== selected.length || !selected.includes(scenario.id)) throw Error('Invalid selected IDs.');
        const scenarios = selected.map(id => {
            const current = context.catalog.scenarios.find(s => s.id === id);
            if (!current) throw Error('Selected ID absent from current catalog: ' + id);
            return current;
        });
        receiptMatches = report.fingerprint === context.fingerprint(scenarios);
    } catch (error) { issues.push('Global receipt cannot be resolved: ' + error); }
    const productionMatches = report.productionFingerprint ? report.productionFingerprint === context.productionFingerprint : null;
    if (result.file !== scenario.file || result.runner !== scenario.runner || result.category !== scenario.category)
        issues.push('Result file/runner/category metadata differs from current catalog.');
    if (report.projectPath && path.resolve(report.projectPath) !== path.resolve(context.projectPath)) issues.push('Report project differs.');
    let input, sourceCurrent = false;
    if (scenario.runner === 'node') {
        sourceCurrent = receiptMatches;
        if (!receiptMatches) issues.push('Node receipt is historical: fingerprint differs from reviewFingerprint(current selected scenarios).');
        if (scenario.requiresUnity) {
            input = customInput(scenario, result, context);
            // SKIP commonly happens before a native input is created. It still overrides older green for this exact receipt.
            if (result.status === 'passed') issues.push(...input.issues);
            const binding = context.scenarioReviewBindings?.get(scenario.id);
            if (binding?.current !== true) issues.push('Custom Node per-case source/support review hashes are absent or stale.');
        }
    } else {
        const expectedSources = unique([scenario.file, ...(scenario.supportFiles ?? [])]);
        const recorded = (report.inputs ?? []).filter(input => input.id === scenario.id);
        if (recorded.length !== 1) input = { verified: false, issues: ['Exactly one report.inputs native bundle receipt is required.'], sources: expectedSources };
        else {
            let currentSha;
            try { currentSha = sha256(currentBundleSources(expectedSources, context.root, context.io)); }
            catch (error) { issues.push('Native bundle cannot be read: ' + error); }
            input = { verified: same(recorded[0].sources, expectedSources) && recorded[0].sha256 === currentSha,
                file: recorded[0].file, sources: expectedSources, recordedSha256: recorded[0].sha256, currentSha256: currentSha,
                issues: [] };
            if (!input.verified) input.issues.push('Native bundle source list/order/SHA differs from current catalog bundle.');
        }
        sourceCurrent = input.verified;
        issues.push(...input.issues);
    }
    const invocation = inspectInvocation(result, scenario, context.projectPath, input?.file);
    issues.push(...invocation.issues);
    if (productionMatches === false) { issues.push('Production source fingerprint is stale.'); sourceCurrent = false; }
    if (result.uncertain || result.testResult?.recoveryRequired) issues.push('Execution/cleanup remains uncertain.');
    if (result.status === 'passed' && (result.testResult?.status !== 'passed' || !Array.isArray(result.testResult?.failures) || result.testResult.failures.length ||
        !Number.isSafeInteger(result.testResult.checks) || result.testResult.checks <= 0))
        issues.push('Structured passed result is absent/contradictory.');
    const regression = ['regression', 'source-guard', 'runner'].includes(scenario.category);
    return { action: report.action, id: scenario.id, status: result.status, category: scenario.category,
        sourceCurrent, receiptMatches, productionMatches, input: input ?? null, invocation,
        proof: report.action === 'run' && sourceCurrent && issues.length === 0 && result.status === 'passed'
            ? regression ? 'current-regression-run' : 'current-diagnostic-run-not-regression' : 'not-current-proof',
        issues, nativeFixtureVerified: scenario.id === 'fault-release-workflow-v2' &&
            ['Setup', 'Faults', 'PollDeferred', 'Cleanup'].every(phase => result.testResult?.facts?.[phase]?.status === 'passed') &&
            ['install', 'cleanup'].every(kind => result.testResult?.facts?.nativeCompilations?.some(e =>
                e.kind === kind && e.nativeCompilationComplete === true && e.runId === result.testResult?.facts?.input?.runId)),
        equivalence: 'not-inferred-from-runtime-green' };
}

export function chooseNewestEvidence(events) {
    const ordered = [...events].sort((a, b) => Number(b.chronologyKnown === false) - Number(a.chronologyKnown === false) || b.timestamp - a.timestamp ||
        // With indistinguishable timestamps prefer a non-pass; never resurrect ambiguous green.
        Number(a.status === 'passed') - Number(b.status === 'passed') || b.report.localeCompare(a.report));
    const current = ordered.filter(e => e.action === 'run' && e.sourceCurrent);
    const latest = current[0] ?? null;
    return { status: !latest ? 'pending-no-current-runtime-proof' : latest.proof === 'current-regression-run' ? 'current-passed'
        : latest.proof === 'current-diagnostic-run-not-regression' ? 'diagnostic-only'
        : ['skip', 'skipped'].includes(latest.status) ? 'current-skipped'
        : latest.status === 'passed' ? 'pending-incomplete-proof' : 'current-not-passed',
        latest, history: ordered.map(e => ({ report: e.report, startedAt: e.startedAt, timestamp: e.timestamp,
            action: e.action, status: e.status, proof: e.proof, sourceCurrent: e.sourceCurrent, chronologyKnown: e.chronologyKnown,
            receiptMatches: e.receiptMatches, productionMatches: e.productionMatches,
            inputSha256: e.input?.recordedSha256 ?? null, issues: e.issues })),
        earlierGreenOverridden: Boolean(latest && latest.proof !== 'current-regression-run' && current.slice(1).some(e => e.proof === 'current-regression-run')) };
}

function dependencyPairs(row, audit) {
    const pairs = [[row.newFile, row.sourceHashes?.replacement, 'replacement']];
    const add = (value, kind) => {
        if (Array.isArray(value)) for (const item of value) pairs.push([item.file, item.sha256, kind]);
        else for (const [file, hash] of Object.entries(value ?? {})) pairs.push([file, typeof hash === 'string' ? hash : hash?.sha256, kind]);
    };
    add(row.sourceHashes?.support, 'sourceHashes.support');
    add(row.additionalDependenciesSha, 'additionalDependenciesSha');
    add(row.supportSources, 'supportSources');
    add(audit.supportSourceHashes, 'audit.supportSourceHashes');
    return pairs;
}

// Parent declaration belongs ONLY on a source-reviewed framework DocRelease row:
// disposition:{kind:'combined-release-source-handoff'},
// supersedesAudits:['unity-b-public.json']. All eight original entries and both
// workflow IDs/dependencies must be reviewed. Only this exact obsolete B row is
// replaceable, including its parent-rebound FaultFixture source identity.
// Also accepts the already-integrated parentCombinedDocRelease v1 exact-hash
// declaration (fullOriginalRead/sourceReplacementReviewed/handoffScenarioIds/
// supersedes). Both forms enforce the same current complete source review.
// This is an explicit human source-review handoff, not a token/count/runtime oracle.
function combinedDocReleasePolicy(records, archive, catalog, root, io) {
    const declarations = records.filter(r => r.row.parentCombinedDocRelease !== undefined ||
        r.row.disposition?.kind === 'combined-release-source-handoff' || r.row.supersedesAudits !== undefined);
    if (!declarations.length) return { approved: false, issues: [], targets: [] };
    const issues = [], record = declarations[0], row = record.row;
    const structured = row.parentCombinedDocRelease !== undefined;
    const declaration = structured ? row.parentCombinedDocRelease : { disposition: row.disposition ?? null, supersedesAudits: row.supersedesAudits ?? null };
    const ids = ['fault-release-workflow-v2', 'player-release-workflow-v2'];
    const original = archive.manifest.files.find(f => f.file === 'DocumentReleaseValidation.cs');
    if (declarations.length !== 1) issues.push('Exactly one parent combined source declaration is required.');
    if (record.auditFile !== 'framework-and-auxiliary.json') issues.push('Only the parent framework audit may supersede the obsolete B row.');
    if ((structured ? declaration?.version !== 1 || declaration.fullOriginalRead !== true || declaration.sourceReplacementReviewed !== true ||
        !Array.isArray(declaration.handoffScenarioIds) || !same([...declaration.handoffScenarioIds].sort(), [...ids].sort())
        : row.disposition?.kind !== 'combined-release-source-handoff' || !same(row.supersedesAudits, ['unity-b-public.json'])) ||
        row.newFile !== 'Tests~/Cases/UnityB/FaultFixture.cs')
        issues.push('Explicit combined-source handoff/B-only supersession/native Fault replacement approval is missing.');
    if (row.reviewStatus !== 'source-reviewed' || !Array.isArray(row.coverage) || !row.coverage.length ||
        (row.gaps ?? []).some(g => splitGap(g) === 'source')) issues.push('Combined parent source review is incomplete or has actual source gaps.');
    if (!archive.verified || row.sourceHashes?.legacy !== original?.sha256) issues.push('Combined legacy raw SHA is not bound to the verified archive.');
    const entries = (Array.isArray(row.legacyEntries) ? row.legacyEntries : []).map(e => typeof e === 'string' ? e : e?.entry ?? e?.name ?? '').join('\n');
    for (const name of ['Install', 'Faults', 'PrepareDeferredFailure', 'VerifyDeferredFailure', 'PreparePlayer', 'RestartPlayerLive', 'InspectBuild', 'Cleanup'])
        if (!new RegExp('\\b' + name + '(?:\\s*\\(|$)', 'm').test(entries)) issues.push('Parent full-original entry review missing: ' + name);
    if (!Array.isArray(row.scenarioIds) ||
        !same([...row.scenarioIds].sort(), [...ids].sort())) issues.push('Combined review must hand off exactly both Fault and Player workflows.');
    const dependencies = dependencyPairs(row, record.audit);
    for (const [file, hash] of dependencies) {
        try { if (!shaPattern.test(hash ?? '') || sha256(io.readFileSync(localFile(root, file))) !== hash) issues.push('Combined support/replacement SHA stale or missing: ' + file); }
        catch { issues.push('Combined dependency unavailable: ' + file); }
    }
    for (const id of ids) {
        const scenario = catalog.scenarios.find(s => s.id === id);
        if (!scenario || scenario.runner !== 'node' || scenario.category !== 'regression' || scenario.requiresUnity !== true) {
            issues.push('Current regression native workflow metadata missing: ' + id); continue;
        }
        for (const file of unique([scenario.file, ...(scenario.supportFiles ?? []), ...(scenario.reviewFiles ?? [])]))
            if (!dependencies.some(([reviewed]) => reviewed === file)) issues.push('Combined review has no raw support SHA for selected dependency: ' + file);
    }
    const targets = records.filter(r => r.auditFile === 'unity-b-public.json' &&
        ['Tests~/Cases/UnityB/DocumentReleaseValidationTests.cs', 'Tests~/Cases/UnityB/FaultFixture.cs'].includes(r.row.newFile) &&
        r.row.sourceHashes?.legacy === original?.sha256 && shaPattern.test(r.row.sourceHashes?.replacement ?? ''));
    if (structured && (declaration?.supersedes?.auditFile !== 'unity-b-public.json' || targets.length !== 1 ||
        declaration.supersedes.newFile !== targets[0].row.newFile ||
        declaration.supersedes.sourceHashes?.legacy !== targets[0].row.sourceHashes.legacy ||
        declaration.supersedes.sourceHashes?.replacement !== targets[0].row.sourceHashes.replacement))
        issues.push('Structured declaration must match the exact obsolete B review hash identity.');
    if (targets.length !== 1)
        issues.push('Exact obsolete B review hash identity is missing or ambiguous.');
    return { approved: issues.length === 0, issues, targets: issues.length ? [] : targets,
        declaration, auditFile: record.auditFile, newFile: row.newFile,
        proof: 'Explicit current parent full-original source review only; Fault/Player runtime evaluated independently.' };
}

export function buildInventory({ root = defaultRoot, projectPath = path.resolve(root, '../..'), io = fs,
    names = auditNames, expectedManifestSha = frozenManifestSha } = {}) {
    root = path.resolve(root); projectPath = path.resolve(projectPath);
    const snapshots = new Map(), changedDuringRead = new Map();
    const trackedIo = { ...io, readFileSync(file, encoding) {
        const bytes = io.readFileSync(file);
        const absolute = path.resolve(file), actual = sha256(bytes);
        if (!snapshots.has(absolute)) snapshots.set(absolute, actual);
        else if (snapshots.get(absolute) !== actual)
            changedDuringRead.set(absolute, { file: absolute, expected: snapshots.get(absolute), observed: actual, kind: 'changed-during-source-read' });
        return encoding ? bytes.toString(encoding) : bytes;
    }, readdirSync: (...args) => io.readdirSync(...args) };
    const pending = [], sourceIssues = [], auditInputs = [], allRows = [];
    const archive = verifyFrozenLegacy(root, trackedIo, expectedManifestSha);
    const catalog = readJson(trackedIo, localFile(root, 'Tests~/scripts/test-catalog.json'));
    if (!Array.isArray(catalog.scenarios) || unique(catalog.scenarios.map(s => s.id)).length !== catalog.scenarios.length)
        throw Error('Current catalog scenarios are malformed/duplicated.');
    for (const name of names) {
        const file = 'Tests~/CoverageAudit/' + name;
        try {
            const audit = readJson(trackedIo, localFile(root, file));
            if (audit.version !== 1 || !Array.isArray(audit.files)) throw Error('Expected version:1/files audit schema.');
            auditInputs.push({ file, sha256: snapshots.get(localFile(root, file)), rows: audit.files.length });
            for (const row of audit.files) allRows.push({ auditFile: name, row, audit });
        } catch (error) { pending.push({ kind: 'audit-unavailable', file, detail: String(error) }); }
    }
    const grouped = new Map();
    for (const record of allRows) {
        if (typeof record.row.legacyFile !== 'string') { sourceIssues.push({ kind: 'audit-row-without-legacy-file', audit: record.auditFile }); continue; }
        const file = legacyName(record.row.legacyFile);
        if (!grouped.has(file)) grouped.set(file, []);
        grouped.get(file).push(record);
    }
    const combinedRelease = combinedDocReleasePolicy(grouped.get('DocumentReleaseValidation.cs') ?? [], archive, catalog, root, trackedIo);
    const currentProduction = productionSha(root, trackedIo);
    const scenarioReviewBindings = new Map();
    for (const record of allRows) {
        if (combinedRelease.targets.includes(record)) continue;
        // Apply only the explicitly authorized four-B source supersessions at this stage.
        const original = legacyName(record.row.legacyFile ?? '');
        if (record.auditFile === 'unity-b-public.json' && restoredB.has(original) &&
            (grouped.get(original) ?? []).some(r => r.auditFile === 'framework-and-auxiliary.json')) continue;
        const problems = [];
        if (record.row.sourceHashes?.legacy !== archive.manifest.files.find(f => f.file === original)?.sha256)
            problems.push('Legacy review hash not bound: ' + original);
        for (const [file, expected] of dependencyPairs(record.row, record.audit)) {
            try { if (!shaPattern.test(expected ?? '') || sha256(trackedIo.readFileSync(localFile(root, file))) !== expected) problems.push('Stale/unbound reviewed dependency: ' + file); }
            catch { problems.push('Unavailable reviewed dependency: ' + file); }
        }
        for (const id of record.row.scenarioIds ?? []) {
            const binding = scenarioReviewBindings.get(id) ?? { current: true, reviews: [], issues: [] };
            binding.current &&= problems.length === 0;
            binding.reviews.push({ auditFile: record.auditFile, legacyFile: original, newFile: record.row.newFile });
            binding.issues.push(...problems); scenarioReviewBindings.set(id, binding);
        }
    }
    const fingerprintCache = new Map();
    const context = { root, projectPath, io: trackedIo, catalog, scenarioReviewBindings, productionFingerprint: currentProduction,
        fingerprint(scenarios) {
            const key = JSON.stringify(scenarios.map(s => s.id));
            if (!fingerprintCache.has(key)) fingerprintCache.set(key, currentReviewFingerprint(scenarios, root, trackedIo));
            return fingerprintCache.get(key);
        } };
    const evidence = new Map(), reportIssues = [], reportInputs = [];
    const directory = path.join(projectPath, 'Temp/WhimTex/test-runs');
    let reportFiles = [];
    try { reportFiles = io.readdirSync(directory, { withFileTypes: true }).filter(e => !e.isDirectory() && !e.isSymbolicLink() && e.name.endsWith('.json')).map(e => path.join(directory, e.name)).sort(); }
    catch (error) { pending.push({ kind: 'runtime-report-directory-unavailable', detail: String(error) }); }
    for (const file of reportFiles) {
        let report;
        try { report = readJson(io, file); } // Reports can be actively growing: excluded from source snapshot stability checks.
        catch (error) { reportIssues.push({ file, kind: 'unreadable-or-in-progress-report', detail: String(error) }); continue; }
        reportInputs.push({ file, action: report.action ?? null, startedAt: report.startedAt ?? null });
        if (!Array.isArray(report.results)) continue; // Native artefacts/recovery journals are not uniform-runner reports.
        for (const result of report.results) {
            const scenario = catalog.scenarios.find(s => s.id === result.id);
            if (!scenario) { reportIssues.push({ file, kind: 'historical-unknown-scenario', id: result.id }); continue; }
            const event = inspectRuntimeReport(report, result, scenario, context);
            const timestamp = Date.parse(report.startedAt);
            event.chronologyKnown = Number.isFinite(timestamp);
            event.timestamp = Number.isFinite(timestamp) ? timestamp : 0;
            event.report = slash(path.relative(projectPath, file)); event.startedAt = report.startedAt ?? null;
            if (!Number.isFinite(timestamp)) {
                event.issues.push('No usable report start timestamp; chronology unavailable.');
                event.proof = 'not-current-proof';
            }
            if (!evidence.has(result.id)) evidence.set(result.id, []);
            evidence.get(result.id).push(event);
        }
    }
    const sourceFiles = archive.manifest.files.filter(item => sourceExtension.test(item.file));
    const inventory = unique([...sourceFiles.map(item => item.file), ...grouped.keys()]).sort();
    const files = inventory.map(legacyFile => {
        const original = archive.manifest.files.find(item => item.file === legacyFile);
        const records = grouped.get(legacyFile) ?? [];
        const parent = records.some(record => record.auditFile === 'framework-and-auxiliary.json');
        const superseded = parent && restoredB.has(legacyFile)
            ? records.filter(record => record.auditFile === 'unity-b-public.json') : [];
        if (legacyFile === 'DocumentReleaseValidation.cs') superseded.push(...combinedRelease.targets);
        const faultEvidence = chooseNewestEvidence(evidence.get('fault-release-workflow-v2') ?? []).latest;
        const nativeFaultVerified = faultEvidence?.proof === 'current-regression-run' && faultEvidence.nativeFixtureVerified === true;
        const obsoleteFixtureIdsRetired = records.filter(r => r.auditFile === 'framework-and-auxiliary.json')
            .some(r => (r.row.scenarioIds ?? []).some(id => !catalog.scenarios.some(s => s.id === id)));
        if (legacyFile === 'Fixtures/WhimTexImportFailureProbe.cs' && nativeFaultVerified &&
            (!obsoleteFixtureIdsRetired || combinedRelease.approved) && records.some(r => r.auditFile === 'fault.json'))
            superseded.push(...records.filter(r => r.auditFile === 'framework-and-auxiliary.json'));
        const selected = records.filter(record => !superseded.includes(record));
        const stale = [], missing = [], gaps = [], externalGaps = [], runtimePending = [], notes = [], subsets = [];
        if (legacyFile === 'DocumentReleaseValidation.cs' && combinedRelease.issues.length)
            runtimePending.push({ kind: 'parent-combined-doc-release-declaration-rejected', issues: combinedRelease.issues });
        stale.push(...archive.issues.filter(issue => issue.file === legacyFile));
        if (!original) gaps.push({ kind: 'audit-file-not-in-frozen-manifest', legacyFile });
        if (!selected.length) missing.push('No source review row.');
        const reviews = selected.map(({ row, auditFile, audit }) => {
            const issues = [];
            if (row.sourceHashes?.legacy !== original?.sha256) issues.push({ kind: 'legacy-review-sha-mismatch', expected: original?.sha256, actual: row.sourceHashes?.legacy });
            for (const [file, expected, kind] of dependencyPairs(row, audit)) {
                try {
                    const actual = sha256(trackedIo.readFileSync(localFile(root, file)));
                    if (!shaPattern.test(expected ?? '') || expected !== actual) issues.push({ kind: 'stale-or-missing-reviewed-hash', dependency: kind, file, expected: expected ?? null, actual });
                } catch (error) { issues.push({ kind: 'review-dependency-unavailable', dependency: kind, file: file ?? null, detail: String(error) }); }
            }
            if (!Array.isArray(row.coverage) || !row.coverage.length || !Array.isArray(row.legacyEntries) || !row.legacyEntries.length)
                missing.push(auditFile + ': concrete coverage/public-entry review evidence missing.');
            for (const text of row.gaps ?? []) {
                const gap = { auditFile, newFile: row.newFile, text };
                const type = splitGap(text);
                if (type === 'metadata') runtimePending.push({ ...gap, ...metadataGapEvidence(text, row, catalog) });
                else if (type === 'runtime-blocker') runtimePending.push({ ...gap, kind: 'unresolved-regression-runtime-blocker',
                    proof: 'original predicate retained; failed/unreached runtime coverage is not equivalence' });
                else (type === 'source' ? gaps : type === 'external' ? externalGaps : type === 'pending' ? runtimePending : type === 'subset' ? subsets : notes).push(gap);
            }
            // A current reviewed row with only runtime/external debt remains a source review, not successful execution.
            if (row.reviewStatus !== 'source-reviewed' && !(row.reviewStatus === 'gap' && (row.gaps ?? []).length && !(row.gaps ?? []).some(text => splitGap(text) === 'source')))
                gaps.push({ auditFile, kind: 'source-review-not-complete', reviewStatus: row.reviewStatus ?? null });
            stale.push(...issues.map(issue => ({ auditFile, ...issue })));
            return { auditFile, newFile: row.newFile, scenarioIds: row.scenarioIds ?? [], legacyEntries: row.legacyEntries ?? [],
                coverage: row.coverage ?? [], originalGaps: row.gaps ?? [], disposition: row.disposition ?? null,
                sourceHashes: row.sourceHashes ?? null, current: issues.length === 0, issues };
        });
        const ids = unique(selected.flatMap(record => record.row.scenarioIds ?? []));
        const runtime = ids.map(id => {
            const scenario = catalog.scenarios.find(s => s.id === id);
            if (!scenario) { runtimePending.push({ kind: 'scenario-not-in-current-catalog', id }); return { id, status: 'pending-catalog-integration', category: null }; }
            const item = { id, category: scenario.category, ...chooseNewestEvidence(evidence.get(id) ?? []),
                sourceReviewBinding: scenarioReviewBindings.get(id) ?? null };
            if (legacyFile === 'DocumentReleaseValidation.cs' && combinedRelease.approved && nativeFaultVerified &&
                ['document-release-validation-faults-v2', 'document-release-validation-deferred-v2'].includes(id))
                item.supersededRuntimeBy = 'fault-release-workflow-v2; native install + both bodies + removal verified';
            return item;
        });
        const categories = unique(runtime.map(item => item.category).filter(Boolean));
        const entryKinds = selected.flatMap(record => record.row.legacyEntries ?? []).filter(e => typeof e === 'object').flatMap(e => [e.kind, e.classification]).filter(Boolean);
        const dispositionKinds = selected.map(record => record.row.disposition?.kind ?? '').filter(Boolean);
        const explicitlyManual = dispositionKinds.some(k => /integrated-helper|manual|diagnostic/.test(k)) ||
            entryKinds.length > 0 && entryKinds.every(k => /manual|capture|diagnostic/.test(k));
        const classification = explicitlyManual ? 'manual-or-diagnostic'
            : categories.some(c => ['regression', 'source-guard', 'runner'].includes(c)) ? 'regression-linked'
            : categories.includes('diagnostic') ? 'diagnostic-only'
            : entryKinds.some(k => /manual|capture|diagnostic/.test(k)) || dispositionKinds.some(k => /manual|diagnostic/.test(k)) ? 'manual-or-diagnostic'
            : 'support-or-unregistered';
        if (subsets.length && legacyFile !== 'DocumentReleaseValidation.cs') gaps.push(...subsets.map(s => ({ ...s, kind: 'subset-without-explicit-combined-file' })));
        if (subsets.length && legacyFile === 'DocumentReleaseValidation.cs' && selected.length < 2) gaps.push({ kind: 'missing-other-document-release-subset', details: subsets });
        const regressions = runtime.filter(item => !item.supersededRuntimeBy && ['regression', 'source-guard', 'runner'].includes(item.category));
        const sourceCurrent = selected.length > 0 && !missing.length && !stale.length && !gaps.length;
        return { legacyFile, sourceFile: sourceExtension.test(legacyFile), classification,
            mergePolicy: legacyFile === 'DocumentReleaseValidation.cs' ? combinedRelease.approved
                ? 'explicit-parent-combined-source-review-obsolete-B-only; runtime-independent' : 'distinct-subsets-combined-no-source-suppression'
                : legacyFile === 'Fixtures/WhimTexImportFailureProbe.cs' && superseded.length ? 'exact-GUID-native-fixture-after-current-verified-fault-workflow'
                : superseded.length ? 'parent-restored-four-B-only' : 'all-review-rows-retained',
            supersededReviews: superseded.map(({ auditFile, row }) => ({ auditFile, newFile: row.newFile, sourceHashes: row.sourceHashes, gaps: row.gaps,
                reason: legacyFile === 'DocumentReleaseValidation.cs' ? combinedRelease.proof : legacyFile === 'Fixtures/WhimTexImportFailureProbe.cs' ? 'Current receipt/native input/per-case reviewed hashes + native install/both bodies/removal verified; no uniform global fixture green inferred.' : 'Explicit parent supersession for this restored B file only.' })),
            ...(legacyFile === 'DocumentReleaseValidation.cs' ? { parentCombinedDocRelease: {
                approved: combinedRelease.approved, declaration: combinedRelease.declaration ?? null,
                auditFile: combinedRelease.auditFile ?? null, newFile: combinedRelease.newFile ?? null,
                issues: combinedRelease.issues, proof: combinedRelease.proof ?? null } } : {}),
            reviews, scenarioIds: ids, missing, stale, sourceGaps: gaps, externalGaps, pending: runtimePending, notes, subsetResponsibilities: subsets,
            sourceReviewStatus: sourceCurrent ? 'current-reviewed-claims' : selected.length ? 'stale-or-incomplete-source-review' : 'missing-source-review',
            runtime, runtimeStatus: classification === 'diagnostic-only' || classification === 'manual-or-diagnostic' ? 'not-regression-equivalence'
                : regressions.length && regressions.every(r => r.status === 'current-passed') ? 'current-regression-reports-present' : 'pending-or-not-passed',
            equivalence: 'requires-human-oracle-and-subset-review; never-inferred-from-counts', archiveRemovalAllowed: false };
    });
    const snapshotChanges = [...changedDuringRead.values()];
    for (const [file, expected] of snapshots) {
        try { const actual = sha256(io.readFileSync(file)); if (actual !== expected) snapshotChanges.push({ file, expected, actual }); }
        catch (error) { snapshotChanges.push({ file, expected, detail: String(error) }); }
    }
    const sources = files.filter(f => f.sourceFile);
    if (snapshotChanges.length) {
        pending.push({ kind: 'source-freeze-required-before-runtime-binding', changedFiles: unique(snapshotChanges.map(change => change.file)) });
        for (const file of files) file.runtimeStatus = 'pending-source-freeze';
    }
    return { version: 1, scope: 'Read-only merged source-review freshness and current runtime evidence inventory; no automatic equivalence verdict.',
        archive: { ...archive, manifest: undefined }, auditInputs, reportInputs, pending, sourceIssues, reportIssues, snapshotChanges,
        summary: { expectedSourceFiles: sourceFiles.length, expectedSourceCountMatches360: sourceFiles.length === 360,
            sourceFilesWithReview: sources.filter(f => f.reviews.length).length,
            currentReviewedSourceClaims: sources.filter(f => f.sourceReviewStatus === 'current-reviewed-claims').length,
            missingReview: sources.filter(f => f.missing.length).length, staleReview: sources.filter(f => f.stale.length).length,
            sourceGapFiles: sources.filter(f => f.sourceGaps.length).length, externalGapFiles: sources.filter(f => f.externalGaps.length).length,
            regressionLinkedFiles: sources.filter(f => f.classification === 'regression-linked').length,
            currentRegressionReportFiles: sources.filter(f => f.runtimeStatus === 'current-regression-reports-present').length,
            diagnosticOnlyFiles: sources.filter(f => ['diagnostic-only', 'manual-or-diagnostic'].includes(f.classification)).length,
            supportOrUnregisteredFiles: sources.filter(f => f.classification === 'support-or-unregistered').length,
            snapshotStable: snapshotChanges.length === 0, automaticFullCoverage: false, archiveRemovalAllowed: false },
        archiveRemovalAllowed: false, automaticFullCoverage: false, files };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
    try {
        const argv = process.argv.slice(2), allowed = new Set(['--root', '--project-path']);
        const options = {};
        for (let i = 0; i < argv.length; i += 2) {
            if (!allowed.has(argv[i]) || !argv[i + 1]) throw Error('Usage: coverage-gate.mjs [--root PACKAGE] [--project-path PROJECT]');
            options[argv[i] === '--root' ? 'root' : 'projectPath'] = argv[i + 1];
        }
        console.log(JSON.stringify(buildInventory(options), null, 2));
    } catch (error) {
        console.log(JSON.stringify({ version: 1, error: String(error), archiveRemovalAllowed: false, automaticFullCoverage: false }));
        process.exitCode = 1;
    }
}
