// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
const context = new TestContext("NoiseContract source/reference tests");
const assert = context.assert;
context.case("NoiseContract original assertion inputs and source contracts", async () => {

  const read = path => readFileSync(new URL('../../../' + path, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
  const fnl = read('src/Shaders/ThirdParty/FastNoiseLite.hlsl');
  const layer = read('src/Layers/NoiseLayerBehaviour.cs');
  const shader = read('src/Shaders/Noise.shader');
  const api = read('src/Automation/WhimTexApi.Noise.cs');
  const ui = read('src/Layers/Editors/NoiseLayerEditorWindow.cs');
  const guard = '#ifndef WHIMTEX_FASTNOISELITE_INCLUDED\n#define WHIMTEX_FASTNOISELITE_INCLUDED\n\n';
  assert.ok(fnl.includes(guard) && fnl.endsWith('#endif\n'), 'Built-in noise has a duplicate-include guard');
  const upstream = fnl.replace(guard, '').replace(/#endif\n$/, '')
      .replace(/#ifdef WHIMTEX_NOISE_LATTICE\n[\s\S]*?#else\n([\s\S]*?)#endif\n/g, '$1')
      .replace(/#if defined\(WHIMTEX_NOISE_TYPE\)\n[\s\S]*?#elif UNITY_VERSION\n/, '#if UNITY_VERSION\n');
  assert.equal(createHash('sha256').update(upstream).digest('hex'),
      '275f0e558ea7fd967dd0a3f47f14397759e9e6da403d11c290484707dfb8c2cb', 'Pinned upstream HLSL is unchanged');

  const mappings = {
      NoiseType: ['FNL_NOISE_OPENSIMPLEX2', 'FNL_NOISE_OPENSIMPLEX2S', 'FNL_NOISE_CELLULAR', 'FNL_NOISE_PERLIN', 'FNL_NOISE_VALUE_CUBIC', 'FNL_NOISE_VALUE'],
      FractalType: ['FNL_FRACTAL_NONE', 'FNL_FRACTAL_FBM', 'FNL_FRACTAL_RIDGED', 'FNL_FRACTAL_PINGPONG'],
      CellularDistance: ['FNL_CELLULAR_DISTANCE_EUCLIDEAN', 'FNL_CELLULAR_DISTANCE_EUCLIDEANSQ', 'FNL_CELLULAR_DISTANCE_MANHATTAN', 'FNL_CELLULAR_DISTANCE_HYBRID'],
      CellularReturn: ['FNL_CELLULAR_RETURN_TYPE_CELLVALUE', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2ADD', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2SUB', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2MUL', 'FNL_CELLULAR_RETURN_TYPE_DISTANCE2DIV'],
      WarpType: [null, 'FNL_DOMAIN_WARP_OPENSIMPLEX2', 'FNL_DOMAIN_WARP_OPENSIMPLEX2_REDUCED', 'FNL_DOMAIN_WARP_BASICGRID']
  };
  for (const [type, constants] of Object.entries(mappings)) {
      const entries = layer.match(new RegExp(`public enum ${type} \\{([^}]+)\\}`))[1].split(',').map(s => s.trim());
      assert.equal(entries.length, constants.length + (type === 'NoiseType' ? 2 : 0), type);
      if (type === 'NoiseType') assert.equal(entries[6], 'WhiteNoise', 'White Noise appends without renumbering FastNoiseLite algorithms');
      if (type === 'NoiseType') assert.equal(entries[7], 'BlueNoise', 'Blue Noise appends without renumbering previous algorithms');
      constants.forEach((name, index) => {
          if (name === null) return;
          const value = Number(fnl.match(new RegExp(`#define ${name} (\\d+)\\b`))[1]);
          assert.equal(value, index - (type === 'WarpType' ? 1 : 0), `${type}.${entries[index]}`);
      });
  }
  for (const [, name] of layer.matchAll(/material\.Set(?:Integer|Float|Vector)\("([^"]+)"/g))
      assert.match(shader, new RegExp(`\\b${name}\\b`), `Shader uniform ${name}`);
  for (const [, field] of layer.matchAll(/^        public (?:\w+) (\w+)(?:[ \t]*=(?!>).*)?;/gm)) {
      if (field === 'scale' || field === 'scaleY' || field === 'scaleZ') {
          assert.ok(api.includes('layer.Scale') && ui.includes('layer.Scale'), 'Scale axes use the shared value accessor');
          continue;
      }
      if (field === 'warpScale' || field === 'warpScaleY' || field === 'warpScaleZ') {
          assert.ok(api.includes('layer.WarpScale') && ui.includes('layer.WarpScale'), 'Warp axes use the shared value accessor');
          continue;
      }
      assert.ok(api.includes(`"${field}"`), `API setting: ${field}`);
      assert.ok(ui.includes(`layer.${field}`), `UI setting: ${field}`);
  }
  assert.match(layer, /SetInteger\("_NoiseSeed", seed\)/, 'No lossy float conversion of seed');
  assert.match(shader, /#pragma target 4\.5/);
  assert.match(shader, /if \(_NoiseType == 6 \|\| _NoiseType == 7\)/);
  assert.ok(shader.indexOf('WhiteNoise(i.uv)') < shader.indexOf('fnl_state state = fnlCreateState(_NoiseSeed)'), 'White Noise bypasses fractal and warp');
  const commonFractal = shader.match(/float FractalNoise\([\s\S]*?(?=\n            float Potential)/)?.[0];
  assert.ok(commonFractal, 'Nonperiodic noise has a shared fractal evaluator');
  assert.equal((commonFractal.match(/_fnlGenNoiseSingle2D\(/g) ?? []).length, 1, 'One 2D kernel call site for all fractals');
  assert.equal((commonFractal.match(/_fnlGenNoiseSingle3D\(/g) ?? []).length, 1, 'One 3D kernel call site for all fractals');
  assert.match(commonFractal, /\[loop\] for/, 'Fractal kernel stays inside a rolled loop');
  assert.match(shader, /return FractalNoise\(state, p\)/);
  const lattice = read('src/Shaders/NoiseLattice.cginc');
  assert.match(lattice, /WtPosition\(int octave, float2 uv, float3 displacement, int layout\)/);
  assert.match(lattice, /wtLayout = layout;/, 'Each caller supplies the lattice layout explicitly');
  assert.doesNotMatch(lattice, /wtLayout = \(int\)_NoiseLattice/, 'Noise hash does not depend on a runtime layout array lookup');
  assert.match(shader, /WtPosition\(octave, uv, displacement, WT_NOISE_LATTICE_LAYOUT\)/);
  assert.match(shader, /WtPosition\(8, uv, float3\(0, 0, zDelta\), \(int\)_NoiseWarpInverse\.w\)/, 'Warp keeps its independent layout');
  assert.match(ui, /fractalChoice.EnableInClassList\("whimtex-hidden", isGrain \|\| isCellDirection\)/);
  assert.match(ui, /warpChoice.EnableInClassList\("whimtex-hidden", isGrain\)/);
  assert.match(shader, /return float4\(rgb, 1\.0\)/);
  assert.match(shader, /if \(_NoiseEncoding == 0\) rgb = SpriteDecode\(rgb\)/);
  assert.doesNotMatch(layer, /ReadPixels|GetPixels|SetPixels|GetRawTextureData/);
  assert.doesNotMatch(ui, /\.Clear\(|\.isDelayed\s*=\s*true/);
  assert.match(ui, /ImmediateLayerPreviewUpdates => true/);
  assert.match(read('src/Utils.cs'), /protected virtual bool ImmediateLayerPreviewUpdates => false/);
  assert.doesNotMatch(read('src/Utils.cs'), /protected virtual bool ImmediatePreviewUpdates/);
  assert.match(read('src/WhimTexWindow.cs'), /immediate \|= GetSelectedLayer\(\)\?\.Behaviour is NoiseLayerBehaviour/);
  assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-hidden,\s*\.whimtex-brush-setting--hidden\s*\{\s*display: none;/);
  assert.match(read('src/LayerTypeRegistry.cs'), /new Entry\("noise", "Noise", "Noise", "Noise Layer", typeof\(NoiseLayerBehaviour\)/);
  assert.match(read('src/Automation/WhimTexApi.Layers.cs'), /LayerTypeRegistry.Find\(type\)/);
  assert.match(read('src/Automation/WhimTexApi.Inspect.cs'), /LayerTypeRegistry.Find\(layer\?\.Behaviour\?\.GetType\(\)\)\?\.ApiId/);
  const schema = JSON.parse(read('Documentation~/AI/agent-fields.schema.json'));
  const fields = schema.$defs.noise.properties;
  const keys = [...api.match(/Keys\(value,([\s\S]*?)\);/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]);
  const snapshotKeys = [...api.split('private static JObject NoiseSnapshot')[1].matchAll(/\["([^"]+)"\] =/g)].map(m => m[1]);
  assert.deepEqual(Object.keys(fields).sort(), keys.sort());
  assert.deepEqual(snapshotKeys.sort(), keys.sort());
  for (const [field, type] of Object.entries({field:'Field',vectorOutput:'VectorOutput',noiseType:'NoiseType',dimensions:'NoiseDimensions',periodic:'PeriodicAxes',encoding:'OutputEncoding',fractal:'FractalType',warp:'WarpType'})) {
      const names = layer.match(new RegExp(`public enum ${type} \\{([^}]+)\\}`))[1].split(',').map(s => s.trim());
      assert.deepEqual(fields[field].enum, names, field);
  }
  assert.equal(fields.encoding.default, 'LinearData');
  assert.equal(fields.dimensions.default, 'TwoD');
  assert.equal(fields.periodic.default, 'None');
  assert.equal(fields.periodic1D.default, false);
  assert.equal(fields.periodic1D.type, 'boolean');
  assert.equal(fields.linkScale.default, true);
  assert.equal(fields.linkWarpScale.default, true);
  assert.deepEqual(fields.warpScale.default, [1,1,1]);
  assert.equal(fields.warpScale.oneOf[1].maxItems, 3);
  assert.equal(fields.field.default, 'Value');
  assert.equal(fields.vectorOutput.default, 'PackedVector');
  assert.equal(fields.normalize.default, false);
  assert.equal(fields.strength.default, 1);
  assert.equal(fields.warpSeed.default, 1337);
  assert.deepEqual(fields.scale.default, [8,8,1]);
  assert.equal(fields.scale.oneOf[1].maxItems, 3);
  assert.match(layer, /public float scaleZ = 1f;/, 'Old files retain their slice coordinate');
  assert.match(ui, /scale3D.EnableInClassList\("whimtex-hidden", !three\)/, 'Scale Z is visible only in effective 3D');
  assert.deepEqual(fields.offset.default, [0,0,0]);
  assert.match(fields.periodic.description, /UI Seamless/);
  assert.match(fields.gradient.description, /does not change encoding/);
  const sdf = schema.$defs.sdf.properties;
  assert.deepEqual(sdf.encoding.enum, ['LinearData','Gradient']);
  assert.equal(sdf.encoding.default, 'Gradient');
  assert.equal(sdf.gradient.$ref, '#/$defs/gradient');
  assert.equal(fields.gradient.$ref, '#/$defs/gradient');
  assert.ok(!Object.hasOwn(fields, 'useGradient') && !Object.hasOwn(sdf, 'useGradient'));
  assert.ok(!Object.hasOwn(fields, 'seamless') && !Object.hasOwn(fields, 'scaleY') && !Object.hasOwn(fields, 'offsetZ'));

});
await finish(context);
