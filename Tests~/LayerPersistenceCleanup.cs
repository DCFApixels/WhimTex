// Removes only the disposable fixture owned by LayerPersistenceSetup, never user documents.
const string Path = "Assets/WhimTexCompositionVerification-b4f21b35.asset";
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
string unsavedId = SessionState.GetString("WhimTex.Verification.UnsavedId", "");
string missingId = SessionState.GetString("WhimTex.Verification.MissingId", "");
int closed = 0;
bool unsavedPixelsSurvived = false;
foreach (var window in Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
{
    var doc = (DCFApixels.WhimTex.TextureCompositor)typeof(DCFApixels.WhimTex.TextureCompositorWindow).GetField("compositor", Hidden).GetValue(window);
    if (doc == null || AssetDatabase.Contains(doc) || string.IsNullOrEmpty(unsavedId) ||
        doc.layers.Count != 1 || doc.layers[0].Id != unsavedId) continue;
    var pixel = doc.layers[0].GetPreviewTexture(8).GetPixel(4,4);
    unsavedPixelsSurvived = pixel.r > .99f && pixel.a > .99f;
    Undo.ClearUndo(doc);
    typeof(EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window, false);
    UnityEngine.Object.DestroyImmediate(window);
    closed++;
}
var fixture = AssetDatabase.LoadAssetAtPath<DCFApixels.WhimTex.TextureCompositor>(Path);
bool removed = false;
if (fixture != null)
{
    if (string.IsNullOrEmpty(missingId) || typeof(DCFApixels.WhimTex.TextureCompositor)
        .GetMethod("FindLayer", Hidden).Invoke(fixture, new object[] { missingId }) == null)
        throw new Exception("Fixture ownership check failed; nothing deleted.");
    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(Path)) Undo.ClearUndo(obj);
    removed = AssetDatabase.DeleteAsset(Path);
    if (!removed) throw new Exception("Could not remove the disposable fixture.");
}
foreach (string key in new[] { "UnsavedId", "MissingId", "MissingGroupId", "DrawingId", "Color" })
    SessionState.EraseString("WhimTex.Verification." + key);
return new { success = true, closed, removed, unsavedPixelsSurvived };
