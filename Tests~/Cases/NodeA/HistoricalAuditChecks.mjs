// Text-only audit fixtures and in-memory filesystem probes. No C# execution.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { auditHistoricalSources as auditSources, auditOutput, option }
    from '../../Framework/NodeSupportA/AuditSources.mjs';

const fixtureFile = new URL('./Fixtures/AuditInputs.json', import.meta.url);
const hash = source => createHash('sha256').update(source).digest('hex');

// Supplied file data is the whole virtual filesystem, including empty directories.
function memoryFiles(root, files) {
    const contents = new Map(Object.entries(files).map(([name, source]) => [path.resolve(root, name), source]));
    const directories = new Map(['Tests~', 'src', 'Documentation~'].map(name => [path.resolve(root, name), new Map()]));
    for (const name of contents.keys()) {
        let child = name;
        while (child !== root) {
            const parent = path.dirname(child);
            if (parent === child) throw Error('Virtual file outside root: ' + name);
            if (!directories.has(parent)) directories.set(parent, new Map());
            directories.get(parent).set(path.basename(child), !contents.has(child));
            child = parent;
        }
    }
    return {
        readdirSync(directory, options) {
            const entries = directories.get(path.resolve(directory));
            if (!entries) throw Error('Unknown virtual directory: ' + directory);
            return [...entries].map(([name, directory]) => options?.withFileTypes ? { name, isDirectory: () => directory } : name);
        },
        readFileSync(file, encoding) {
            const source = contents.get(path.resolve(file));
            if (source === undefined) throw Error('Unknown virtual file: ' + file);
            return encoding ? source : Buffer.from(source);
        }
    };
}

// Only historical test inputs are substituted. Production and documentation remain live.
function historicalTestInputs(root, fixture) {
    const directory = path.join(root, 'Tests~');
    const byName = new Map(fixture.files.map(input => [input.file, input.source]));
    return {
        readdirSync(file, options) {
            if (file === directory) return [...byName.keys()].map(name => options?.withFileTypes ? { name, isDirectory: () => false } : name);
            return fs.readdirSync(file, options);
        },
        readFileSync(file, encoding) {
            if (path.dirname(file) === directory && byName.has(path.basename(file))) {
                const value = byName.get(path.basename(file));
                return encoding ? value : Buffer.from(value);
            }
            return fs.readFileSync(file, encoding);
        }
    };
}

