// Read-only source audit. Emits JSON to stdout; never writes files or invokes Unity.
// Token matches are source anchors, not runtime or coverage equivalence verdicts.
import fs from 'node:fs';
import crypto from 'node:crypto';

const batch = JSON.parse(fs.readFileSync('Tests~/Batches/unity-b.json', 'utf8'));
const manifest = JSON.parse(fs.readFileSync('Tests~/legacy-manifest.json', 'utf8'));
const blocked = new Set(['DocumentPreparationTests.cs', 'DocumentReleaseValidationTests.cs',
  'DocumentSaveTailTests.cs', 'EyedropperTests.cs', 'FileNavigationTests.cs']);
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
// Preserve strings and numeric spelling; remove comments/formatting only.
function tokens(source) {
  const pattern = /\/\*[\s\S]*?\*\/|\/\/[^\r\n]*|\$?@"(?:[^"]|"")*"|\$?"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|[A-Za-z_]\w*|(?:\d*\.\d+|\d+)(?:[Ee][+-]?\d+)?[A-Za-z]*|==|!=|<=|>=|&&|\|\||=>|\+\+|--|\?\?|\?\.|<<|>>|[^\s]/g;
  return [...source.matchAll(pattern)].filter(m => !m[0].startsWith('//') && !m[0].startsWith('/*'))
    .map(m => ({v:m[0], start:m.index, end:m.index+m[0].length,
      line:source.slice(0,m.index).split('\n').length}));
}
function end(t,i,open='(',close=')') {
  let depth=0;
  for(let j=i;j<t.length;j++){if(t[j].v===open)depth++;if(t[j].v===close&&--depth===0)return j;}
  return -1;
}
function text(t,a,b) { return t.slice(a,b).map(x=>x.v).join(' '); }
function key(t) { return t.map(x=>x.v ?? x).join('\u001f'); }
function peel(t) {
  while(t[0]?.v==='(' && end(t,0)===t.length-1)t=t.slice(1,-1);
  if(t[0]?.v==='!' && t[1]?.v==='(' && end(t,1)===t.length-1) {
    const inner=peel(t.slice(2,-1));
    if(inner[0]?.v==='!')return peel(inner.slice(1));
    return [{v:'!'},...inner];
  }
  return t;
}
function negate(t){t=peel(t);return t[0]?.v==='!'?peel(t.slice(1)):[{v:'!'},...t];}
function args(t,a,b) {
  const result=[];let depth=0,start=a;
  for(let i=a;i<b;i++){
    if(['(','[','{'].includes(t[i].v))depth++;
    if([')',']','}'].includes(t[i].v))depth--;
    if(t[i].v===','&&depth===0){result.push(t.slice(start,i));start=i+1;}
  }
  result.push(t.slice(start,b));return result;
}
function entries(t) {
  const out=[];
  for(let i=0;i<t.length;i++){
    if(t[i].v!=='public'||t[i+1]?.v!=='static')continue;
    let j=i+2;while(j<t.length&&!['(',';','{','=>'].includes(t[j].v))j++;
    if(t[j]?.v!=='(')continue;
    let k=end(t,j);if(k<0)continue;
    out.push({name:t[j-1].v,signature:text(t,i,k+1),line:t[i].line});i=k;
  }
  return out;
}
const helperNames = new Set(['Check','Assert','Equal','Near','Same','Different','Reject','Rejected','Expect','ExpectRejected']);
function oracles(t) {
  const out=[];
  for(let i=0;i<t.length;i++) {
    if(helperNames.has(t[i].v)&&t[i+1]?.v==='(') {
      const k=end(t,i+1);if(k<0)continue;
      if(['{','=>'].includes(t[k+1]?.v))continue; // Helper declaration, not invocation.
      const a=args(t,i+2,k);
      const boolean=t[i].v==='Check'||t[i].v==='Assert';
      out.push({line:t[i].line,kind:boolean?'predicate':'helper',
        value:boolean?key(peel(a[0])):key(t.slice(i,k+1)),
        message:boolean?key(a.slice(1).flat()):'',code:text(t,i,k+1)});
    }
    if(t[i].v==='if'&&t[i+1]?.v==='(') {
      const k=end(t,i+1);let j=k+1;
      if(t[j]?.v==='{')j++;
      if(t[j]?.v!=='throw'||t[j+1]?.v!=='new')continue;
      let p=j+2;while(p<t.length&&!['(',';','{'].includes(t[p].v))p++;
      if(t[p]?.v!=='(')continue;
      const q=end(t,p);if(q<0)continue;
      out.push({line:t[i].line,kind:'predicate',value:key(negate(t.slice(i+2,k))),
        message:key(t.slice(p+1,q)),code:text(t,i,q+1)});
    }
  }
  return out;
}
function controls(t) {
  const out=[];
  for(let i=0;i<t.length;i++)if(['for','foreach','while','switch'].includes(t[i].v)&&t[i+1]?.v==='('){
    const k=end(t,i+1);if(k>=0)out.push({line:t[i].line,code:text(t,i,k+1),tokens:t.slice(i,k+1)});
  }
  return out;
}
const notes = {
  'DocumentBurstHashProbe.cs': ['Run(runId, mebibytes=64, runs=3) retains original default budget, first worker call, known vectors including million-a, lengths 0..256 at offsets 0/1/13, twelve MiB boundaries, four concurrent states and reversed timing order. Non-default bounded mebibytes/runs remain callable but are not separately selected.'],
  'DocumentPerformanceProbe.cs': ['Run(size=2048,layers=3,hdr=false,randomPixels=true) retains seeded pixel patterns, four save/load measurements, timestamp and layer-count correctness gates. Four registered scenarios cover HDR/Standard x random/constant at 2048/3. Windows P/Invoke sampler was replaced with public Process.PrivateMemorySize64/WorkingSet64; native sampling mechanism is not identical. Other allowed size/layer budgets remain callable, unselected.'],
  'DocumentJsonApiSmoke.cs': ['Restored original Assets/Learn/Pass/Sphere_Distortion.tiff read input with its actual import metadata/storage context; same document payload feeds API create/revision/resize/serialize/validate/Compact/insert/replace. All writes target owned JSON. Added source TIFF and .meta byte-equality oracles. Requires the same pre-existing sample asset as Legacy.'],
  'DocumentJsonContractSmoke.cs': ['Restored original Assets/Learn/Steam.png reference with its actual GUID/path/localId/import settings; original missing-reference literal retained. Full/inactive settings, Drawing omission, shared FX, snapshot overwrite and external-change rejection retain original oracles. Added source PNG and .meta byte-equality oracles. Requires the same pre-existing PNG as Legacy.'],
  'DocumentJsonSmoke.cs': ['Run(start=0,count=8) uses the same sorted two package TIFF fixtures, all JSON modes and 0.00001 render delta. Additional Run(1,1) preserves ranged invocation; invalid start/count guard remains.'],
  'DocumentPreparationSmoke.cs': ['PARENT OWNED: notes only. Parent restored actual Save rejection for Assets/../<owned GUID>.tiff and Assets/StreamingAssets/<owned GUID>.tiff, adding no-destination/no-meta assertions. Same invalid components as escape.tiff; NormalizeDestination rejects StreamingAssets prefix or the .. component regardless of filename. Parent reports pass; current shared-helper hash requires rerun.'],
  'DocumentReleaseValidation.cs': ['PARENT OWNED: Faults and deferred retry oracle retained. Install, PrepareDeferredFailure/VerifyDeferredFailure and Cleanup were folded into owned lifecycle; original Player PreparePlayer/RestartPlayerLive/InspectBuild must remain distinct reachable checks. User now explicitly permits test-only Unity reflection and Player build; parent owns restoration/execution.'],
  'DocumentSaveTailProbe.cs': ['PARENT OWNED: profiling=true default, ReadProfile, Frames/raw-frame/sample >=2ms, top25 and frame >=30ms/WhimTexSaveProbe selection now authored with authorized UnityEditorInternal.ProfilerDriver. Parent reports no-profiler pass; profiling and raw capture require validation/metadata. No profiler execution here.'],
  'EyedropperSmoke.cs': ['PARENT OWNED: seven original GUIView/m_Parent, StealMouseCapture, SetEyeDropperOpen, capture container, ContainerWindow.SetInvisible, SetCurrentViewCursor and Vector2 desktop position availability assertions now restored alongside original CPU inputs. Parent reports pass; current shared-helper hash requires rerun. Legacy did not exercise real desktop input capture.'],
  'FileNavigationSmoke.cs': ['PARENT OWNED: conditional DockArea identity/shared-tab-host check now restored with same dockAvailable predicate, plus explicit parent-field availability. Parent reports conditional pass; current shared-helper hash requires rerun.'],
  'DocumentReloadSmoke.cs': ['Read-only review of reload files only. Prepare/Verify mapped to Begin/Trigger/ReloadPoll/Verify via Node. Carrier-only fallback is not full surviving-window coverage. Parent reports prior real-original reload proof with 50/96 checks for document/guide workflows and will bind reports; this agent did not execute or independently validate that evidence. Current fingerprints require parent validation.'],
  'GuideReloadSmoke.cs': ['Read-only review of reload files only. Prepare/Verify mapped to GUID-owned native reload orchestration with original hidden/locked/snap/position/angle/document-switch checks. Parent reports prior real-original 50/96-check reload evidence and will bind reports; no reload execution or proof binding by this agent.'],
  'DocumentSaveCostProbe.cs': ['Restored Container(layers=4,opaque=true): same 64 MiB/layer seeded data, three streamed writes, cache hit/size/timing measurements; added streamed nonempty and exact raw/deflated prepared-output byte checks in bounded 65536-byte chunks (does not compare the untouched input block to itself). Restored CacheSafety: created/SetPixel/Apply/raw-write updateCount observations and opaque=true/false 1 MiB native cache cases; added serialized-byte equality, same-key raw-edit rejection/no insertion and recovery hit. Reports for all five entries included in structured message. Hashes, FastFingerprint and Tiff retain original oracles.', 'PROPOSED parent metadata (not applied): Container(layers,opaque) for layers=1/2/3/4 x opaque=true/false, IDs document-save-cost-probe-container-{layers}-{opaque|alpha}-v2, category diagnostic, timeoutMs 240000. CacheSafety(), ID document-save-cost-probe-cache-safety-v2, category regression, timeoutMs 60000. Both use runner run_script, effects [user-state], supportFiles [Tests~/Framework/TestApi.cs,Tests~/Cases/UnityB/UnityBSupport.cs], groups [unity-b,unity,independent-port], same reviewed idle-editor/owned-cache prerequisites as existing file.'],
  'HistogramArithmeticAudit.cs': ['Restored Main: seeded Random(55) 17x13 RGBAFloat input, scheduled Configure snapshot, managed Build.Execute for all four channels, per-table changed count/max/first and per-statistic difference report. Added finite table/statistic checks without inventing a zero-difference threshold; report included in structured message. Memory preserves nested rent isolation, reuse and >30s idle disposal.', 'PROPOSED parent metadata (not applied): histogram-arithmetic-audit-main-v2, entry HistogramArithmeticAuditTests.Main, args [], runner run_script, category diagnostic, timeoutMs 60000, effects [user-state], supportFiles [Tests~/Framework/TestApi.cs,Tests~/Cases/UnityB/UnityBSupport.cs], groups [unity-b,unity,independent-port]; same reviewed graphical-editor/package-shader prerequisites as Memory.'],
  'HistogramSeamlessSmoke.cs': ['Main retains seven degenerate/odd/large sizes x five constant/transparent/gray/random/HDR fixtures, Random(928), finite RGBA, alpha bounds, exact center and caller render-state oracles. Restored Preview requires exact project-relative output/copy-blend-lab-2026-09-28/00_source.png; no substitution if absent, source bytes preserved, original sRGB-load/linear-ARGBFloat render/gamma PNG and input/output float dumps retained. Restored Benchmark uses 256/512/1024, x/size,y/size,.3,1 RGBAFloat linear input, one warmup and median of three synchronous Render/Read measurements, finite/nonnegative time without fabricated performance threshold. Restored ReferenceFixtures uses sequential Random(391), 64x48/17x13/128x128 and exact original RGBA ranges; binary dimensions+float payload lengths checked. Dumps are diagnostic reference material, NOT invented golden oracles. GUID no-overwrite/link-guarded Temp/WhimTex/diagnostics outputs survive cleanup. Parent must append three scenarios from proposal, diagnostic timeout240000.'],
  'ExportWindowSmoke.cs': ['Run retains seven exports, JSON modes, EXR float16/float32 x ZIP/RLE/PIZ/None, JPEG quality, cancellation, stale source, Drawing warning and source/render-state invariants. Raster exports now survive scope disposal via EvidencePath. Restored ShowVisual/ShowExrVisual/CloseVisual use a persistent N-GUID SessionState journal with object ID/name/asset guards and original-window exclusion: original 16x16 Drawing, 2x2 pixels, JSON 420x280 and EXR 360x240 controls checked; only owned windows/documents/pixels closed, prior focus restored, failed cleanup keeps journal. VisualSequence exercises bodies/cleanup but is NOT a screenshot/layout oracle. Public three-step manual workflow remains open between invocations for real human/native visual inspection.'],
  'GradientHistorySmoke.cs': ['Setup+Verify+Cleanup combined into History(runId), retaining 40 HSV history colors plus HDR, real layout wait, Gamma/Linear, alpha/midpoint guards, recency and duplicate prevention. PickerRecency remains independent. Restored Setup/Capture/PollCapture/Cleanup require one persistent N-GUID and no pre-existing picker/gradient window to avoid singleton acquisition. Same 40 HSV+HDR document and 560x420 view. Capture uses supported Unity ImmediateModeElement.ImmediateRepaint and Texture2D.ReadPixels from active target/framebuffer, scales by pixelsPerPoint, PNG nonempty, state restored and owned texture disposed. No OS native capture, no private Unity capture API, no manufactured golden. No actual repaint yields explicit failure/prerequisite, never a successful capture. Durable link/no-overwrite GUID diagnostics survive cleanup. CaptureWorkflow adds async wait/poll/finally owned cleanup; parent must append scenario with temp-files effect. API references: https://docs.unity3d.com/cn/6000.0/ScriptReference/UIElements.ImmediateModeElement.html ; https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/texture2d/readpixels'],
  'GradientKeyPickerSmoke.cs': ['Setup+Verify combined into Interactions(runId) with actual layout wait and Gamma/Linear x HDR/Standard, single/double click, capture, alpha, stale callback and midpoint oracles. FocusLifecycle separately retains close/Escape/reopen and unrelated-picker isolation.'],
  'HealingNoiseSeamDiagnostic.cs': ['Not selected by B72 despite correctness gates: smooth/FBm Perlin ColorValues scale8 seed1337, 256 canvas, size64 hardness.8 search64 Balanced/current, actual tiled vertical+horizontal strokes; retain curvature 1.25/1.4, seam .5, donor deviation .9..1.1 and fine contrast 1.04/1.02, finite/opaque checks.']
};
const knownGaps = {
  'DocumentReleaseValidation.cs': ['PARENT PREREQUISITE: native Faults/Deferred failure-probe fixture is currently absent; parent is authoring a selectable native fixture driver. SKIP without fixture is not original failure-path proof. Player preparation/build/live restart/build-report proof also remains parent-owned. Inspect/bind actual selected driver, assertion/input branches and retained artifacts before equivalence.', 'PARENT NOTE: inspect any remaining TempPath diagnostic JSON and migrate to EvidencePath if post-run inspection is required.'],
  'DocumentSaveTailProbe.cs': ['Parent-authored profiling=true and ReadProfile require current-fingerprint runtime proof; no-profiler pass cannot establish raw-capture equivalence. Parent reports EvidencePath restoration for retained probe/profile JSON; no implementation edit by this agent.'],
  'DocumentReloadSmoke.cs': ['Parent-reported real-original 50/96-check reload reports require evidence binding/current-fingerprint validation; carrier-only branch is not full original coverage.'],
  'GuideReloadSmoke.cs': ['Parent-reported real-original 50/96-check reload reports require evidence binding/current-fingerprint validation.'],
  'HistogramSeamlessSmoke.cs': ['Public Preview/Benchmark/ReferenceFixtures restored, but three additional scenario registrations and current-source runtime/input artifacts remain pending parent; Preview needs exact original source file, and diagnostics are not comparison goldens.'],
  'ExportWindowSmoke.cs': ['Public three-step visual ability restored, but parent must append VisualSequence and preserve manual GUID workflow for actual visual inspection; no original screenshot/layout oracle has been executed here.'],
  'GradientHistorySmoke.cs': ['Supported public repaint Capture restored; append async CaptureWorkflow metadata and validate actual framebuffer PNG on supported graphical Unity Editor. Missing visible window/repaint is an explicit prerequisite/failure, not equivalent successful capture.']
};

