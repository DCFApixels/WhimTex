// Reference arithmetic and source contracts only; does not compile or run Unity.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const dynamics = read('src/BrushDynamics.cs');
const brush = read('src/Layers/DrawingLayerBehaviour.Brush.cs');
const paint = read('src/Layers/DrawingLayerBehaviour.cs');
const shader = read('src/Shaders/PaintBrush.shader');
const blend = read('src/Shaders/Blend.shader');
const ui = read('src/TextureCompositorWindow.PostFx.cs');
const api = read('src/Automation/WhimTexApi.Paint.cs');
const inspect = read('src/Automation/WhimTexApi.Inspect.cs');
let checks = 0;
function near(a, b) { assert.ok(Math.abs(a - b) < 1e-7, a + ' != ' + b); checks++; }

// Execute the scalar spacing method after only translating C# declarations/casts.
const method = dynamics.slice(dynamics.indexOf('spacing = Math.Max'), dynamics.indexOf('return count;') + 13)
  .replace(/\bspacing =/, 'spacing =')
  .replace(/\bint count =/, 'let count =')
  .replace(/\(int\)/g, '').replace(/int.MaxValue/g, '2147483647')
  .replace(/Math.Max/g, 'Math.max').replace(/Math.Min/g, 'Math.min').replace(/Math.Floor/g, 'Math.floor')
  .replace(/(\d)d\b/g, '$1').replace('return count;', 'return {count, first, remaining};');
const sample = new Function('distance', 'spacing', 'includeStart', 'remaining', 'let first; ' + method);
function stamps(lengths, spacing) {
  let remaining = 0, position = 0;
  const result = [];
  lengths.forEach((length, i) => {
    const state = sample(length, spacing, i === 0, remaining);
    for (let j = 0; j < state.count; j++) result.push(position + state.first + j * Math.max(1, spacing));
    remaining = state.remaining;
    position += length;
  });
  return result;
}
assert.deepEqual(stamps([20], 4), [0, 4, 8, 12, 16, 20]); checks++;
assert.deepEqual(stamps([0, 1, 2, 3, 5, 9], 4), stamps([20], 4)); checks++;
assert.deepEqual(stamps([0, 0, 0], 4), [0]); checks++;
assert.deepEqual(stamps([5], .01), [0, 1, 2, 3, 4, 5]); checks++;
for (let spacing of [1, 1.25, 3.3, 10, 100]) {
  const one = stamps([100], spacing);
  const many = stamps(Array(400).fill(.25), spacing);
  assert.equal(one.length, many.length);
  one.forEach((value, i) => near(value, many[i]));
}
for (let opacity of [0, .1, .4, 1]) for (let flow of [0, .1, .5, 1]) {
  let stroke = 0;
  for (let i = 0; i < 20; i++) {
    stroke = flow + stroke * (1 - flow);
    assert.ok(stroke * opacity <= opacity + 1e-8); checks++;
  }
  near(stroke * opacity, opacity * (1 - (1 - flow) ** 20));
  near(.8 * (1 - stroke * opacity), .8 - .8 * stroke * opacity);
}
assert.match(dynamics, /opacity < 1f \|\| !erase && blend != BlendMode.Normal/);
assert.match(paint, /if \(isolatedStroke && segmentStamps.Count > 0\) EnsureAdvancedStroke/);
assert.match(brush, /Graphics.Blit\(advancedStrokeBase, target, material, 1\)/);
assert.match(read('src/TextureCompositor.cs'), /Graphics.Blit\(accumulator, result, material, 0\)/);
assert.match(blend, /float4 fragBrush/);
assert.match(shader, /multi_compile_local __ BRUSH_DYNAMICS BRUSH_TEXTURE/);
assert.ok(!paint.includes('GL.MultiTexCoord4('), 'Only supported GL coordinate methods');
assert.ok(paint.includes('if (!stampBlend) BeginBrushMesh();'));
const mesh = read('src/Layers/DrawingLayerBehaviour.BrushMesh.cs');
assert.ok(mesh.includes('brushMesh.SetUVs(5, meshColors);'));
assert.ok(mesh.includes('brushMesh.SetUVs(6, meshStamps);'));
assert.ok(mesh.includes('brushCommands.DrawMesh('));
assert.ok(shader.includes('float3 color : TEXCOORD5;'));
assert.ok(shader.includes('output.color = float4(input.color, input.size.y);'));
assert.match(shader, /brushDelta \/ _TipAspect/);
assert.ok(ui.indexOf('BuildBrushTab(tabs)') < ui.indexOf('tabs.Add(postFxTab)'));
assert.match(ui, /if \(postFxExpanded\) brushesExpanded = (?:uvExpanded = )?false/,
  'Expanding Post FX collapses Brushes');
