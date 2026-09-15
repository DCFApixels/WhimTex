// Source and scalar/reference checks; no Unity compilation or GPU execution.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const shader = read('src/Shaders/PaintBrush.shader');
const cache = read('src/Layers/DrawingLayerBehaviour.BrushGradient.cs');
const width = Number(cache.match(/SdfGradientWidth = (\d+)/)[1]);
const saturate = v => Math.max(0,Math.min(1,v));
const coordBody = shader.match(/float u = ([^;]+);/)[1];
const coord = new Function('value','_BrushSdfGradient_TexelSize','saturate','return '+coordBody);
const uv = value => coord(value,{x:1/width},saturate);
let checks = 0;
const near = (a,b) => { assert.ok(Math.abs(a-b)<1e-8, a+' != '+b); checks++; };
near(uv(0),.5/width); near(uv(1),1-.5/width);
near(uv(-10),uv(0)); near(uv(10),uv(1));
for(let i=0;i<width;i++) near(uv(i/(width-1))*width-.5,i);

// Reference linear filtering of the premultiplied LUT, including zero-alpha RGB.
const gradient = t => [1-t,.25,t,1-saturate((t-.4)/.2)];
const lut = Array.from({length:width},(_,i)=>{
  const [r,g,b,a]=gradient(i/(width-1)); return [r*a,g*a,b*a,a];
});
function sample(value) {
  const p=uv(value)*width-.5, lo=Math.max(0,Math.floor(p)), hi=Math.min(width-1,lo+1), f=p-lo;
  return lut[lo].map((v,i)=>v+(lut[hi][i]-v)*f);
}
for(let i=0;i<=1000;i++) {
  const v=i/1000, rgba=sample(v), reference=gradient(v);
  assert.ok(rgba.every(Number.isFinite));
  assert.ok(Math.abs(rgba[3]-reference[3])<.002);
  const color=rgba.slice(0,3).map(c=>c/Math.max(rgba[3],.00001));
  for(let channel=0;channel<3;channel++) near(color[channel]*rgba[3],rgba[channel]);
  checks+=2;
}
near(sample(0)[3],1); near(sample(.5)[3],.5); near(sample(1)[3],0);
assert.ok(cache.includes('sdfGradientSnapshot.Equals(source)'));
assert.ok(cache.indexOf('return sdfGradientTexture;') < cache.indexOf('SetPixels('));
assert.ok(cache.includes('TextureFormat.RGBAHalf, true, true'));
assert.ok(cache.includes('filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp'));
assert.ok(cache.includes('sdfGradientTexture.Apply(true, false)'));
assert.ok(cache.includes('HdrUtility.DecodePaintColor(evaluated.EvaluateEncoded('));
assert.ok(cache.includes('color.r * color.a, color.g * color.a, color.b * color.a, color.a'));
assert.ok(cache.includes('sdfGradientStandardInputs == standardInputs'));
assert.ok(cache.includes('WhimTexColorInputs.StandardColor(colors[i].color)'));
assert.ok(cache.includes('sdfGradientSnapshot = source.Clone()'));
assert.ok(cache.includes('Object.DestroyImmediate(sdfGradientTexture)'));
assert.ok(read('src/Layers/DrawingLayerBehaviour.BrushMesh.cs').includes('ReleaseBrushSdfGradient();'));
assert.ok(shader.includes('float tipValue = tip.a;') && shader.includes('float tipOpacity = 1.0;'));
assert.ok(shader.includes('tipValue = _TipChannel > 1.5 ? 1.0 - value : value;'));
assert.ok(shader.includes('tipOpacity = tip.a;'));
assert.ok(shader.includes('coverage = tipValue * tipOpacity;'));
assert.ok(shader.includes('coverage = gradient.a * tipOpacity;'));
assert.ok(shader.includes('color.rgb *= gradient.rgb / max(gradient.a, .00001);'));
assert.ok(!shader.includes('_TipThreshold') && !shader.includes('SdfTipCoverage'));
const dynamics = read('src/BrushDynamics.cs'), drawer = read('src/TextureCompositorWindow.Brushes.cs');
assert.ok(dynamics.includes('public WhimTexGradient tipGradient = DefaultTipGradient();'));
const usesSdfBody = dynamics.match(/internal bool UsesSdfGradient => ([^;]+);/)[1];
const usesSdf = new Function('tip','tipSdf','proceduralMode','BrushProceduralMode','return '+usesSdfBody);
for(const tip of [null,{}]) for(const textureSdf of [false,true]) for(const mode of [0,1])
  assert.equal(usesSdf(tip,textureSdf,mode,{SdfGradient:1}),tip ? textureSdf : mode===1);