// Manually reviewed contextual adaptations. These explain specific non-identical source
// anchors, never turn a missing runtime invocation into a completed check.
const contextual = {
  'DocumentPerformanceProbe.cs:31': ['samplingError', 'Platform-native Windows GetProcessMemoryInfo failure propagation adapted to public Process.Refresh/PrivateMemorySize64/WorkingSet64 exceptions; sampler thread is stopped/joined and samplingError rethrown. Whole-editor private/working-set metric intent retained; platform API difference is documented, not a missing original assertion or a fabricated memory threshold.'],
  'ExportWindowSmoke.cs:158': ['VisualJournal', 'Original name-selected ShowVisual window scan replaced by persistent GUID journal and pre-existing window exclusion; same JSON 16x16 Drawing/2x2 input and 420x280 controls. New windows must match owned owner/source and ID/name.'],
  'ExportWindowSmoke.cs:170': ['Only journal-owned', 'CloseVisual resolves all journal identities before destruction, closes only owned objects, never pre-existing windows; cleanup failures retain journal and success restores focus. Public manual persistent lifecycle retained.'],
  'ExportWindowSmoke.cs:182': ['ReadJournal(runId)', 'ShowExrVisual now retrieves only the same journal-owned window with ID/name/asset/original-window guards, then applies original format3 and 360x240 size. No broad name acquisition.'],
  'DocumentPreparationSmoke.cs:117': ['RejectSaveDestination', 'Actual WhimTexDocumentFile.Save is invoked inside RejectSaveDestination with Assets/../ plus owned GUID; identical .. rejection branch, additional no-file/no-meta oracle. Filename uniqueness is an ownership adaptation.'],
  'DocumentPreparationSmoke.cs:118': ['RejectSaveDestination', 'Actual Save with Assets/StreamingAssets/ plus owned GUID; identical rejected prefix branch, additional no-file/no-meta oracle.'],
  'DocumentReloadSmoke.cs:15': ['context.True(condition, message)', 'Local Check routes predicate to TestContext.True; TestApi.True throws on false. Removed FAIL message prefix is result-format adaptation.'],
  'DocumentReloadSmoke.cs:48': ['UnityBReload.ValidateAssetDirectory', 'Original owned-prefix/name check replaced by GUID-scoped ValidateAssetDirectory in ReloadSupport; asset root and ownership validated before creating/reusing directory.'],
  'GuideReloadSmoke.cs:18': ['context.True(condition, message)', 'Local Check routes identical condition/message to TestContext.True with false rejection.'],
  'DocumentSaveTailProbe.cs:69': ['UnityBRun.NextUpdate', '5000ms bounded Editor update wait moved to shared NextUpdate; same event completion plus cancellation/finally detachment.'],
  'DocumentSaveTailProbe.cs:102': ['Stop existing profiling', 'Same enabled/deepProfiling idle precondition expressed as Check(!enabled && !deepProfiling).'],
  'DocumentSaveTailProbe.cs:213': ['Unsafe cleanup target', 'Cleanup guard now requires exact Assets/WhimTexTestMigration descendant rather than historical filename prefix. Parent source uses bounded owned GUID root.'],
  'GradientClipboardCleanupSmoke.cs:177': ['UnityBRun.IsOwnedTemp', 'Original Temp/name cleanup guard adapted to IsOwnedTemp + DeleteTemp: only unique system-temp GUID root/descendants accepted; link-safe containment checks in UnityBTemp. Original preset and unknown-transition checks unaffected.'],
  'GradientHistorySmoke.cs:84': ['UnityBRun.Start', 'History task owns window/document/session via UnityBRun.Create/Track; cleanup is shared owned-scope disposal after task drains, not a broad Resources scan by display name.'],
  'GradientHistorySmoke.cs:85': ['UnityBRun.Start', 'Same task-owned document cleanup supersedes name-based scan; resources created by History are owned and cleaned together.']
};