assert.match(ui, /if \(brushesExpanded\) postFxExpanded = false/,
  'Expanding Brushes collapses Post FX');
assert.ok(!dynamics.includes('public Color tint'), 'No constant tint setting');
assert.ok(!brush.includes('dynamics.tint;'), 'No hidden constant tint multiplier');
assert.ok(!read('src/TextureCompositorWindow.Brushes.cs').includes('new ColorField("Tint")'));
assert.ok(!dynamics.includes('public bool randomTint'), 'Tint randomness is derived from gradient keys');
assert.ok(!read('src/TextureCompositorWindow.Brushes.cs').includes('new Toggle("Random Tint")'));
assert.ok(dynamics.includes('alphas[0].alpha != alphas[i].alpha'), 'Alpha keys also enable randomness');
assert.ok(brush.includes('if (!brushTintPrepared)'), 'Gradient analysis is outside the per-stamp loop');
assert.ok(dynamics.includes('tintGradient.EvaluateEncoded(SampleRandom(ref state, stampIndex, 4)) : constantTint'));
assert.ok(read('src/TextureCompositorWindow.Brushes.cs').includes('paintSettings.dynamics.ResetTint()'));
for (const field of ['opacity','flow','scatter','scatterBias','sizeJitter','angleJitter','angleOffset','flipX','flipY','rotationMode','randomAlgorithm','tip','tipChannel','tipSdf','proceduralMode','blend','blendApplication','seed']) {
  assert.ok(api.includes('"' + field + '"'), 'API write: ' + field);
  assert.ok(inspect.includes('["' + field + '"]'), 'API inspect: ' + field);
  checks += 2;
}
for (let degrees = -180; degrees <= 180; degrees++) {
  const a = degrees * Math.PI / 180, c = Math.cos(a), s = Math.sin(a);
  const extent = Math.abs(c) + Math.abs(s);
  for (const [x,y] of [[-1,-1],[-1,1],[1,-1],[1,1]]) {
    const rx = c*x-s*y, ry = s*x+c*y;
    assert.ok(Math.abs(rx) <= extent+1e-8 && Math.abs(ry) <= extent+1e-8);
    near(c*rx+s*ry,x); near(-s*rx+c*ry,y);
  }
}
// Execute Sobol's scalar C# bodies, translating declarations and unsigned arithmetic.
function body(signature) {
  const start = dynamics.indexOf('{', dynamics.indexOf(signature));
  assert.ok(start >= 0, signature);
  let depth = 1, end = start + 1;
  while (depth) { if (dynamics[end] === '{') depth++; if (dynamics[end] === '}') depth--; end++; }
  return dynamics.slice(start + 1, end - 1);
}
function js(source) {
  return source.replace(/\bunchecked\s*/g, '')
    .replace(/\b(uint|int)\s+(\w+)/g, 'let $2').replace(/\(uint\)/g, '')
    .replace(/\b(0x[\da-f]+|\d+)u\b/gi, '$1').replace(/(\d)f\b/g, '$1')
    .replace(/>>/g, '>>>').replace(/(result|directions)\[([^,\]]+), ([^\]]+)\]/g, '$1[$2][$3]');
}
const fill = new Function('result', 'dimension', 'degree', 'coefficients', 'initial', js(body('private static void FillDirections')));
const directions = Array.from({length: 7}, () => new Uint32Array(32));
for (let bit = 0; bit < 32; bit++) directions[0][bit] = 1 << (31-bit);
for (const match of dynamics.matchAll(/FillDirections\(result, (\d), (\d), (\d), new uint\[\] \{ ([\d, ]+) \}\)/g))
  fill(directions, +match[1], +match[2], +match[3], match[4].split(',').map(Number));
