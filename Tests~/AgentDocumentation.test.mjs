// Read-only cross-checks for command discovery and executable JSON examples.
// Runtime persistence/identity semantics are exercised by AgentEditingSmoke.cs.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = new URL('../', import.meta.url);
const read = file => readFileSync(new URL(file, root), 'utf8');
const api = read('Documentation~/AgentAPI.md');
const live = read('Documentation~/LiveAgentAPI.md');
const context = read('Context~/TIFF_AGENT_COMMANDS.md');
const commands = [...read('src/Automation/Pipeline/WhimTexCommands.cs').matchAll(/CliCommand\("([^"]+)"/g)].map(m => m[1]);
for (const command of commands) {
  assert.ok(api.includes('`' + command + '`'), `AgentAPI must document ${command}`);
  assert.ok(context.includes('`' + command + '`'), `Command map must document ${command}`);
}
const limit = read('src/Automation/WhimTexApi.cs').match(/MaxFxParameters\s*=\s*(\d+)/)[1];
assert.ok(live.includes(`Parameters: at most ${limit},`), 'Live parameter limit must match the parser');
for (const type of ['Bool', 'Float', 'Enum', 'Color', 'Vector2', 'Vector3', 'Vector', 'Normal', 'Point', 'Texture2D', 'Curve', 'Gradient', 'Transform2D'])
  assert.ok(live.includes('`' + type + '`'), `Live parameter type ${type} must be documented`);

let examples = 0;
function parseExamples(text) {
  const values = [];
  let start = 0, depth = 0, quoted = false, escaped = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (escaped) escaped = false;
      else if (c === '\\') escaped = true;
      else if (c === '"') quoted = false;
    } else if (c === '"') quoted = true;
    else if (c === '{' || c === '[') depth++;
    else if (c === '}' || c === ']') {
      if (--depth === 0) {
        values.push(JSON.parse(text.slice(start, i + 1)));
        start = i + 1;
      }
    }
  }
  assert.equal(depth, 0, 'Unbalanced JSON example');
  assert.equal(text.slice(start).trim(), '', 'Unparsed JSON example content');
  assert.ok(values.length > 0, 'Empty JSON example');
  return values;
}
for (const file of ['Documentation~/AgentAPI.md', 'Documentation~/LiveAgentAPI.md', 'Context~/TIFF_AGENT_COMMANDS.md']) {
  for (const match of read(file).matchAll(/```json\s*\n([\s\S]*?)```/g)) {
    const text = match[1].trim();
    // Reference blocks may contain several independent, multiline request objects.
    const values = parseExamples(text);
    for (const value of values) {
      examples++;
      if (value.create === true) assert.ok(!Object.hasOwn(value, 'expectedRevision'), `${file}: creation must omit expectedRevision`);
      if (value.apiVersion !== undefined) assert.equal(value.apiVersion, 1, `${file}: protocol version`);
    }
  }
}
for (const forbidden of ['visible to an open WhimTex', 'source-space diameter', 'does not author Shader FX', 'retains its fixtures'])
  assert.ok(!api.includes(forbidden), `Obsolete contract: ${forbidden}`);
assert.ok(!live.includes('without an initializer'), 'Gradient defaults support an initializer');
console.log(`Agent documentation contracts checked: ${commands.length} commands, ${examples} JSON examples (${fileURLToPath(root)}).`);
