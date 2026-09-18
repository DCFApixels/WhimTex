using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class OutputSettingsWindowSmoke
{
    public static string Main()
    {
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        var type = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexOutputSettingsWindow", true);
        var open = type.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic);
        var field = type.GetField("document", BindingFlags.Instance | BindingFlags.NonPublic);
        EditorWindow window = null;
        try
        {
            open.Invoke(null, new object[] { document });
            open.Invoke(null, new object[] { document });
            int count = 0;
            foreach (var candidate in Resources.FindObjectsOfTypeAll(type))
                if ((TextureCompositor)field.GetValue(candidate) == document) { count++; window = (EditorWindow)candidate; }
            if (count != 1) throw new Exception("Repeated Open must reuse the document window.");
            if (window.rootVisualElement.Q<ScrollView>() == null) throw new Exception("Settings view was not created.");
            UnityEngine.Object.DestroyImmediate(document);
            type.GetMethod("OnInspectorUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            if (window.rootVisualElement.Q<ScrollView>() != null) throw new Exception("Destroyed document left stale bindings.");
            return "PASS: window creation, reuse and destroyed-document cleanup";
        }
        finally
        {
            if (window != null) window.Close();
            if (document != null) UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