const shiftCode = js(body('private static uint DigitalShift'))
  .replace('0x9e3779b9 * (dimension + 1)', 'Math.imul(0x9e3779b9, dimension + 1)')
  .replace(/\(value \^ \(value >>> (\d+)\)\) \* (0x[\da-f]+)/g, 'Math.imul(value ^ (value >>> $1), $2)');
const digitalShift = new Function('seed', 'dimension', shiftCode);
const sobol = new Function('directions', 'DigitalShift', 'index', 'dimension', 'seed', js(body('internal static float Sample')))
  .bind(null, directions, digitalShift);
// Published first three dimensions: https://web.maths.unsw.edu.au/~fkuo/sobol/
const expected = [[0,0,0],[.5,.5,.5],[.75,.25,.25],[.25,.75,.75],[.375,.375,.625],
  [.875,.875,.125],[.625,.125,.875],[.125,.625,.375],[.1875,.3125,.9375],[.6875,.8125,.4375]];
for (let index = 0; index < expected.length; index++) for (let dim = 0; dim < 3; dim++) {
  const unshifted = ((sobol(index, dim, 1) * 16777216) ^ (digitalShift(1, dim) >>> 8)) / 16777216;
  near(unshifted, expected[index][dim]);
}
for (const seed of [0,1,123,2147483647,4294967295]) for (let dim = 0; dim < 7; dim++) {
  for (const count of [2,4,16,256,1024]) {
    const bins = new Set();
    for (let i = 0; i < count; i++) {
      const value = sobol(i, dim, seed);
      assert.ok(value >= 0 && value < 1); checks++;
      bins.add(Math.floor(value * count));
    }
    assert.equal(bins.size, count, 'Every dyadic interval receives one sample'); checks++;
  }
  for (const index of [0,1,1023,65536,2147483648,4294967295]) {
    const value = sobol(index, dim, seed);
    assert.ok(value >= 0 && value < 1);
    near(value, sobol(index, dim, seed));
  }
}
assert.notDeepEqual(Array.from({length:16}, (_,i) => sobol(i,0,1)), Array.from({length:16}, (_,i) => sobol(i,0,2)));
const flipPoints = [[0,.5,.75,.25,.125,.625,.875,.375], [0,.5,.25,.75,.375,.875,.125,.625]];
for (let axis = 0; axis < 2; axis++) for (let i = 0; i < 8; i++) {
  const dim = 5 + axis;
  near(((sobol(i,dim,1)*16777216) ^ (digitalShift(1,dim) >>> 8)) / 16777216, flipPoints[axis][i]);
}
for (const seed of [1,123,2147483647]) {
  const combinations = [0,0,0,0];
  for (let i = 0; i < 256; i++)
    combinations[(sobol(i,5,seed)<.5?1:0) + (sobol(i,6,seed)<.5?2:0)]++;
  assert.deepEqual(combinations, [64,64,64,64], 'Flip axes do not collapse to identical decisions'); checks++;
}
// Calls for unrelated controls do not perturb position or size dimensions.
for (let i = 0; i < 32; i++) {
  const before = sobol(i, 0, 17);
  sobol(i, 4, 17); sobol(i, 2, 17);
  near(before, sobol(i, 0, 17));
}
assert.match(paint, /brushStampIndex = 0;/);
assert.ok(brush.indexOf('brushStampIndex = unchecked(brushStampIndex + (uint)count)') < brush.indexOf('for (long i'));
assert.ok(brush.includes('uint stampIndex = unchecked(firstStamp + (uint)i)'));
for (let dim = 0; dim < 4; dim++) assert.ok(brush.includes('SampleRandom(ref brushRandomState, stampIndex, ' + dim + ')'));
assert.ok(dynamics.includes(': Random01(ref state)'), 'Default Random retains the existing generator');
assert.ok(read('src/TextureCompositorWindow.Brushes.cs').includes('new EnumField("Randomization"'));
const scalarJs = source => source.replace(/(\d)f\b/g, '$1').replace(/Mathf.Pow/g, 'Math.pow').replace(/Mathf.Sqrt/g, 'Math.sqrt');
const scatterExponent = new Function('scatterBias', scalarJs(body('internal float GetScatterExponent')));
const scatterRadius = new Function('sample', 'exponent', scalarJs(body('internal static float ScatterRadius')));
near(scatterExponent(0), .5);
near(scatterExponent(-1), 8);
near(scatterExponent(1), 1/32);
for (const bias of [-1,-.5,0,.5,1]) {
  const exponent = scatterExponent(bias);
  near(scatterRadius(0, exponent), 0);
  near(scatterRadius(1, exponent), 1);
  let sum = 0, previous = 0;
  for (let i = 0; i < 4096; i++) {
    const u = (i + .5) / 4096;
    const radius = scatterRadius(u, exponent);
    assert.ok(radius >= previous && radius >= 0 && radius <= 1); checks++;
    if (bias < 0) assert.ok(radius <= Math.sqrt(u));
    if (bias > 0) assert.ok(radius >= Math.sqrt(u));
    if (bias === 0) assert.equal(radius, Math.sqrt(u));
    previous = radius; sum += radius;
  }
  assert.ok(Math.abs(sum / 4096 - 1/(exponent+1)) < .0001); checks++;
  for (let i = 0; i < 256; i++) {
    const radius = scatterRadius(sobol(i, 1, 123), exponent);
    assert.ok(Number.isFinite(radius) && radius >= 0 && radius <= 1); checks++;
  }
}
assert.match(dynamics, /scatterBias = Mathf.Clamp\(Finite\(scatterBias, 0f\), -1f, 1f\)/);
assert.ok(brush.indexOf('dynamics.GetScatterExponent()') < brush.indexOf('for (long i'));
assert.ok(brush.includes('BrushDynamics.ScatterRadius(dynamics.SampleRandom(ref brushRandomState, stampIndex, 1), scatterExponent)'));
assert.ok(read('src/TextureCompositorWindow.Brushes.cs').includes('new Slider("Scatter Bias", -100f, 100f)'));
const settingsSource = read('src/PaintToolSettings.cs');
const drawerSource = read('src/TextureCompositorWindow.Brushes.cs');
for (const [section, fields] of [
  ['Tip', ['dynamics.tip', 'dynamics.source', 'dynamics.hlslCode', 'dynamics.hlslParameters', 'dynamics.hlslResolution', 'clipboardTipId', 'dynamics.tipChannel', 'dynamics.tipSdf', 'dynamics.proceduralMode', 'dynamics.tipGradient', 'brushTipGuid', 'brushTipLocalId', 'brushTipPresetPath']],
  ['Stamps', ['dynamics.randomAlgorithm', 'dynamics.scatter', 'dynamics.scatterBias', 'dynamics.sizeJitter', 'dynamics.angleJitter', 'dynamics.angleOffset', 'dynamics.rotationMode', 'dynamics.flipX', 'dynamics.flipY']],
  ['Color', ['dynamics.blend', 'dynamics.blendApplication']]
]) {
  const reset = settingsSource.match(new RegExp('void ResetBrush' + section + '\\(\\)\\s*\\{([^}]+)\\}'))?.[1];
  assert.ok(reset, section + ' reset exists');
  const assigned = [...reset.matchAll(/^\s*([\w.]+) = /gm)].map(match => match[1]);
  assert.deepEqual(assigned, fields, section + ' resets only its fields');
  for (const preserved of ['brushSize', 'brushHardness', 'brushSpacing', 'dynamics.opacity', 'dynamics.flow'])
    assert.ok(!assigned.includes(preserved), section + ' preserves ' + preserved);
  assert.ok(drawerSource.includes('CreateBrushSectionHeader("' + section + '", () => paintSettings.ResetBrush' + section + '()'));
  checks += 3;
}
assert.ok(settingsSource.includes('dynamics.ResetTint();'));
assert.ok(drawerSource.includes('ApplyPaintToolChange(reset);'));
assert.ok(drawerSource.includes('brushSettingsBindings?.Refresh(true);'));
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-brush-section-header\s*\{[^}]*background-color: rgba\(0, 0, 0, 0.15\)/);
const movementAngle = new Function('dx', 'dy', 'previous', scalarJs(body('internal static float MovementAngle')).replace('Mathf.Atan2', 'Math.atan2'));
for (const [dx,dy,expected] of [[1,0,0],[0,1,Math.PI/2],[-1,0,Math.PI],[0,-1,-Math.PI/2],[1,1,Math.PI/4]])
  near(movementAngle(dx,dy,0),expected);
