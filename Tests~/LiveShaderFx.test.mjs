// Executes extracted API policy code with stand-ins; does not compile Unity or HLSL.
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const source=read('src/Automation/WhimTexApi.LiveFx.cs');
function body(text,signature) {
  const start=text.indexOf('{',text.indexOf(signature)); assert.ok(start>=0);
  let depth=1,end=start+1;
  while(depth) {if(text[end]==='{')depth++;if(text[end]==='}')depth--;end++;}
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
  .replaceAll('new List<Object>', 'new List')
  .replace('foreach (var item in (JArray)token)','for (const item of token)')
  .replace('foreach (var parameter in fx.TextureLayerParameters())','for (const parameter of fx.TextureLayerParameters())')
  .replace(/\bJObject spec\b|\bstring op\b|\bint index\b|\bvar parameters\b|\bvar fx\b/g,m=>'let '+m.split(' ')[1])
  .replaceAll('spec["code"]?.Type == JTokenType.String','typeof spec["code"] === "string"')
  .replaceAll('((string)spec["code"]).Length','spec["code"].length')
  .replaceAll('(string)spec["code"]','spec["code"]')
  .replace('catch (Exception error)','catch (error)').replaceAll('error.Message','error.message');
const create=(owner,code,parameters)=>({owner,code,parameters,Parameters:new List(parameters),TextureLayerParameters(){return [];},ApplyAgentDraft(){if(code==='INVALID')throw Error('bad shader');}});
const mutate=new Function('Require','Text','Int','Keys','Obj','List','ReadLiveFxParameters','RequireGraphics','ShaderFX','WhimTexApiException',
  `return (layer,token,owner,created)=>{${code}}`)(Require,Text,Int,Keys,v=>v,List,v=>v??[],()=>{}, {CreateAgentDraft:create},class extends Error{});
const a={},b={}; const make=()=>({modifiers:new List([a,b]),IsGroup:false});
let target=make(), created=new List();
mutate(target,[{code:'A'},{op:'replace',index:0,code:'B'},{op:'remove',index:1}],doc,created);
assert.deepEqual(Array.from(target.modifiers,v=>v.code),['B','A']);
assert.equal(created.length,2); assert.equal(created[0].owner,doc);
target=make(); mutate(target,[{code:'C',index:1}],doc,new List());
assert.equal(target.modifiers[0],a); assert.equal(target.modifiers[2],b); assert.equal(target.modifiers[1].code,'C');
for(const operations of [
  [{op:'replace',code:'A'}],[{op:'remove',index:7}],[{op:'remove',index:0,code:'A'}],
  [{op:'unknown'}],[{code:''}],[{code:'X'.repeat(65537)}],[{code:'A',typo:1}],Array.from({length:17},()=>({code:'A'})),
  [{code:'A',parameters:Array.from({length:33},()=>({}))}]
]) assert.throws(()=>mutate(make(),operations,doc,new List()));
target=make(); assert.throws(()=>mutate(target,[{op:'replace',index:0,code:'INVALID'}],doc,new List()));
assert.equal(target.modifiers[0],a); // Failed compilation cannot publish the failing effect.
assert.throws(()=>mutate({modifiers:new List(),IsGroup:false},[{op:'remove',index:0}],doc,new List()));
const groupTarget={modifiers:new List(),IsGroup:true};
mutate(groupTarget,[{code:'A'}],doc,new List());
assert.equal(groupTarget.modifiers.length,1);
const compile=body(read('src/ShaderFX.cs'),'internal void ApplyAgentDraft(');
for(const forbidden of ['Undo.','AssetDatabase.AddObjectToAsset','SaveAsset','PersistEmbedded','NotifyValuesChanged','SetDirty'])
  assert.ok(!compile.includes(forbidden),`Trial compilation must not mutate live state: ${forbidden}`);
assert.ok(compile.indexOf('if (errors ||')<compile.indexOf('compiledShader = candidate'));
assert.ok(compile.includes('ShaderFXSourceBuilder.Build(this, SourcePath)'));
const edit=body(source,'private static JObject ApplyLiveEdit(');
assert.ok(edit.indexOf('ApplyLiveFx(candidate')<edit.indexOf('LiveChange(job.document'));
assert.ok(edit.includes('LiveLayerRevision(target) == job.targetRevision'));
assert.ok(edit.includes('if (!committed)'));
const docs=read('Documentation~/LiveAgentAPI.md');
for(const match of docs.matchAll(/```json\s+([\s\S]*?)```/g)) JSON.parse(match[1]);
console.log('Live Shader FX: extracted lock lifecycle, FX operations, validation and transaction guards passed; Unity/GPU not executed.');
