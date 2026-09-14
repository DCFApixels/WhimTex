// Executes extracted scalar/control-flow functions. Does not compile Unity or run its renderer.
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read = p => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const images = read('src/Automation/WhimTexApi.LiveImages.cs');
const jobs = read('src/Automation/WhimTexApi.Live.cs');
const complete = read('src/Automation/WhimTexApi.LiveCompletion.cs');
const model = read('src/TextureCompositor.cs');
function body(source, signature) {
  const start = source.indexOf('{', source.indexOf(signature));
  assert.ok(start > 0, signature);
  let depth = 1, end = start + 1;
  while (depth && end < source.length) { if (source[end] === '{') depth++; if (source[end] === '}') depth--; end++; }
  assert.equal(depth, 0);
  return source.slice(start + 1, end - 1);
}
const rgba = (r, g, b, a) => ({r, g, b, a});
const mul = (c, a) => rgba(c.r*a, c.g*a, c.b*a, c.a*a);
const lerp = (a, b, t) => a*(1-t)+b*t;
const Color = {clear: rgba(0,0,0,0), Lerp: (a,b,t) => rgba(...['r','g','b','a'].map(k=>lerp(a[k],b[k],t)))};
let code = body(images, 'internal static Color BlendLiveCoverage(')
  .replace(/\bfloat\b|\bColor(?= premultiplied)/g, 'let')
  .replaceAll('UnityEngine.Color', 'Color').replaceAll('Mathf.Lerp','lerp')
  .replaceAll('before * before.a', 'mul(before, before.a)').replaceAll('after * after.a', 'mul(after, after.a)')
  .replaceAll('new Color(', 'rgba(');
const blend = new Function('Color','lerp','mul','rgba',`return (before,after,coverage)=>{${code}}`)(Color,lerp,mul,rgba);
let checks = 0;
const close = (a,b) => { assert.ok(Math.abs(a-b)<1e-9, `${a} != ${b}`); checks++; };
// Zero coverage is a true no-op, even for hidden RGB/HDR. Full coverage replaces rather than overlays.
for (const a of [0,.1,.5,1]) for (const b of [0,.2,.7,1]) for (const r of [0,.5,1,8]) {
  const before=rgba(r,2*r,3*r,a), after=rgba(.3,.8,.2,b);
  assert.deepEqual(blend(before,after,0),before);
  assert.deepEqual(blend(before,after,1),after); checks+=2;
  for (const t of [.01,.25,.5,.8,.99]) {
    const actual=blend(before,after,t), alpha=lerp(a,b,t);
    close(actual.a,alpha);
    for (const k of ['r','g','b']) close(actual[k],alpha ? lerp(before[k]*a,after[k]*b,t)/alpha : 0);
  }
}
// Transparent red next to opaque blue must not produce red fringes on resampling.
assert.deepEqual(blend(rgba(1,0,0,0),rgba(0,0,1,1),.5),rgba(0,0,1,.5)); checks++;
const PendingLayerBehaviour = class {};
code=body(model,'private static int NextContentLayer(')
  .replaceAll('container.Count','container.length').replaceAll(' is PendingLayerBehaviour',' instanceof PendingLayerBehaviour');
