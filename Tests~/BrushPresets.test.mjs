import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { gzipSync, gunzipSync } from 'node:zlib';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8').replace(/\r\n/g,'\n');
const library=read('src/BrushPresetLibrary.cs'), settings=read('src/PaintToolSettings.cs');
const ui=read('src/TextureCompositorWindow.BrushPresets.cs');
const drawer = read('src/TextureCompositorWindow.Brushes.cs').split('private void AddBrushEdgeHeader')[0];
assert.ok(drawer.includes('new FloatField("Size")'));
assert.ok(drawer.includes('brushSettingsBindings.Track(size, () => paintSettings.brushSize)'));
for (const [label, member] of [['Opacity', 'opacity'], ['Flow', 'flow']])
  assert.ok(drawer.includes(`AddBrushPercent(scroll, "${label}", () => paintSettings.dynamics.${member}, v => paintSettings.dynamics.${member} = v`));
for (const member of ['brushSize', 'brushHardness', 'brushSpacing'])
  assert.ok(drawer.includes(`paintSettings.${member}`), `Preset setting accessible in drawer: ${member}`);
const dynamics = read('src/BrushDynamics.cs').split('[NonSerialized]')[0];
for (const [, member] of dynamics.matchAll(/public (?:float|int|bool|Gradient|Texture2D|Brush\w+|BlendMode) (\w+)/g)) {
  if (member === 'seed') continue; // Per-stroke runtime value, refreshed automatically when painting starts.
  assert.ok(drawer.includes(`paintSettings.dynamics.${member}`), `Preset dynamics accessible in drawer: ${member}`);
}
function body(source,name) {
  const declaration=source.search(new RegExp('(?:internal|private) (?:static )?(?:int|void) '+name+'\\('));
  assert.ok(declaration>=0,name);
  const start=source.indexOf('{',declaration); let end=start+1,depth=1;
  while(depth) { if(source[end]==='{')depth++; if(source[end]==='}')depth--; end++; }
  return source.slice(start+1,end-1);
}
const tipBytes=new Function('width','height','srgb',body(library,'TipByteCount')
  .replace(/\(long\)/g,'').replace('MaxTipPixels','16777216')
  .replace(/throw new IOException\([^;]+\);/g,'throw new Error("invalid dimensions");')
  .replace('checked(width * height * (srgb ? 4 : 8))','(width * height * (srgb ? 4 : 8))'));
assert.equal(tipBytes(0,0,true),0);
for (const [w,h] of [[1,1],[3,5],[512,512],[4096,4096],[8192,2048]])
  for(const srgb of [true,false]) assert.equal(tipBytes(w,h,srgb),w*h*(srgb?4:8));
for (const [w,h] of [[0,1],[1,0],[-1,1],[8193,1],[8192,8192],[2147483647,2147483647]])
  assert.throws(()=>tipBytes(w,h,false));

const apply=new Function('settings','preset','tip','path',
  'with(settings){'+body(settings,'ApplyPreset').replaceAll('string.Empty','""')+'}');
const current={brushSize:12,brushHardness:.9,brushSpacing:.1,dynamics:{},ownedPresetTip:{},
  brushTipGuid:'old',brushTipLocalId:22,brushTipPresetPath:'old-file',brushColor:'red',secondaryBrushColor:'blue',
  tool:'Eraser',pencilSize:7,fillTolerance:42,ReleasePresetTip(){this.ownedPresetTip=null;}};
