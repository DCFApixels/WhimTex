using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Test-only Unity non-public dock-host access explicitly approved for Legacy coverage.
public static class NewDocumentWindowTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NewDocumentWindowTests", Body);
    static void Body()
    {
        // Unity Pipeline eval_file: temporary test tabs only; preserve all original documents.
        var type = typeof(DCFApixels.WhimTex.WhimTexWindow);
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var field = type.GetField("activeDocument", Flags);
        var parentField = typeof(EditorWindow).GetField("m_Parent", Flags);
        WhimTex.Tests.UnityC.FixtureContext.Context.True(parentField != null,
            "Approved test-only dock-host field remains available");
        var previousFocus = EditorWindow.focusedWindow;
        var originals = Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>();
        var originalDocuments = new System.Collections.Generic.Dictionary<DCFApixels.WhimTex.WhimTexWindow, object>();
        foreach (var item in originals) originalDocuments[item] = field.GetValue(item);
        var origin = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(EditorWindow.CreateWindow<DCFApixels.WhimTex.WhimTexWindow>("New Document Test", type));
        var created = new System.Collections.Generic.List<DCFApixels.WhimTex.WhimTexWindow>();
        var document = (DCFApixels.WhimTex.WhimTexDocument)field.GetValue(origin);
        int checks = 0;
        void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
        try
        {
            document.layers.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour()));
            type.GetField("temporaryDocumentDirty", Flags).SetValue(origin, true);
            type.GetMethod("UpdateUnsavedChangesState", Flags).Invoke(origin, null);
            Check(origin.hasUnsavedChanges, "Source has unsaved edits");
            for (int i = 0; i < 2; i++)
            {
                var next = (DCFApixels.WhimTex.WhimTexWindow)type.GetMethod("OpenNewDocument", Flags).Invoke(origin, null);
                created.Add(next);
                var blank = (DCFApixels.WhimTex.WhimTexDocument)field.GetValue(next);
                Check(next != origin && blank != document, "New window and document instances");
                Check(blank.layers.Count == 0 && !AssetDatabase.Contains(blank), "Empty unsaved document");
                Check(next.titleContent.text == "Untitled" && next.titleContent.image != null, "Default tab name and icon");
                Check(!next.hasUnsavedChanges, "Empty new document does not prompt to save");
                Check(EditorWindow.focusedWindow == next, "New tab receives focus");
                Check(object.ReferenceEquals(field.GetValue(origin), document) && document.layers.Count == 1 && origin.hasUnsavedChanges, "Original document and unsaved edits preserved");
                var parent = parentField.GetValue(origin);
                if (parent?.GetType().FullName == "UnityEditor.DockArea")
                    Check(parent == parentField.GetValue(next), "New document joins the existing tab group");
            }
            Check(field.GetValue(created[0]) != field.GetValue(created[1]), "Repeated New does not reuse an empty document");
            foreach (var item in originals) Check(field.GetValue(item) == originalDocuments[item], "User's document remains open and unchanged");
            return;
        }
        finally
        {
            foreach (var window in created) if (window != null) global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window);
            document.layers.Clear();
            type.GetField("temporaryDocumentDirty", Flags).SetValue(origin, false);
            type.GetMethod("UpdateUnsavedChangesState", Flags).Invoke(origin, null);
            global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(origin);
            if (previousFocus != null) previousFocus.Focus();
        }
        
    }
}