near(movementAngle(0,0,1.2),1.2);
near(movementAngle(.00001,0,1.2),1.2);
near(movementAngle(.25*128,.5*64,0),Math.PI/4); // Non-square source pixels, not UV-space angle.
for (const jitter of [0,10,90,180]) for (const base of [0,Math.PI/4,Math.PI,-Math.PI/2]) {
  for (let i=0;i<32;i++) {
    const offset=jitter*Math.PI/180*(2*sobol(i,3,123)-1);
    assert.ok(Math.abs((base+offset)-base)<=jitter*Math.PI/180+1e-8); checks++;
  }
}
assert.ok(brush.includes('float rotation = baseRotation;'));
const constantOffset = 'if (dynamics?.tip != null) baseRotation += dynamics.angleOffset * Mathf.Deg2Rad;';
assert.ok(brush.includes(constantOffset));
assert.ok(brush.indexOf('baseRotation = brushDirection;') < brush.indexOf(constantOffset));
assert.ok(brush.indexOf(constantOffset) < brush.indexOf('float rotation = baseRotation;'));
assert.ok(brush.indexOf('float rotation = baseRotation;') < brush.indexOf('rotation += dynamics.angleJitter'));
assert.ok(dynamics.includes('angleOffset != 0f || angleJitter > 0f'));
assert.ok(dynamics.includes('angleOffset = Mathf.Clamp(Finite(angleOffset, 0f), -180f, 180f)'));
assert.ok(api.includes('Number(brush, "angleOffset", dynamics.angleOffset, -180f, 180f)'));
assert.ok(drawerSource.includes('new Slider("Angle Offset (°)", -180f, 180f)'));
const applyOffset = new Function('baseRotation', 'dynamics', 'Mathf', constantOffset + 'return baseRotation;');
for (const base of [0,Math.PI/4,Math.PI,-Math.PI/2])
  for (const angleOffset of [-180,-90,0,90,180]) {
    near(applyOffset(base,{tip:{},angleOffset},{Deg2Rad:Math.PI/180}),base+angleOffset*Math.PI/180);
    near(applyOffset(base,{tip:null,angleOffset},{Deg2Rad:Math.PI/180}),base);
  }
assert.ok(brush.includes('rotation += dynamics.angleJitter'));
assert.ok(brush.includes('MovementAngle((to.x - from.x) * width, (to.y - from.y) * height, brushDirection)'));
assert.ok(brush.indexOf('MovementAngle(')<brush.indexOf('for (long i'));
assert.ok(paint.includes('brushDirection = 0f;'));
assert.ok(paint.includes('parameters.Dynamics?.CanRotateTip == true'));
assert.ok(api.includes('parameters.Dynamics?.CanRotateTip == true'));
assert.ok(dynamics.includes('HasTint || CanRotateTip'));
assert.ok(drawerSource.includes('new EnumField("Rotation"'));
console.log('Brush reference/source checks passed: ' + checks + ' (GPU/UI not executed)');