assert.ok(dynamics.includes('proceduralMode = BrushProceduralMode.Hardness;'));
assert.ok(read('src/Layers/DrawingLayerBehaviour.cs').includes('bool sdfGradient = !pixelPerfect && dynamics != null && dynamics.UsesSdfGradient;'));
assert.ok(shader.includes('SdfTipGradient(radius)'));
assert.ok(shader.includes('SdfTipGradient(1.0 - tipValue)'));
const textureCoordinate = new Function('tipValue', 'return ' + shader.match(/SdfTipGradient\((1\.0 - tipValue)\)/)[1]);
near(textureCoordinate(1), 0); near(textureCoordinate(0), 1);
for (let i = 0; i <= 100; i++) near(textureCoordinate(1-i/100), i/100);
const proceduralStart=shader.indexOf('SdfTipGradient(radius)');
assert.ok(shader.lastIndexOf('if (radius > 1.0) discard;',proceduralStart)>shader.indexOf('#else',shader.indexOf('float radius = length')));
assert.ok(shader.includes('coverage = 1.0 - smoothstep(inner, 1.0, radius);'));
for(const r of [0,.1,.25,.5,.75,.9,1]) {
  const value=r;
  near(uv(value)*width-.5,value*(width-1));
  for(let angle=0;angle<Math.PI*2;angle+=.15)
    near(Math.hypot(r*Math.cos(angle),r*Math.sin(angle)),value);
}
assert.ok(drawer.includes('new GUIContent("Hardness")'));
assert.ok(drawer.includes('new GUIContent("Gradient")'));
assert.ok(drawer.includes('bool sdf = paintSettings.dynamics.UsesSdfGradient;'));
assert.ok(read('src/PaintToolSettings.cs').includes('dynamics.proceduralMode = defaults.dynamics.proceduralMode;'));
assert.ok(dynamics.includes('new GradientAlphaKey(1f, .4f), new GradientAlphaKey(0f, .6f)'));
assert.ok(drawer.includes('new WhimTexGradientValueField("Gradient")'));
assert.ok(!drawer.includes('new Slider("Threshold"'));
assert.ok(drawer.includes('hardness.SetEnabled(paintSettings.dynamics.tip == null)'));
const header = drawer.split('private void AddBrushEdgeHeader(VisualElement row)')[1].split('private void AddBrushHeaderPercent')[0];
assert.ok(header.includes('new FloatField("Hardness")'), 'Keep the native draggable field label');
assert.ok(header.includes('toolkitHeaderBindings.Track(hardness, () => paintSettings.brushHardness * 100f)'));
assert.ok(header.includes('toolkitHeaderBindings, () => paintSettings.dynamics.tipGradient'));
assert.ok(header.includes('hardness.EnableInClassList("whimtex-brush-setting--hidden", sdf)'));
assert.ok(header.includes('gradient.EnableInClassList("whimtex-brush-setting--hidden", !sdf)'));
assert.ok(header.includes('mode.SetEnabled(paintSettings.dynamics.tip == null)'));
assert.ok(!header.includes('GetSelectedLayer'), 'Brush settings remain editable without a Drawing layer');
const drawerControls = drawer.split('private void AddBrushEdgeHeader')[0];
assert.ok(drawerControls.includes('new WhimTexGradientValueField("Gradient")'));
assert.ok(drawerControls.includes('new DropdownField("Mode"'));
assert.ok(drawerControls.includes('AddBrushPercent(scroll, "Hardness", () => paintSettings.brushHardness'));
assert.ok(drawerControls.includes('brushSettingsBindings, () => paintSettings.dynamics.tipGradient'));
const applyChange = read('src/TextureCompositorWindow.Tools.cs').split('private void ApplyPaintToolChange(Action change)')[1].split('private void SavePaintToolSettings')[0];
assert.ok(applyChange.includes('toolkitHeaderBindings.Refresh();'));
assert.ok(applyChange.includes('brushSettingsBindings?.Refresh();'), 'Both copies update after editing either location');
assert.match(read('src/TextureCompositorWindow.UI.cs'), /brushRow.Add\(size\);\s*AddBrushEdgeHeader\(brushRow\);\s*AddBrushHeaderPercent\(brushRow, "Opacity"/);
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-brush-edge\s*\{\s*width: 150px;\s*height: 20px;\s*flex-shrink: 0;/);
assert.ok(read('src/Layers/DrawingLayerBehaviour.cs').includes('GetBrushSdfGradient(dynamics, standardColorInputs)'));
assert.ok(read('src/Automation/WhimTexApi.Paint.cs').includes('dynamics.tipGradient = ReadGradient(brush["tipGradient"])'));
assert.ok(read('src/Automation/WhimTexApi.Inspect.cs').includes('["tipGradientKeys"] = GradientSnapshot(dynamics.tipGradient)'));
assert.ok(read('src/PaintToolSettings.cs').includes('dynamics.tipGradient = defaults.dynamics.tipGradient'));
assert.ok(read('src/PaintStrokeParameters.cs').includes('StandardColorInputs = source.StandardColorInputs'));
for(const path of ['src/PaintToolSettings.cs','src/TextureCompositorWindow.BrushPreview.cs'])
  assert.ok(read(path).includes('standardColorInputs: !WhimTexColorInputs.Hdr'));
assert.ok(read('src/BrushPresetLibrary.cs').includes('JsonUtility.ToJson(settings.dynamics)'));
console.log('SDF gradient LUT scalar/reference/source checks passed: '+checks+' (Unity/GPU not executed).');