export function registerHistoricalCases(context, root) {
    const assert = context.assert;
    const read = file => fs.readFileSync(path.join(root, file), 'utf8');
    const fixture = JSON.parse(fs.readFileSync(fixtureFile, 'utf8'));
    let originalInputs;
    const inputs = () => originalInputs ??= auditSources(root, historicalTestInputs(root, fixture));
    const virtualRoot = path.join(root, 'Tests~', 'Cases', 'NodeA', 'virtual-input');

    context.case('Original audit assertions on exact historical text inputs and current production', () => {
        assert.equal(fixture.version, 1);
        assert.deepEqual(fixture.files.map(input => input.file), ['SoftRangeSmoke.cs', 'TwoChoiceDropdownSmoke.cs',
            'CanvasViewFooterSmoke.cs', 'ContentFillUiSmoke.cs', 'UvUiSmoke.cs', 'GradientClipboardCleanupSmoke.cs',
            'RemainingLegacyCleanupSmoke.cs', 'UserSettingsCleanupSmoke.cs']);
        for (const input of fixture.files) assert.equal(hash(Buffer.from(input.source)), input.sha256, input.file + ': exact source bytes');
        const { tests, candidates } = inputs();
        const audit = {tests, methodCandidates: candidates};
        for (const file of ['LayerPersistenceSetup.cs', 'LayerPersistenceVerify.cs', 'LayerPersistenceCleanup.cs', 'SpriteEditorCompileSmoke.cs'])
          assert.ok(!audit.tests.some(test => test.file === file), file);
        for (const [file, name] of [
          ['src/TextureCompositor.cs', 'CloneEmbeddedShaderFX'],
          ['src/TextureCompositor.cs', 'CloneDrawingLayerTextures'],
          ['src/Layers/SDFLayerBehaviour.cs', 'ConvertDistance'],
          ['src/MissingLayerRecovery.cs', 'GradientTime'],
          ['src/TextureCompositorWindow.UI.cs', 'FillRect'],
          ['src/WhimTexDocumentSerializer.cs', 'ReflectedFieldCount']
        ]) {
          assert.doesNotMatch(read(file), new RegExp('\\b' + name + '\\s*\\('), name);
          assert.ok(!audit.methodCandidates.some(candidate => candidate.name === name), name);
        }
        assert.equal(audit.tests.find(test => test.file === 'SoftRangeSmoke.cs').primary, 'SoftRangeSmoke.Main');
        assert.equal(audit.tests.find(test => test.file === 'TwoChoiceDropdownSmoke.cs').primary, 'TwoChoiceDropdownSmoke.Main');
        for (const file of ['CanvasViewFooterSmoke.cs', 'ContentFillUiSmoke.cs', 'UvUiSmoke.cs'])
          assert.equal(audit.tests.find(test => test.file === file).family, 'multi-step');
        for (const file of ['GradientClipboardCleanupSmoke.cs', 'RemainingLegacyCleanupSmoke.cs', 'UserSettingsCleanupSmoke.cs'])
          assert.equal(audit.tests.find(test => test.file === file).family, 'regression');
        // Original kernel predicates also remain in the final entry's live-source case.
    });

    context.case('Exact class/method regex grammar, Main preference and classification precedence', () => {
        const rows = [
            ['MainPriority.cs', 'public class Window {} internal static class Worker { public static void Run() {} public static async Task<List<int>> Main(string[] args, int n = 1) {} }', 'Worker.Main', 'regression', 'run_script'],
            ['Fallback.cs', 'internal class Plain { public static int Run() {} }', 'Plain.Run', 'regression', 'run_script'],
            ['Private.cs', 'public class Plain { private static void Main() {} internal static void Run() {} public void Main() {} }', null, 'multi-step', 'run_script'],
            ['Async.cs', 'public static class Async { public static async Task<string> Run(\n int value\n) {} }', 'Async.Run', 'regression', 'run_script'],
            ['Lowercase.cs', 'Public Class Wrong {} Public static void Main() {}', null, 'regression', 'eval_file'],
            ['Bare.cs', 'public static void Main() {}', 'null.Main', 'regression', 'eval_file'],
            ['DiagnosticSetup.cs', 'public static class Diagnostic { public static void Main() {} }', 'Diagnostic.Main', 'diagnostic', 'run_script'],
            ['Capture.cs', 'public class Capture {}', null, 'manual-helper', 'run_script'],
            ['CaptureWithMain.cs', 'public class Capture { public static void Main() {} }', 'Capture.Main', 'regression', 'run_script'],
            ['Marker.cs', '// Run CanvasViewFooterSetup\npublic class Marker { public static void Run() {} }', 'Marker.Run', 'multi-step', 'run_script'],
            ['MarkerWithoutClass.cs', '// Run ContentFillUiSetup\nDoWork();', null, 'multi-step', 'eval_file'],
            ['NearMarker.cs', '// run UvUiSetup\nDoWork();', null, 'regression', 'eval_file'],
            ['NoPrimary.cs', 'public class Window {}', null, 'multi-step', 'run_script'],
            ['ReturnComma.cs', 'public static class C { public static Dictionary<int, string> Main() {} }', null, 'multi-step', 'run_script'],
            ['Metadata.cs', 'public static class Meta { public static void Run() {} finally Guid.NewGuid() EditorPrefs "Assets/X" "Assets/X" "Temp/X" "Packages/X" "assets/no" AssetDatabase.CreateAsset AssetDatabase.CreateAsset File.WriteAllText SaveAndReimport EditorSceneManager BuildPipeline typeof(EditorGUIUtility) NonPublic\n}', 'Meta.Run', 'regression', 'run_script']
        ];
        const files = Object.fromEntries(rows.map(([name, source]) => ['Tests~/' + name, source]));
        files['Tests~/nested/Hidden.cs'] = 'public class Hidden {}';
        files['Tests~/Wrong.CS'] = 'public class Wrong {}';
        for (const token of ['Probe', 'Benchmark', 'Experiment', 'Audit', 'Equivalence', 'Diagnostic'])
            files['Tests~/' + token + '.cs'] = 'public class X {}';
        for (const token of ['Setup', 'Capture', 'Cleanup', 'HintResize', 'PersistenceVerify'])
            files['Tests~/Manual' + token + '.cs'] = '';
        const markers = ['CanvasViewFooterSetup', 'ContentFillUiSetup', 'UvUiSetup'];
        for (const [index, marker] of markers.entries()) {
            files['Tests~/Marker' + index + '.cs'] = '// Run ' + marker;
            files['Tests~/Call' + marker + '.cs'] = '// Run ' + marker;
        }
        for (const [key, text] of [['Focus', 'EditorWindow.focusedWindow'], ['Selection', 'Selection.activeObject'],
            ['Session', 'SessionState'], ['Prefs', 'EditorPrefs'], ['Clipboard', 'systemCopyBuffer']]) files['Tests~/State' + key + '.cs'] = text;
        const result = auditSources(virtualRoot, memoryFiles(virtualRoot, files));
        assert.deepEqual(result.tests.map(t => t.file), Object.keys(files).filter(f => !f.slice('Tests~/'.length).includes('/') && f.endsWith('.cs')).map(f => f.slice('Tests~/'.length)).sort());
        for (const [file, , primary, family, runner] of rows) {
            const actual = result.tests.find(t => t.file === file);
            assert.equal(actual.primary, primary, file + ': primary');
            assert.equal(actual.family, family, file + ': family');
            assert.equal(actual.runner, runner, file + ': runner');
        }
        const main = result.tests.find(t => t.file === 'MainPriority.cs');
        assert.deepEqual(main.entries, [{name:'Run',parameters:''}, {name:'Main',parameters:'string[] args, int n = 1'}]);
        assert.deepEqual(result.tests.find(t => t.file === 'Async.cs').entries, [{name:'Run',parameters:'\n int value\n'}]);
        for (const token of ['Probe', 'Benchmark', 'Experiment', 'Audit', 'Equivalence', 'Diagnostic'])
            assert.equal(result.tests.find(t => t.file === token + '.cs').family, 'diagnostic');
        for (const token of ['Setup', 'Capture', 'Cleanup', 'HintResize', 'PersistenceVerify'])
            assert.equal(result.tests.find(t => t.file === 'Manual' + token + '.cs').family, 'manual-helper');
        for (const [index, marker] of markers.entries()) {
            assert.equal(result.tests.find(t => t.file === 'Marker' + index + '.cs').family, 'multi-step');
            assert.equal(result.tests.find(t => t.file === 'Call' + marker + '.cs').family, 'manual-helper', 'Filename Setup precedence over source marker');
        }
        for (const key of ['Focus','Selection','Session','Prefs','Clipboard'])
            assert.equal(result.tests.find(t => t.file === 'State' + key + '.cs').userStateAccess, true);
        const flags = result.tests.find(t => t.file === 'Metadata.cs');
        assert.deepEqual(flags.paths, ['Assets/X', 'Temp/X', 'Packages/X']);
        assert.deepEqual(flags.assetMutations, ['AssetDatabase.CreateAsset','File.WriteAllText','SaveAndReimport','EditorSceneManager','BuildPipeline']);
        assert.equal(flags.hasFinally, true);
        assert.equal(flags.uniqueFixture, true);
        assert.equal(flags.unityInternalReflectionHint, true);
        assert.equal(flags.lines, 2);
        assert.equal(main.hasFinally, false);
        assert.equal(main.uniqueFixture, false);
        assert.equal(main.userStateAccess, false);
        assert.equal(main.unityInternalReflectionHint, false);
        for (const [text, expected] of [['typeof(EditorWindow) NonPublic',true], ['typeof(EditorGUIUtility) NonPublic',true],
            ['typeof(EditorWindow)',false], ['NonPublic',false], ['typeof(Other) NonPublic',false]]) {
            const probe = auditSources(virtualRoot, memoryFiles(virtualRoot, {'Tests~/Flags.cs':text}));
            assert.equal(probe.tests[0].unityInternalReflectionHint, expected, text);
        }
    });

    context.case('Candidate regex, exact word counts, recursive extensions and documentation site exclusion', () => {
        const methods = ['Unique','Referenced','NestedUse','ModuleUse','MarkdownUse','JsonUse','DocCsUse','DocModuleUse','SiteOnly','ExtensionOnly','On','DisposeMore'];
        const source = methods.map(name => 'private static void ' + name + '() {}').join('\n') + '\n' +
            ['OnDisable','Only','Dispose','Equals','GetHashCode','GetEnumerator'].map(name => 'internal void ' + name + '() {}').join('\n') + '\n' +
            'public void PublicOnly() {}\nprotected void ProtectedOnly() {}\ninternal static List<int>[] Generic(int value) {}\n';
        const files = {
            'src/nested/Methods.cs':source, 'src/ignored.txt':'Unique',
            'Tests~/X.cs':'Referenced ReferencedLonger', 'Tests~/nested/X.cs':'NestedUse',
            'Tests~/X.mjs':'ModuleUse', 'Tests~/X.md':'MarkdownUse', 'Tests~/ignored.json':'Unique',
            'Tests~/ignored.js':'Unique', 'Documentation~/X.json':'JsonUse', 'Documentation~/X.cs':'DocCsUse',
            'Documentation~/X.mjs':'DocModuleUse', 'Documentation~/_site/X.md':'SiteOnly',
            'Documentation~/ignored.txt':'ExtensionOnly', 'Documentation~/words.md':'UniqueLonger'
        };
        const actual = auditSources(virtualRoot, memoryFiles(virtualRoot, files));
        assert.deepEqual(actual.candidates, ['Unique','SiteOnly','ExtensionOnly','On','DisposeMore','Generic'].map(name => ({
            file:'src/nested/Methods.cs', name, line:source.slice(0, source.indexOf(name + '(')).split('\n').length, occurrences:1
        })));
        assert.ok(actual.candidates.every(c => c.name !== 'OnDisable' && c.name !== 'Only' && c.name !== 'Dispose'));
        const empty = auditSources(virtualRoot, memoryFiles(virtualRoot, {}));
        assert.deepEqual(empty, {tests:[],candidates:[]});
    });

    context.case('CLI output, full summary, slicing, numeric coercion and error semantics', () => {
        const original = inputs();
        const argv = ['node', 'AuditSources.mjs'];
        const full = auditOutput(original, argv);
        const summary = {regression:5,'multi-step':3};
        assert.deepEqual(full, {tests:original.tests,summary,methodCandidates:original.candidates});
        assert.deepEqual(auditOutput(original, [...argv,'--start','1','--count','2']), {tests:original.tests.slice(1,3),summary,methodCandidates:original.candidates});
        assert.deepEqual(auditOutput(original, [...argv,'--count','0']).tests, []);
        assert.deepEqual(auditOutput(original, [...argv,'--start','999999']).tests, []);
        assert.deepEqual(auditOutput(original, [...argv,'--count','999999']).tests, original.tests);
        assert.deepEqual(auditOutput(original, [...argv,'--candidates','--start','bad','--count','-1']), original.candidates);
        assert.deepEqual(auditOutput(original, [...argv,'--unknown','123']), full);
        assert.equal(option('--start',7,argv),7);
        assert.equal(option('--start',7,[...argv,'--start','2','--start','9']),2);
        for (const [text,value] of [['0',0],['-0',-0],['',0],[' ',0],['1e2',100],['0x10',16],['+2',2],['9007199254740991',9007199254740991]])
            assert.equal(option('--start',7,[...argv,'--start',text]),value,text);
        for (const flag of ['--start','--count']) for (const tail of [[], ['-1'], ['1.5'], ['Infinity'], ['NaN'], ['bad'], ['9007199254740992'], ['--other']])
            assert.throws(() => auditOutput(original,[...argv,flag,...tail]), {name:'Error',message:'Invalid '+flag});
        assert.deepEqual(auditOutput({tests:[],candidates:[]},argv),{tests:[],summary:{},methodCandidates:[]});
    });

    context.case('Standalone CLI body/guard/output/errors in an isolated read-only JavaScript context', () => {
        const helper = fileURLToPath(new URL('./AuditSources.mjs', import.meta.url));
        const originalArgv = process.argv.slice();
        const source = fs.readFileSync(helper,'utf8');
        // Execute the actual CLI body with only module syntax adapted for the isolated context.
        const script = source.replace("import fileSystem from 'node:fs';",'')
            .replace("import path from 'node:path';",'')
            .replace("import { fileURLToPath } from 'node:url';",'')
            .replace(/^export (?=const|function)/gm,'').replaceAll('import.meta.url','moduleUrl');
        assert.doesNotMatch(script,/^\s*(?:import|export)\b/m,'All module syntax adapters accounted for');
        const io = memoryFiles(root,Object.fromEntries(fixture.files.map(input=>['Tests~/'+input.file,input.source])));
        const data = auditSources(root,io);
        const run = argv => {
            let stdout = '';
            const sandbox = {fileSystem:io,path,fileURLToPath,moduleUrl:new URL('./AuditSources.mjs',import.meta.url).href,
                process:{argv},console:{log:message=>{stdout+=String(message)+'\n';}}};
            vm.runInNewContext(script,sandbox,{timeout:5000,filename:helper});
            return stdout;
        };
        for (const args of [[],['--candidates'],['--start','1','--count','2'],['--candidates','--count','bad']]) {
            assert.equal(run(['node',helper,...args]),JSON.stringify(auditOutput(data,['node',helper,...args]),null,2)+'\n');
        }
        for (const args of [['--start'],['--count','-1'],['--start','1.5']]) {
            assert.throws(()=>run(['node',helper,...args]),{name:'Error',message:'Invalid '+args[0]});
        }
        assert.equal(run(['node',fileURLToPath(import.meta.url),'--candidates']),'','Imported helper does not print CLI output');
        assert.equal(run([]),'','Missing argv[1] does not execute CLI');
        assert.equal(run(['node',path.relative(process.cwd(),helper),'--count','1']),JSON.stringify(auditOutput(data,['node',helper,'--count','1']),null,2)+'\n');
        assert.deepEqual(process.argv,originalArgv,'No borrowed argv mutation');
    });
}
