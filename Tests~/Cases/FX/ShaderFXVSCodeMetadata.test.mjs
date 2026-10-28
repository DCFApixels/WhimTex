// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { buildVsix } from '../../../ExternalTools~/WhimTexVSCode/build-vsix.mjs';
const context = new TestContext("ShaderFXVSCodeMetadata source/reference tests");
const assert = context.assert;
try {
  const require = createRequire(import.meta.url);
  const { validate } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
  const cases = JSON.parse(readFileSync(new URL('../../Fixtures/ShaderFXVSCodeMetadata.node.cases.json', import.meta.url), 'utf8'));

  for (const { name, source, valid } of cases) context.case(name, async () => {
    const errors = validate(source).filter(d => !d.warning);
    assert.equal(errors.length === 0, valid, JSON.stringify(errors));
  });

  context.case('Unity and Node metadata fixtures retain identical inputs and expectations', async () => {
    const shared = JSON.parse(readFileSync(new URL('../../Fixtures/ShaderFXVSCodeMetadata.cases.json', import.meta.url), 'utf8'));
    assert.deepEqual(cases, shared);
    assert.equal(new Set(cases.map(c => c.name)).size, cases.length);
    for (const item of cases.filter(c => c.name.startsWith('layout:') && c.valid))
      assert.deepEqual(validate(item.source), [], item.name);
  });

  context.case('layout diagnostics are errors at the offending directive', async () => {
    for (const [source, line, message] of [
      ['// @foldout\n// @preview(P)\n// @endfoldout', 0, /requires a title/],
      ['// @tabs\n// @preview(P)\n// @endtabs', 1, /inside a @tab page/],
      ['// @tabs\n// @param hidden float _A\n// @endtabs', 1, /inside a @tab page/],
      ['// @foldout(Title)\n// @preview(P)\n// @endgroup', 2, /at this level/],
      ['// @tabs\n// @tab(Page)\n// @preview(P)\n// @endtab\n// @tab(Page)', 4, /Duplicate tab title/],
      ['// @foldout(Title; _Missing)\n// @preview(P)\n// @endfoldout', 0, /header parameter _Missing/],
      ['// @foldout(Title)\n// @param hidden float _A\n// @endfoldout', 0, /visible parameter or standalone preview/]
    ]) {
      const errors = validate(source);
      const error = errors.find(d => !d.warning && d.line === line && message.test(d.message));
      assert.ok(error, JSON.stringify(errors));
      assert.equal(error.start, 3);
      assert.equal(error.end, source.split('\n')[line].length);
    }
  });

  context.case('every container opens outside conditions and closes after its condition', async () => {
    for (const condition of ['if', 'enableif']) for (const kind of ['group', 'foldout', 'tabs', 'tab']) {
      const open = `// @${kind}(Title)`;
      const close = `// @end${kind}`;
      const outer = kind === 'tab' ? '// @tabs\n' : '';
      const row = kind === 'tabs' ? '// @tab(Page)\n// @preview(P)\n// @endtab\n' : '// @preview(P)\n';
      const insideIf = `// @param toggle(float) _Enabled\n${outer}// @${condition} _Enabled == 1\n${open}\n`;
      assert.ok(validate(insideIf).some(d => !d.warning && d.message === `@${kind} must be outside conditional blocks.`));
      const closingIf = `// @param toggle(float) _Enabled\n${outer}${open}\n${row}// @${condition} _Enabled == 1\n${close}`;
      assert.ok(validate(closingIf).some(d => !d.warning && d.message === `Conditional block must end before @end${kind}.`));
      assert.ok(validate(close).some(d => !d.warning && /no matching open/.test(d.message)));
      assert.ok(validate(`${outer}${open}\n${row}${close}(extra)`).some(d => !d.warning && /takes no arguments/.test(d.message)));
    }
  });

  context.case('conditional standalone previews retain dependency validation inside pages', async () => {
    for (const kind of ['if', 'enableif']) {
      const page = driver => `// @tabs\n// @tab(Page)\n// @${kind} ${driver} == 1\n// @preview(P)\n// @end${kind}\n// @endtab\n// @endtabs`;
      assert.deepEqual(validate('// @param toggle(float) _Enabled\n' + page('_Enabled')), []);
      assert.ok(validate(page('_Missing')).some(d => !d.warning && /undeclared parameter _Missing/.test(d.message)));
      assert.ok(validate('// @param float2 _Vector\n' + page('_Vector')).some(d => !d.warning && /requires a scalar/.test(d.message)));
    }
    assert.deepEqual(validate('/*\n// @tabs\n// @tab()\n// @foldout\n// @endgroup\n*/'), []);
  });

  context.case('conditions enforce combined depth, row dependencies and matching innermost ends', async () => {
    const block = (kind, driver, content) => `// @${kind} ${driver} != -1e-2\n${content}\n// @end${kind}`;
    const drivers = '// @param toggle(float) _Enabled\n// @param enum _Mode {Off: 0, On: 1}\n';
    for (const outer of ['if', 'enableif']) {
      const inner = outer === 'if' ? 'enableif' : 'if';
      assert.deepEqual(validate(drivers + block(outer, '_Enabled', block(inner, '_Mode', '// @preview(P)'))), []);
      const wrongEnd = drivers + block(outer, '_Enabled', `// @${inner} _Mode == 1\n// @preview(P)`);
      assert.ok(validate(wrongEnd).some(d => !d.warning && /innermost open condition/.test(d.message)));
      const empty = block(outer, '_Missing', '// @header(Heading)\n// @helpbox(Hint)');
      assert.deepEqual(validate(empty), []);
      assert.ok(validate(block(outer, '_Missing', '// @param hidden float _Value')).some(d => !d.warning && /undeclared parameter _Missing/.test(d.message)));
      assert.ok(validate(block(outer, '_Missing', '// @preview(P)')).some(d => !d.warning && /undeclared parameter _Missing/.test(d.message)));
    }
    for (const kind of ['if', 'enableif']) {
      const nested = depth => drivers + Array.from({length: depth}, (_, i) =>
        `// @${kind === 'if' && i === 7 ? 'if' : 'enableif'} _Enabled == 1`).join('\n') +
        '\n// @preview(P)\n' + Array.from({length: depth}, (_, i) =>
        `// @end${kind === 'if' && depth - i - 1 === 7 ? 'if' : 'enableif'}`).join('\n');
      assert.deepEqual(validate(nested(16)), []);
      assert.ok(validate(nested(17)).some(d => !d.warning && /16 condition levels/.test(d.message)));
    }
    assert.ok(validate(drivers + block('if', '_Mode', block('enableif', '_Enabled', block('if', '_Mode', '// @preview(P)'))))
      .some(d => !d.warning && /Nested @if/.test(d.message)));
    const nestedMissing = drivers + block('enableif', '_Missing', block('enableif', '_Enabled', '// @preview(P)'));
    assert.ok(validate(nestedMissing).some(d => !d.warning && /undeclared parameter _Missing/.test(d.message)));
  });

  context.case('enableif drivers may be disabled but neither condition accepts a driver declared under if', async () => {
    for (const kind of ['if', 'enableif']) {
      const use = `// @${kind} _Driver == 1\n// @param float _Value\n// @end${kind}`;
      assert.deepEqual(validate('// @param toggle(float) _Enabled\n// @enableif _Enabled == 1\n// @param toggle(float) _Driver\n// @endenableif\n' + use), []);
      assert.ok(validate('// @param toggle(float) _Enabled\n// @if _Enabled == 1\n// @param toggle(float) _Driver\n// @endif\n' + use)
        .some(d => !d.warning && /outside @if blocks/.test(d.message)));
    }
    for (const kind of ['group', 'foldout']) {
      const source = `// @param toggle(float) _Enabled\n// @${kind}(Title; _Header)\n// @enableif _Enabled == 1\n// @param hidden toggle(float) _Header\n// @param float _Value\n// @endenableif\n// @end${kind}`;
      assert.ok(validate(source).some(d => !d.warning && /must be unconditional/.test(d.message)));
    }
  });

  context.case('layout grammar recognizes new opening and closing directives', async () => {
    const grammar = JSON.parse(readFileSync(new URL('../../../ExternalTools~/WhimTexVSCode/syntaxes/whimtex-hlsl.tmLanguage.json', import.meta.url), 'utf8'));
    const begin = new RegExp(grammar.patterns[0].begin);
    for (const name of ['group', 'endgroup', 'foldout', 'endfoldout', 'tabs', 'endtabs', 'tab', 'endtab', 'if', 'endif', 'enableif', 'endenableif']) {
      for (const spacing of ['', ' ', '\t'])
        assert.equal(begin.exec(`//${spacing}@${name}`)?.[1], '@' + name);
      assert.equal(begin.test(`// @${name}Extra`), false);
    }
  });

  context.case('layout snippets preserve native suffix expansion and generate balanced containers', async () => {
    const snippets = JSON.parse(readFileSync(new URL('../../../ExternalTools~/WhimTexVSCode/snippets/whimtex.json', import.meta.url), 'utf8'));
    assert.deepEqual(Object.values(snippets).map(s => s.prefix), ['@if', '@enableif', '@group', '@foldout', '@tabs', '@tab']);
    const expand = snippet => snippet.body.join('\n').replace(/\$\{\d+:([^}]*)\}/g, '$1').replace(/\$0/g, '');
    for (const snippet of Object.values(snippets).filter(s => !['@if', '@enableif'].includes(s.prefix))) {
      const body = expand(snippet).replace('// \n', '// @preview(P)\n');
      for (const prefix of ['//', '// ', '    // ', '\t//']) {
        const typed = prefix + snippet.prefix;
        const source = typed.slice(0, -snippet.prefix.length) + body;
        assert.equal(source.split('\n')[0], prefix + body.split('\n')[0]);
        assert.deepEqual(validate(snippet.prefix === '@tab' ? '// @tabs\n' + source + '\n// @endtabs' : source), []);
      }
      if (snippet.prefix === '@tabs') {
        assert.equal(snippet.body[0], '@tabs${1:}');
        assert.deepEqual(validate('// ' + body.replace('@tabs\n', '@tabs(Settings)\n')), []);
      }
      if (snippet.prefix === '@group' || snippet.prefix === '@foldout') {
        const bound = body.replace('Title', 'Title; _Enabled').replace('// @preview(P)', '// @param hidden toggle(float) _Enabled\n// @preview(P)');
        assert.deepEqual(validate('// ' + bound), []);
      }
    }
  });

  context.case('all bundled FX presets, including the Mask screenshot, validate without warnings', async () => {
    const folder = new URL('../../../src/FXPresets/', import.meta.url);
    for (const file of readdirSync(folder).filter(f => f.endsWith('.hlsl'))) {
      assert.deepEqual(validate(readFileSync(new URL(file, folder), 'utf8')), [], file);
    }
  });

  context.case('tooltips and disabled examples do not produce declarations', async () => {
    assert.deepEqual(validate('// @param curve _Profile // @param broken ;\n/*\n// @param bad _Bad\n*/'), []);
  });

  context.case('typed seed fields enforce exact integer bounds without name inference', async () => {
    const { parseParameter } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
    assert.equal(parseParameter('label(Seed) hidden seed(float) _Seed = 2 [~2 .. ~3]').seed, true);
    assert.equal(parseParameter('float _Seed = 0 [0 .. 10]').seed, false);
    for (const declaration of ['seed bool _Seed', 'seed enum _Seed {A:0}', 'seed(float) _Seed = 0.5',
      'seed(float) _Seed [0 ..]', 'seed(float) _Seed [0.1 .. 0.9]', 'seed seed(float) _Seed [0 .. 10]',
      'seed(float) _Seed [-16777218 .. 0]', 'seed(float) _Seed [0 .. 16777218]'])
      assert.throws(() => parseParameter(declaration), undefined, declaration);
    for (const n of [-16777216, 0, 16777216]) assert.equal(parseParameter(`seed(float) _Seed = ${n} [${n} .. ${n}]`).seed, true);
  });

  context.case('preview arguments refer to declared parameters and permit forward references', async () => {
    const { parseParameter } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
    assert.deepEqual(parseParameter('preview(P, _B, _A) float _A').previewParameters, ['_B', '_A']);
    assert.deepEqual(validate('// @preview(P, _B)\n// @param hidden float _B'), []);
    const missing = validate('// @preview(P, _MainTex)');
    assert.ok(missing.length === 1 && missing[0].warning && /undeclared @param _MainTex/.test(missing[0].message));
  });

  context.case('catalog marker is recognized and reports misplaced markers as a warning', async () => {
    assert.deepEqual(validate('// @whimtex-effect Color/Mask'), []);
    assert.equal(validate('\n// @whimtex-effect Color/Mask')[0].warning, true);
  });

  context.case('quoted preset groups do not open parameter containers', async () => {
    const layout = '// @group(Settings)\n// @param float _Amount\n// @endgroup';
    const header = '\uFEFF// @whimtex-effect Demo\n// @group(" Color ")';
    assert.deepEqual(validate(header + '\n' + layout), []);
    assert.deepEqual(validate(header + '\n// @icon("../Icons/color.svg")\n' + layout), []);
    assert.deepEqual(validate('// @group("Shapes/Colors")\n' + layout), []);
    assert.deepEqual(validate('/* // @group("Fake") */\n' + layout), []);
    for (const invalid of ['// @group("")', '// @group(" ")', '// @group("Color"',
      '// @group("Color")\n// @group("Tone")', 'float _Data;\n// @group("Late")',
      'x\n// @group("Late")', header + '\nfloat _Data;\n// @group("Late")'])
      assert.ok(validate(invalid).some(d => !d.warning), invalid);
  });

  context.case('logical pixel metadata supports only explicit numeric lengths', async () => {
    const { parseParameter } = require('../../../ExternalTools~/WhimTexVSCode/metadata.js');
    for (const kind of ['float', 'float2', 'float3', 'float4'])
      assert.equal(parseParameter(`hidden label(Length) px ${kind} _Length`).pixelUnits, true);
    assert.equal(parseParameter('float _Radius').pixelUnits, false);
    assert.deepEqual(validate('// @param px float _A\n// @param px float _A [0 .. 8]'), []);
    assert.ok(validate('// @param px float _A\n// @param float _A').some(d => !d.warning));
    for (const declaration of ['px bool _A', 'px color _A', 'px normal _A', 'px point _A',
      'px texture2D _A', 'px curve _A', 'px gradient _A', 'px transform2D _A', 'px enum _A {A:0}',
      'px seed(float) _A [0 .. 100]', 'px px float _A'])
      assert.throws(() => parseParameter(declaration), undefined, declaration);
  });

  context.case('removed migration directives warn without creating aliases', async () => {
    for (const source of ['// @formerlyserializedas(_Old)',
      '// @formerlyserializedas(_Old)\n// @param float _New',
      '// @formerlyserializedas(_Old)\n// @formerlyserializedas(_Old)\n// @param float _New']) {
      const messages = validate(source);
      assert.ok(messages.length > 0 && messages.every(d => d.warning && /Unknown WhimTex directive/.test(d.message)));
    }
  });

  context.case('effect control is optional, warning-only and last declaration wins', async () => {
    const param = '\n// @param hidden float _Opacity = 1 [0..1]';
    assert.deepEqual(validate('// @control(_Opacity)' + param), []);
    assert.deepEqual(validate('// @whimtex-effect Color/Test\n// @control(_Opacity)' + param), []);
    const duplicate = validate('// @control(_Missing)\n// @control(_Opacity)' + param);
    assert.equal(duplicate.length, 1);
    assert.ok(duplicate[0].warning && /last declaration wins/.test(duplicate[0].message));
    for (const source of ['// @control(_Missing)', '// @control()', '\n// @control(_Opacity)' + param]) {
      const warnings = validate(source);
      assert.ok(warnings.length && warnings.every(d => d.warning));
    }
    assert.deepEqual(validate('/*\n// @control(_Missing)\n*/'), []);
    assert.equal(validate('// @control(_Opacity)\n// @control()' + param).length, 2);
  });

  context.case('installer checks VSIX content as well as its unchanged version', async () => {
    const source = readFileSync(new URL('../../../src/Editor/ShaderFXExternalCode.cs', import.meta.url), 'utf8');
    assert.match(source, /sha\.ComputeHash\(archive\)/);
    assert.match(source, /File\.ReadAllText\(installedMarker\)\.Trim\(\) == extensionStamp/);
    assert.match(source, /pendingExtensionStamp, utf8WithoutBom/);
  });

  context.case('bundled VSIX includes the current validator and grammar', async () => {
    const archive = readFileSync(new URL('../../../ExternalTools~/WhimTexVSCode/whimtex-fx-tools.vsix', import.meta.url));
    assert.ok(archive.equals(buildVsix()), 'Run ExternalTools~/WhimTexVSCode/build-vsix.mjs');
  });

} catch (error) {
  context.case('Fixture initialization', async () => { throw error; });
}
await finish(context);
