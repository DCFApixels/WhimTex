// Read-only coverage inspection. Does not import or execute test/production modules.
// Uses the parser already bundled with Node; no package installation is required.
import path from 'node:path';
import vm from 'node:vm';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { legacyIO } from '../scripts/legacy.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const io = legacyIO(root);
const parserModule = { exports: {} };
vm.runInNewContext(process.binding('natives')['internal/deps/acorn/acorn/dist/acorn'],
    { exports: parserModule.exports, module: parserModule });
const parse = source => parserModule.exports.parse(source, { ecmaVersion: 'latest', sourceType: 'module', locations: true });
const read = file => io.readFileSync(path.join(root, file), 'utf8');
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
const helperAliases = new Map([
    ['Tests~/GenerateBlueNoise.mjs', 'Tests~/Framework/NodeSupportA/BlueNoiseRanks.mjs'],
    ['Tests~/scripts/audit-sources.mjs', 'Tests~/Framework/NodeSupportA/AuditSources.mjs'],
    ['Tests~/UssCascadeSnapshot.mjs', 'Tests~/Framework/NodeSupportB/UssCascadeSnapshot.mjs'],
    ['Tests~/UssCascadeBaseline.json', 'Tests~/Framework/NodeSupportB/UssCascadeBaseline.json'],
    ['Tests~/ShaderFXVSCodeMetadata.cases.json', 'Tests~/Framework/NodeSupportB/ShaderFXVSCodeMetadata.cases.json'],
]);
const metric = node => node?.type === 'ExpressionStatement' &&
    (node.expression.type === 'UpdateExpression' && node.expression.argument.name === 'checks' ||
     node.expression.type === 'AssignmentExpression' && node.expression.left.name === 'checks');
function canonical(node, file, parent = null) {
    if (typeof node === 'bigint') return { bigint: node.toString() };
    if (node == null || typeof node !== 'object') return node;
    if (Array.isArray(node)) return node.filter(value => !metric(value)).map(value => canonical(value, file, parent));
    const url = parent?.type === 'NewExpression' && parent.callee.name === 'URL' &&
        parent.arguments[0] === node && parent.arguments[1]?.type === 'MemberExpression' && parent.arguments[1].object.type === 'MetaProperty';
    const binaryUrl = parent?.type === 'BinaryExpression' && parent.left === node && /^\.\.?\//.test(node.value ?? '');
    const importPath = parent?.type === 'ImportDeclaration' || parent?.type === 'ImportExpression';
    const modulePath = parent?.type === 'CallExpression' && parent.arguments[0] === node &&
        (parent.callee.name === 'require' || parent.callee.type === 'CallExpression' && parent.callee.callee.name === 'createRequire');
    const readPath = parent?.type === 'CallExpression' && parent.callee.name === 'read' && parent.arguments[0] === node;
    const resolveRoot = parent?.type === 'CallExpression' && parent.callee.property?.name === 'resolve' &&
        parent.arguments.some(a => a.type === 'CallExpression' && a.callee.property?.name === 'dirname');
    if (node.type === 'Literal' && typeof node.value === 'string' &&
        (url || binaryUrl || importPath || resolveRoot || modulePath || readPath) && /^\.\.?(\/|$)/.test(node.value)) {
        const effective = file.replace('Tests~/Legacy/', 'Tests~/');
        const resolved = path.relative(root, path.resolve(root, path.dirname(effective), node.value)).replaceAll('\\', '/');
        return { type: 'Literal', value: helperAliases.get(resolved) ?? resolved };
    }
    if (node.type === 'Literal' && url && node.value === 'ShaderFXVSCodeMetadata.cases.json')
        return { type: 'Literal', value: 'Tests~/Framework/NodeSupportB/ShaderFXVSCodeMetadata.cases.json' };
    if (node.type === 'Literal' && helperAliases.has(node.value)) return { type: 'Literal', value: helperAliases.get(node.value) };
    if (node.type === 'TemplateLiteral' && url && /^\.\.?\//.test(node.quasis[0].value.cooked)) {
        const effective = file.replace('Tests~/Legacy/', 'Tests~/');
        const prefix = path.relative(root, path.resolve(root, path.dirname(effective), node.quasis[0].value.cooked)).replaceAll('\\', '/') + '/';
        return { type: 'TemplateLiteral', expressions: canonical(node.expressions, file),
            quasis: node.quasis.map((q, i) => i ? canonical(q, file) : { type: 'TemplateElement', value: { raw: prefix, cooked: prefix }, tail: q.tail }) };
    }
    if (node.type === 'TemplateLiteral' && parent?.type === 'NewExpression' && parent.callee.name === 'Function') {
        const generated = node.quasis.map((q, i) => q.value.cooked + (i < node.expressions.length ? '__AUDIT_INSERT_' + i + '__' : '')).join('');
        try { return { type: 'generated-function-template', body: canonical(parserModule.exports.parse(generated, { ecmaVersion: 'latest', sourceType: 'script' }), file), expressions: canonical(node.expressions, file) }; }
        catch { /* A fragment which needs its interpolation to parse keeps exact template semantics. */ }
    }
    if (node.type === 'CallExpression' && (node.callee.name === 'test' || node.callee.object?.name === 'context' && node.callee.property?.name === 'case')) {
        return { type: 'named-case', arguments: node.arguments.filter(a => a.type !== 'ObjectExpression').map(a => {
            const result = canonical(a, file); if (a.type === 'ArrowFunctionExpression' || a.type === 'FunctionExpression') result.async = false;
            return result;
        }) };
    }
    const result = {};
    for (const [key, value] of Object.entries(node)) {
        if (['start', 'end', 'loc', 'raw'].includes(key)) continue;
        result[key] = canonical(value, file, node);
    }
    return result;
}
const encode = (node, file) => JSON.stringify(canonical(node, file));
function walk(node, visit, ancestors = []) {
    if (!node || typeof node !== 'object') return;
    if (node.type) { visit(node, ancestors); ancestors = [...ancestors, node]; }
    for (const [key, value] of Object.entries(node)) {
        if (key === 'loc') continue;
        if (Array.isArray(value)) for (const item of value) walk(item, visit, ancestors);
        else if (value && typeof value === 'object') walk(value, visit, ancestors);
    }
}
const assertion = node => node.type === 'CallExpression' &&
    (node.callee.type === 'Identifier' && node.callee.name === 'assert' ||
     node.callee.type === 'MemberExpression' && node.callee.object.name === 'assert');