const sdfStops=[{time:.2,color:[1,0,0,0]},{time:.7,color:[0,0,1,1]}];
const preset={size:46,hardness:.3,spacing:.6,dynamics:{angleOffset:90,opacity:.4,flow:.2,flipX:.5,flipY:1,tipSdf:true,proceduralMode:1,blendApplication:1,tipGradient:sdfStops}};
const tip={}; apply(current,preset,tip,'/library/Brushes/test.sebrush');
assert.equal(current.brushSize,46); assert.equal(current.brushHardness,.3); assert.equal(current.brushSpacing,.6);
assert.equal(current.dynamics,preset.dynamics); assert.equal(current.dynamics.tip,tip);
assert.equal(current.dynamics.flipX,.5); assert.equal(current.dynamics.flipY,1);
assert.deepEqual(current.dynamics.tipGradient,sdfStops); assert.equal(current.dynamics.tipSdf,true);
assert.equal(current.dynamics.proceduralMode,1);
assert.equal(current.dynamics.blendApplication,1);
assert.equal(current.ownedPresetTip,tip); assert.equal(current.brushTipGuid,''); assert.equal(current.brushTipLocalId,0);
for (const [key,value] of Object.entries({brushColor:'red',secondaryBrushColor:'blue',tool:'Eraser',pencilSize:7,fillTolerance:42}))
  assert.equal(current[key],value);
apply(current,{...preset,dynamics:{}},null,'/library/Brushes/round.sebrush');
assert.equal(current.brushTipPresetPath,'');

// Reference binary framing, not execution of the C# codec or Unity texture upload.
const magic=Number(library.match(/Magic = (0x[\da-f]+)/i)[1]);
const int=value=>{const b=Buffer.alloc(4);b.writeInt32LE(value);return b;};
function encode(meta,pixels=Buffer.alloc(0),version=1) {
  const json=Buffer.from(JSON.stringify(meta));
  return gzipSync(Buffer.concat([int(magic),int(version),int(json.length),json,int(pixels.length),pixels]));
}
function decode(compressed) {
  const data=gunzipSync(compressed); let p=0;
  const integer=()=>{const v=data.readInt32LE(p);p+=4;return v;};
  assert.equal(integer(),magic); assert.equal(integer(),1);
  const size=integer();assert.ok(size>=2&&size<=512*1024);
  const meta=JSON.parse(data.subarray(p,p+size));p+=size;
  const bytes=integer();assert.equal(bytes,tipBytes(meta.width,meta.height,meta.srgb));
  assert.equal(data.length-p,bytes);
  return {meta,pixels:data.subarray(p)};
}
for(const srgb of [true,false]) {
  const meta={size:46,hardness:.3,spacing:.6,dynamics:{angleOffset:90,flipX:.5,flipY:1,tipSdf:true,proceduralMode:1,blendApplication:1,tipGradient:sdfStops},width:3,height:5,srgb};
  const pixels=Buffer.from(Array.from({length:tipBytes(3,5,srgb)},(_,i)=>i%256));
  assert.deepEqual(decode(encode(meta,pixels)),{meta,pixels});
}
assert.throws(()=>decode(encode({width:0,height:0},Buffer.alloc(0),2)));
assert.throws(()=>decode(encode({width:3,height:5,srgb:true},Buffer.alloc(2))));
assert.throws(()=>decode(encode({width:0,height:0},Buffer.alloc(1))));
assert.ok(library.includes('Path.Combine(SpriteEditorUserSettings.PresetsFolder, "Brushes")'));
assert.ok(library.includes('PresetLibraryPaths.ProjectFiles(Extension)'));
assert.ok(library.includes('PresetLibraryPaths.UserFiles(Folder, Extension)'));
assert.ok(library.includes('FileMode.CreateNew'));
assert.ok(library.includes('File.Replace(temporary, path, path + ".bak")'));
assert.ok(library.includes('if (!overwrite) throw'));
assert.ok(library.includes('GL.sRGBWrite = previousSrgb'));
assert.ok(library.includes('RenderTexture.active = previous'));
assert.ok(library.includes('tip.isDataSRGB'));
assert.ok(library.includes('TextureFormat.RGBA32 : TextureFormat.RGBAHalf'));
assert.ok(ui.includes('BrushPresetLibrary.Load(path, out tip)'));
assert.ok(ui.includes('ApplyPaintToolChange(() => paintSettings.ApplyPreset'));
assert.ok(ui.includes('if (!string.IsNullOrEmpty(path)) SaveBrushPreset(path)'));
assert.ok(!/(AssetDatabase\.|Undo\.|ImportAsset|Refresh\(\))/.test(library+ui));
console.log('Brush preset settings/dimension/source checks and reference format round-trips passed (Unity/GPU not executed).');
