import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { test } from 'node:test';

const root = new URL('../ExternalTools~/WhimTexVSCode/', import.meta.url);
const manifest = JSON.parse(readFileSync(new URL('package.json', root), 'utf8'));
const contribution = manifest.contributes.snippets;
const snippets = JSON.parse(readFileSync(new URL(contribution[0].path, root), 'utf8'));
const { validate } = createRequire(import.meta.url)('../ExternalTools~/WhimTexVSCode/metadata.js');

// Expand the limited placeholder forms used by these templates. This checks their
// generated source, not VS Code's own snippet engine or interactive keybindings.
function defaults(snippet) {
  return snippet.body.join('\n').replace(/\$\{\d+:([^}]*)\}/g, '$1')
    .replace(/\$\{\d+\|([^|]*)\|\}/g, (_, values) => values.split(',')[0]).replace(/\$0/g, '');
}

test('block snippets are contributed only to HLSL with overridable native Tab completion', () => {
  assert.equal(contribution.length, 1);
  assert.equal(contribution[0].language, 'hlsl');
  assert.deepEqual(manifest.contributes.configurationDefaults, {
    '[hlsl]': { 'editor.tabCompletion': 'onlySnippets' }
  });
  assert.deepEqual(Object.values(snippets).map(s => s.prefix), ['@if', '@group']);
});

test('conditional snippet supplies comparison choices, ordered placeholders and matching end', () => {
  const snippet = snippets['WhimTex conditional block'];
  assert.deepEqual(snippet.body, ['@if ${1:_Mode} ${2|==,!=|} ${3:1}', '// $0', '// @endif']);
  for (const op of ['==', '!=']) {
    const source = '// @param enum _Mode = 1 {Off: 0, On: 1}\n// ' + defaults(snippet).replace('==', op);
    assert.deepEqual(validate(source), []);
  }
});

test('group snippet places its title inside parentheses and accepts a header binding', () => {
  const snippet = snippets['WhimTex parameter group'];
  assert.deepEqual(snippet.body, ['@group(${1:Title})', '// $0', '// @endgroup']);
  // Groups must contain a parameter: the inserted body is intentionally left for the author.
  const body = defaults(snippet).replace('// \n', '// @param hidden bool _Enabled = true\n// @param float _Amount = 1\n');
  assert.deepEqual(validate('// ' + body.replace('Title', 'Tint; _Enabled')), []);
  assert.deepEqual(validate('// ' + body), []);
});

test('native suffix replacement preserves comment markers, spacing and surrounding code', () => {
  for (const snippet of Object.values(snippets)) {
    for (const prefix of ['//', '// ', '    // ', '\t//']) {
      const line = prefix + snippet.prefix;
      const expanded = line.slice(0, -snippet.prefix.length) + defaults(snippet);
      assert.equal(expanded.split('\n')[0], prefix + defaults(snippet).split('\n')[0]);
      assert.equal((expanded.match(/@endif|@endgroup/g) || []).length, 1);
      assert.match(expanded, /^\s*\/\/\s*@/);
    }
  }
});
