// Read-only manifest-bound record of the parent's source review. Not runtime evidence.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { verifyLegacy } from '../scripts/legacy.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const hash = file => createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex');
verifyLegacy(root);
const rows = [
    ['TestRunner.test.mjs', 'Tests~/Framework/Runner.test.mjs', ['framework'], ['Node cases'], [
        'Original 24 runner cases retained: catalog validation, selectors, receipt sensitivity, transport/API/execution/assertion distinctions, eval unwrapping, explicit JSON success, Node exit/SKIP/timeout, CLI units and project, async polling/failure/timeout/cleanup.',
        'Legacy core selection is explicitly legacy-core, not silently changed to the independent core. Shared counted Node assertions replace node:test scheduling.',
        'Additional structured result, lifecycle cancellation, empty assertion, source bundling, compile-only, entry-only, nested uncertainty and bounded Player/diagnostic budget guards do not replace old checks.'
    ]],
    ['RunnerProtocolSmoke.cs', 'Tests~/Cases/Runner/NegativeProtocol.cs', ['runner-live-v2'], ['Pass', 'InnerFailure', 'AssertionFailure', 'NeedsArgument', 'Skip', 'Start', 'Result', 'Cleanup'], [
        'Expected negative and polling fixtures are infrastructure, not successful domain assertions. LiveRunner invokes every original negative class with the same verdict, verifies asynchronous failed poll, cleanup, and fresh-call empty session.',
        'Thrown execution failure is still distinguished from structured assertion failure. GUID-owned keys replace globally shared fixture state.'
    ]],
    ['TestRunnerLive.test.mjs', 'Tests~/Cases/Runner/LiveRunner.mjs', ['runner-live-v2'], ['Node module evaluation'], [
        'All five old negative entry classes retain their expected verdicts; thrown old AssertionFailure is covered by ExecutionFailure, with structured assertion failure additionally tested separately.',
        'Old asynchronous StartFailure/Result/Cleanup and subsequent CheckCleanup are retained with GUID-owned state and counted fresh-call assertions; transport uncertainty remains a stop condition.'
    ]],
    ['GaussianKernelMath.mjs', 'Tests~/Cases/Math/GaussianKernel.mjs', ['gaussian-kernel-math-v2'], ['Node module evaluation'], [
        'Complete original kernel/edge/premultiplied arithmetic body retained within one counted case: sizes 1,2,3,17,64,65; 12 radii through256; Transparent/Clamp/Repeat/Mirror; epsilon1e-10.',
        'Scalar versus paired samples, constant HDR normalization, 65-pixel alpha impulse/rgb8 unpremultiplication retained. CPU-only, not GPU validation.'
    ]],
    ['PsdWriter/Program.cs', 'Tests~/Cases/Export/PsdWriter.cs', ['psd-writer-v2', 'psd-writer-fixture-v2'], ['Main'], [
        'All original seed42/9widths/5patterns PackBits rows, nine-layer descriptors including Unicode/nested/group/clipping/fill/stroke/gradient, exact merged RGBA/matte orientation and disposal checks preserved.',
        'Exact original signature/version/depth/RGB, negative alpha count, section lengths, channel encoding, trailing-byte rejection and width30001 rejection preserved.',
        'Optional old arbitrary output argument becomes explicit GUID CreateNew artifact under project Temp; current encoder compiled through native Editor, not a separate build system.'
    ]],
    ['Compatibility0125.test.mjs', 'Tests~/Cases/Files/Frozen0125.mjs', ['frozen-files-v2'], ['Node cases'], [
        'Original frozen file SHA-256, canonical layer/parameter forms and nested records/images retained; independent paired migration comparison explicitly validates facts and counts for this reviewed pair.'
    ]],
    ['DisplayChannelsSmoke.cs', 'Tests~/Cases/Rendering/DisplayChannels.cs', ['display-channels-v2'], ['Run'], [
        'Six original RGBA samples by16 masks, original component outputs/tolerance retained; no graphics/API behavior inferred from CPU assertions.'
    ]],
    ['DocumentMigrationProbe.cs', 'Tests~/Cases/Serialization/CanonicalReader.cs', ['canonical-reader-v2'], ['Run'], [
        'Original canonical scalar/array JSON accepted and unsupported historical representations rejected; same reflected reader input/output branches. Independent paired migration report required.'
    ]],
    ['Fixtures/WhimTexImportFailureProbe.cs', 'Tests~/Cases/UnityB/FaultFixture.cs', ['fault-release-workflow-v2'], ['OnPreprocessTexture'], [
        'Same .failimport marker and context.LogImportError trigger retained in generated GUID-specific native Editor class. Prefix narrowed to its exact authorized Assets/WhimTexTestMigration/GUID folder.',
        'Installed native MonoScript.GetClass identity, original failure/retry bodies and absence from all assemblies after cleanup reload are checked by the selectable fault lifecycle; old ephemeral global-probe ports retired.'
    ]],
    ['DocumentReleaseValidation.cs', 'Tests~/Cases/UnityB/FaultFixture.cs', ['fault-release-workflow-v2', 'player-release-workflow-v2'],
        ['Install()', 'Faults()', 'PrepareDeferredFailure()', 'VerifyDeferredFailure()', 'PreparePlayer()', 'RestartPlayerLive()', 'InspectBuild()', 'Cleanup()'], [
        'Full original reviewed independently. Write interruptions/cancellation/external writes/locked destination, inert orphan, exact file/meta/live/build/recovery guards, failed-import/retry/newest contents and deferred retryable dirty binding retain their original inputs and assertions in FaultFixture.',
        'PlayerReleaseTests retains green256 TIFF, two disabled Drawing1024 RGBA32 seed17/18, identical flattened Plain and Standalone Compressed128 DXT5 with mips, unsaved red live state, packed Texture2D-only/positive-size/1024-byte delta, no shipped WhimTex assembly and runtime green/non-readable checks. Additional prebuild red-pixel evidence does not replace original Player assertions.',
        'Old global SessionState key/folder prefix adapted to CreateNew per-GUID journals, exact AssetDatabase folder/script/scene GUIDs, old-domain marker and verified native install/removal. Existing fixture refusal and deletion assertions remain; cleanup enumerates only exact owned document markers, drains live work and clears only owned recovery, preserving borrowed scene/selection/focus.',
        'Faults/Deferred now share one selectable native-fixture lifecycle; Player is a separate explicitly authorized player-build lifecycle. Source handoff is not a Player build/runtime PASS and cannot certify full coverage while either runtime proof is absent.'
    ]],
    ['scripts/run-tests.mjs', 'Tests~/scripts/run-tests.mjs', ['framework', 'runner-live-v2'], ['CLI dispatcher'], [
        'Infrastructure replacement keeps explicit bounded invocation, structured API versus inner execution versus domain assertion results, async polling with cleanup, review fingerprints, native source bundling and explicit project targeting.',
        'Old text verdicts remain only for explicitly archived invocations. New cases cannot pass with zero checks, malformed output, missing cleanup or contradictory failed process exit.'
    ]],
    ['DocumentPreparationSmoke.cs', 'Tests~/Cases/UnityB/DocumentPreparationTests.cs', ['document-preparation-smoke-v2'], ['Run'], [
        'All original owned document preparation checks retained; public Save rejection actually invoked for traversal and StreamingAssets paths, not substituted by a pure validator.',
        'User-approved unique GUID rejected destinations; no pre-existing file/meta may be touched. Added no-file/no-meta assertions and exact-path finally cleanup only if a regression writes test output.'
    ]],
    ['EyedropperSmoke.cs', 'Tests~/Cases/UnityB/EyedropperTests.cs', ['eyedropper-smoke-v2'], ['Run'], [
        'Original seven Unity internal capture/cursor/window availability bindings retained with approved test-only reflection; this is availability coverage, not actual desktop capture.',
        'Original CPU sampling1/3/11, alpha0/.25/1 and rectangle bounds/center offsets preserved. No OS-native implementation added.'
    ]],
    ['FileNavigationSmoke.cs', 'Tests~/Cases/UnityB/FileNavigationTests.cs', ['file-navigation-smoke-v2'], ['Run'], [
        'Original document opening/count/reuse/focus/protected document checks retained with only owned temporary asset paths.',
        'Original conditional docked DockArea host identity assertion restored using approved test-only m_Parent binding. No available dock host means branch not exercised, not proof of docking.'
    ]],
    ['DocumentSaveTailProbe.cs', 'Tests~/Cases/UnityB/DocumentSaveTailTests.cs', ['document-save-tail-probe-no-profiler-v2', 'document-save-tail-probe-profile-v2', 'document-save-tail-read-profile-v2'], ['Run', 'ReadProfile'], [
        'Original save timings, trial inputs and default profiler branch restored; ProfilerDriver frame range/rawFrameDataView, ms>=2/top25 and total>=30 or save probe samples retained.',
        'Caller profiler enabled/deep state is not interrupted; editor/CPU area state restored in finally. ReadProfile captures observational data, not a new performance threshold.'
    ]]
];
const batches = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/Batches/framework-and-auxiliary.json')));
const catalog = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/scripts/test-catalog.json')));
const ids = new Set(catalog.scenarios.map(s => s.id));
const files = rows.map(([legacyFile,newFile,scenarioIds,legacyEntries,coverage]) => ({
    legacyFile,newFile,scenarioIds,legacyEntries,coverage,
    gaps: scenarioIds.filter(id => !ids.has(id)).map(id => 'Invocation not yet integrated: ' + id),
    reviewStatus: 'source-reviewed', runtimeStatus: 'pending-parent-validation',
    sourceHashes: { legacy: hash('Tests~/Legacy/' + legacyFile), replacement: hash(newFile) }
}));
const combined = files.find(r => r.legacyFile === 'DocumentReleaseValidation.cs');
const obsolete = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/CoverageAudit/unity-b-public.json'))).files.find(r => r.legacyFile === combined.legacyFile);
const nativeFiles = ['Tests~/Cases/UnityB/FaultWorkflow.mjs', 'Tests~/Cases/UnityB/PlayerWorkflow.mjs',
    'Tests~/Cases/UnityB/PlayerRelease.mjs', 'Tests~/Cases/UnityB/PlayerReleaseTests.cs', 'Tests~/Cases/Support/PlayerProbe.cs'];
combined.sourceHashes.support = Object.fromEntries(nativeFiles.map(file => [file, hash(file)]));
combined.parentCombinedDocRelease = { version:1, fullOriginalRead:true, sourceReplacementReviewed:true,
    handoffScenarioIds:combined.scenarioIds,
    supersedes:{auditFile:'unity-b-public.json',newFile:obsolete.newFile,
        sourceHashes:{legacy:obsolete.sourceHashes.legacy,replacement:obsolete.sourceHashes.replacement}} };
files.find(r => r.legacyFile === 'Fixtures/WhimTexImportFailureProbe.cs').sourceHashes.support = {
    'Tests~/Cases/UnityB/FaultWorkflow.mjs':hash('Tests~/Cases/UnityB/FaultWorkflow.mjs')};
console.log(JSON.stringify({ version:1, scope:'Parent-reviewed framework, auxiliary and four blocked UnityB source ports. Source hashes must be rebound to current runtime evidence; no archive deletion authorized.', files }, null, 2));
