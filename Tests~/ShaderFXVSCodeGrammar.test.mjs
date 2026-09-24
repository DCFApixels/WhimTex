// Optional integration test using an existing VS Code installation's TextMate
// and Oniguruma engines. No downloads or dependencies on project Assets.
// WHIMTEX_VSCODE_APP points to <VS Code>/resources/app (Contents/Resources/app on macOS).
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { test } from 'node:test';
const require = createRequire(import.meta.url);
const app = process.env.WHIMTEX_VSCODE_APP;

test('actual HLSL TextMate injection colors parameter syntax without coloring ordinary comments',
  { skip: !app && 'Set WHIMTEX_VSCODE_APP to an installed VS Code resources/app folder.' }, async () => {
    let archive, header, dataOffset;
    const asar = path.join(app, 'node_modules.asar');
    if (fs.existsSync(asar)) {
      archive = fs.readFileSync(asar);
      header = JSON.parse(archive.subarray(16, 16 + archive.readUInt32LE(12)).toString());
      dataOffset = 8 + archive.readUInt32LE(4);
    }
    const readModule = relative => {
      if (!archive) return fs.readFileSync(path.join(app, 'node_modules', relative));
      let entry = header;
      for (const segment of relative.split('/')) entry = entry.files[segment];
      if (entry.unpacked) return fs.readFileSync(path.join(asar + '.unpacked', relative));
      const start = dataOffset + Number(entry.offset);
      return archive.subarray(start, start + entry.size);
    };
    const loadModule = name => {
      if (!archive) return require(path.join(app, 'node_modules', name));
      const Module = require('node:module');
      const file = path.join(app, 'node_modules', name, 'release', 'main.js');
      const module = new Module(file);
      module.filename = file;
      module.paths = Module._nodeModulePaths(path.dirname(file));
      module._compile(readModule(name + '/release/main.js').toString(), file);
      return module.exports;
    };
    const tm = loadModule('vscode-textmate'), onig = loadModule('vscode-oniguruma');
    const wasm = readModule('vscode-oniguruma/release/onig.wasm');
    await onig.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
    const injection = JSON.parse(fs.readFileSync(new URL('../ExternalTools~/WhimTexVSCode/syntaxes/whimtex-hlsl.tmLanguage.json', import.meta.url), 'utf8'));
    const hlsl = JSON.parse(fs.readFileSync(path.join(app, 'extensions/hlsl/syntaxes/hlsl.tmLanguage.json'), 'utf8'));
    const registry = new tm.Registry({
      onigLib: Promise.resolve({ createOnigScanner: patterns => new onig.OnigScanner(patterns), createOnigString: s => new onig.OnigString(s) }),
      loadGrammar: async scope => scope === injection.scopeName ? injection : scope === hlsl.scopeName ? hlsl : null,
      getInjections: scope => scope === hlsl.scopeName ? [injection.scopeName] : []
    });
    try {
      const grammar = await registry.loadGrammar(hlsl.scopeName);
      const scopesAt = (line, word) => {
        const pos = line.indexOf(word);
        assert.notEqual(pos, -1, word);
        return grammar.tokenizeLine(line, tm.INITIAL).tokens.find(t => t.startIndex <= pos && pos < t.endIndex).scopes.join(' ');
      };
      const line = '// @param label("Amount (px)") hidden float _Amount = 1e-2 [~-1 .. ~2] // hint';
      for (const [word, scope] of [['@param', 'keyword.control'], ['label', 'storage.modifier'], ['Amount', 'string.quoted'],
        ['hidden', 'storage.modifier'], ['float', 'storage.type'], ['_Amount', 'variable.other.parameter'],
        ['1e-2', 'constant.numeric'], ['..', 'keyword.operator'], ['hint', 'comment.line.tooltip']])
        assert.ok(scopesAt(line, word).includes(scope), word + ': ' + scopesAt(line, word));
      assert.match(scopesAt('// @param gradient _Ramp = #FF0000 -> #0000FFFF', '#FF0000'), /constant.other.color/);
      assert.match(scopesAt('// @param enum _Mode = A {A: 0, B: 1}', 'B:'), /constant.other.enum/);
      assert.match(scopesAt('// @whimtex-effect Color/Mask', '@whimtex-effect'), /keyword.control/);
      assert.doesNotMatch(scopesAt('// A note about @param float _A = 1', 'float'), /storage.type.whimtex/);
      let stack = grammar.tokenizeLine('/*', tm.INITIAL).ruleStack;
      const disabled = grammar.tokenizeLine('// @param float _A = 1', stack);
      assert.ok(disabled.tokens.every(t => !t.scopes.some(s => s.endsWith('.whimtex'))));
      assert.ok(grammar.tokenizeLine('float value = 1;', tm.INITIAL).tokens.every(t => !t.scopes.some(s => s.endsWith('.whimtex'))));
    } finally { registry.dispose(); }
  });
