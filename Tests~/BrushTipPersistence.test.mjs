// Source-translated persistence methods with a mock asset database; no Unity compilation.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const source = read('src/PaintToolSettings.cs');
function body(name) {
  const declaration = source.search(new RegExp('(?:internal|private) (?:void|bool) ' + name + '\\('));
  assert.ok(declaration >= 0, name);
  const start = source.indexOf('{', declaration);
  let end = start + 1, depth = 1;
  while (depth) { if (source[end] === '{') depth++; if (source[end] === '}') depth--; end++; }
  return source.slice(start + 1, end - 1);
}
function compile(name) {
  const code = body(name)
    .replace(/out string guid, out long localId/g, '')
    .replace(/, \)/g, ')')
    .replace(/string.IsNullOrEmpty\((\w+)\)/g, '(!$1)')
    .replace(/string.Empty/g, '""')
    .replace(/\b(string|Texture2D) (\w+)\s*=/g, 'let $2 =')
    .replace(/LoadAssetAtPath<Texture2D>/g, 'LoadAssetAtPath')
    .replace('foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))', 'for (const asset of AssetDatabase.LoadAllAssetsAtPath(path))')
    .replace('asset is Texture2D candidate', '((candidate = asset).kind === "Texture2D")')
    .replace(/catch\(Exception (\w+)\)/g, 'catch($1)')
    .replace(/(\w+)\.Message/g, '$1.message');
  return new Function('settings','database','EditorApplication','texture', `
    let guid, localId, candidate;
    const AssetDatabase = { ...database, TryGetGUIDAndLocalFileIdentifier(t) {
      const identity = database.identify(t);
      if (!identity) return false;
      guid = identity.guid; localId = identity.id; return true;
    }};
    with (settings) { ${code} }
  `);
}
const remember=compile('RememberBrushTip'), restore=compile('TryRestoreBrushTip'), assign=compile('SetBrushTip');
const main={kind:'Texture2D',guid:'asset-guid',id:10}, child={kind:'Texture2D',guid:'asset-guid',id:20};
let available=false;
const db={
  identify:t=>t?.guid?{guid:t.guid,id:t.id}:null,
  GUIDToAssetPath:guid=>available&&guid==='asset-guid'?'Assets/Tip.asset':'',
  LoadAssetAtPath:()=>main,
  LoadAllAssetsAtPath:()=>[main,child]
};
const editor={isCompiling:false,isUpdating:false};
const settings={BrushTipSource:{HLSL:'HLSL'},dynamics:{tip:null,source:'Standard'},clipboardTipId:'',tipRestoreFailed:false,brushTipGuid:'asset-guid',brushTipLocalId:20,brushTipPresetPath:'',ownedPresetTip:null,
  ReleasePresetTip() { this.ownedPresetTip=null; }};
settings.RememberBrushTip=()=>remember(settings,db,editor);
settings.MatchesBrushTip=t=>t.guid===settings.brushTipGuid&&t.id===settings.brushTipLocalId;
assert.equal(restore(settings,db,editor),false,'Unavailable database: defer restoration');
remember(settings,db,editor);
assert.equal(settings.brushTipGuid,'asset-guid','Saving other settings must not erase an unresolved tip');
assert.equal(settings.brushTipLocalId,20);
available=true;
editor.isUpdating=true;
assert.equal(restore(settings,db,editor),false,'Do not load while import is running');
editor.isUpdating=false;
assert.equal(restore(settings,db,editor),true);
assert.equal(settings.dynamics.tip,child,'Restore exact subasset, not the first texture');
settings.dynamics.tip=null;
editor.isCompiling=true;
assert.equal(restore(settings,db,editor),false);
editor.isCompiling=false;
assert.equal(restore(settings,db,editor),true,'Retry after compilation');
assign(settings,db,editor,null);
assert.equal(settings.brushTipGuid,'','Explicit clear removes persistence');
assert.equal(settings.brushTipLocalId,0);
assert.equal(restore(settings,db,editor),false,'Cleared tip does not resurrect');
assign(settings,db,editor,main);
assert.equal(settings.brushTipGuid,'asset-guid');
assert.equal(settings.brushTipLocalId,10);
settings.dynamics.tip=null;
assert.equal(restore(settings,db,editor),true);
assert.equal(settings.dynamics.tip,main);
settings.dynamics.tip=null; settings.brushTipLocalId=0;
assert.equal(restore(settings,db,editor),true,'Legacy GUID-only settings still load');
settings.dynamics.tip=null; settings.brushTipLocalId=999;
assert.equal(restore(settings,db,editor),false,'Do not substitute a different subasset');
remember(settings,db,editor);
assert.equal(settings.brushTipLocalId,999,'Keep missing identity for later recovery');
settings.brushTipPresetPath='/library/Brushes/Test.sebrush';
settings.RestorePresetTip=()=>{ settings.dynamics.tip=child; return true; };
assert.equal(restore(settings,db,editor),true,'Portable preset tip restores before project GUID lookup');
assert.equal(settings.dynamics.tip,child);
assign(settings,db,editor,null);
assert.equal(settings.brushTipPresetPath,'','Explicit clear forgets the preset tip');
const window=read('src/TextureCompositorWindow.cs'), tools=read('src/TextureCompositorWindow.Tools.cs');
for(const event of ['delayCall','projectChanged']) {
  assert.ok(window.includes(`EditorApplication.${event} += RestoreBrushTipAfterReload`));
  assert.ok(window.includes(`EditorApplication.${event} -= RestoreBrushTipAfterReload`));
}
assert.ok(tools.includes('paintSettings.RememberBrushTip();'));
assert.ok(!tools.includes('paintSettings.dynamics.tip == null ? string.Empty'));
assert.ok(read('src/TextureCompositorWindow.Brushes.cs').includes('paintSettings.SetBrushTip(texture)'));
console.log('Brush tip persistence checks passed (mock asset database; Unity not executed).');
