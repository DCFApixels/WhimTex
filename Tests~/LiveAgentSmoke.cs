// Opt-in C# eval smoke test AFTER manual compilation. Requires graphics.
// Creates only a separate unsaved window and uniquely named Temp/WhimTex PNGs.
// Never opens, saves or edits an existing document. Do not run during a paint gesture.
var api = typeof(DCFApixels.WhimTex.WhimTexApi);
var instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var jsonType = api.GetMethod("SetNoise", statics).GetParameters()[1].ParameterType;
object Json(string text) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
object At(object value, params object[] keys)
{
    foreach (var key in keys) value = value.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(value, new[] { key });
    return value;
}
string Text(object value, params object[] keys) => At(value, keys).ToString();
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
object Call(string fields, string error = null)
{
    var result = Json(DCFApixels.WhimTex.WhimTexApi.LiveJson("{\"apiVersion\":1," + fields + "}"));
    bool success = bool.Parse(Text(result, "success"));
    Check(error == null ? success : !success && Text(result, "errorCode") == error, result.ToString());
    return result;
}
var windowType = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var window = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
var document = (DCFApixels.WhimTex.TextureCompositor)windowType.GetField("compositor", instance).GetValue(window);
document.width = 8; document.height = 8;
var mark = typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("MarkChanged", instance);
void Mark() => mark.Invoke(document, null);
DCFApixels.WhimTex.Layer Find(string id) => (DCFApixels.WhimTex.Layer)typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("FindLayer", instance).Invoke(document, new object[] { id });
void Edit(System.Action action)
{
    UnityEditor.Undo.IncrementCurrentGroup();
    UnityEditor.Undo.RegisterCompleteObjectUndo(document, "Live Agent Smoke User Edit");
    action(); Mark(); UnityEditor.Undo.FlushUndoRecordObjects(); UnityEditor.Undo.IncrementCurrentGroup();
}
string session = (string)windowType.GetProperty("AgentSessionId", instance).GetValue(window);
string scope = "\"sessionId\":\"" + session + "\"";
string jobFields(object job) => "\"jobId\":\"" + Text(job, "jobId") + "\"";
object Begin(string extra = "") => Call("\"op\":\"begin\"," + scope + ",\"requestId\":\"" + System.Guid.NewGuid().ToString("N") + "\",\"name\":\"Generated\"" + extra);
string png = null;
try
{
    var inspection = Call("\"op\":\"inspect\"," + scope);
    Check(Text(inspection, "document", "assetPath") == "", "Unsaved document is accessible");
    string quickId=System.Guid.NewGuid().ToString("N");
    var quick=Json(DCFApixels.WhimTex.WhimTexApi.LiveBegin(quickId,"Quick",sessionId:session));
    Check(bool.Parse(Text(quick,"success")) && Text(quick,"capture","source")=="none","Direct fast begin reserves without an image capture");
    var quickRetry=Json(DCFApixels.WhimTex.WhimTexApi.LiveBegin(quickId,"Quick",sessionId:session));
    Check(Text(quick,"jobId")==Text(quickRetry,"jobId"),"Direct fast begin is idempotent");
    Check(Text(quick,"context","selectionActive").ToLowerInvariant()=="false","Fast begin returns frozen compact context");
    Call("\"op\":\"cancel\","+scope+",\"layerId\":\""+Text(quick,"layerId")+"\"");
    var baseLayer = new DCFApixels.WhimTex.ColorFillLayerBehaviour { layerName = "Unrelated", color = UnityEngine.Color.blue };
    Edit(() => document.layers.Add(baseLayer));
    var requestId = System.Guid.NewGuid().ToString("N");
    string begin = "\"op\":\"begin\"," + scope + ",\"requestId\":\"" + requestId + "\",\"name\":\"Fog\"";
    var job = Call(begin);
    var retry = Call(begin);
    Check(Text(job,"jobId") == Text(retry,"jobId") && document.layers.Count == 2, "Begin retry is idempotent");
    Call(begin.Replace("Fog", "Other"), "request_conflict");
    var pending = Find(Text(job,"layerId"));
    Check(pending?.Behaviour is DCFApixels.WhimTex.PendingLayerBehaviour, "Placeholder type");
    Edit(() => { pending.layerName = "User name"; pending.enabled = false; document.layers.Remove(pending); document.layers.Add(pending); baseLayer.color = UnityEngine.Color.green; });
    string spec = "\"layer\":{\"type\":\"noise\",\"settings\":{\"noise\":{\"scale\":6,\"seed\":472}}}";
    string before = UnityEditor.EditorJsonUtility.ToJson(document);
    int undoBefore = UnityEditor.Undo.GetCurrentGroup();
    var trial = Call("\"op\":\"preview\"," + jobFields(job) + "," + spec + ",\"maxSize\":8,\"view\":\"composite\"");
    Check(System.IO.File.Exists(Text(trial,"outputPath")), "Trial PNG exists");
    Call("\"op\":\"preview\"," + jobFields(job) + "," + spec + ",\"maxSize\":8,\"view\":\"layer\"");
    Check(before == UnityEditor.EditorJsonUtility.ToJson(document) && undoBefore == UnityEditor.Undo.GetCurrentGroup(), "Trial does not modify document or Undo");
    string completion = "\"op\":\"complete\"," + jobFields(job) + "," + spec;
    Call(completion); Call(completion);
    var result = Find(Text(job,"layerId"));
    Check(ReferenceEquals(result, pending), "Completion retains the reserved wrapper instance");
    Check(result?.Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour n && n.seed == 472 && n.scale == 6, "Configured layer completed");
    Check(result.layerName == "User name" && !result.enabled && document.layers[1] == result, "Name, visibility and placement survive");
    Check(baseLayer.color == UnityEngine.Color.green, "Independent changes survive");
    UnityEditor.Undo.PerformUndo();
    Check(Find(Text(job,"layerId"))?.Behaviour is DCFApixels.WhimTex.PendingLayerBehaviour, "Completion Undo restores reservation");
    Call(completion);
    Check(Find(Text(job,"layerId"))?.Behaviour is DCFApixels.WhimTex.PendingLayerBehaviour, "Retry does not defeat Undo");
    UnityEditor.Undo.PerformRedo();
    Check(Find(Text(job,"layerId"))?.Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour, "Completion Redo restores result");

    var cancelled = Begin();
    Call("\"op\":\"cancel\"," + scope + ",\"layerId\":\"" + Text(cancelled,"layerId") + "\"");
    UnityEditor.Undo.PerformUndo();
    Check(Find(Text(cancelled,"layerId"))?.Behaviour is DCFApixels.WhimTex.PendingLayerBehaviour, "Cancelled row can be restored with Undo");
    Call("\"op\":\"complete\"," + jobFields(cancelled) + "," + spec, "job_closed");
    Check(Text(Call("\"op\":\"status\"," + jobFields(cancelled)),"state") == "cancelled", "Cancellation is terminal");

    var deleted = Begin();
    Edit(() => document.layers.Remove(Find(Text(deleted,"layerId"))));
    UnityEditor.Undo.PerformUndo();
    Call("\"op\":\"complete\"," + jobFields(deleted) + "," + spec, "job_closed");

    // Inline HLSL is compiled by these opt-in calls, never as a project compilation.
    var shaderJob=Begin();
    string shaderSpec="\"layer\":{\"type\":\"shaderProcessor\",\"fx\":[{\"code\":\"float4 ApplyFX(float2 uv, float4 color) { color.rgb *= _Gain; return color; }\",\"parameters\":[{\"name\":\"_Gain\",\"type\":\"Float\",\"value\":1.25}]}]}";
    string beforeShader=UnityEditor.EditorJsonUtility.ToJson(document);
    int shaderUndo=UnityEditor.Undo.GetCurrentGroup();
    Call("\"op\":\"preview\","+jobFields(shaderJob)+","+shaderSpec+",\"maxSize\":8");
    Check(beforeShader==UnityEditor.EditorJsonUtility.ToJson(document) && shaderUndo==UnityEditor.Undo.GetCurrentGroup(),"Shader preview leaves document and Undo unchanged");
    Call("\"op\":\"complete\","+jobFields(shaderJob)+","+shaderSpec);
    var shaderLayer=Find(Text(shaderJob,"layerId"));
    var firstFx=shaderLayer.modifiers[0];
    Check(firstFx is DCFApixels.WhimTex.ShaderFX && !UnityEditor.AssetDatabase.Contains(firstFx),"Inline FX is owned in memory, without a separate asset");
    var shaderType=typeof(DCFApixels.WhimTex.ShaderFX);
    Check((bool)shaderType.GetProperty("HasAppliedShader",instance).GetValue(firstFx),"Inline shader compiled");
    Check(ReferenceEquals(shaderType.GetProperty("EmbeddedOwner",instance).GetValue(firstFx),document),"FX belongs to its document");
    string editRequest="\"op\":\"lock\","+scope+",\"layerId\":\""+shaderLayer.Id+"\",\"requestId\":\""+System.Guid.NewGuid().ToString("N")+"\"";
    var editJob=Call(editRequest);
    Check(Text(Call(editRequest),"jobId")==Text(editJob,"jobId"),"Lock retry reuses its job");
    Call(editRequest.Replace("\"requestId\":\"","\"requestId\":\"other-"),"layer_locked");
    Check(Text(editJob,"context","layer","fx",0,"code").Contains("_Gain"),"Lock captures existing inline code");
    Edit(()=>{shaderLayer.layerName="User FX name";shaderLayer.enabled=false;});
    string badFx="\"changes\":{\"fx\":[{\"op\":\"replace\",\"index\":0,\"code\":\"float4 ApplyFX(float2 uv, float4 color) { return invalid_identifier; }\"}]}";
    Call("\"op\":\"preview\","+jobFields(editJob)+","+badFx+",\"maxSize\":8","shader_compile_failed");
    Check(shaderLayer.modifiers[0]==firstFx && Text(Call("\"op\":\"status\","+jobFields(editJob)),"state")=="pending","Invalid shader preserves working FX and permits correction");
    string edits="\"changes\":{\"settings\":{\"opacity\":0.5},\"fx\":[{\"op\":\"replace\",\"index\":0,\"code\":\"float4 ApplyFX(float2 uv, float4 color) { return float4(color.bgr, color.a); }\"}]}";
    string beforeEdit=UnityEditor.EditorJsonUtility.ToJson(document);
    int editUndo=UnityEditor.Undo.GetCurrentGroup();
    Call("\"op\":\"preview\","+jobFields(editJob)+","+edits+",\"view\":\"layer\",\"maxSize\":8");
    Check(beforeEdit==UnityEditor.EditorJsonUtility.ToJson(document) && editUndo==UnityEditor.Undo.GetCurrentGroup() && shaderLayer.modifiers[0]==firstFx,"Edit trial is detached");
    string editComplete="\"op\":\"complete\","+jobFields(editJob)+","+edits;
    Call(editComplete); Call(editComplete);
    Check(shaderLayer.modifiers.Count==1 && shaderLayer.modifiers[0]!=firstFx && shaderLayer.opacity==.5f,"FX replaced exactly once");
    Check(shaderLayer.layerName=="User FX name" && !shaderLayer.enabled,"FX edit preserves user name and visibility");
    Check(!bool.Parse(Text(Call("\"op\":\"status\","+jobFields(editJob)),"contentLocked")),"Successful edit releases lock");
    UnityEditor.Undo.PerformUndo();
    shaderLayer=Find(Text(shaderJob,"layerId"));
    Check(shaderLayer.modifiers.Count==1 && (bool)shaderType.GetProperty("HasAppliedShader",instance).GetValue(shaderLayer.modifiers[0]),"Undo restores a compiled FX");
    UnityEditor.Undo.PerformRedo();
    shaderLayer=Find(Text(shaderJob,"layerId"));
    Check(shaderLayer.opacity==.5f && (bool)shaderType.GetProperty("HasAppliedShader",instance).GetValue(shaderLayer.modifiers[0]),"Redo restores the inline result");
    var releaseJob=Json(DCFApixels.WhimTex.WhimTexApi.LiveLock(System.Guid.NewGuid().ToString("N"),shaderLayer.Id,session));
    Check(bool.Parse(Text(releaseJob,"success")),"Direct lock shortcut succeeds");
    Call("\"op\":\"unlock\","+jobFields(releaseJob)); Call("\"op\":\"unlock\","+jobFields(releaseJob));
    Call("\"op\":\"complete\","+jobFields(releaseJob)+","+edits,"job_closed");

    var texture = new UnityEngine.Texture2D(4,4,UnityEngine.TextureFormat.RGBA32,false);
    var colors = new UnityEngine.Color[16];
    for (int i=0;i<16;i++) colors[i]=new UnityEngine.Color(.5f,.25f,.75f,1);
    try
    {
        texture.SetPixels(colors); texture.Apply();
        var folder = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,"Temp/WhimTex");
        System.IO.Directory.CreateDirectory(folder);
        png = System.IO.Path.Combine(folder,"live-smoke-"+System.Guid.NewGuid().ToString("N")+".png");
        System.IO.File.WriteAllBytes(png,UnityEngine.ImageConversion.EncodeToPNG(texture));
    }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
    string imageField = "\"imagePath\":\"" + png.Replace('\\','/') + "\",\"fit\":\"stretch\"";
    var imageJob=Begin();
    var imagePlaceholder = Find(Text(imageJob,"layerId"));
    Call("\"op\":\"complete\","+jobFields(imageJob)+","+imageField);
    Check(ReferenceEquals(imagePlaceholder, Find(Text(imageJob,"layerId"))), "Image completion retains the reserved wrapper");
    UnityEditor.Undo.PerformUndo();
    Check(Find(Text(imageJob,"layerId"))?.Behaviour is DCFApixels.WhimTex.PendingLayerBehaviour, "Image Undo restores the placeholder");
    UnityEditor.Undo.PerformRedo();
    Check(Find(Text(imageJob,"layerId"))?.Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour, "Image Redo restores owned Drawing content");
    var drawing=(DCFApixels.WhimTex.DrawingLayerBehaviour)Find(Text(imageJob,"layerId"));
    var storedProperty=typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetProperty("StoredTexture",instance);
    UnityEngine.Texture2D Stored() => (UnityEngine.Texture2D)storedProperty.GetValue(drawing);
    Check(Stored().width==4 && Stored().height==4 && drawing.colorRange==DCFApixels.WhimTex.LayerColorRange.Standard,"PNG keeps its original Drawing resolution");
    Check(drawing.transform.scale==UnityEngine.Vector2.one && drawing.transform.position==UnityEngine.Vector2.zero,"Full-canvas stretch uses identity transform");
    Check(UnityEngine.Mathf.Abs(Stored().GetPixel(3,3).r-.5f)<.01f,"sRGB round trip does not darken image");
    var originalPixels=Stored().GetRawTextureData();
    string originalJson=UnityEditor.EditorJsonUtility.ToJson(document);
    var another=Begin();
    Call("\"op\":\"preview\","+jobFields(another)+","+spec+",\"maxSize\":8");
    Check(Stored()!=null && System.Linq.Enumerable.SequenceEqual(originalPixels,Stored().GetRawTextureData()),"Preview cannot destroy or change unsaved Drawing textures");

    // Capture a soft selection, then change the live selection before delivering.
    var selection=windowType.GetMethod("GetAreaSelection",instance).Invoke(window,null);
    var selectionType=selection.GetType();
    var combineType=api.Assembly.GetType("DCFApixels.WhimTex.SelectionCombine");
    var replace=System.Enum.Parse(combineType,"Replace");
    var mask=new byte[64]; mask[3*8+3]=255; mask[3*8+4]=128;
    selectionType.GetMethod("Set",instance).Invoke(selection,new object[]{mask,replace});
    var regional=Begin(",\"source\":\"merged\",\"area\":\"selection\",\"padding\":1");
    Check(Text(regional,"capture","selectionMode")=="strict" && bool.Parse(Text(regional,"capture","maskEnforced")),"Strict is the backwards-compatible default");
    var guide=Json(DCFApixels.WhimTex.WhimTexApi.LiveBegin(System.Guid.NewGuid().ToString("N"),"Guided",area:"selection",sessionId:session,selectionMode:"guide",padding:1));
    Check(bool.Parse(Text(guide,"success")) && Text(guide,"capture","selectionMode")=="guide" && !bool.Parse(Text(guide,"capture","maskEnforced")),"Fast begin accepts guide and padding");
    int countBeforeInvalid=document.layers.Count;
    Call("\"op\":\"begin\","+scope+",\"requestId\":\""+System.Guid.NewGuid().ToString("N")+"\",\"area\":\"selection\",\"selectionMode\":\"typo\"","invalid_request");
    Check(document.layers.Count==countBeforeInvalid,"Invalid mode cannot insert a placeholder");
    Check(System.IO.File.Exists(Text(regional,"capture","imagePath")) && System.IO.File.Exists(Text(regional,"capture","maskPath")),"Context and mask captured");
    selectionType.GetMethod("Clear",instance).Invoke(selection,null);
    string forkRequest="\"op\":\"fork\","+jobFields(regional)+",\"requestId\":\""+System.Guid.NewGuid().ToString("N")+"\",\"name\":\"Second region\"";
    var fork=Call(forkRequest);
    Check(Text(fork,"jobId")!=Text(regional,"jobId") && Text(fork,"layerId")!=Text(regional,"layerId"),"Fork has independent identity");
    Check(Text(fork,"capture","maskPath")==Text(regional,"capture","maskPath") && Text(fork,"capture","imagePath")==Text(regional,"capture","imagePath"),"Fork reuses frozen capture after selection changes");
    Check(Text(Call(forkRequest),"jobId")==Text(fork,"jobId"),"Fork retry is idempotent");
    Call("\"op\":\"complete\","+jobFields(regional)+","+imageField);
    Call("\"op\":\"complete\","+jobFields(fork)+","+imageField);
    Check(Find(Text(fork,"layerId"))?.Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour,"Fork completes independently after source completion");
    var regionalDrawing=(DCFApixels.WhimTex.DrawingLayerBehaviour)Find(Text(regional,"layerId"));
    var regionalPixels=(UnityEngine.Texture2D)storedProperty.GetValue(regionalDrawing);
    Check(regionalPixels.width==4 && regionalPixels.height==4,"Regional image keeps source dimensions");
    Check(regionalDrawing.transform.scale==new UnityEngine.Vector2(.5f,.375f) && regionalDrawing.transform.position==new UnityEngine.Vector2(0,-.5f),"Transform places context crop on canvas");
    Check(regionalPixels.GetPixel(1,1).a>.99f && UnityEngine.Mathf.Abs(regionalPixels.GetPixel(2,1).a-128f/255)<.01f,"Frozen soft selection is mapped to source pixels");
    Check(regionalPixels.GetPixel(0,1).a==0 && regionalPixels.GetPixel(0,0).a==0,"Context outside mask remains transparent");

    var guideFork=Call("\"op\":\"fork\","+jobFields(guide)+",\"requestId\":\""+System.Guid.NewGuid().ToString("N")+"\"");
    Check(Text(guideFork,"capture","selectionMode")=="guide" && !bool.Parse(Text(guideFork,"capture","maskEnforced")),"Fork retains guide mode after selection changes");
    foreach(var guided in new[]{guide,guideFork})
    {
        Call("\"op\":\"complete\","+jobFields(guided)+","+imageField);
        var guidedDrawing=(DCFApixels.WhimTex.DrawingLayerBehaviour)Find(Text(guided,"layerId"));
        var guidedPixels=(UnityEngine.Texture2D)storedProperty.GetValue(guidedDrawing);
        Check(guidedPixels.width==4 && guidedPixels.height==4 && guidedDrawing.transform.scale==regionalDrawing.transform.scale,"Guide retains source resolution and crop placement");
        Check(guidedPixels.GetPixel(0,0).a>.99f && guidedPixels.GetPixel(2,1).a>.99f,"Guide does not clip or soften output to the selection");
    }

    string target=",\"destination\":\"replacePixels\",\"targetLayerId\":\""+drawing.Id+"\"";
    var replacement=Begin(target);
    Edit(()=>{ drawing.layerName="Renamed during generation"; drawing.enabled=false; });
    Call("\"op\":\"complete\","+jobFields(replacement)+","+imageField);
    Check(Find(drawing.Id)==drawing.Owner && !drawing.enabled && drawing.layerName=="Renamed during generation","Pixel replacement preserves identity and independent properties");
    var conflict=Begin(target);
    Edit(()=>drawing.transform.rotation+=15);
    var conflictResult=Call("\"op\":\"complete\","+jobFields(conflict)+","+imageField,"revision_conflict");
    Check(Find(Text(conflict,"layerId"))?.Behaviour is DCFApixels.WhimTex.PendingLayerBehaviour,"Conflict retains reservation");

    var invalid=Begin();
    Call("\"op\":\"complete\","+jobFields(invalid)+",\"layer\":{\"type\":\"noise\",\"settings\":{\"name\":\"Overwrite\"}}","invalid_request");
    var resized=Begin();
    Edit(()=>document.width=9);
    Call("\"op\":\"complete\","+jobFields(resized)+","+spec,"revision_conflict");
    Edit(()=>document.width=8);

    var closed=Begin();
    typeof(UnityEditor.EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window, false);
    UnityEngine.Object.DestroyImmediate(window);
    Check(Text(Call("\"op\":\"status\","+jobFields(closed)),"state")=="cancelled","Closing cancels delivery");
    Call("\"op\":\"complete\","+jobFields(closed)+","+spec,"job_closed");
    return "Live agent Editor checks passed: "+checks+". Temporary PNG retained: "+png;
}
finally
{
    if(window!=null) { typeof(UnityEditor.EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window, false); UnityEngine.Object.DestroyImmediate(window); }
}
