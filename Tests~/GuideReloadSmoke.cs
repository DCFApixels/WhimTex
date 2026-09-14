// Run Prepare through Unity Pipeline run_script, reload scripts, then run Verify.
// Uses one temporary window/document; no project assets or user documents are changed.
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GuideReloadSmoke
{
    private const string Key = "WhimTex.Tests.GuideReload";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type WindowType = typeof(TextureCompositorWindow);
    private static FieldInfo Field(string name) => WindowType.GetField(name, Hidden);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static string Prepare()
    {
        Check(SessionState.GetString(Key, "").Length == 0, "A guide reload test is already pending.");
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        try
        {
            var document = (TextureCompositor)Field("compositor").GetValue(window);
            document.name = "Guide Reload Test " + Guid.NewGuid().ToString("N");
            window.ShowUtility();
            window.CreateGUI();
            var guides = (IList)Field("previewGuides").GetValue(window);
            var guideType = WindowType.GetNestedType("PreviewGuide", BindingFlags.NonPublic);
            for (int i = 0; i < 3; i++)
            {
                var guide = Activator.CreateInstance(guideType);
                guideType.GetField("normal").SetValue(guide, new Vector2(Mathf.Cos(i), Mathf.Sin(i)));
                guideType.GetField("position").SetValue(guide, 37f + i * 61f);
                guides.Add(guide);
            }
            Field("previewGuidesHidden").SetValue(window, true);
            Field("previewGuidesLocked").SetValue(window, true);
            Field("previewGuidesSnap").SetValue(window, false);

            // Rebuilding the view must rebind its runtime document reference, not discard guides.
            Field("previewGuidesDocument").SetValue(window, null);
            window.CreateGUI();
            Validate(window);
            window.CreateGUI();
            Validate(window);
            SessionState.SetString(Key, document.name);
            return "Guide UI rebuild checks passed. Reload scripts, then run GuideReloadSmoke.Verify.";
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    public static string Verify()
    {
        string marker = SessionState.GetString(Key, "");
        Check(marker.Length != 0, "Run Prepare before reloading scripts.");
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
        {
            var document = (TextureCompositor)Field("compositor").GetValue(window);
            if (document == null || document.name != marker) continue;
            try
            {
                Validate(window);
                window.CreateGUI();
                Validate(window);
                var setDocument = WindowType.GetMethod("SetCompositor", Hidden);
                setDocument.Invoke(window, new object[] { document });
                Validate(window);
                var next = ScriptableObject.CreateInstance<TextureCompositor>();
                next.hideFlags = HideFlags.HideAndDontSave;
                setDocument.Invoke(window, new object[] { next });
                Check(((IList)Field("previewGuides").GetValue(window)).Count == 0,
                    "Switching to another document must clear guides.");
                return "Guide reload passed: positions, angles, hidden/locked/snap state, UI rebuild and document switching.";
            }
            finally
            {
                window.Close();
                SessionState.EraseString(Key);
            }
        }
        SessionState.EraseString(Key);
        throw new Exception("Test document did not survive script reload.");
    }

    private static void Validate(TextureCompositorWindow window)
    {
        var guides = (IList)Field("previewGuides").GetValue(window);
        Check(guides.Count == 3, "Guide list was cleared during restoration.");
        Check((bool)Field("previewGuidesHidden").GetValue(window), "Hidden state was lost.");
        Check((bool)Field("previewGuidesLocked").GetValue(window), "Locked state was lost.");
        Check(!(bool)Field("previewGuidesSnap").GetValue(window), "Snapping preference was lost.");
        Check((TextureCompositor)Field("previewGuidesDocument").GetValue(window) ==
            (TextureCompositor)Field("compositor").GetValue(window), "Guide view is not bound to the restored document.");
        for (int i = 0; i < guides.Count; i++)
        {
            var guide = guides[i];
            Check((float)guide.GetType().GetField("position").GetValue(guide) == 37f + i * 61f, "Guide position changed.");
            Check((Vector2)guide.GetType().GetField("normal").GetValue(guide) ==
                new Vector2(Mathf.Cos(i), Mathf.Sin(i)), "Guide angle changed.");
        }
    }
}
