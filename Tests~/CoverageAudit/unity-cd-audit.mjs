// Read-only source inventory. This does not execute Unity or assert runtime equivalence.
import path from 'node:path';
import crypto from 'node:crypto';
import { legacyIO } from '../scripts/legacy.mjs';
const root = path.resolve(import.meta.dirname, '../..');
const io = legacyIO(root);
const read = p => io.readFileSync(path.join(root, p), 'utf8');
const hash = p => crypto.createHash('sha256').update(io.readFileSync(path.join(root, p))).digest('hex');
function masked(s) {
  // Preserve offsets; ignore delimiters inside comments and C# strings/chars.
  return s.replace(/\/\/[^\n]*|\/\*[\s\S]*?\*\/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/g,
    x => x.replace(/[^\n]/g, ' '));
}
function end(s, start, open, close) {
  let depth = 0;
  for (let i = start; i < s.length; i++) {
    if (s[i] === open) depth++;
    if (s[i] === close && --depth === 0) return i;
  }
  throw new Error('Unbalanced source at ' + start);
}
function calls(s, pattern) {
  const m = masked(s), out = [];
  for (const match of m.matchAll(pattern)) {
    const at = m.indexOf('(', match.index), to = end(m, at, '(', ')');
    out.push({ line: s.slice(0, match.index).split('\n').length, text: s.slice(match.index, to + 1), contents: s.slice(at + 1, to) });
  }
  return out;
}
function canon(s) {
  return s.replace(/\/\/[^\n]*|\/\*[\s\S]*?\*\//g, '')
    .replace(/(?:global::)?WhimTex\.Tests\.Unity[CD]\.FixtureContext\.(?:Scope|Context)\./g, '')
    .replace(/\bcontext\.True\(/g, 'True(')
    .replace(/(?:global::)?WhimTex\.Tests\.UnityD\.MigrationD\./g, '')
    .replace(/\b(?:WhimTex\.Tests\.UnityD\.)?PublicInput\.Click/g, 'Click')
    .replace(/\bExecute(?=[A-Z])/g, '')
    .replace(/\b[A-Za-z_][\w]*(?:Smoke|Tests)\b/g, 'Case')
    .replace(/\bQuiltingEquivalence\b/g, 'Case')
    .replace(/\s+/g, '');
}
function assertionKey(c) {
  // Compare original failing predicates with the port's explicit negation.
  let x = c.contents;
  if (/\.True\(|\bTrue\(/.test(c.text) && x.trim().startsWith('!(')) {
    const at = x.indexOf('('), to = end(masked(x), at, '(', ')');
    return 'reject:' + canon(x.slice(at + 1, to));
  }
  return 'check:' + canon(x);
}
function checks(s) {
  const assertions = calls(s, /\b(?:Check|True|Assert|ExpectError)\s*\(/g)
    .filter(c => !/^\s*(?:bool|string|Action|System\.Action)\s+\w+\s*(?:,|$)/.test(c.contents));
  const guards = calls(s, /\bif\s*\(/g).filter(c => /^\s*throw\s+new\s+(?:[\w.]*Exception)\s*\(/.test(s.slice(s.indexOf(c.text, s.split('\n').slice(0, c.line - 1).join('\n').length) + c.text.length)))
    .map(c => ({ ...c, key: 'reject:' + canon(c.contents) }));
  return [...assertions.map(c => ({ ...c, key: assertionKey(c) })), ...guards];
}
function methods(s) {
  const m = masked(s), out = [];
  const re = /\b(?:(?:public|private|internal|protected)\s+)?(?:(?:static|async)\s+)*[\w.<>,\[\]?]+\s+(\w+)\s*\(/g;
  for (const x of m.matchAll(re)) {
    const at = m.indexOf('(', x.index), to = end(m, at, '(', ')');
    if (/^\s*(?:return|throw|new|using|else)\b/.test(x[0])) continue;
    if (!/^\s*(?:\{|=>)/.test(m.slice(to + 1))) continue;
    out.push({ name: x[1], signature: s.slice(x.index, to + 1).trim(), line: s.slice(0, x.index).split('\n').length,
      kind: /\bpublic\b/.test(x[0]) ? 'public-entry-or-public-helper' : 'helper-or-top-level-local-function' });
  }
  return out;
}
const records = [];
for (const g of ['c', 'd']) {
  for (const r of JSON.parse(read('Tests~/Batches/unity-' + g + '.json')).replacements) {
    const legacy = 'Tests~/Legacy/' + r.legacyFile, old = read(legacy), now = read(r.newFile);
    const currentChecks = checks(now), keys = new Set(currentChecks.map(c => c.key));
    const originals = checks(old).map(c => {
      let disposition = keys.has(c.key) ? 'predicate-and-message-retained-source-only' : 'requires-semantic-review';
      if (disposition === 'requires-semantic-review' && /^reject:!(value|ok|condition|b|v|valid|yes|test)$/.test(c.key))
        disposition = 'reviewed: original Check helper failure delegates to TestContext.True with the same condition/message; counting is not equivalence proof';
      if (r.legacyFile === 'NewDocumentWindowSmoke.cs' && c.line === 31)
        disposition = 'reviewed: ReferenceEquals preserves exact document identity and the same layer-count/unsaved predicates';
      if (r.legacyFile === 'NativeManualFxFileSmoke.cs' && c.line === 75)
        disposition = 'reviewed: cleanup ownership assertion now requires the exact Assets/WhimTexTestMigration/UnityC-GUID folder';
      if (r.legacyFile === 'ShapeNamingSmoke.cs' && c.line === 20)
        disposition = 'reviewed: exact expected/actual string equality delegates to TestContext.Equal';
      if (r.legacyFile === 'SmallDocumentSaveProbe.cs' && c.line === 132)
        disposition = 'reviewed: unique GUID folder nonexistence/creation is validated by MigrationD.AssetFolder';
      if (r.legacyFile === 'SmallDocumentSaveProbe.cs' && c.line === 204)
        disposition = 'reviewed: noOpTimestampPreserved is directly asserted true; original no-rewrite condition retained';
      if (r.legacyFile === 'SmallDocumentSaveProbe.cs' && c.line === 214)
        disposition = 'reviewed: cleanup path restricted to exact parent Assets/WhimTexTestMigration, GUID child and DeleteAsset guard';
      if (r.legacyFile === 'UvUiSmoke.cs' && c.line === 5)
        disposition = 'reviewed: externally named setup-window prerequisite replaced by self-contained identical owned mesh/window setup, with layout delay';
      if (r.legacyFile === 'WhimTexGradientReloadSmoke.cs' && c.line === 13)
        disposition = 'reviewed: duplicate fixture guard is per-run GUID SessionState rather than the old shared key';
      if (r.legacyFile === 'WhimTexGradientReloadSmoke.cs' && c.line === 35)
        disposition = 'reviewed: End asserts exact GUID-named restored host exists after a real reload';
      if (r.legacyFile === 'WhimTexGradientReloadSmoke.cs' && c.line === 40)
        disposition = 'reviewed: HDR key 4, alpha .3 and midpoint .23 are independently asserted with TestContext.Equal';
      return { line: c.line, assertion: c.text, key: c.key, disposition };
    });
    const oldLoops = calls(old, /\b(?:for|foreach|while)\s*\(/g);
    const newLoops = new Set(calls(now, /\b(?:for|foreach|while)\s*\(/g).map(c => canon(c.text)));
    const entries = methods(old);
    if (!/\bpublic\s+static\s+(?:class|(?:async\s+)?[\w.<>,\[\]?]+\s+\w+\s*\()/.test(masked(old)))
      entries.unshift({ name: '(top-level eval body)', signature: '(top-level eval body)', line: 1, kind: 'public-eval-entry' });
    const replacementMethods = methods(now);
    for (const entry of entries) {
      entry.replacementMethods = replacementMethods.filter(m => m.name === entry.name || m.name === 'Execute' + entry.name).map(m => m.signature);
      if (entry.name === '(top-level eval body)') entry.replacementMethods = replacementMethods.filter(m => ['Body', 'Execute', 'ExecuteMain', 'ExecuteRun'].includes(m.name)).map(m => m.signature);
      entry.scheduledEntries = r.scenarios.filter(s => (s.entry ?? '').endsWith('.' + entry.name) || entry.name === '(top-level eval body)' ||
        (entry.name === 'Main' && (s.entry ?? '').endsWith('.Run')) || (entry.name === 'Bindings' && (s.entry ?? '').endsWith('.StartBindings'))).map(s => s.id);
      entry.review = 'Body/helper formulas reviewed in the same source; scheduled entries are separate from helper declarations. Runtime remains pending.';
    }
    records.push({ legacyFile: r.legacyFile, legacySha256: hash(legacy), replacement: r.newFile,
      replacementSha256: hash(r.newFile), proofLevel: 'source-inventory-only; runtime equivalence not established',
      originalEntries: entries, scheduledInputs: r.scenarios.map(s => ({ id: s.id, entry: s.entry ?? s.runner,
        args: s.args, category: s.category, prerequisites: s.prerequisites })),
      originalChecks: originals,
      originalInputBranches: calls(old, /\bif\s*\(/g).map(c => ({ line: c.line, condition: c.contents })),
      originalLoops: oldLoops.map(c => ({ line: c.line, input: c.text, sourceRetained: newLoops.has(canon(c.text)) })),
      originalFormulaLines: old.split('\n').map((text, i) => ({ line: i + 1, formula: text.trim() }))
        .filter(x => /Mathf?\.|Math\.|(?:[+*/%]|\s-\s)\s*(?:[\d.]|[a-zA-Z_])/.test(x.formula) && !x.formula.startsWith('//')),
      originalCleanupLines: old.split('\n').map((text, i) => ({ line: i + 1, cleanup: text.trim() }))
        .filter(x => /finally|Dispose\(|DestroyImmediate\(|ReleaseTemporary\(|\.Close\(|DeleteAsset\(|ClearUndo\(|RevertAllDownToGroup\(|RenderTexture.active\s*=|GL.sRGBWrite\s*=/.test(x.cleanup)),
      gaps: [], runtimeValidation: 'Not executed: parent owns sequential Unity validation.' });
  }
}
const external = {
  'PatchQuiltingEquivalence.cs': ['Benchmark and CurrentTiming: original live_4.bin unavailable; bodies are ported but await explicit reviewed path/SHA-256/provenance. Main/Managed synthetic frozen-search comparisons do not prove this real-input branch.'],
  'PatchQuiltingContrastSmoke.cs': ['Capture: original live_4.bin unavailable; restored green-channel .2/.45 width, 0/.5/1 compensation image sheet and 3 warmups/15 samples await original input. No direct standalone image verdict; Render helper state guard still fails on errors.'],
  'PatchQuiltingFeatherSmoke.cs': ['Capture: original live_4.bin unavailable; restored green-channel .02/.45 width, 0/50/100 feather image sheet awaits original input/manual review; no direct original visual assertion.'],
  'QuiltingAlongSearchSmoke.cs': ['Capture: original live_4.bin unavailable; restored green-channel 0/.125/.25 range image/timing output and seven samples await original input/manual review; Render helper guard retained.'],
  'MirrorEnhancementsSmoke.cs': ['Preview: original-path copy-blend-source input is explicitly registered by the parent with reviewed path/SHA-256/provenance; native Preview execution and manual artifact review remain pending. Original four compensation/correction combinations restored; no direct original visual assertion. Existing PNG is input only, not an algorithm golden; no prior frozen PNG digest proves historical byte continuity.'],
  'SeamlessControlsSmoke.cs': ['Preview: original-path copy-blend-source input is explicitly registered by the parent with reviewed path/SHA-256/provenance; native Preview execution and manual artifact review remain pending. Two algorithms and four edge masks plus RGBA float/PNG output restored; no direct original visual assertion. Existing PNG is input only, not an algorithm golden; no prior frozen PNG digest proves historical byte continuity.'],
  'ScreenedSeamlessSmoke.cs': ['Preview: original screened-poisson-lab-2026-09-28/00_original.png unavailable; source-decode float output, current-render float output and display PNG restored; no original visual assertion.'],
  'SeamlessOptimizationSmoke.cs': ['Compare/Audit: 252 pre-change float snapshots unavailable. Ported read-only authenticated comparisons retain original caller-state/finiteness, Compare 2e-5 tolerance, Audit delta-only branch. Native Capture intentionally skips; CaptureHistorical ports the 252-case producer and caller-state guard but requires a separately reviewed pre-change renderer and is not a native scenario. Its outputs are diagnostic artifacts requiring explicit path/hash/provenance review. No current-algorithm golden data or historical runtime-equivalence claim.']
};
for (const r of records) {
  // Missing historical inputs are runtime prerequisites, not missing restored source.
  r.gaps.push(...(external[r.legacyFile] ?? []).map(message => 'External prerequisite: ' + message));
  const unknown = r.originalChecks.filter(c => c.disposition === 'requires-semantic-review');
  if (unknown.length) r.gaps.push(...unknown.map(c => `Unresolved source assertion at Legacy:${c.line}: ${c.assertion}`));
}
const files = records.map(r => ({ legacyFile: r.legacyFile, newFile: r.replacement,
  scenarioIds: r.scheduledInputs.map(x => x.id), legacyEntries: r.originalEntries,
  coverage: [
    ...r.originalChecks.map(c => `Legacy:${c.line} assertion ${c.assertion}; ${c.disposition}`),
    ...r.originalLoops.map(c => `Legacy:${c.line} input ${c.input}; ${c.sourceRetained ? 'loop header retained' : 'adapted or missing loop, reviewed separately'}`),
    ...r.originalInputBranches.map(c => `Legacy:${c.line} branch ${c.condition}`),
    ...r.originalFormulaLines.map(c => `Legacy:${c.line} formula ${c.formula}`),
    ...r.originalCleanupLines.map(c => `Legacy:${c.line} cleanup ${c.cleanup}`)
    , 'Cleanup review: original owned resources/finally operations retained or restricted to GUID fixtures; render state, focus, clipboard, selection, Undo and scheduled work are released/restored by the declared C/D support sources. Runtime cleanup failures remain failures.'
  ], gaps: r.gaps, reviewStatus: r.gaps.some(gap => !gap.startsWith('External prerequisite: ')) ? 'gap' : 'source-reviewed',
  runtimeStatus: 'pending-parent-validation',
  sourceHashes: { legacy: r.legacySha256, replacement: r.replacementSha256 }, scheduledInputs: r.scheduledInputs,
  proofLevel: r.proofLevel }));
const supplements = [
  ['NoisePeriodicReference.csv', 'Tests~/Cases/UnityC/NoisePeriodicReference.csv', 'CSV standard fixture: 5184 independent CPU reference rows; six types, two dimensions, axes/fractal/warp/sample inputs. Only CRLF to LF differs; all field values identical.'],
  ['NoisePeriodicReference.stress.csv', 'Tests~/Cases/UnityC/NoisePeriodicReference.stress.csv', 'CSV stress fixture: 1296 independent CPU reference rows; four extreme profiles, original coordinates/scales/octave/warp settings; only CRLF to LF differs.'],
  ['ShaderFXVSCodeMetadata.cases.json', 'Tests~/Cases/UnityD/ShaderFXVSCodeMetadata.cases.json', 'Parameterized parser fixtures: parsed JSON is identical; source hashes differ due to formatting only. All expected values/error inputs retained.'],
  ['UvUiSetup.cs', 'Tests~/Cases/UnityD/UvUiTests.cs', 'Original no-assertion setup helper integrated into owned asynchronous Start: same eleven vertices/UVs, triangles, three islands, 512x512 canvas, tool/drawer values and mesh; focus/model/mesh/window cleanup is self-contained.'],
  ['UvUiCapture.cs', 'Tests~/Cases/UnityD/UvUiCaptureDiagnostic.cs', 'Original screenshot helper has no image correctness assertions, but requires the named setup window and produces a PNG using InternalEditorUtility.ReadScreenPixel. Current body is explicitly blocked; automated UV assertions do not replace this manual capture.']
];
for (const [legacyFile, newFile, summary] of supplements) {
  // A formerly excluded helper may now have its own explicit replacement row.
  if (files.some(f => f.legacyFile === legacyFile)) continue;
  const old = read('Tests~/Legacy/' + legacyFile), now = read(newFile);
  if (legacyFile.endsWith('.csv') && old.replace(/\r/g, '') !== now.replace(/\r/g, '')) throw new Error('Reference data changed: ' + legacyFile);
  if (legacyFile.endsWith('.json') && JSON.stringify(JSON.parse(old)) !== JSON.stringify(JSON.parse(now))) throw new Error('Parameterized data changed: ' + legacyFile);
  const linked = records.filter(r => r.replacement === newFile || (legacyFile.startsWith('NoisePeriodicReference') && r.legacyFile === 'NoisePeriodicGpuSmoke.cs') ||
    (legacyFile.startsWith('ShaderFXVSCodeMetadata') && r.legacyFile === 'ShaderFXVSCodeMetadataSmoke.cs'));
  files.push({ legacyFile, newFile, scenarioIds: linked.flatMap(r => r.scheduledInputs.map(x => x.id)),
    legacyEntries: [{ name: legacyFile.endsWith('.cs') ? '(top-level helper)' : '(parameterized fixture data)', kind: 'helper-or-fixture' }],
    coverage: [summary], gaps: legacyFile === 'UvUiCapture.cs' ? ['Original manual screenshot producer is not ported: unsupported internal screen-pixel access remains unavailable under current authority. Human approval for test-only eyedropper/dock/profiler reflection does not cover a new screen capture dependency. No fake screenshot or green verdict.'] : [],
    reviewStatus: legacyFile === 'UvUiCapture.cs' ? 'gap' : 'source-reviewed', runtimeStatus: 'pending-parent-validation',
    sourceHashes: { legacy: hash('Tests~/Legacy/' + legacyFile), replacement: hash(newFile) } });
}
const manifest = JSON.parse(read('Tests~/legacy-manifest.json'));
const archiveFailures = manifest.files.filter(f => hash('Tests~/Legacy/' + f.file) !== f.sha256 || io.statSync(path.join(root, 'Tests~/Legacy/', f.file)).size !== f.bytes).map(f => f.file);
if (archiveFailures.length) throw new Error('Frozen archive differs: ' + archiveFailures.join(', '));
const report = { version: 1, scope: ['UnityC', 'UnityD'], methodology: 'Actual frozen source predicates, public and helper signatures, inputs, branches, formula lines and cleanup reviewed separately from runtime. No claim from check counts. Current inputs require parent validation.',
  archive: { manifestSha256: hash('Tests~/legacy-manifest.json'), filesVerified: manifest.files.length, failures: archiveFailures },
  supportSourceHashes: Object.fromEntries(['Tests~/Cases/UnityC/UnityCFixture.cs', 'Tests~/Cases/UnityC/ReviewedOracle.cs', 'Tests~/Cases/UnityD/MigrationD.cs', 'Tests~/Cases/UnityD/AsyncD.cs', 'Tests~/Cases/UnityD/GradientReload.mjs', 'Tests~/CoverageAudit/unity-cd-oracles.json'].map(f => [f, hash(f)])),
  slowRegressionInputs: { ids: [0, 2, 3, 4].map(m => 'seamless-release-stress-2048-mode-' + m + '-v2'), args: [0, 2, 3, 4].map(m => [2048, m]), nativeTimeoutMs: 60000,
    requestedTimeoutMs: 180000, reason: 'Three 2048x1536 render/readbacks and about 37.7M component assertions per mode; Legacy documents combined 2K timeout. Keep regression classification; diagnostic workflow is not an override for regressions.', runtimeStatus: 'pending-parent-validation' }, files };
const selected = process.argv.indexOf('--offset');
if (selected >= 0) report.files = files.slice(Number(process.argv[selected + 1]), Number(process.argv[selected + 1]) + 5);
if (process.argv.includes('--restore-bodies')) {
  const names = new Set(['PatchQuiltingContrastSmoke.cs', 'PatchQuiltingFeatherSmoke.cs', 'QuiltingAlongSearchSmoke.cs', 'MirrorEnhancementsSmoke.cs', 'SeamlessControlsSmoke.cs', 'ScreenedSeamlessSmoke.cs']);
  console.log(JSON.stringify(records.filter(r => names.has(r.legacyFile)).map(r => {
    const old = read('Tests~/Legacy/' + r.legacyFile), now = read(r.replacement);
    const method = r.legacyFile.includes('Quilting') ? 'Capture' : 'Preview';
    const entry = methods(old).find(m => m.name === method), mask = masked(old);
    const start = old.split('\n').slice(0, entry.line - 1).join('\n').length + 1;
    const brace = mask.indexOf('{', start), to = end(mask, brace, '{', '}');
    const stub = new RegExp('    static string Execute' + method + '\\(\\)\\s*\\n    \\{ return null;[^\\n]*\\}').exec(now)?.[0];
    const wrapper = now.split('\n').find(l => new RegExp('public static string ' + method + '\\(').test(l));
    if (!stub || !wrapper) throw new Error('Missing stub ' + r.legacyFile);
    return { legacyFile: r.legacyFile, file: r.replacement, method, body: old.slice(brace, to + 1), stub, wrapper };
  })));
}
else if (process.argv.includes('--json')) console.log(JSON.stringify(report));
else if (process.argv.includes('--gaps')) console.log(JSON.stringify(files.filter(f => f.gaps.length).map(f => ({ file: f.legacyFile, gaps: f.gaps }))));
else if (process.argv.includes('--formula-gaps')) {
  for (const r of records) {
    const current = canon(read(r.replacement));
    const missing = r.originalFormulaLines.filter(x => !current.includes(canon(x.formula)))
      .filter(x => !/Check\(|throw |if\(|if \(|new |DestroyImmediate|ReleaseTemporary|return |File\.|GetPixels|GetRawTextureData|\.Close\(|SetPixel|Write|Append|Read|^\/\//.test(x.formula));
    if (missing.length) console.log(JSON.stringify({ file: r.legacyFile, formulas: missing }));
  }
}
else for (const r of records) {
  const misses = r.originalChecks.filter(c => c.disposition === 'requires-semantic-review');
  const loops = r.originalLoops.filter(c => !c.sourceRetained);
  if (misses.length || loops.length) console.log(JSON.stringify({ file: r.legacyFile, checks: misses.map(c => c.line + ':' + c.assertion), loops: loops.map(c => c.line + ':' + c.input) }));
}