// Verify every frozen file, not just files assigned to this batch.
for(const f of manifest.files){const bytes=fs.readFileSync('Tests~/Legacy/'+f.file);if(hash(bytes)!==f.sha256||bytes.length!==f.bytes)throw new Error('Frozen archive mismatch: '+f.file);}
const files = batch.replacements.map(r=>{
  const legacyBytes=fs.readFileSync('Tests~/Legacy/'+r.legacyFile),replacementBytes=fs.readFileSync(r.newFile);
  const a=legacyBytes.toString('utf8'),n=replacementBytes.toString('utf8');
  const sourceHashes={legacy:hash(legacyBytes),replacement:hash(replacementBytes)};
  const at=tokens(a),nt=tokens(n),ao=oracles(at),no=oracles(nt),ntKey=key(nt);
  const gaps=[...(knownGaps[r.legacyFile]??[])];
  const coverage=[`Frozen Legacy SHA-256 ${sourceHashes.legacy}; reviewed replacement SHA-256 ${sourceHashes.replacement}. Source evidence only; no runtime-equivalence conclusion.`,...(notes[r.legacyFile]??[])];
  if(n.includes('UnityBRun.EvidencePath('))coverage.push('Diagnostic outputs use parent-owned UnityBRun.EvidencePath within Run scope: GUID Temp/WhimTex/diagnostics tree, no existing file overwrite or link traversal, evidence retained after transient cleanup. Parent metadata requires temp-files effect and current shared-helper fingerprint; runtime artifact inspection pending.');
  for(const o of ao){
    const m=no.find(x=>x.kind===o.kind&&x.value===o.value&&x.message===o.message);
    if(m)coverage.push(`Oracle Legacy:${o.line} -> replacement:${m.line}: ${o.code}`);
    else if(contextual[r.legacyFile+':'+o.line] && n.includes(contextual[r.legacyFile+':'+o.line][0]))
      coverage.push(`Contextual oracle Legacy:${o.line}: ${o.code}; ${contextual[r.legacyFile+':'+o.line][1]}`);
    else {
      coverage.push(`Oracle requiring contextual review Legacy:${o.line}: ${o.code}`);
      gaps.push(`No exact predicate/helper-and-message token match for Legacy:${o.line}; inspect contextual adaptation before equivalence: ${o.code}`);
    }
  }
  for(const c of controls(at)){
    const atIndex=ntKey.indexOf(key(c.tokens));
    if(atIndex>=0)coverage.push(`Input/branch loop retained Legacy:${c.line}: ${c.code}`);
    else if(contextual[r.legacyFile+':'+c.line] && n.includes(contextual[r.legacyFile+':'+c.line][0]))
      coverage.push(`Contextual input/cleanup branch Legacy:${c.line}: ${c.code}; ${contextual[r.legacyFile+':'+c.line][1]}`);
    else if(c.tokens[0].v==='for'&&n.includes('CheckCancellation')&&c.code.includes('iterations'))
      coverage.push(`Input/branch Legacy:${c.line}: ${c.code}; cancellation check inserted into same iteration bound.`);
    else {coverage.push(`Input/branch requiring contextual review Legacy:${c.line}: ${c.code}`);gaps.push(`No exact loop/switch token match at Legacy:${c.line}: ${c.code}`);}
  }
  const oldEntries=entries(at);
  coverage.push(...oldEntries.map(e=>`Original entry Legacy:${e.line}: ${e.signature}; selected replacements: ${r.scenarios.map(s=>s.entry??s.file).join(', ')}`));
  if(!oldEntries.length)coverage.push('Legacy is a top-level run_script snippet; replacement public Run/Main entry wraps its independent body.');
  coverage.push(...r.scenarios.map(s=>`Registered input ${s.id}: ${s.entry??s.file}(${JSON.stringify(s.args??[])}); category=${s.category}; runner=${s.runner}.`));
  if(blocked.has(r.newFile.split('/').at(-1)))coverage.push('Parent-owned blocked file: notes only; this audit agent did not edit implementation. Reflection/Player approval is now explicit, but parent work/runtime evidence remains separate.');
  return {legacyFile:r.legacyFile,newFile:r.newFile,scenarioIds:r.scenarios.map(s=>s.id),
    legacyEntries:oldEntries.length?oldEntries.map(e=>`${e.signature} [Legacy:${e.line}]`):['top-level run_script snippet'],
    coverage,gaps,reviewStatus:gaps.length?'gap':'source-reviewed',runtimeStatus:'pending-parent-validation',sourceHashes};
});
for(const e of batch.exclusions)files.push({legacyFile:e.legacyFile,newFile:e.newFile??null,scenarioIds:[],legacyEntries:[],coverage:[],gaps:[`Excluded original requires explicit coverage review: ${JSON.stringify(e)}`],reviewStatus:'gap',runtimeStatus:'pending-parent-validation'});
const report={version:1,scope:'All replacements/exclusions in Batches/unity-b.json against frozen Legacy (466 hashes verified). Exact assertion/helper token anchors and loop/input evidence are conservative source evidence; contextual mismatches stay gaps. Full Legacy coverage and runtime equivalence are not inferred from checks counts, entry presence, source matches or prior B72 passes. Parent owns blocked implementations, shared helpers, reload support, batch/catalog/global metadata and all Unity validation. No Unity commands/build/assets/compilation executed by this audit.',files};
const start=Number(process.argv[2]??0),count=Number(process.argv[3]??files.length);
if(process.argv.includes('--summary'))console.log(JSON.stringify(files.map(f=>({file:f.legacyFile,gaps:f.gaps})),null,2));
else console.log(JSON.stringify({...report,files:files.slice(start,start+count)},null,2));
