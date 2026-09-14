// After LayerPersistenceSetup and a real domain reload with the fixture behaviour removed.
const string Path = "Assets/WhimTexCompositionVerification-b4f21b35.asset";
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var type=typeof(DCFApixels.WhimTex.TextureCompositor);
var doc=AssetDatabase.LoadAssetAtPath<DCFApixels.WhimTex.TextureCompositor>(Path);
if(doc==null)throw new Exception("Missing verification asset");
int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
object Call(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,Hidden).Invoke(obj,args);
DCFApixels.WhimTex.Layer Find(string id)=>(DCFApixels.WhimTex.Layer)Call(doc,"FindLayer",id);
string leafId=SessionState.GetString("WhimTex.Verification.MissingId","");
string groupId=SessionState.GetString("WhimTex.Verification.MissingGroupId","");
string drawId=SessionState.GetString("WhimTex.Verification.DrawingId","");
Check(type.Assembly.GetType("DCFApixels.WhimTex.CompositionMissingTestBehaviour")==null,"The original type truly disappeared");
var leaf=Find(leafId); var group=Find(groupId);
Check(leaf!=null && leaf.Behaviour==null && leaf.layerName=="Missing leaf" && leaf.opacity==.35f && leaf.transform.rotation==37,"Missing leaf retains common fields");
Check(group!=null && group.Behaviour==null && group.children.Count==1 && group.layerName=="Missing group","Missing group retains descendants");
Check(UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(doc).Length==2,"Unity reports two actual missing references");
Check(Find(drawId).GetPreviewTexture(8).GetPixel(4,4).r>.99f,"Saved Drawing pixels survive reload");
Check(((DCFApixels.WhimTex.OutlineLayerBehaviour)doc.layers[0].Behaviour).TargetLayerId==drawId,"Effect target survives reload");
var fx=doc.layers[1].modifiers[0];
Check(fx is DCFApixels.WhimTex.ShaderFX && AssetDatabase.GetAssetPath(fx)==Path,"Embedded FX survives as a subasset");
var image=doc.Compose();
try { var actual=image.GetPixel(4,4);
    Check(actual.b>.99f && actual.g<.001f && Mathf.Abs(actual.a-.7f)<.001f,"Missing group is hidden; surviving blue group renders with its saved opacity"); }
finally {UnityEngine.Object.DestroyImmediate(image);}
var recovery=type.Assembly.GetType("DCFApixels.WhimTex.MissingLayerRecovery");
var statics=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
var record=recovery.GetMethod("FindRecord",statics).Invoke(null,new object[]{doc,typeof(DCFApixels.WhimTex.Layer).GetField("behaviourId",Hidden).GetValue(leaf)});
Check(record!=null,"Native recovery record matches stable behaviour ID");
var replacement=new DCFApixels.WhimTex.NoiseLayerBehaviour();
recovery.GetMethod("Copy",statics).Invoke(null,new object[]{record,replacement,doc});
Check(replacement.seed==613,"Compatible parameter transfers from native missing-type data");
Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(doc,"Verify missing behaviour recovery");
leaf.SetBehaviour(replacement);group.SetBehaviour(new DCFApixels.WhimTex.GroupLayerBehaviour());
Call(doc,"MarkChanged");Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
Check(ReferenceEquals(Find(leafId),leaf)&&ReferenceEquals(Find(groupId),group),"Recovery does not replace wrappers");
Undo.PerformUndo();
Check(Find(leafId).Behaviour==null && Find(groupId).Behaviour==null && Find(groupId).children.Count==1,"Recovery Undo restores missing state and children");
Undo.PerformRedo();
Check(Find(leafId).Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour noise && noise.seed==613 && Find(groupId).Behaviour is DCFApixels.WhimTex.GroupLayerBehaviour,"Recovery Redo restores behaviours");
EditorUtility.SetDirty(doc);AssetDatabase.SaveAssetIfDirty(doc);
var beforeReopen=doc.Compose();Color savedColor;
try {savedColor=beforeReopen.GetPixel(4,4);} finally {UnityEngine.Object.DestroyImmediate(beforeReopen);}
foreach(var obj in AssetDatabase.LoadAllAssetsAtPath(Path)) Undo.ClearUndo(obj);
Resources.UnloadAsset(doc);
doc=AssetDatabase.LoadAssetAtPath<DCFApixels.WhimTex.TextureCompositor>(Path);
Check(Find(leafId).Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour reopened && reopened.seed==613 && Find(leafId).opacity==.35f,"Recovered document saves and reopens");
var afterReopen=doc.Compose();
try {Check(((Vector4)(afterReopen.GetPixel(4,4)-savedColor)).sqrMagnitude<.000001f,"Composition remains identical across save and reopen");}
finally {UnityEngine.Object.DestroyImmediate(afterReopen);}
var window=Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>()
    .Single(w=>w.titleContent.text=="WhimTex verification b4f21b35");
var unsaved=(DCFApixels.WhimTex.TextureCompositor)window.GetType().GetField("compositor",Hidden).GetValue(window);
Check(!AssetDatabase.Contains(unsaved) && unsaved.layers[0].Id==SessionState.GetString("WhimTex.Verification.UnsavedId", ""),"Unsaved document retains identity across actual domain reload");
Check(unsaved.layers[0].GetPreviewTexture(8).GetPixel(4,4).r>.99f && unsaved.layers[0].GetPreviewTexture(8).GetPixel(4,4).a>.99f,"Unsaved Drawing pixels survive actual domain reload");
return new { success=true,checks };
