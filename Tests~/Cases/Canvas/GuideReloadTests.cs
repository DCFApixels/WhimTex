// Node orchestration: Begin -> one real Unity recompile -> Verify -> public GUID Cleanup.
// Uses one temporary window/document; no project assets or user documents are changed.
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GuideReloadTests
{
    static string Key;
    static string RunId;
    static WhimTex.Tests.TestContext context;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type WindowType = typeof(TextureCompositorWindow);
    private static FieldInfo Field(string name) => WindowType.GetField(name, Hidden);
    private static void Check(bool condition, string message)
    {
        context.True(condition, message);
    }

    static string ExecutePrepare()
    {
        Check(SessionState.GetString(Key, "").Length == 0, "A guide reload test is already pending.");
        var window = EditorWindow.CreateWindow<TextureCompositorWindow>();
        window.name = "WhimTex Guide Reload " + RunId;
        try
        {
            var document = (TextureCompositor)Field("compositor").GetValue(window);
            document.name = "Guide Reload Test " + RunId;
            window.ShowUtility();
            window.CreateGUI();
            var guides = (IList)Field("canvasGuides").GetValue(window);
            var guideType = WindowType.GetNestedType("CanvasGuide", BindingFlags.NonPublic);
            for (int i = 0; i < 3; i++)
            {
                var guide = Activator.CreateInstance(guideType);
                guideType.GetField("normal").SetValue(guide, new Vector2(Mathf.Cos(i), Mathf.Sin(i)));
                guideType.GetField("position").SetValue(guide, 37f + i * 61f);
                guides.Add(guide);
            }
            Field("canvasGuidesHidden").SetValue(window, true);
            Field("canvasGuidesLocked").SetValue(window, true);
            Field("canvasGuidesSnap").SetValue(window, false);

            // Rebuilding the view must rebind its runtime document reference, not discard guides.
            Field("canvasGuidesDocument").SetValue(window, null);
            window.CreateGUI();
            Validate(window);
            window.CreateGUI();
            Validate(window);
            SessionState.SetString(Key, document.name);
            return "Guide UI rebuild checks passed. Reload scripts, then run GuideReloadTests.Verify.";
        }
        catch (Exception primary)
        {
            var document = (TextureCompositor)Field("compositor").GetValue(window);
            try { UnityBReload.Drain(new Action[] { () => UnityBRun.CloseOwned(window), () => UnityBReload.DestroyOwned(document) }); }
            catch (Exception cleanup) { throw new AggregateException("Begin failed and owned cleanup also failed", primary, cleanup); }
            throw;
        }
    }

    static string ExecuteVerify()
    {
        string marker = SessionState.GetString(Key, "");
        Check(marker.Length != 0, "Run Prepare before reloading scripts.");
        Check(marker == "Guide Reload Test " + RunId, "Owned GUID marker is unchanged.");
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
        {
            var document = (TextureCompositor)Field("compositor").GetValue(window);
            if (document == null || document.name != marker) continue;
            Validate(window);
            window.CreateGUI();
            Validate(window);
            var setDocument = WindowType.GetMethod("SetCompositor", Hidden);
            setDocument.Invoke(window, new object[] { document });
            Validate(window);
            var next = ScriptableObject.CreateInstance<TextureCompositor>();
            next.name = "Guide Reload Test " + RunId;
            next.hideFlags = HideFlags.HideAndDontSave;
            setDocument.Invoke(window, new object[] { next });
            Check(((IList)Field("canvasGuides").GetValue(window)).Count == 0,
                "Switching to another document must clear guides.");
            // Node's finally invokes public Cleanup even on assertion failure. Keep the
            // current model alive until its host's OnDisable; cleanup cannot hide this verdict.
            return "";
        }
        throw new Exception("Test document did not survive script reload.");
    }

    private static void Validate(TextureCompositorWindow window)
    {
        var guides = (IList)Field("canvasGuides").GetValue(window);
        Check(guides.Count == 3, "Guide list was cleared during restoration.");
        Check((bool)Field("canvasGuidesHidden").GetValue(window), "Hidden state was lost.");
        Check((bool)Field("canvasGuidesLocked").GetValue(window), "Locked state was lost.");
        Check(!(bool)Field("canvasGuidesSnap").GetValue(window), "Snapping preference was lost.");
        Check((TextureCompositor)Field("canvasGuidesDocument").GetValue(window) ==
            (TextureCompositor)Field("compositor").GetValue(window), "Guide view is not bound to the restored document.");
        for (int i = 0; i < guides.Count; i++)
        {
            var guide = guides[i];
            Check((float)guide.GetType().GetField("position").GetValue(guide) == 37f + i * 61f, "Guide position changed.");
            Check((Vector2)guide.GetType().GetField("normal").GetValue(guide) ==
                new Vector2(Mathf.Cos(i), Mathf.Sin(i)), "Guide angle changed.");
        }
    }

    static void Bind(string runId, WhimTex.Tests.TestContext value) {
        if (!Guid.TryParseExact(runId, "N", out _)) throw new ArgumentException("Use one stable per-run N-format GUID across the real reload.");
        RunId=runId; Key="WhimTex.Tests.UnityB.GuideReloadTests."+runId; context=value;
    }
    public static string Begin(string runId) => WhimTex.Tests.TestContext.Run("GuideReloadTests: Begin", value => {
        var previous = context;
        try { Bind(runId,value); UnityBReload.Begin("Guide",runId,value); ExecutePrepare(); UnityBReload.Arm("Guide",runId); }
        finally { context = previous; }
    });
    public static string Trigger(string runId) => UnityBReload.Trigger("Guide",runId);
    public static string ReloadPoll(string runId) => UnityBReload.ReloadPoll("Guide",runId);
    public static string Verify(string runId) => WhimTex.Tests.TestContext.Run("GuideReloadTests: real reload verification", value => {
        var previous = context;
        try { Bind(runId,value); UnityBReload.Verify("Guide",runId,value); ExecuteVerify(); }
        finally { context = previous; }
    });
    public static string Cleanup(string runId) => UnityBReload.Cleanup("Guide",runId, () => {
        var previous = context;
        try {
        Bind(runId,null);
        var steps = new System.Collections.Generic.List<Action>();
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>()) {
            var document = (TextureCompositor)Field("compositor").GetValue(window);
            if (window.name != "WhimTex Guide Reload " + RunId && (document == null || document.name != "Guide Reload Test " + RunId)) continue;
            steps.Add(() => UnityBRun.CloseOwned(window));
            if (document != null && document.name == "Guide Reload Test " + RunId) steps.Add(() => UnityBReload.DestroyOwned(document));
        }
        foreach (var document in Resources.FindObjectsOfTypeAll<TextureCompositor>())
            if (document.name == "Guide Reload Test " + RunId) steps.Add(() => UnityBReload.DestroyOwned(document));
        UnityBReload.Drain(steps);
        SessionState.EraseString(Key);
        } finally { context = previous; }
    });
}
