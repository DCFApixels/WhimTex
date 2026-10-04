import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const require = createRequire(import.meta.url);
const Module = require('node:module');
const originalLoad = Module._load;
let completionRegistration;
let saveCallbacks = [];
let applyRequest;
class Range {
    constructor(startLine, startColumn, endLine, endColumn) {
        Object.assign(this, { startLine, startColumn, endLine, endColumn });
    }
}
class Diagnostic {
    constructor(range, message, severity) { Object.assign(this, { range, message, severity }); }
}
class CompletionItem {
    constructor(label, kind) { Object.assign(this, { label, kind }); }
}
Module._load = function (request, parent, isMain) {
    if (request === 'vscode')
        return {
            Range,
            Diagnostic,
            CompletionItem,
            CompletionItemKind: { Keyword: 1 },
            DiagnosticSeverity: { Error: 0, Warning: 1 },
            Uri: { file: fsPath => ({ scheme: 'file', fsPath }) },
            window: { showErrorMessage() {} },
            languages: {
                createDiagnosticCollection: () => ({ set() {} }),
                registerCompletionItemProvider: (...args) => {
                    completionRegistration = args;
                    return {};
                }
            },
            workspace: {
                onDidOpenTextDocument: () => ({}),
                onDidChangeTextDocument: () => ({}),
                onDidSaveTextDocument: callback => { saveCallbacks.push(callback); return {}; },
                fs: { writeFile: (uri, data) => { applyRequest = { uri, data }; return Promise.resolve(); } },
                textDocuments: []
            }
        };
    return originalLoad.call(this, request, parent, isMain);
};
let extension;
try { extension = require('../ExternalTools~/WhimTexVSCode/extension.js'); }
finally { Module._load = originalLoad; }

const validate = code => {
    const diagnostics = [];
    extension.validateForTests({ getText: () => code }, diagnostics);
    return diagnostics;
};

test('WhimTex extension augments built-in HLSL and ships a bundled VSIX', () => {
    const root = new URL('../ExternalTools~/WhimTexVSCode/', import.meta.url);
    const manifest = JSON.parse(readFileSync(new URL('package.json', root), 'utf8'));
    const installerSource = readFileSync(new URL('../../src/Editor/ShaderFXExternalCode.cs', root), 'utf8');
    const vsixManifest = readFileSync(new URL('extension.vsixmanifest', root), 'utf8');
    const vsix = readFileSync(new URL('whimtex-fx-tools.vsix', root));
    assert.equal(manifest.publisher + '.' + manifest.name, 'dcfapixels.whimtex-fx-tools');
    assert.deepEqual(manifest.activationEvents, ['onLanguage:hlsl']);
    assert.equal(manifest.capabilities.untrustedWorkspaces.supported, true);
    assert.equal(manifest.contributes.languages, undefined);
    assert.deepEqual(manifest.contributes.grammars[0].injectTo, ['source.hlsl']);
    assert.equal(manifest.contributes.grammars[0].scopeName, 'whimtex.hlsl.injection');
    const grammar = JSON.parse(readFileSync(new URL('syntaxes/whimtex-hlsl.tmLanguage.json', root), 'utf8'));
    assert.equal(grammar.injectionSelector, 'L:comment.line.double-slash.hlsl');
    assert.match(readFileSync(new URL('extension.js', root), 'utf8'), /registerCompletionItemProvider\('hlsl'/);
    assert.match(vsixManifest, new RegExp(`Version="${manifest.version}"`));
    assert.match(installerSource, new RegExp(`ExtensionVersion = "${manifest.version}"`));
    assert.equal(vsix[0], 0x50);
    assert.equal(vsix[1], 0x4b);
    assert.equal(manifest.version, '0.1.3');
});

test('directive completion uses HLSL and replaces the typed @ prefix', () => {
    saveCallbacks = [];
    applyRequest = undefined;
    extension.activate({ subscriptions: [] });
    assert.equal(completionRegistration[0], 'hlsl');
    assert.equal(completionRegistration[2], '@');
    const provider = completionRegistration[1];
    const items = provider.provideCompletionItems({ lineAt: () => ({ text: '// @pa' }) }, { line: 4, character: 6 });
    const param = items.find(item => item.label === '@param');
    assert.equal(param.insertText, '@param ');
    assert.deepEqual(param.range, new Range(4, 3, 4, 6));
    assert.ok(items.every(item => !['@if', '@group()'].includes(item.label)), 'Block snippets must not have duplicate plain completions.');

    const requestApplyAfterSave = saveCallbacks[saveCallbacks.length - 1];
    const document = {
        languageId: 'hlsl',
        uri: { scheme: 'file', fsPath: 'D:\\Project\\Library\\WhimTex\\ExternalCode\\fx.hlsl' }
    };
    assert.equal(extension.isWhimTexCodeDocumentForTests(document), true);
    requestApplyAfterSave(document);
    assert.equal(applyRequest.uri.fsPath, document.uri.fsPath + '.apply');
    assert.equal(applyRequest.data.toString().length > 0, true);
    assert.equal(extension.isWhimTexCodeDocumentForTests({
        languageId: 'hlsl', uri: { scheme: 'file', fsPath: 'D:\\Project\\Assets\\Other.hlsl' }
    }), false);
});

test('directive diagnostics accept valid parameter modifiers, conditions, and groups', () => {
    const diagnostics = validate([
        '/*',
        '// @if _Missing == 1',
        '// @param unknown _Ignored',
        '*/',
        '// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }',
        '// @if _Mode == 1',
        '// @param label(Detail Level) hidden float _Detail = 0.5 [0 .. 1]',
        '// @endif',
        '// @group(Tint; _EnableTint)',
        '// @param bool _EnableTint = true',
        '// @param color _Tint = (1, 1, 1, 1)',
        '// @endgroup'
    ].join('\n'));
    assert.deepEqual(diagnostics, []);
});

test('directive diagnostics report unknown directives, incompatible storage, and unmatched blocks', () => {
    const diagnostics = validate([
        '// @unknown test',
        '// @If _Amount == 1',
        '// @param float _Amount = 1',
        '// @param color _Amount = (1,1,1,1)',
        '// @if _Amount == 1',
        '// @endgroup'
    ].join('\n'));
    assert.equal(diagnostics.filter(item => item.severity === 0).length, 3);
    assert.equal(diagnostics.filter(item => item.severity === 1).length, 2);
    assert.match(diagnostics.map(item => item.message).join('\n'), /Conflicting storage types/);
    assert.match(diagnostics.map(item => item.message).join('\n'), /no matching @endif/);
});
