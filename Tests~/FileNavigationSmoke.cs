// Unity Pipeline eval_file: only transient empty test document/window; original windows are preserved.
var type = typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var staticFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var open = type.GetMethod("OpenReferencedDocument", staticFlags);
var field = type.GetField("compositor", instanceFlags);
var previousFocus = UnityEditor.EditorWindow.focusedWindow;
var originals = UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>();
var documents = new System.Collections.Generic.Dictionary<DCFApixels.SpriteEditor.TextureCompositorWindow, object>();
foreach (var item in originals) documents[item] = field.GetValue(item);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 32;
document.name = "Reference Test";
DCFApixels.SpriteEditor.TextureCompositorWindow created = null;
int checks = 0;
void Check(bool condition) { if (!condition) throw new System.Exception("File navigation check failed: " + checks); checks++; }
try
{
    Check(open.Invoke(null, new object[] { null }) == null);
    created = (DCFApixels.SpriteEditor.TextureCompositorWindow)open.Invoke(null, new object[] { document });
    Check(created != null && field.GetValue(created) == document);
    Check(created.titleContent.text == "Reference Test");
    Check(created.titleContent.image != null);
    document.name = "Renamed Test";
    type.GetMethod("RefreshDocumentTitle", instanceFlags).Invoke(created, new object[] { false });
    Check(created.titleContent.text == "Renamed Test");
    var blank = (DCFApixels.SpriteEditor.TextureCompositor)type.GetMethod("CreateTemporaryCompositor", instanceFlags).Invoke(created, null);
    try { Check(blank.name == "Untitled"); }
    finally { UnityEngine.Object.DestroyImmediate(blank); }
    Check(!documents.ContainsKey(created));
    Check(open.Invoke(null, new object[] { document }) == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>().Length == originals.Length + 1);
    var parentField = typeof(UnityEditor.EditorWindow).GetField("m_Parent", instanceFlags);
    bool dockAvailable = false, sharesTabs = false;
    if (parentField != null)
        foreach (var item in originals)
        {
            var parent = parentField.GetValue(item);
            if (parent?.GetType().FullName != "UnityEditor.DockArea") continue;
            dockAvailable = true;
            sharesTabs |= parent == parentField.GetValue(created);
        }
    if (dockAvailable) Check(sharesTabs);
    foreach (var item in originals) Check(field.GetValue(item) == documents[item]);
    return "File navigation: " + checks + " Unity checks passed; new window and reuse verified; shared tab group: " + sharesTabs;
}
finally
{
    if (created != null) created.Close();
    if (document != null) UnityEngine.Object.DestroyImmediate(document);
    if (previousFocus != null) previousFocus.Focus();
}
