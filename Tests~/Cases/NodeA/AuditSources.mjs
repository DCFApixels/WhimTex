// Independent read-only implementation of the historical discovery contract.
// Regexes and precedence are deliberate compatibility behavior, not a C# parser.
import fileSystem from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');

export function auditSources(root = packageRoot, io = fileSystem) {
    const fs = io;
    const walk = directory => fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        const full = path.join(directory, entry.name);
        return entry.isDirectory() ? walk(full) : [full];
    });
    const read = file => fs.readFileSync(file, 'utf8');
    const relative = file => path.relative(root, file).replaceAll('\\', '/');
    const top = fs.readdirSync(path.join(root, 'Tests~')).filter(file => file.endsWith('.cs')).sort();
    const tests = top.map(file => {
        const source = read(path.join(root, 'Tests~', file));
        const entryClass = /(?:public|internal)\s+static\s+class\s+(\w+)/.exec(source)?.[1]
            ?? /(?:public|internal)\s+class\s+(\w+)/.exec(source)?.[1] ?? null;
        const entries = [...source.matchAll(/public\s+static\s+(?:async\s+)?[\w<>\[\].]+\s+(\w+)\(([^)]*)\)/g)]
            .map(match => ({ name: match[1], parameters: match[2] }));
        const primary = entries.find(entry => entry.name === 'Main') ?? entries.find(entry => entry.name === 'Run');
        const operations = [...source.matchAll(/AssetDatabase\.(?:DeleteAsset|CreateAsset|CreateFolder|SaveAssets|Refresh)|File\.(?:Write\w*|Delete)|SaveAndReimport|EditorSceneManager|BuildPipeline/g)].map(match => match[0]);
        const paths = [...source.matchAll(/"(?:Assets|Packages|Temp)\/[^"\r\n]*/g)].map(match => match[0].slice(1));
        const family = /Probe|Benchmark|Experiment|Audit|Equivalence|Diagnostic/.test(file) ? 'diagnostic'
            : !primary && /Setup|Capture|Cleanup|HintResize|PersistenceVerify/.test(file) ? 'manual-helper'
            : /Run (?:CanvasViewFooterSetup|ContentFillUiSetup|UvUiSetup)/.test(source) || entryClass && !primary ? 'multi-step'
            : 'regression';
        return { file, family, runner: entryClass ? 'run_script' : 'eval_file',
            primary: primary ? entryClass + '.' + primary.name : null, entries,
            assetMutations: [...new Set(operations)], paths: [...new Set(paths)],
            hasFinally: /\bfinally\b/.test(source), uniqueFixture: /Guid\.NewGuid\(/.test(source),
            userStateAccess: /EditorWindow\.focusedWindow|Selection\.activeObject|SessionState|EditorPrefs|systemCopyBuffer/.test(source),
            unityInternalReflectionHint: /typeof\(EditorWindow\)|typeof\(EditorGUIUtility\)/.test(source) && /NonPublic/.test(source),
            lines: source.split('\n').length };
    });
    const sourceFiles = walk(path.join(root, 'src')).filter(file => file.endsWith('.cs'));
    const allEvidence = [...sourceFiles, ...walk(path.join(root, 'Tests~')).filter(file => /\.(cs|mjs|md)$/.test(file)),
        ...walk(path.join(root, 'Documentation~')).filter(file => /\.(md|cs|mjs|json)$/.test(file) && !file.includes(path.sep + '_site' + path.sep))];
    const evidence = allEvidence.map(read).join('\n');
    const candidates = [];
    for (const file of sourceFiles) {
        const source = read(file);
        for (const match of source.matchAll(/^\s*(?:private|internal)\s+(?:static\s+)?(?:[\w<>\[\].,?]+\s+)+(\w+)\s*\(/gm)) {
            const name = match[1];
            if (/^(On\w+|Dispose|Equals|GetHashCode|GetEnumerator)$/.test(name)) continue;
            const count = [...evidence.matchAll(new RegExp('\\b' + name + '\\b', 'g'))].length;
            if (count === 1) candidates.push({ file: relative(file), name, line: source.slice(0, match.index).split('\n').length, occurrences: count });
        }
    }
    return { tests, candidates };
}

export function option(name, fallback, argv = process.argv) {
    const index = argv.indexOf(name);
    if (index < 0) return fallback;
    const value = Number(argv[index + 1]);
    if (!Number.isSafeInteger(value) || value < 0) throw new Error('Invalid ' + name);
    return value;
}

export function auditOutput({ tests, candidates }, argv = process.argv) {
    if (argv.includes('--candidates')) return candidates;
    return { tests: tests.slice(option('--start', 0, argv), option('--start', 0, argv) + option('--count', tests.length, argv)),
        summary: tests.reduce((result, test) => {
            result[test.family] = (result[test.family] ?? 0) + 1; return result;
        }, {}), methodCandidates: candidates };
}

// Preserve the public exports and direct-execution guard. No generated reports/files.
export const { tests, candidates } = auditSources();
if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url))
    console.log(JSON.stringify(auditOutput({ tests, candidates }), null, 2));
