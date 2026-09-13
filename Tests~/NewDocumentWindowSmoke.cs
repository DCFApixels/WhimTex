// Unity Pipeline eval_file: temporary test tabs only; preserve all original documents.
var type = typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var field = type.GetField("compositor", Flags);
var parentField = typeof(EditorWindow).GetField("m_Parent", Flags);
var previousFocus = EditorWindow.focusedWindow;
var originals = Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>();
var originalDocuments = new System.Collections.Generic.Dictionary<DCFApixels.SpriteEditor.TextureCompositorWindow, object>();
foreach (var item in originals) originalDocuments[item] = field.GetValue(item);
var origin = EditorWindow.CreateWindow<DCFApixels.SpriteEditor.TextureCompositorWindow>("New Document Test", type);
var created = new System.Collections.Generic.List<DCFApixels.SpriteEditor.TextureCompositorWindow>();
var document = (DCFApixels.SpriteEditor.TextureCompositor)field.GetValue(origin);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
try
{
    document.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.ColorFillLayerBehaviour()));
    type.GetField("temporaryDocumentDirty", Flags).SetValue(origin, true);
    type.GetMethod("UpdateUnsavedChangesState", Flags).Invoke(origin, null);
    Check(origin.hasUnsavedChanges, "Source has unsaved edits");
    for (int i = 0; i < 2; i++)
    {
        var next = (DCFApixels.SpriteEditor.TextureCompositorWindow)type.GetMethod("OpenNewDocument", Flags).Invoke(origin, null);
        created.Add(next);
        var blank = (DCFApixels.SpriteEditor.TextureCompositor)field.GetValue(next);
        Check(next != origin && blank != document, "New window and document instances");
        Check(blank.layers.Count == 0 && !AssetDatabase.Contains(blank), "Empty unsaved document");
        Check(next.titleContent.text == "Untitled" && next.titleContent.image != null, "Default tab name and icon");
        Check(!next.hasUnsavedChanges, "Empty new document does not prompt to save");
        Check(EditorWindow.focusedWindow == next, "New tab receives focus");
        Check(field.GetValue(origin) == document && document.layers.Count == 1 && origin.hasUnsavedChanges, "Original document and unsaved edits preserved");
        var parent = parentField.GetValue(origin);
        if (parent?.GetType().FullName == "UnityEditor.DockArea")
            Check(parent == parentField.GetValue(next), "New document joins the existing tab group");
    }
    Check(field.GetValue(created[0]) != field.GetValue(created[1]), "Repeated New does not reuse an empty document");
    foreach (var item in originals) Check(field.GetValue(item) == originalDocuments[item], "User's document remains open and unchanged");
    return "New document tabs: " + checks + " checks passed";
}
finally
{
    foreach (var window in created) if (window != null) window.Close();
    document.layers.Clear();
    type.GetField("temporaryDocumentDirty", Flags).SetValue(origin, false);
    type.GetMethod("UpdateUnsavedChangesState", Flags).Invoke(origin, null);
    origin.Close();
    if (previousFocus != null) previousFocus.Focus();
}
