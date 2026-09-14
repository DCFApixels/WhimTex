// Standalone reference-math and source-contract checks; no Unity compilation or GPU validation.
// Run: node Tests~/PostFxSurfaceMath.test.mjs
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const shader = read('../src/PostFx/URP/URPPreviewSurface.shader');
const backend = read('../src/PostFx/URP/UrpPostFxPreview.cs');
const window = read('../src/TextureCompositorWindow.PostFx.cs');
assert.deepEqual([...shader.matchAll(/Name "([^"]+)"/g)].map(m => m[1]),
  ['Surface', 'DepthOnly', 'NormalizeOutput', 'DepthNormals']);
for (const contract of ['"LightMode"="UniversalForwardOnly"', '"LightMode"="DepthNormalsOnly"',
  'SurfaceDepth(alpha)', 'SurfaceDistance(alpha)', 'saturate(left)', 'saturate(right)',
  '#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION',
  '#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT',
  '#pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS', '_ScaleBiasRt.x'])
  assert.ok(shader.includes(contract), contract);
for (const contract of ['camera.cameraType = CameraType.Game;', 'data.requiresDepthTexture = false;',
  'data.requiresColorTexture = false;', 'camera.scene = scene;', 'RendererStateHash(source, hash)',
  'camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);',
  'Graphics.Blit(rendered, destination, material, 2);'])
  assert.ok(backend.includes(contract), contract);
assert.ok(backend.indexOf('camera.overrideSceneCullingMask =') > backend.indexOf('camera.CopyFrom(source);'),
  'Reset the copied Scene View stage mask after CopyFrom, not only when creating the camera');
assert.ok(!backend.includes('ReadPixels'));
assert.ok(window.includes('if (!postFxEnabled || previewTexture == null) return;'));
assert.ok(window.includes('postFxBackend?.Dispose(); postFxBackend = null;'));
assert.ok(shader.includes('lerp(BackgroundColor(input.uv),color.rgb,saturate(color.a))'));
assert.ok(shader.indexOf('output.depth = SurfaceDepth(color.a);') < shader.indexOf('lerp(BackgroundColor(input.uv)'));
assert.ok(window.includes('new Vector2(compositor.width, compositor.height)'), 'Checker scale must not depend on Live Quality');
const request = read('../src/PostFxPreview.cs');
assert.ok(request.includes('checkerSize = WhimTexUserSettings.CheckerSize;'));
assert.ok(request.includes('checkerLight = WhimTexUserSettings.CheckerLight;'));
assert.ok(request.includes('checkerDark = WhimTexUserSettings.CheckerDark;'));
const checker = (x,y,size) => (Math.floor(x/size)+Math.floor(y/size))%2;
assert.equal(checker(0,0,16),0);
assert.equal(checker(16,0,16),1);
assert.equal(checker(0,16,16),1);
assert.equal(checker(16,16,16),0);
assert.equal(checker(31.99,0,16),1);
assert.equal(checker(32,0,16),0);

const clamp = x => Math.min(1, Math.max(0,x));
const distance = (alpha,mode,invert=false) => {
  const h = invert ? 1-clamp(alpha) : clamp(alpha);
  return mode === 'Solid' ? 10 : mode === 'AlphaHeight' ? 10+(1-h)*5 : h<.5 ? 1000 : 10;
};
assert.equal(distance(0,'AlphaHeight'),15);
assert.equal(distance(1,'AlphaHeight'),10);
assert.equal(distance(0,'AlphaHeight',true),10);
assert.equal(distance(.49,'AlphaMask'),1000);
assert.equal(distance(.5,'AlphaMask'),10);
const sub = (a,b) => a.map((x,i) => x-b[i]);
const dot = (a,b) => a.reduce((v,x,i) => v+x*b[i],0);
const unit = a => a.map(x => x/Math.hypot(...a));
const cross = (a,b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const view = (uv,d,ortho) => [(2*uv[0]-1)*(ortho?5:d*.6),(2*uv[1]-1)*(ortho?5:d*.6),-d];
function normal(uv,sample,ortho,size) {
  const step=1/size, d=sample(uv), center=view(uv,d,ortho);
  const derivative = axis => {
    const a=[...uv], b=[...uv]; a[axis]-=step; b[axis]+=step;
    const da=sample(a.map(clamp)), db=sample(b.map(clamp));
    return Math.abs(db-d)<=Math.abs(da-d) ? sub(view(b,db,ortho),center) : sub(center,view(a,da,ortho));
  };
  const n=unit(cross(derivative(0),derivative(1)));
  return n[2]<0 ? n.map(x=>-x) : n;
}
for(const ortho of [false,true]) for(const size of [64,512,2024]) {
  const flat=normal([.5,.5],()=>10,ortho,size);
  assert.ok(dot(flat,[0,0,1])>.99999);
  const slope=normal([.5,.5],uv=>10+uv[0]*5,ortho,size);
  const inverted=normal([.5,.5],uv=>15-uv[0]*5,ortho,size);
  assert.ok(slope[0]>0 && inverted[0]<0 && slope[2]>0 && inverted[2]>0);
  const edge=normal([.5,.5],uv=>uv[0]<.5?1000:10,ortho,size);
  assert.ok(dot(edge,[0,0,1])>.99999, 'Prefer the continuous side of an alpha-mask edge');
}
// URP deferred octahedral packing: verify both hemispheres and the 12+12 bit payload.
function encode(n) {
  n=n.map(x=>x/n.reduce((s,y)=>s+Math.abs(y),0));
  const t=clamp(-n[2]);
  const i=n.slice(0,2).map(x=>Math.trunc(clamp((x+(x>=0?t:-t))*.5+.5)*4095.5));
  return [i[0]&255,i[1]&255,(i[0]>>8)|((i[1]>>8)<<4)];
}
function decode(bytes) {
  const f=[bytes[0]|((bytes[2]&15)<<8),bytes[1]|((bytes[2]>>4)<<8)].map(x=>x/4095*2-1);
  const n=[...f,1-Math.abs(f[0])-Math.abs(f[1])], t=Math.max(-n[2],0);
  n[0]+=n[0]>=0?-t:t; n[1]+=n[1]>=0?-t:t;
  return unit(n);
}
for(let i=0;i<10000;i++) {
  const z=2*(i+.5)/10000-1, phi=i*2.399963229728653;
  const n=[Math.sqrt(1-z*z)*Math.cos(phi),Math.sqrt(1-z*z)*Math.sin(phi),z];
  assert.ok(dot(n,decode(encode(n)))>.99999,'Octahedral normal round trip');
}
console.log('Post FX source contracts, relief normals and 10000 packed-normal reference cases passed. GPU validation still required.');
