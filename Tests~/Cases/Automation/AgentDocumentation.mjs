// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('AgentDocumentation: source/scalar contracts');
const assert = context.assert;

context.case('AgentDocumentation original assertions and branches', async () => {
    // Read-only cross-checks for command discovery and executable JSON examples.
    // Runtime persistence/identity semantics are exercised by AgentEditingSmoke.cs.
    
    const root = new URL('../../../', import.meta.url);
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
    const authoring = read('Documentation~/AI/README.md');
    assert.match(read('Documentation~/AI/BRUSHES.md'), /README\.md#standalone-gradient-json/, 'Brush gradient reference must target the current value contract');
    assert.match(authoring, /^## Standalone gradient JSON\r?$/m, 'The linked gradient section must exist');
    for (const file of ['README.md', 'README-RU.md', 'README-ZH.md']) {
      const entry = read(file);
      assert.ok(entry.includes('Documentation~/AI/document.schema.json'), `${file}: current document schema entry point`);
      assert.ok(!entry.includes('Documentation~/AI/layers.schema.json'), `${file}: no legacy schema as the primary contract`);
      assert.ok(!entry.includes('legacy URL-input fixture'), `${file}: removed linked-image fixture is not advertised`);
    }
    assert.match(api, /\| `assetPath` \|[^\n]*\*\.tiff[^\n]*\*\.json/, 'Batch path table must document both storage formats');
    assert.ok(api.includes('not a `whimtex.document` file'), 'Batch command envelopes are distinguished from document content');
    const shaders = read('Documentation~/ShaderFX.md');
    assert.ok(shaders.includes('textureLayerId'), 'Unified JSON exposes stored FX layer bindings');
    assert.ok(!shaders.includes('Clipboard JSON does not expose these bindings'), 'No obsolete FX binding restriction');
    assert.ok(!shaders.includes('retains the last working shader'), 'Failed Apply must not promise rendering a stale shader');
    for (const lang of ['en', 'ru', 'zh']) {
      assert.ok(read(`Documentation~/${lang}/selection.md`).includes('AI/LEGACY_LAYERS.md'), `${lang}: old clipboard upgrade route is documented`);
      assert.ok(read(`Documentation~/${lang}/tiff-format.md`).includes('JSON'), `${lang}: editable documents are not TIFF-only`);
    }
    assert.ok(!read('Documentation~/TIFF_FORMAT.md').includes('documents are TIFF-only'), 'Technical TIFF reference must acknowledge JSON');
    assert.ok(read('Documentation~/Examples/index.md').includes('not stored documents or clipboard recipes'), 'API examples distinguish operations from content');
    for (const file of ['AI_AUTHORING.md', 'Documentation~/AI/README.md', 'Documentation~/Examples/Clipboard/README.md']) {
      const text = read(file);
      assert.ok(text.includes('whimtex.document') && text.includes('document.schema.json'), `${file}: current authoring contract`);
      assert.ok(!text.includes('Copy as Portable'), `${file}: obsolete menu name`);
    }
    assert.ok(authoring.includes('LEGACY_LAYERS.md'), 'Old clipboard upgrade route is documented separately');
    assert.ok(authoring.includes('compilation failure does not reject') && !authoring.includes('successful compilation before insertion'), 'Paste documents recoverable FX errors');
    assert.ok(!authoring.includes('"format": "whimtex.layers",'), 'No new recipe teaches legacy output');
    const layerMenu = read('src/WhimTexWindow.cs');
    assert.ok(layerMenu.includes('new GUIContent("Copy as JSON")'), 'Layer copy menu uses the current name');
    assert.ok(layerMenu.includes('DisplayDialog("Copy as JSON",'), 'Copy failure dialog uses the current name');
    assert.ok(!layerMenu.includes('Copy as Portable') && !layerMenu.includes('Portable layer JSON copied.'), 'No obsolete copy UI messages');
    for (const lang of ['en', 'ru', 'zh']) {
      assert.ok(read(`Documentation~/${lang}/layers.md`).includes('Copy as JSON'), `Localized ${lang} copy label`);
      assert.ok(read(`Documentation~/${lang}/ai-authoring.md`).includes('whimtex.document'), `Localized ${lang} authoring format`);
    }
});

await finish(context);

