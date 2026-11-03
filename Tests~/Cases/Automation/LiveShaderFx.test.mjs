// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import {readFileSync} from 'node:fs';
const context = new TestContext("LiveShaderFx source/reference tests");
const assert = context.assert;
context.case("LiveShaderFx original assertion inputs and source contracts", async () => {
  // Executes extracted API policy code with stand-ins; does not compile Unity or HLSL.
  const read=p=>readFileSync(new URL('../../../'+p,import.meta.url),'utf8');
  const source=read('src/Automation/WhimTexApi.LiveFx.cs');
  function body(text,signature) {
    const signatureStart=text.indexOf(signature); assert.ok(signatureStart>=0, `Missing method: ${signature}`);
    const start=text.indexOf('{',signatureStart); assert.ok(start>=0);
    let depth=1,end=start+1;
    while(depth && end<text.length) {if(text[end]==='{')depth++;if(text[end]==='}')depth--;end++;}
    assert.equal(depth,0,`Unclosed method: ${signature}`);
    return text.slice(start+1,end-1);
  }
  const jobs=[];
  const lockCode=body(source,'internal static bool IsLayerContentLocked(')
    .replace('foreach (var job in liveJobs.Values)','for (const job of liveJobs)');
  const locked=new Function('liveJobs',`return (document,layer)=>{${lockCode}}`)(jobs);
  const doc={},other={},layer={Id:'layer'};
  assert.equal(locked(doc,layer),false);
  for(const editing of [false,true]) for(const state of ['pending','completed','cancelled'])
  for(const document of [doc,other]) for(const layerId of ['layer','other']) {
    jobs.splice(0,jobs.length,{editing,state,document,layerId});
    assert.equal(locked(doc,layer),editing&&state==='pending'&&document===doc&&layerId==='layer');
    assert.equal(locked(doc,null),false);
  }
  const notices=[];
  const cancelCode=body(source,'private static void CancelLiveEditLocks(')
    .replace('foreach (var job in liveJobs.Values)','for (const job of liveJobs)');
  jobs.splice(0,jobs.length,{editing:true,state:'pending',document:doc},{editing:false,state:'pending',document:doc});
  const cancelAll=new Function('liveJobs','NotifyLiveLockChanged',`return ()=>{${cancelCode}}`)(jobs,d=>notices.push(d));
  cancelAll(); cancelAll();
  assert.equal(jobs[0].state,'cancelled'); assert.equal(jobs[1].state,'pending');
  class List extends Array {
    static get [Symbol.species](){return Array;}
    constructor(items=[]) { super(); this.push(...items); }
    get Count(){return this.length;} Add(v){this.push(v);} Insert(i,v){this.splice(i,0,v);} RemoveAt(i){this.splice(i,1);}
  }
  const Require=(ok,message)=>{if(!ok)throw Error(message);};
  const Text=(o,k,f)=>o[k]??f;
  const Int=(o,k,f,min,max)=>{const v=o[k]??f;Require(Number.isInteger(v)&&v>=min&&v<=max,'index');return v;};
  const Keys=(o,...names)=>{for(const k of Object.keys(o))Require(names.includes(k),'Unknown field');};
  let code=body(source,'private static void ApplyLiveFx(')
    .replace('token is JArray array && array.Count <= 16','Array.isArray(token) && token.length <= 16')
    .replaceAll('new List<ShaderFX>', 'new List')
    .replace('foreach (var item in (JArray)token)','for (const item of token)')
    .replace('foreach (var parameter in fx.TextureLayerParameters())','for (const parameter of fx.TextureLayerParameters())')
    .replace(/\bJObject spec\b|\bstring op\b|\bint index\b|\bvar parameters\b|\b(?:var|ShaderFX) fx\b/g,m=>'let '+m.split(' ')[1])
    .replaceAll('spec["draftCode"]?.Type == JTokenType.String','typeof spec["draftCode"] === "string"')
    .replaceAll('((string)spec["draftCode"]).Length','spec["draftCode"].length')
    .replaceAll('(string)spec["draftCode"]','spec["draftCode"]')
    .replace(', false, out _)', ')')
    .replace('effectContext: true','true')
    .replace('catch (Exception error)','catch (error)').replaceAll('error.Message','error.message');
  const create=(owner,code,parameters)=>({owner,code,parameters,Parameters:new List(parameters),TextureLayerParameters(){return [];},NotifyValuesChanged(){},CompileAgentDraft(){if(code==='INVALID')throw Error('bad shader');}});
  const parameterLimit=read('src/Automation/WhimTexApi.cs').match(/const int MaxFxParameters = (\d+);/);
  assert.ok(parameterLimit, 'The FX parameter resource limit must exist');
  const MaxFxParameters=Number(parameterLimit[1]);
  assert.equal(MaxFxParameters,128, 'Public live-authoring parameter budget');
  assert.ok(!source.includes('ReadLiveFxParameters'));
  const metadata={Parse:code=>Array.from({length:code==='LIMIT'?MaxFxParameters:code==='OVER'?MaxFxParameters+1:0},()=>({}))};
  const createProgram=(owner,spec)=>{
    Require(typeof spec.draftCode==='string' && spec.draftCode.length>0 && spec.draftCode.length<=65536,'draftCode');
    const fx=create(owner,spec.draftCode,metadata.Parse(spec.draftCode)); fx.CompileAgentDraft(); return fx;
  };
  const setValues=(fx,values)=>Require(values==null || !Array.isArray(values),'parameters must be an object');
  const mutate=new Function('Require','Text','Int','Keys','Obj','List','RequireGraphics','MaxFxParameters','CreateFxProgramDraft','SetFxValues','RandomizeFxSeeds','WhimTexDocumentJson',
    `return (layer,token,owner,created)=>{${code}}`)(Require,Text,Int,Keys,v=>v,List,()=>{},MaxFxParameters,createProgram,setValues,()=>{},{ValidateShaderProgramInputs(){}});
  const a={},b={}; const make=()=>({fx:new List([a,b]),IsGroup:false});
  let target=make(), created=new List();
  mutate(target,[{draftCode:'A'},{op:'replace',index:0,draftCode:'B'},{op:'remove',index:1}],doc,created);
  assert.deepEqual(Array.from(target.fx,v=>v.code),['B','A']);
  assert.equal(created.length,2); assert.equal(created[0].owner,doc);
  target=make(); mutate(target,[{draftCode:'C',index:1}],doc,new List());
  assert.equal(target.fx[0],a); assert.equal(target.fx[2],b); assert.equal(target.fx[1].code,'C');
  for(const operations of [
    [{op:'replace',draftCode:'A'}],[{op:'remove',index:7}],[{op:'remove',index:0,draftCode:'A'}],
    [{op:'unknown'}],[{draftCode:''}],[{draftCode:'X'.repeat(65537)}],[{draftCode:'A',typo:1}],Array.from({length:17},()=>({draftCode:'A'})),
    [{draftCode:'A',parameters:[]}],[{draftCode:'OVER'}]
  ]) assert.throws(()=>mutate(make(),operations,doc,new List()));
  target=make();
  mutate(target,[{draftCode:'LIMIT'}],doc,new List());
  assert.equal(target.fx.at(-1).Parameters.Count,MaxFxParameters,'The inclusive boundary remains accepted');
  target=make(); assert.throws(()=>mutate(target,[{op:'replace',index:0,draftCode:'INVALID'}],doc,new List()));
  assert.equal(target.fx[0],a); // Failed compilation cannot publish the failing effect.
  assert.throws(()=>mutate({fx:new List(),IsGroup:false},[{op:'remove',index:0}],doc,new List()));
  const groupTarget={fx:new List(),IsGroup:true};
  mutate(groupTarget,[{draftCode:'A'}],doc,new List());
  assert.equal(groupTarget.fx.length,1);
  const fxSource=read('src/ShaderFX.cs');
  const compile=fxSource.match(/internal void CompileAgentDraft\(\) => ([^;]+);/)[1];
  for(const forbidden of ['Undo.','AssetDatabase.AddObjectToAsset','SaveAsset','PersistEmbedded','NotifyValuesChanged','SetDirty'])
    assert.ok(!compile.includes(forbidden),`Trial compilation must not mutate live state: ${forbidden}`);
  assert.equal(compile,'CompileDraftProgram()');
  const compileCore=body(fxSource,'private void BuildProgram(');
  assert.ok(compileCore.indexOf('ShaderProgramRuntime.CompileDraft(')<compileCore.indexOf('runtime = candidate'));
  assert.ok(!compileCore.includes('UpdateShaderAsset'), 'Candidates replace owned runtime only after successful compilation');
  const edit=body(source,'private static JObject ApplyLiveEdit(');
  assert.ok(edit.indexOf('ApplyLiveFx(candidate')<edit.indexOf('LiveChange(job.document'));
  assert.ok(edit.includes('LiveLayerRevision(target) == job.targetRevision'));
  assert.ok(edit.includes('if (!committed)'));
  const docs=read('Documentation~/LiveAgentAPI.md');
  for(const match of docs.matchAll(/```json\s+([\s\S]*?)```/g)) {
    const request = JSON.parse(match[1]);
    for (const operation of request.layer?.fx ?? request.changes?.fx ?? request.fx ?? []) {
      Keys(operation, 'op', 'index', 'draftCode');
      if (operation.draftCode?.includes('// @param')) {
        assert.ok(operation.draftCode.includes('\nfloat4 ApplyFX'), 'A metadata comment must end before the HLSL entry point');
        assert.ok(!operation.draftCode.includes('\\n'), 'JSON examples must decode real newlines, not literal backslash-n');
      }
    }
  }
  assert.ok(docs.includes('`parameters` accepts a name/value object'));
  const skill = read('Skills~/whimtex-live/SKILL.md');
  assert.ok(!skill.includes("reservation workflow's array"));

});
await finish(context);
