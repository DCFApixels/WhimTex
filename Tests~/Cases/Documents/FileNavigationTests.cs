using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

public static class FileNavigationTests
{
    static string Execute()
    {
// Unity Pipeline eval_file: only transient empty test document/window; original windows are preserved.
var type = typeof(DCFApixels.WhimTex.WhimTexWindow);
var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var staticFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var open = type.GetMethod("OpenReferencedDocument", staticFlags);
var field = type.GetField("activeDocument", instanceFlags);
var previousFocus = UnityEditor.EditorWindow.focusedWindow;
var originals = UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>();
var documents = new System.Collections.Generic.Dictionary<DCFApixels.WhimTex.WhimTexWindow, object>();
foreach (var item in originals) documents[item] = field.GetValue(item);
var document = UnityBRun.Create<DCFApixels.WhimTex.WhimTexDocument>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 32;
document.name = "Reference Test";
DCFApixels.WhimTex.WhimTexWindow created = null;
DCFApixels.WhimTex.WhimTexWindow secondWindow = null;
DCFApixels.WhimTex.WhimTexDocument secondDocument = null;
int checks = 0;
void Check(bool condition, string reason = null) { UnityBRun.Check(!(!condition), "File navigation check failed: " + checks + "; " + reason); checks++; }
try
{
    Check(open.Invoke(null, new object[] { null }) == null);
    created = (DCFApixels.WhimTex.WhimTexWindow)open.Invoke(null, new object[] { document });
    Check(created != null && field.GetValue(created) == document);
    Check(created.titleContent.text == "Reference Test");
    Check(created.titleContent.image != null);
    document.name = "Renamed Test";
    type.GetMethod("RefreshDocumentTitle", instanceFlags).Invoke(created, new object[] { false });
    Check(created.titleContent.text == "Renamed Test");
    var blank = (DCFApixels.WhimTex.WhimTexDocument)type.GetMethod("CreateTemporaryDocument", instanceFlags).Invoke(created, null);
    try { Check(blank.name == "Untitled"); }
    finally { UnityEngine.Object.DestroyImmediate(blank); }
    Check(!documents.ContainsKey(created));
    Check(open.Invoke(null, new object[] { document }) == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>().Length == originals.Length + 1);
    DCFApixels.WhimTex.WhimTexWindow.Open(null);
    DCFApixels.WhimTex.WhimTexWindow.Open(document);
    Check(UnityEditor.EditorWindow.focusedWindow == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>().Length == originals.Length + 1);
    secondDocument = UnityBRun.Create<DCFApixels.WhimTex.WhimTexDocument>();
    secondDocument.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    secondDocument.width = secondDocument.height = 32;
    secondDocument.name = "Second Document Test";
    DCFApixels.WhimTex.WhimTexWindow.Open(secondDocument);
    foreach (var item in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>())
        if (field.GetValue(item) == secondDocument) secondWindow = item;
    Check(secondWindow != null && secondWindow != created && !documents.ContainsKey(secondWindow));
    Check(field.GetValue(created) == document);
    Check(UnityEditor.EditorWindow.focusedWindow == secondWindow);
    DCFApixels.WhimTex.WhimTexWindow.Open(document);
    Check(UnityEditor.EditorWindow.focusedWindow == created);
    Check(UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>().Length == originals.Length + 2);
    // Test-only host identity inspection: do not mutate an existing dock/layout.
    var parentField = typeof(UnityEditor.EditorWindow).GetField("m_Parent", instanceFlags);
    Check(parentField != null, "Native tab-host accessor is available");
    bool dockAvailable = false, sharesTabs = false;
    foreach (var item in originals)
    {
        if (!item.docked) continue;
        var parent = parentField.GetValue(item);
        if (parent?.GetType().FullName != "UnityEditor.DockArea") continue;
        dockAvailable = true;
        sharesTabs |= parent == parentField.GetValue(created);
    }
    if (dockAvailable) Check(sharesTabs, "Existing docked window was not reused as a tab host; new parent=" + parentField.GetValue(created));
    foreach (var item in originals) Check(field.GetValue(item) == documents[item], "Original document changed in " + item.name);
    return "File navigation: " + checks + " Unity checks passed; new window and reuse verified";
}
finally
{
    if (secondWindow != null) UnityBRun.CloseOwned(secondWindow);
    if (secondDocument != null) UnityEngine.Object.DestroyImmediate(secondDocument);
    if (created != null) UnityBRun.CloseOwned(created);
    if (document != null) UnityEngine.Object.DestroyImmediate(document);
    if (previousFocus != null) previousFocus.Focus();
}

return "";
    }
    public static string Run() => UnityBRun.Run("FileNavigationSmoke", () => Execute());
}