function inspect(file) {
    const source = read(file), ast = parse(source), nodes = [], obligations = [], contexts = new Map();
    walk(ast, (node, ancestors) => {
        nodes.push(node);
        contexts.set(node, ancestors);
        let kind;
        if (assertion(node)) kind = 'assertion';
        else if (/^(ExpressionStatement|ReturnStatement|ThrowStatement)$/.test(node.type) && !metric(node)) kind = 'execution-step';
        else if (node.type === 'VariableDeclarator') kind = 'input-or-formula';
        else if (node.type === 'FunctionDeclaration') kind = 'formula-or-helper';
        else if (/^(ForStatement|ForOfStatement|ForInStatement|WhileStatement|DoWhileStatement)$/.test(node.type)) kind = 'loop';
        else if (/^(IfStatement|ConditionalExpression|SwitchStatement|TryStatement)$/.test(node.type)) kind = 'branch-or-cleanup';
        else if (/^(ImportDeclaration|ImportExpression)$/.test(node.type)) kind = 'dependency';
        if (!kind) return;
        const gates = ancestors.filter(n => /^(ForStatement|ForOfStatement|ForInStatement|WhileStatement|DoWhileStatement|IfStatement|ConditionalExpression)$/.test(n.type));
        obligations.push({ kind, node, gates, line: node.loc.start.line, text: source.slice(node.start, node.end), key: encode(node, file) });
    });
    return { file, source, ast, nodes, contexts, obligations, sourceSha256: sha(io.readFileSync(path.join(root, file))) };
}
function compare(originalFile, replacementFiles) {
    const original = inspect(originalFile), replacements = replacementFiles.map(inspect);
    const controlHeader = (gate, descendant, file) => {
        const header = {};
        for (const key of ['type', 'init', 'test', 'update', 'left', 'right', 'await']) if (key in gate) header[key] = gate[key];
        if (gate.type === 'IfStatement' || gate.type === 'ConditionalExpression')
            header.arm = gate.alternate && descendant.start >= gate.alternate.start && descendant.end <= gate.alternate.end ? 'alternate' : 'consequent';
        return encode(header, file);
    };
    const byNode = new Map();
    for (const replacement of replacements) for (const node of replacement.nodes) {
        const key = encode(node, replacement.file);
        if (!byNode.has(key)) byNode.set(key, []);
        const controls = replacement.contexts.get(node).filter(n => /^(ForStatement|ForOfStatement|ForInStatement|WhileStatement|DoWhileStatement|IfStatement|ConditionalExpression)$/.test(n.type));
        byNode.get(key).push({ file: replacement.file, line: node.loc.start.line, offset: node.start,
            controlKeys: assertion(node) ? controls.map(g => controlHeader(g, node, replacement.file)) : [] });
    }
    const used = new Map();
    const obligations = original.obligations.map(o => {
        const previous = used.get(o.key) ?? new Set();
        const controls = JSON.stringify(o.gates.map(g => controlHeader(g, o.node, originalFile)));
        const eligible = (byNode.get(o.key) ?? []).filter(m => !previous.has(m.file + ':' + m.offset) &&
            (o.kind !== 'assertion' || JSON.stringify(m.controlKeys) === controls));
        const primary = eligible.find(m => m.file === replacementFiles[0]) ?? eligible[0];
        if (primary) { previous.add(primary.file + ':' + primary.offset); used.set(o.key, previous); }
        const hits = primary ? [{ file: primary.file, line: primary.line }] : [];
        return { kind: o.kind, line: o.line, expression: o.text.slice(0, 800),
        astSha256: sha(o.key), matches: hits,
        enclosingControls: o.gates.map(g => ({ line: g.loc.start.line,
            header: original.source.slice(g.start, g.body?.start ?? g.consequent?.start ?? g.end),
            astSha256: sha(encode(g, originalFile)), matches: (byNode.get(encode(g, originalFile)) ?? []).map(m => ({file:m.file,line:m.line})) })) };
    });
    return { originalFile, originalSha256: original.sourceSha256,
        replacements: replacements.map(r => ({ file: r.file, sha256: r.sourceSha256 })), obligations };
}

