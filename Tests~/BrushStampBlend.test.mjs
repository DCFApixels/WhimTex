// Execute scalar lanes of the shared HLSL arithmetic; Unity/GPU are not executed.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8').replace(/\r\n/g,'\n');
const shared = read('src/Shaders/ColorBlend.cginc');
const scalar = shared.replace(/^#.*$/gm,'')
  .replace(/float[34]? (\w+)\(([^)]*)\)\s*\{/g,(_,name,args)=>'function '+name+'('+args.replace(/float[34]? /g,'')+') {')
  .replace(/\b(?:const )?(?:float[34]?|bool) (\w+)/g,'let $1')
  .replace('return before * (1.0 - alpha);','return float4(before.rgb * (1-alpha), before.a * (1-alpha));');
const saturate = x => Math.max(0,Math.min(1,x));
const lerp = (a,b,t) => a+(b-a)*t;
const factory = new Function('_HdrBlend','saturate','lerp','step','abs','pow','sqrt','max','min','sign','float4',
  scalar+'\nreturn CompositeBrushPixel;');
const composite = hdr => factory(hdr,saturate,lerp,(edge,v)=>v>=edge?1:0,Math.abs,Math.pow,Math.sqrt,Math.max,Math.min,Math.sign,
  (rgb,a)=>({rgb,a}));
const c = composite(1);
const pixel = (color,a=1) => ({rgb:color*a,a});
const mix = (before,after,opacity) => ({rgb:lerp(before.rgb,after.rgb,opacity),a:lerp(before.a,after.a,opacity)});
let checks=0;
function near(a,b) { assert.ok(Math.abs(a-b)<1e-7,a+' != '+b); checks++; }
function same(a,b) {near(a.rgb,b.rgb);near(a.a,b.a);}
const before=pixel(.8), stamp=pixel(.5);
const once=c(before,stamp,1,1,0,0), twice=c(once,stamp,1,1,0,0);
near(once.rgb,.4); near(twice.rgb,.2);
near(c(before,c(pixel(0,0),stamp,1,0,0,0),1,1,0,0).rgb,.4);
same(c(pixel(0,0),stamp,1,1,0,0),stamp);
near(c(c(pixel(.1),pixel(.2),1,4,0,0),pixel(.2),1,4,0,0).rgb,.5);
same(mix(before,twice,0),before); same(mix(before,twice,1),twice);
near(mix(before,twice,.5).rgb,.5);
// Opacity only displays the result, never feeds it back into the next stamp.
same(c(once,stamp,1,1,0,0),twice);
assert.notEqual(c(mix(before,once,.5),stamp,1,1,0,0).rgb,twice.rgb);
for(const hdr of [0,1]) for(const ba of [0,.1,.5,1]) for(const flow of [.1,.4,1]) for(const opacity of [0,.2,.7,1]) {
  const blend=composite(hdr), base=pixel(.6,ba);
  let accumulated=pixel(0,0), working=base;
  for(const shade of [.2,.8,.5]) {
    const s=pixel(shade,flow);
    accumulated=blend(accumulated,s,1,0,0,0);
    working=blend(working,s,1,0,0,0);
  }
  same(mix(base,working,opacity),blend(base,accumulated,opacity,0,0,0));
  near(mix(base,working,opacity).a,ba+(1-ba)*accumulated.a*opacity);
}
for(const hdr of [0,1]) for(const mode of [1,4,7,8,9,10,11,12,21,22])
  for(const ba of [.2,.7,1]) for(const sa of [.1,.5,1]) {
    const result=composite(hdr)(pixel(.4,ba),pixel(.3,sa),1,mode,0,1);
    assert.ok(Number.isFinite(result.rgb)); near(result.a,sa+ba*(1-sa));
  }
// Bounds of copied feedback region conservatively contain all rasterized vertices.
for(const [w,h] of [[1,1],[127,63],[512,1024]]) for(const [l,b,r,t] of [[.1,.3,.2,.6],[-.2,.1,.3,1.1],[0,0,1,1],[1.2,0,1.5,1]]) {
  const clamp=(v,max)=>Math.max(0,Math.min(max,v));
  const x=clamp(Math.floor(l*w)-1,w), y=clamp(Math.floor(b*h)-1,h);
  const right=clamp(Math.ceil(r*w)+1,w), top=clamp(Math.ceil(t*h)+1,h);
  assert.ok(x<=clamp(l*w,w)&&y<=clamp(b*h,h)&&right>=clamp(r*w,w)&&top>=clamp(t*h,h)); checks++;
}
const paint=read('src/Layers/DrawingLayerBehaviour.cs'), brush=read('src/Layers/DrawingLayerBehaviour.Brush.cs'), mesh=read('src/Layers/DrawingLayerBehaviour.BrushMesh.cs');
assert.ok(paint.includes('EnsureAdvancedStroke(surface, stampBlend)'));
assert.ok(brush.includes('if (withBackdrop) Graphics.Blit(advancedStrokeBase, advancedStroke)'));
assert.ok(paint.includes('if (stampBlend) BeginBrushMesh();'));
assert.ok(paint.includes('if (stampBlend) EndBrushMesh(target, material, snapshot);'));
assert.ok(paint.includes('snapshot = RenderTexture.GetTemporary(target.descriptor)'));
assert.ok(paint.includes('snapshot.filterMode = FilterMode.Point'));
assert.ok(mesh.indexOf('CopyStampBackdrop(target, backdrop)')<mesh.indexOf('brushCommands.DrawMesh'));
assert.ok(mesh.includes('Graphics.CopyTexture(target, 0, 0, x, y, right - x, top - y, backdrop, 0, 0, x, y)'));
assert.ok(mesh.includes('else Graphics.Blit(target, backdrop)'));
assert.ok(read('src/Shaders/Blend.shader').includes('if (_BrushStampAccumulation > .5) return lerp(before, stroke, saturate(_Opacity))'));
assert.ok(read('src/Shaders/PaintBrush.shader').includes('CompositeBrushPixel(tex2D(_Backdrop, input.canvasUv), stamp, 1.0, _StampBlendMode, 0.0, _TipStandard)'));
assert.ok(brush.includes('material.SetFloat("_BrushStampAccumulation", 0f)'));
const dynamics=read('src/BrushDynamics.cs');
assert.ok(dynamics.includes('enum BrushBlendApplication { Stroke, Stamp }'));
assert.ok(dynamics.includes('!erase && blend != BlendMode.Normal && blendApplication == BrushBlendApplication.Stamp'));
assert.ok(read('src/Automation/WhimTexApi.Paint.cs').includes('dynamics.blendApplication = Enum(brush, "blendApplication", dynamics.blendApplication)'));
assert.ok(read('src/Automation/WhimTexApi.Inspect.cs').includes('["blendApplication"] = dynamics.blendApplication.ToString()'));
console.log('Brush per-stamp blend scalar/reference/source checks passed: '+checks+' (Unity/GPU not executed).');