const next=new Function('PendingLayerBehaviour',`return (container,index)=>{${code}}`)(PendingLayerBehaviour);
for (let count=0;count<100;count++) {
  const container=Array.from({length:count},(_,i)=>i%4===0?{Behaviour:new PendingLayerBehaviour()}:i%4===1?null:i%4===2?{Behaviour:null}:{Behaviour:{i}});
  for (let index=-1;index<count;index++) {
    let expected=index+1;
    while(expected<count && (container[expected]?.Behaviour==null || container[expected].Behaviour instanceof PendingLayerBehaviour)) expected++;
    assert.equal(next(container,index),expected); checks++;
  }
}
// Structural checks complement, but do not replace, the opt-in Editor smoke test.
assert.ok(jobs.includes('existing.request == canonical') && jobs.includes('existing.requestId == requestId'));
assert.ok(jobs.includes('job.completion == request.ToString(Formatting.None)'));
assert.ok(jobs.includes('Undo.undoRedoPerformed += RefreshLiveJobs'));
assert.ok(jobs.includes('job.document == document && job.layerId == pending.Id'));
assert.ok(jobs.includes('job.width == job.document.width && job.height == job.document.height'));
assert.ok(complete.includes('LiveLayerRevision(target) == job.targetRevision'));
assert.ok(complete.includes('generated.AdoptReservation(reservation)'));
assert.ok(complete.includes('candidate.AdoptReservation(reservation)'));
assert.ok(images.includes('TiledCanvasUtility.ToDocument(uv, target.transform'));
assert.ok(images.includes('(byte[])selection.Coverage.Clone()'));
assert.ok(images.indexOf('uint width = BigEndian') < images.indexOf('ImageConversion.LoadImage'));
assert.ok(images.includes('FileMode.CreateNew'));
for (const src of [jobs,images,complete]) {
  assert.ok(!src.includes('AssetDatabase.Refresh('));
  assert.ok(!src.includes('RequestScriptCompilation('));
  assert.ok(!src.includes('SaveWithOutput('));
}
// Code-fenced JSON examples are syntactically valid, and Noise uses the existing nested settings.
const docs=read('Documentation~/LiveAgentAPI.md');
for(const match of docs.matchAll(/```json\s+([\s\S]*?)```/g)) { JSON.parse(match[1]); checks++; }
assert.ok(!docs.includes('"frequency"'));
assert.ok(docs.includes('"settings":{"noise":{"scale":8,"seed":472}}'));
const skill=read('Skills~/whimtex-live/SKILL.md');
const frontmatter=skill.replaceAll('\r\n','\n').match(/^---\n([\s\S]*?)\n---/)[1];
const fields=Object.fromEntries(frontmatter.split('\n').map(line=>{
  const separator=line.indexOf(':'); assert.ok(separator>0); return [line.slice(0,separator),line.slice(separator+1).trim()];
}));
assert.deepEqual(Object.keys(fields).sort(),['description','name']);
assert.match(fields.name,/^[a-z0-9]+(?:-[a-z0-9]+)*$/);
assert.equal(fields.name,'whimtex-live');
assert.ok(fields.name.length<=64 && fields.description.length>0 && fields.description.length<=1024);
assert.ok(!/[<>]|\[TODO:/.test(fields.description));
assert.ok(!skill.includes('[TODO:'));
// Execute the production placement formula for different canvas/crop/source aspect ratios.
code = body(images, 'private static Rect LiveImagePlacement(')
  .replace(/\bfloat\b/g, 'let').replaceAll('.5f', '.5').replaceAll('Math.Min', 'Math.min');
const Rect = function(x,y,width,height) { Object.assign(this,{x,y,width,height}); };
const placement = new Function('Require','Rect',`return (region,width,height,fit)=>{${code}}`)
  ((valid,message)=>assert.ok(valid,message), Rect);
for (const width of [16,512,2024,4096]) for (const height of [32,512,4096])
for (const region of [new Rect(0,0,512,512),new Rect(9,73,240,115)]) {
  const stretch=placement(region,width,height,'stretch');
  assert.deepEqual(stretch,region); checks++;
  const contain=placement(region,width,height,'contain');
  close(contain.width/contain.height,width/height);
  close(contain.x+contain.width/2,region.x+region.width/2);
  close(contain.y+contain.height/2,region.y+region.height/2);
  assert.ok(contain.width<=region.width+1e-9 && contain.height<=region.height+1e-9); checks++;
}
assert.throws(()=>placement(new Rect(0,0,1,1),1,1,'unknown'));
const originalImage=body(images,'private static Texture2D CreateLiveDrawingImage(');
assert.ok(originalImage.includes('new Texture2D(image.width, image.height'));
assert.ok(originalImage.includes('image.GetPixels32()') && originalImage.includes('texture.SetPixels32(colors)'));
assert.ok(!originalImage.includes('SampleLiveImage(') && !originalImage.includes('Resize'));
assert.ok(complete.includes('(long)generated.StoredTexture.width * generated.StoredTexture.height'));
// Run selection validation/capture bounds and coverage policy from production, without graphics.
code=body(images,'private static void CaptureLiveInput(').split('string source =')[0]
  .replace(/\bstring\b|\bint\b|\bvar\b|\bCanvasSelection\b/g,'let')
  .replace('(byte[])selection.Coverage.Clone()', 'selection.Coverage.slice()')
  .replaceAll('Math.Min','Math.min').replaceAll('Math.Max','Math.max');
const requireValid=(valid,message)=>assert.ok(valid,message);
const Bounds=function(x,y,width,height) { Object.assign(this,{x,y,width,height,xMin:x,yMin:y,xMax:x+width,yMax:y+height}); };
const configure=new Function('Text','Int','Require','RectInt',`return (request,job,window)=>{${code}return job;}`)
  ((r,k,d)=>r[k]??d,(r,k,d,min,max)=>{const n=r[k]??d; assert.ok(Number.isInteger(n)&&n>=min&&n<=max);return n;},requireValid,Bounds);
const selected={AgentSelection:{Active:true,Bounds:new Bounds(200,300,50,60),Coverage:new Uint8Array(512*512)}};
const strictCapture=configure({area:'selection'},{width:512,height:512},selected);
const guideCapture=configure({area:'selection',selectionMode:'guide'},{width:512,height:512},selected);
assert.equal(strictCapture.selectionMode,'strict');
assert.deepEqual(strictCapture.region,new Bounds(168,268,114,124));
assert.deepEqual(guideCapture.region,new Bounds(72,172,306,316));
assert.notEqual(strictCapture.mask,selected.AgentSelection.Coverage);
const fullCapture=configure({area:'selection',selectionMode:'guide',padding:4096},{width:512,height:512},selected);
assert.deepEqual(fullCapture.region,new Bounds(0,0,512,512));
for(const request of [{area:'selection',selectionMode:'typo'},{area:'canvas',selectionMode:'guide'},
  {area:'canvas',padding:4},{area:'selection',padding:-1},{area:'selection',padding:4097}])
  { assert.throws(()=>configure(request,{width:512,height:512},selected)); checks++; }
assert.throws(()=>configure({area:'selection'},{width:512,height:512},{AgentSelection:{Active:false}}));
code=body(images,'private static int LiveSelectionCoverage(');
const coverageAt=new Function(`return (job,x,y)=>{${code}}`)();
for(const mode of ['strict','guide']) for(const mask of [null,Uint8Array.from([0,64,128,255])])
for(let y=-1;y<=2;y++) for(let x=-1;x<=2;x++) {
  const actual=coverageAt({width:2,height:2,selectionMode:mode,mask},x,y);
  const expected=x<0||y<0||x>1||y>1 ? 0 : mode==='guide'||mask===null ? 255 : mask[y*2+x];
  assert.equal(actual,expected); checks++;
}
// Exercise the real new-image alpha loop: strict clips/softens, guide preserves every source pixel.
code=originalImage.slice(originalImage.indexOf('var colors ='),originalImage.indexOf('texture.SetPixels32(colors)'))
  .replace(/\bvar\b|\bint\b/g,'let').replaceAll('.5f','.5').replaceAll('(byte)','')
  .replace('((colors[i].a * coverage + 127) / 255)','Math.floor((colors[i].a * coverage + 127) / 255)');
const applyMask=new Function('Mathf','LiveSelectionCoverage',`return (job,image,placement)=>{${code}return colors;}`)
  ({FloorToInt:Math.floor},coverageAt);
const sourcePixels=Array.from({length:16},(_,i)=>({r:100,g:20,b:200,a:i%2?128:255}));
const inputImage={width:4,height:4,GetPixels32:()=>sourcePixels.map(p=>({...p}))};
const softMask=new Uint8Array(64); softMask[3*8+3]=255; softMask[3*8+4]=128;
for(const selectionMode of ['strict','guide']) {
  const result=applyMask({width:8,height:8,selectionMode,mask:softMask},inputImage,new Rect(2,2,4,3));
  for(let y=0;y<4;y++) for(let x=0;x<4;x++) {
    const i=y*4+x, expected={...sourcePixels[i]};
    if(selectionMode==='strict') expected.a=Math.floor((expected.a*softMask[Math.floor(2+(y+.5)/4*3)*8+2+x]+127)/255);
    assert.deepEqual(result[i],expected); checks++;
  }
}
// Execute the production window-selection control flow with stand-in windows; no Unity side effects.
code=body(jobs,'private static TextureCompositorWindow ResolveLiveBeginWindow(')
  .replaceAll('var ', 'let ').replaceAll('windows.Length','windows.length')
  .replaceAll('long newest', 'let newest').replaceAll('bool tied', 'let tied')
  .replaceAll('foreach (let window in windows)', 'for (const window of windows)')
  .replaceAll('Resources.FindObjectsOfTypeAll<TextureCompositorWindow>()','windowsSource()')
  .replaceAll('.Where(','.filter(').replaceAll('.ToArray()','')
  .replaceAll('.FirstOrDefault(','.find(');
let windows=[], focused={focusedWindow:null};
const explicit={};
const resolve=new Function('Require','windowsSource','EditorWindow','LiveWindow',`return session=>{${code}}`)
  ((valid,message,error)=>{if(!valid)throw Error(error);},()=>windows,focused,session=>explicit);
assert.equal(resolve('explicit-session'),explicit); checks++;
assert.throws(()=>resolve(null),/session_required/); checks++;
const first={AgentDocument:{},AgentFocusOrder:0}, second={AgentDocument:{},AgentFocusOrder:0};
windows=[first,{AgentDocument:null}];
assert.equal(resolve(null),first); checks++;
windows=[first,second];
assert.throws(()=>resolve(null),/session_ambiguous/); checks++;
focused.focusedWindow=second;
assert.equal(resolve(null),second); checks++;
first.AgentFocusOrder=10; second.AgentFocusOrder=9;
assert.equal(resolve(null),second); checks++; // Actual focus wins over history.
focused.focusedWindow=null;
assert.equal(resolve(null),first); checks++;
second.AgentFocusOrder=11;
assert.equal(resolve(null),second); checks++;
assert.equal(resolve('explicit-session'),explicit); checks++;
first.AgentFocusOrder=12;
assert.equal(resolve(null),first); checks++; // Returning to a previous window updates recency.
windows=[second];
assert.equal(resolve(null),second); checks++; // Closed windows cannot win.
windows=[first,second]; second.AgentFocusOrder=12;
assert.throws(()=>resolve(null),/session_ambiguous/); checks++;
const windowApi=read('src/TextureCompositorWindow.Api.cs');
code=body(windowApi,'private void RecordAgentFocus(')
  .replaceAll('Resources.FindObjectsOfTypeAll<TextureCompositorWindow>()','windowsSource()')
  .replaceAll('foreach (var window in windowsSource())','for (const window of windowsSource())')
  .replaceAll('agentFocusSequence','state.sequence').replaceAll('Math.Max','Math.max')
  .replaceAll('agentFocusOrder =','this.agentFocusOrder =');
const counter={sequence:0};
const focusWindows=[{agentFocusOrder:40},{agentFocusOrder:75}];
// Production focus-counter body runs on stand-ins, including restoration after a script reload.
const onFocus=new Function('windowsSource','state',`return function(){${code}}`)(()=>focusWindows,counter);
onFocus.call(focusWindows[0]); assert.equal(focusWindows[0].agentFocusOrder,76); checks++;
onFocus.call(focusWindows[1]); assert.equal(focusWindows[1].agentFocusOrder,77); checks++;
counter.sequence=0;
onFocus.call(focusWindows[0]); assert.equal(focusWindows[0].agentFocusOrder,78); checks++;
const beginBody=body(jobs,'private static JObject BeginLiveJob(');
assert.ok(beginBody.indexOf('existing.request == canonical')<beginBody.indexOf('ResolveLiveBeginWindow('));
const forkBody=body(jobs,'private static JObject ForkLiveJob(');
assert.ok(!forkBody.includes('CaptureLiveInput(') && forkBody.includes('mask = source.mask'));
assert.ok(forkBody.includes('selectionMode = source.selectionMode'));
assert.ok(forkBody.includes('source.state == "pending"') && forkBody.includes('source.targetId == null'));
assert.ok(forkBody.includes('index + 1') && forkBody.includes('reservation.AssignNewId()'));
console.log(`Live agent checks passed: ${checks} scalar/control-flow/example checks plus API safety guards. Unity integration requires LiveAgentSmoke.cs.`);