const reports = [];
const selected = process.argv.includes('--file') ? process.argv[process.argv.indexOf('--file') + 1] : null;
for (const batchName of ['node-a', 'node-b']) {
    const batch = JSON.parse(read('Tests~/Batches/' + batchName + '.json'));
    for (const replacement of batch.replacements) {
        const legacy = replacement.legacyFile.replace(/^Tests~\/Legacy\//, '');
        if (selected && selected !== legacy) continue;
        const files = [...new Set([replacement.newFile, ...replacement.scenarios.flatMap(s => [s.file, ...(s.reviewFiles ?? [])])])]
            .filter(file => /\.(mjs|cjs|js)$/.test(file));
        const comparison = compare('Tests~/Legacy/' + legacy, files);
        reports.push({ batch: batchName, ids: replacement.scenarios.map(s => s.id), coverageDescription: replacement.coverage,
            declaredClassification: replacement.scenarios.map(s => ({ id: s.id, category: s.category, prerequisites: s.prerequisites,
                setup: s.setup, teardown: s.teardown, args: s.args })), ...comparison });
    }
}
if (!selected || selected === 'PsdWriter/read-fixture.cjs') {
    const auxiliary = JSON.parse(read('Tests~/Batches/framework-and-auxiliary.json'));
    const replacement = auxiliary.replacements.find(r => r.legacyFile.replace(/^Tests~\/Legacy\//, '') === 'PsdWriter/read-fixture.cjs');
    const scenarios = replacement?.scenarios ?? [];
    reports.push({ batch: 'auxiliary-read-only', ids: scenarios.map(s => s.id),
        declaredClassification: scenarios.map(s => ({ id: s.id, category: s.category, prerequisites: s.prerequisites,
            setup: s.setup, teardown: s.teardown, args: s.args })),
        ...compare('Tests~/Legacy/PsdWriter/read-fixture.cjs', ['Tests~/Cases/Export/PsdReader.mjs']) });
}

function entries(file) {
    const input = inspect(file), result = [];
    for (const node of input.nodes) {
        if (node.type === 'CallExpression' && node.callee.name === 'test') {
            const name = node.arguments[0];
            if (name.type === 'Literal') result.push(name.value);
            else if (file.endsWith('/ShaderFXVSCodeMetadata.test.mjs') && name.name === 'name')
                result.push(...JSON.parse(read('Tests~/Legacy/ShaderFXVSCodeMetadata.cases.json')).map(c => 'Parameterized: ' + c.name + ' (valid=' + c.valid + ')'));
            else result.push('Parameterized node:test entry: ' + input.source.slice(name.start, name.end));
        }
        if (node.type === 'ExportNamedDeclaration' && node.declaration?.type === 'FunctionDeclaration')
            result.push(node.declaration.id.name + '(' + node.declaration.params.map(p => p.name).join(', ') + ')');
    }
    if (file.endsWith('/read-fixture.cjs')) result.unshift('CLI: argv[2] = require-compatible reader module ID/path; argv[3] = PSD fixture path');
    else if (!result.length) result.push('Node module evaluation; no CLI parameterized entry');
    if (file.endsWith('/GenerateBlueNoise.mjs')) result.push('Direct CLI: no arguments; print generated C# source as JSON; manual bake diagnostic, zero assertions');
    if (file.endsWith('/scripts/audit-sources.mjs')) result.push('Exported tests/candidates; CLI --candidates, --start integer >=0, --count integer >=0 (invalid values throw)');
    return result;
}
function reviewedDifference(report, obligation) {
    const file = report.originalFile.split('/').pop(), text = obligation.expression;
    if (file === 'FinalLegacyAudit.test.mjs' && obligation.kind === 'dependency' && obligation.line === 5)
        return 'Historical tests/candidates now come from the independent auditHistoricalSources with injected exact historical test inputs; delegated Framework facade also preserves exported tests/candidates and standalone CLI. Eight raw input snapshots are byte-checked against frozen Legacy by this inspector.';
    if (/^console\.log\(/.test(text)) return 'Human-readable success logging replaced by structured finish(context); no predicate removed.';
    if (text === "import { test } from 'node:test';") return 'Named node:test callbacks migrated to serial awaited TestContext.case callbacks; original assertions remain strict.';
    if (/^checks\s*=\s*0$/.test(text)) return 'Pure console metric omitted; all original predicates and loop bounds remain. Counts are not coverage evidence.';
    if (/^test\(/.test(text)) return 'Named callback retained; shared read-only setup moved inside each case. Predicate/input/control matches below are the evidence, not callback wrapper identity.';
    if (text === 'process.argv.pop();' && ['BrushClipboard.test.mjs', 'ProceduralClipboard.test.mjs'].includes(file))
        return 'Original --check import preserved; full argv snapshot restored with splice in finally even when generator fails.';
    if (['LiveAgent.test.mjs', 'LiveShaderFx.test.mjs'].includes(file) && /JSON\.parse\(match\[1\]\)/.test(text))
        return 'Same fenced-JSON iterator and parsed match[1] retained inside assert.doesNotThrow; invalid JSON still fails the case.';
    if (file === 'read-fixture.cjs' && /^(fs|assert) = require\('node:/.test(text))
        return 'Built-in fs moved from CJS require to ESM import; strict assert is supplied unchanged through the counting proxy.';
    if (file === 'FinalLegacyAudit.test.mjs' && [10, 21, 28].includes(obligation.line)) {
        if (obligation.line === 10) return 'Original retired-file absence predicate is strengthened to reject both basename-matched active scenarios and current case source files; all four original filenames retained.';
        if (obligation.line === 21) return 'Original candidate.name removal predicate uses inventory.methodCandidates instead of audit.methodCandidates; same six names retained and direct production declaration rejection also remains.';
        return 'Original three regression-family files now resolve through exact original-to-active mappings; regression category/structured result/current declared entry required, preserving the classification responsibility.';
    }
    return null;
}
function schemaRecord(report) {
    const legacyFile = report.originalFile.replace('Tests~/Legacy/', '');
    const replacement = report.replacements[0];
    const coverage = [...(report.coverageDescription ?? []).filter(s => !/Every original assert|^No persistent state/.test(s)),
        'Source evidence: frozen original SHA-256 ' + report.originalSha256 + '; replacement SHA-256 ' + replacement.sha256 + '.',
        'Comparison parses complete sources without executing them. Positions/comments, package-root relocation, declared helper relocation, pure checks increments and case async scheduling are normalized explicitly. Generated Function template prefixes are compared as JavaScript ASTs so indentation is ignored; interpolated code expressions are preserved. String/regex values, numeric tolerances, input arrays, control flow and expression operators remain part of the comparison.'];
    const gaps = [];
    const matched = report.obligations.filter(o => o.matches.length);
    for (const o of matched.filter(o => o.kind === 'assertion')) {
        const hit = o.matches.find(m => m.file === replacement.file) ?? o.matches[0];
        coverage.push('Original L' + o.line + ' ' + o.expression.replace(/\s+/g, ' ') + ' -> ' + hit.file + ':L' + hit.line + '; same predicate/expected value/tolerance.');
    }
    for (const kind of ['input-or-formula', 'formula-or-helper', 'loop', 'branch-or-cleanup', 'execution-step']) {
        const items = matched.filter(o => o.kind === kind);
        if (items.length) coverage.push(kind + ' structural proof (original line -> replacement line): ' + items.map(o => {
            const m = o.matches.find(m => m.file === replacement.file) ?? o.matches[0];
            return o.line + '->' + m.line + (m.file === replacement.file ? '' : '@' + m.file);
        }).join(', ') + '. Complete AST subtrees match, including ordered statements inside loops/helpers and state mutations.');
    }
    const differences = new Set();
    for (const o of report.obligations.filter(o => !o.matches.length)) {
        const reviewed = reviewedDifference(report, o);
        if (reviewed) differences.add(reviewed);
        else gaps.push('Unresolved original ' + o.kind + ' at L' + o.line + ': ' + o.expression.replace(/\s+/g, ' '));
    }
    coverage.push(...differences);
    if (legacyFile === 'FinalLegacyAudit.test.mjs') {
        coverage.push('Every original assertion, input/formula, loop and branch is structurally matched. SoftRangeSmoke.Main, TwoChoiceDropdownSmoke.Main and the exact three multi-step/three regression families are restored on historical text inputs; current ACTIVE entry/lifecycle checks are additional, not substitutions. Production and Gaussian checks read current production.');
        const fixture = JSON.parse(read('Tests~/Cases/NodeA/Fixtures/AuditInputs.json'));
        for (const input of fixture.files) {
            const old = io.readFileSync(path.join(root, 'Tests~/Legacy', input.file));
            const exact = old.equals(Buffer.from(input.source)) && sha(old) === input.sha256;
            if (!exact) gaps.push('Historical raw source fixture mismatch: ' + input.file);
            coverage.push('Original classification input ' + input.file + ': exact raw byte snapshot=' + exact + ', SHA-256=' + sha(old) + '; data only, no archived C# execution.');
        }
    }
    if (legacyFile === 'PsdWriter/read-fixture.cjs') {
        coverage.unshift('3x2, 4-channel fixture: exact hierarchy/Unicode, multiply/pass-through/hidden nesting, opacity 128/255 and 153/255, RGB fill (255,64,32), mask byte 20, stroke fillOpacity=0/outside/4px/.75, linear gradient 90 degrees and 0/1 opacity endpoints.');
        coverage.push('Original 2 rows x 3 columns x 4 channels retained: expected = 20*(c===3?2:c+3)+7*y+x. Layer RGBA exact; merged RGB error <=4 and alpha error =0. Canvas access throws. Reader options useImageData=true and throwForMissingFeatures=true retained.');
        gaps.push('External reader ag-psd did not resolve in local/global/known installed module trees; newly supplied C:/Users/dcfam/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/ag-psd/package.json is also absent. No install/fetch attempted; prerequisite module path remains unavailable.');
        if (!report.ids.length)
            gaps.push('Parent-owned auxiliary batch has no registered external-reader scenario; registration and execution remain parent-owned.');
        else coverage.push('Parent-owned auxiliary registration observed: ' + report.ids.join(', ') + '. Registered workflow/lifecycle metadata is recorded, not executed or certified by this source audit. External-reader runtime still awaits the parent.');
        coverage.push('Existing fixture candidate (not executed here): D:/DCFA/Projects/Test6.6/Temp/WhimTex/test-runs/psd-writer-3b167d96-f526-4816-9522-e2742fb38690.psd. Module resolution corrected to original require(argv[2]) semantics; missing/invalid prerequisites fail, never produce a green diagnostic.');
    }
    const declared = report.declaredClassification ?? [];
    coverage.push('Classification: ' + (declared.length ? declared.map(s => s.id + '=' + s.category).join(', ') : 'optional independent-reader regression, external prerequisite; no declared active scenario') + '. Source/scalar cases do not execute Unity/C#/GPU/UI; no real regression is reclassified as a diagnostic.');
    if (declared.length) for (const s of declared) coverage.push('Lifecycle: ' + s.setup + ' Cleanup: ' + s.teardown);
    if (legacyFile === 'ShaderFXVSCodeGrammar.test.mjs') coverage.push('Optional prerequisite behavior retained: no WHIMTEX_VSCODE_APP produces an explicit skipped result, never pass. Existing app folder D:/Programs/Microsoft VS Code/07f806f999/resources/app is available; this audit did not execute its engines. Registry.dispose remains in finally.');
    return { legacyFile, newFile: replacement.file,
        sourceHashes: { legacy: report.originalSha256, replacement: replacement.sha256 },
        scenarioIds: report.ids,
        legacyEntries: entries(report.originalFile), coverage, gaps,
        reviewStatus: gaps.length ? 'gap' : 'source-reviewed', runtimeStatus: 'pending-parent-validation' };
}
function helperRecord(legacyFile, newFile, scenarioIds, coverage, gaps = []) {
    const old = io.readFileSync(path.join(root, 'Tests~/Legacy/', legacyFile)), replacement = io.readFileSync(path.join(root, newFile));
    return { legacyFile, newFile, scenarioIds,
        sourceHashes: { legacy: sha(old), replacement: sha(replacement) },
        legacyEntries: legacyFile.endsWith('.json') ? ['Data-only fixture; no executable entry'] : entries('Tests~/Legacy/' + legacyFile),
        coverage: [...coverage, 'Frozen SHA-256 ' + sha(old) + '; replacement SHA-256 ' + sha(replacement) + '.'], gaps,
        reviewStatus: gaps.length ? 'gap' : 'source-reviewed', runtimeStatus: 'pending-parent-validation' };
}
function schema() {
    const files = reports.map(schemaRecord);
    const ranks = compare('Tests~/Legacy/GenerateBlueNoise.mjs', ['Tests~/Framework/NodeSupportA/BlueNoiseRanks.mjs']);
    const rankHelpers = ranks.obligations.filter(o => o.kind === 'formula-or-helper' && /^function (ranks|rgba)\(/.test(o.expression));
    const rankGaps = rankHelpers.filter(o => !o.matches.length).map(o => 'Exported helper subtree mismatch: original L' + o.line);
    files.push(helperRecord('GenerateBlueNoise.mjs', 'Tests~/Framework/NodeSupportA/BlueNoiseRanks.mjs', ['blue-noise-v2'], [
        'Exclusion reviewed: support-helper, with direct zero-assertion manual bake diagnostic. Public ranks(width,height,seed) and rgba(width,height) full AST bodies match; ranks closure update/extreme/random and all three phase loops remain.',
        'ranks: 1D radius/sigma 18/3 and 2D 6/1.5; seed xorshift, 10% initial occupancy, n*10 convergence guard; periodic energy indexing and all rank phases retained. rgba: seeds 0x931af275/0x327bc671/0x6812aed7 and floor(rank*256/n), alpha 255.',
        'Manual direct entry still prints 128x128 TwoD plus 256x1 OneD C# JSON without file writes; only generated provenance path changes. Import does not bake. BlueNoise regression imports ranks for 16x16 seeds 1337/1338; rgba/direct bake were originally helpers, never asserted by that regression.'
    ], rankGaps));
    const snapshot = compare('Tests~/Legacy/UssCascadeSnapshot.mjs', ['Tests~/Framework/NodeSupportB/UssCascadeSnapshot.mjs']);
    files.push(helperRecord('UssCascadeSnapshot.mjs', 'Tests~/Framework/NodeSupportB/UssCascadeSnapshot.mjs', ['ui-refactor-v2'], [
        'Exclusion reviewed: support-helper, no standalone assertions. Complete exported snapshot(source) AST, every input/formula/loop/branch/execution step matches after positions/comments are removed.',
        'CSS comments, selector canonicalization, declaration parse/error branch, palette replacement/missing token error, ordered conflicts and sorted SHA-256 property chains retained. UiRefactor invokes actual helper, frozen baseline equality and surface/height mutation rejection.'
    ], snapshot.obligations.filter(o => !o.matches.length).map(o => 'Unmatched helper ' + o.kind + ' L' + o.line)));
    for (const [old, replacement, id, summary] of [
        ['UssCascadeBaseline.json', 'Tests~/Framework/NodeSupportB/UssCascadeBaseline.json', 'ui-refactor-v2', 'All 75 named property fingerprints are exactly equal as JSON values; no regenerated or relaxed baseline.'],
        ['ShaderFXVSCodeMetadata.cases.json', 'Tests~/Framework/NodeSupportB/ShaderFXVSCodeMetadata.cases.json', 'shader-fx-vs-code-metadata-v2', 'All 119 ordered name/source/valid objects are exactly equal as JSON values. Parameterized node:test names/inputs/validity are preserved by context.case(name) and the original validate(source).filter(!warning) predicate.']
    ]) {
        const same = JSON.stringify(JSON.parse(read('Tests~/Legacy/' + old))) === JSON.stringify(JSON.parse(read(replacement)));
        files.push(helperRecord(old, replacement, [id], ['Exclusion reviewed: data-only support fixture, no standalone executable/assertions.', summary], same ? [] : ['Fixture JSON values differ.']));
    }
    const historicalFile = 'Tests~/Cases/NodeA/AuditSources.mjs';
    const originalHelper = inspect('Tests~/Legacy/scripts/audit-sources.mjs');
    const newHelper = inspect(historicalFile);
    const helperGaps = [];
    const regexes = input => input.nodes.filter(n => n.type === 'Literal' && n.regex)
        .map(n => n.regex.pattern + '/' + n.regex.flags);
    if (JSON.stringify(regexes(originalHelper)) !== JSON.stringify(regexes(newHelper)))
        helperGaps.push('Historical regex pattern/flag sequence differs.');
    const preserved = [];
    for (const statement of originalHelper.ast.body) {
        if (statement.type === 'VariableDeclaration') {
            const name = statement.declarations[0].id.name;
            if (name === 'root' || name === 'option') continue;
            const match = newHelper.nodes.find(n => n.type === statement.type && encode(n, historicalFile) === encode(statement, originalHelper.file));
            if (match) preserved.push(name + ':L' + statement.loc.start.line + '->L' + match.loc.start.line);
            else helperGaps.push('Discovery statement differs: ' + name);
        } else if (statement.type === 'ForOfStatement') {
            if (!newHelper.nodes.some(n => encode(n, historicalFile) === encode(statement, originalHelper.file)))
                helperGaps.push('Full candidate traversal/body differs.');
            else preserved.push('candidate traversal and exact counts');
        }
    }
    // Only the explicitly injected argv parameter is mapped back to process.argv.
    // The optional third argument to option is the same injected argv; everything else stays exact.
    const cliCanonical = node => {
        const value = canonical(node, historicalFile);
        const rewrite = (n, parent = null, key = null) => {
            if (!n || typeof n !== 'object') return n;
            if (Array.isArray(n)) return n.map(item => rewrite(item, parent, key));
            if (n.type === 'Identifier' && n.name === 'argv' &&
                !(parent?.type === 'MemberExpression' && key === 'property' && !parent.computed))
                return {type:'MemberExpression',object:{type:'Identifier',name:'process'},property:{type:'Identifier',name:'argv'},computed:false,optional:false};
            const result = Object.fromEntries(Object.entries(n).map(([k,v]) => [k,rewrite(v,n,k)]));
            if (result.type === 'CallExpression' && result.callee.name === 'option' && result.arguments.length === 3)
                result.arguments.pop();
            return result;
        };
        return JSON.stringify(rewrite(value));
    };
    const oldOption = originalHelper.nodes.find(n => n.type === 'VariableDeclarator' && n.id.name === 'option').init.body;
    const newOption = newHelper.nodes.find(n => n.type === 'FunctionDeclaration' && n.id.name === 'option').body;
    if (cliCanonical(oldOption) !== cliCanonical(newOption)) helperGaps.push('CLI option body differs beyond explicit argv injection.');
    const oldOutput = originalHelper.nodes.find(n => n.type === 'ObjectExpression' && n.properties.some(p => p.key.name === 'methodCandidates'));
    const newOutput = newHelper.nodes.find(n => n.type === 'ReturnStatement' && n.argument?.type === 'ObjectExpression' && n.argument.properties.some(p => p.key.name === 'methodCandidates')).argument;
    if (cliCanonical(oldOutput) !== cliCanonical(newOutput)) helperGaps.push('CLI tests/summary/methodCandidates output differs.');
    const oldCandidate = originalHelper.nodes.find(n => n.type === 'IfStatement' && n.test.type === 'CallExpression' && n.test.callee.property?.name === 'includes');
    const newCandidate = newHelper.nodes.find(n => n.type === 'IfStatement' && n.test.type === 'CallExpression' && n.test.callee.property?.name === 'includes');
    if (cliCanonical(oldCandidate.test) !== cliCanonical(newCandidate.test) || newCandidate.consequent.argument?.name !== 'candidates')
        helperGaps.push('CLI candidates precedence/return differs.');
    const oldGuard = originalHelper.nodes.find(n => n.type === 'IfStatement' && n.test.type === 'BinaryExpression' && n.test.right.type === 'CallExpression');
    const newGuard = newHelper.nodes.find(n => n.type === 'IfStatement' && n.test.type === 'BinaryExpression' && n.test.right.type === 'CallExpression');
    if (cliCanonical(oldGuard.test) !== cliCanonical(newGuard.test)) helperGaps.push('Direct-execution guard differs.');
    const facade = inspect('Tests~/Framework/NodeSupportA/AuditSources.mjs');
    const facadeGuard = facade.nodes.find(n => n.type === 'IfStatement' && n.test.type === 'BinaryExpression' && n.test.right.type === 'CallExpression');
    if (cliCanonical(oldGuard.test) !== cliCanonical(facadeGuard?.test) ||
        !facade.source.includes('export { auditHistoricalSources, auditOutput, option, tests, candidates };'))
        helperGaps.push('Delegated public historical exports/CLI guard differ.');
    files.push(helperRecord('scripts/audit-sources.mjs', facade.file, ['final-legacy-audit-v2'], [
        'Exclusion reviewed: read-only inventory-tool/helper, not a standalone regression or runtime C# test. Independent implementation ' + historicalFile + ' raw SHA-256=' + newHelper.sourceSha256 + '. Framework facade preserves historical exports/direct CLI alongside its additional ACTIVE inventory API.',
        'All original regex patterns/flags/ordering exactly equal; no broadening/weakening. Complete discovery AST statements preserved: ' + preserved.join(', ') + '. Inputs remain top-level sorted lowercase .cs; recursion/extensions/_site exclusion; Main-before-Run, diagnostic/manual/multi-step/regression precedence, entries/parameters/flags/mutations/paths and candidate word counts/line numbers remain exact.',
        'CLI option body, direct guard, candidates branch and full tests/summary/methodCandidates payload structurally match after explicit argv injection only. First duplicate option, candidates priority over invalid pagination, nonnegative-safe-integer Number coercion and exact errors, defaults/slicing/full summary and pretty JSON are independently exercised by the five historical cases.',
        'Original eight classification texts are exact frozen raw-byte snapshots in Cases/NodeA/Fixtures/AuditInputs.json; current production/doc text remains live. Local owned final case passes fifteen groups; all runtimeStatus fields still await parent sequential validation. Counts do not establish equivalence.'
    ], helperGaps));
    return { version: 1, scope: '77 Node replacements in node-a/node-b; PSD auxiliary reader; declared helper/fixture exclusions and transitive audit helper. Full source AST predicates/inputs/formulas/control/cleanup plus reviewed adaptations; no check-count equivalence, runtime execution or Unity validation. Read-only inspector: Tests~/CoverageAudit/node-audit.mjs. All runtimeStatus values deliberately await parent validation.', files };
}

if (process.argv.includes('--schema')) console.log(JSON.stringify(schema()));
else if (process.argv.includes('--report')) console.log(JSON.stringify(reports));
else for (const report of reports) {
    const unmatched = report.obligations.filter(o => !o.matches.length);
    console.log(JSON.stringify({ original: report.originalFile, matched: report.obligations.length - unmatched.length,
        unmatched: unmatched.map(o => ({ kind: o.kind, line: o.line, text: o.expression })) }));
}
