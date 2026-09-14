// Run with the disposable CompositionMissingTestBehaviour present, then remove that type and compile.
const string Path = "Assets/WhimTexCompositionVerification-b4f21b35.asset";
const string WindowTitle = "WhimTex verification b4f21b35";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var docType = typeof(DCFApixels.WhimTex.TextureCompositor);
if (System.IO.File.Exists(Path)) throw new Exception("Fixture already exists; do not overwrite it.");
var missingType = docType.Assembly.GetType("DCFApixels.WhimTex.CompositionMissingTestBehaviour", true);
var doc = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.width = doc.height = 8;
var window = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
var temporary = (DCFApixels.WhimTex.TextureCompositor)window.GetType().GetField("compositor", flags).GetValue(window);
temporary.width = temporary.height = 8;
var previouslyFocused = EditorWindow.focusedWindow;
bool saved = false;
try
{
    var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour { brushColor = Color.red, brushSize = 8, brushHardness = 1 };
    object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, flags).Invoke(obj, args);
    Call(drawing, "PaintPoint", new Vector2(.5f,.5f), 8,8,Call(drawing,"GetStrokeParameters",false));
    Call(drawing, "SyncSurfaceToTexture");
    temporary.layers.Add(drawing);
    Call(temporary,"NormalizeModel");
    window.titleContent = new GUIContent(WindowTitle);
    window.ShowUtility();
    window.position = new Rect(100,100,800,700);
    window.titleContent = new GUIContent(WindowTitle);
    SessionState.SetString("WhimTex.Verification.UnsavedId", drawing.Id);
    var group = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.GroupLayerBehaviour()) { layerName="Saved group", opacity=.7f };
    var missing = new DCFApixels.WhimTex.Layer((DCFApixels.WhimTex.LayerBehaviour)Activator.CreateInstance(missingType)) { layerName="Missing leaf", opacity=.35f };
    missing.transform.rotation = 37;
    group.children.Add(missing);
    group.children.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color=Color.blue }));
    var missingGroup = new DCFApixels.WhimTex.Layer((DCFApixels.WhimTex.LayerBehaviour)Activator.CreateInstance(missingType)) { layerName="Missing group" };
    typeof(DCFApixels.WhimTex.Layer).GetField("group",flags).SetValue(missingGroup,true);
    missingGroup.children = new List<DCFApixels.WhimTex.Layer>{new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour {color=Color.green})};
    doc.layers.Add(group); doc.layers.Add(missingGroup);
    var persistedDrawing = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    var pixels = UnityEngine.Object.Instantiate(drawing.GetPreviewTexture(8));
    typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetField("pixels", flags).SetValue(persistedDrawing, pixels);
    group.children.Add(persistedDrawing);
    Call(doc,"NormalizeModel");
    var outline = new DCFApixels.WhimTex.OutlineLayerBehaviour { inputMode=DCFApixels.WhimTex.EffectInputMode.Specific, TargetLayerId=persistedDrawing.Id };
    doc.layers.Insert(0,outline);
    var fx = (DCFApixels.WhimTex.ShaderFX)Call(doc,"AddEmbeddedShaderFX",group);
    Call(doc,"MarkChanged");
    AssetDatabase.CreateAsset(doc,Path); saved=true;
    Call(doc,"MarkChanged");
    AssetDatabase.SaveAssetIfDirty(doc);
    SessionState.SetString("WhimTex.Verification.MissingId", missing.Id);
    SessionState.SetString("WhimTex.Verification.MissingGroupId", missingGroup.Id);
    SessionState.SetString("WhimTex.Verification.DrawingId", persistedDrawing.Id);
    var image=doc.Compose();
    try { SessionState.SetString("WhimTex.Verification.Color", JsonUtility.ToJson(image.GetPixel(4,4))); }
    finally { UnityEngine.Object.DestroyImmediate(image); }
    if(previouslyFocused!=null)previouslyFocused.Focus();
    return new {path=Path, window=WindowTitle, missingId=missing.Id};
}
catch
{
    typeof(EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window,false);
    UnityEngine.Object.DestroyImmediate(window);
    if(saved)AssetDatabase.DeleteAsset(Path); else UnityEngine.Object.DestroyImmediate(doc);
    throw;
}
