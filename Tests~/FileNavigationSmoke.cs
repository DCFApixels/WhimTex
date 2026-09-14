// Unity Pipeline eval_file: only transient empty test document/window; original windows are preserved.
var type = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var staticFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var open = type.GetMethod("OpenReferencedDocument", staticFlags);
var field = type.GetField("compositor", instanceFlags);
var previousFocus = UnityEditor.EditorWindow.focusedWindow;
var originals = UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>();
var documents = new System.Collections.Generic.Dictionary<DCFApixels.WhimTex.TextureCompositorWindow, object>();
foreach (var item in originals) documents[item] = field.GetValue(item);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 32;
document.name = "Reference Test";
DCFApixels.WhimTex.TextureCompositorWindow created = null;
DCFApixels.WhimTex.TextureCompositorWindow secondWindow = null;
DCFApixels.WhimTex.TextureCompositor secondDocument = null;
int checks = 0;
void Check(bool condition) { if (!condition) throw new System.Exception("File navigation check failed: " + checks); checks++; }
try
{
    Check(open.Invoke(null, new object[] { null }) == null);
    created = (DCFApixels.WhimTex.TextureCompositorWindow)open.Invoke(null, new object[] { document });
    Check(created != null && field.GetValue(created) == document);
    Check(created.titleContent.text == "Reference Test");
    Check(created.titleContent.image != null);
    document.name = "Renamed Test";
    type.GetMethod("RefreshDocumentTitle", instanceFlags).Invoke(created, new object[] { false });
    Check(created.titleContent.text == "Renamed Test");
    var blank = (DCFApixels.WhimTex.TextureCompositor)type.GetMethod("CreateTemporaryCompositor", instanceFlags).Invoke(created, null);
    try { Check(blank.name == "Untitled"); }
    finally { UnityEngine.Object.DestroyImmediate(blank); }
    Check(!documents.ContainsKey(created));
    Check(open.Invoke(null, new object[] { document }) == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>().Length == originals.Length + 1);
    DCFApixels.WhimTex.TextureCompositorWindow.Open(null);
    DCFApixels.WhimTex.TextureCompositorWindow.Open(document);
    Check(UnityEditor.EditorWindow.focusedWindow == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>().Length == originals.Length + 1);
    secondDocument = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
    secondDocument.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    secondDocument.width = secondDocument.height = 32;
    secondDocument.name = "Second Document Test";
    DCFApixels.WhimTex.TextureCompositorWindow.Open(secondDocument);
    foreach (var item in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
        if (field.GetValue(item) == secondDocument) secondWindow = item;
    Check(secondWindow != null && secondWindow != created && !documents.ContainsKey(secondWindow));
    Check(field.GetValue(created) == document);
    Check(UnityEditor.EditorWindow.focusedWindow == secondWindow);
    DCFApixels.WhimTex.TextureCompositorWindow.Open(document);
    Check(UnityEditor.EditorWindow.focusedWindow == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>().Length == originals.Length + 2);
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
    if (secondWindow != null) secondWindow.Close();
    if (secondDocument != null) UnityEngine.Object.DestroyImmediate(secondDocument);
    if (created != null) created.Close();
    if (document != null) UnityEngine.Object.DestroyImmediate(document);
    if (previousFocus != null) previousFocus.Focus();
}
