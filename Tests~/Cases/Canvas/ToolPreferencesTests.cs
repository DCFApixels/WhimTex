using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class ToolPreferencesTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [Serializable] public sealed class Pair { public int a, b; public float[] curve = new[] { 0f, 1f }; }
    static object Get(object target, string name) => target.GetType().GetField(name, F).GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, F);
        field.SetValue(target, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
    }
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static void Flush(WhimTexWindow window) => Call(Get(window, "toolPreferences"), "Flush");
    static Dictionary<string, string> Snapshot(WhimTexWindow window)
    {
        var result = new Dictionary<string, string>();
        foreach (var entry in (System.Collections.IEnumerable)Get(Get(window, "toolPreferences"), "entries"))
        {
            string name = (string)entry.GetType().GetProperty("Name", F).GetValue(entry);
            result.Add(name, (string)((Delegate)Get(entry, "encode")).DynamicInvoke(((Delegate)Get(entry, "get")).DynamicInvoke()));
        }
        return result;
    }
    public static string Run() => TestContext.Run("Unified tool preferences", t =>
    {
        var assembly = typeof(WhimTexWindow).Assembly;
        var type = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings+ToolPreferenceSession", true);
        var windows = new List<WhimTexWindow>(); var sessions = new List<IDisposable>();
        var keys = new HashSet<string>(); string prefix = "DCFApixels.WhimTex.Tests.F26." + Guid.NewGuid().ToString("N") + ".";
        int writes = 0;
        Func<string, string> read = name => EditorPrefs.HasKey(prefix + name) ? EditorPrefs.GetString(prefix + name) : null;
        Action<string, string> write = (name, value) => { keys.Add(prefix + name); EditorPrefs.SetString(prefix + name, value); writes++; };
        object Session()
        {
            object session = Activator.CreateInstance(type, F, null, new object[] { read, write }, null);
            sessions.Add((IDisposable)session); return session;
        }
        var bridge = Session(); object store = type.GetProperty("Store", F).GetValue(bridge);
        WhimTexWindow Window(bool reset = false)
        {
            var window = ScriptableObject.CreateInstance<WhimTexWindow>(); windows.Add(window);
            Call(window, "InitializeToolPreferences", store);
            if (reset) Call(Get(window, "toolPreferences"), "Reset");
            return window;
        }
        void Register(object session, Func<Pair> get, Action<Pair> set)
            => type.GetMethod("Register", F).MakeGenericMethod(typeof(Pair)).Invoke(session,
                new object[] { "pair", get, set, (Func<Pair>)(() => new Pair()), null, null, null, true });
        try
        {
            Pair a = new Pair(), b = new Pair(); var sa = Session(); var sb = Session();
            Register(sa, () => a, value => a = value); Register(sb, () => b, value => b = value);
            int initial = writes; a.a = 4;
            for (int i = 0; i < 30; i++) Call(sa, "ScheduleSave");
            t.Equal(initial, writes, "Scheduling never writes synchronously"); t.True((bool)Get(sa, "queued"), "Repeated edits coalesce");
            Call(sa, "Flush"); t.Equal(initial + 1, writes, "One pending settings write");
            b.b = 9; Call(sb, "Flush"); Call(sa, "Flush");
            t.True(read("pair").Contains("\"a\":4") && read("pair").Contains("\"b\":9"), "Stale windows merge only changed fields");
            Call(sa, "Refresh"); t.Equal(9, a.b, "Focus refresh imports another window's field");
            initial = writes; Call(sa, "Flush"); Call(sa, "Refresh"); t.Equal(initial, writes, "Unchanged refresh/close has no write");
            b.curve = new[] { .2f, .8f, 1f }; Call(sb, "Flush"); Call(sa, "Refresh");
            t.Equal(3, a.curve.Length, "Arrays are a complete independent user value");
            ((IDisposable)sa).Dispose(); ((IDisposable)sb).Dispose();

            var first = Window(true); var second = Window(true);
            // Same tool section, independent fields: no stale wholesale brush overwrite.
            Set(Get(first, "paintSettings"), "brushSize", 173f); Flush(first);
            Set(Get(second, "paintSettings"), "fillTolerance", 71); Flush(second);
            Call(Get(first, "toolPreferences"), "Refresh");
            t.Equal(71, (int)Get(Get(first, "paintSettings"), "fillTolerance"), "Paint fields merge across windows");
            t.Equal(173f, (float)Get(Get(first, "paintSettings"), "brushSize"), "Unrelated brush size retained");
            UnityEngine.Object.DestroyImmediate(second); windows.Remove(second);

            var paint = Get(first, "paintSettings");
            Set(paint, "pencilSize", 9); Set(paint, "pencilShape", 2); Set(paint, "blurFlow", .29f);
            Set(paint, "smudgeMixing", .41f); Set(paint, "healingSearch", 101); Set(paint, "fillContiguous", false);
            Set(paint, "secondaryBrushColor", new Color(.1f, .2f, .3f, .4f));
            var dynamics = Get(paint, "dynamics"); Set(dynamics, "writeChannels", 5);
            Set(Get(dynamics, "pressureSize"), "enabled", true);
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Packages/com.dcfapixels.whimtex/src/Editor/Icons" });
            t.True(paths.Length > 0, "Existing read-only texture fixture");
            var tip = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(paths[0]));
            Call(paint, "SetBrushTip", tip);
            Set(Get(first, "textToolSettings"), "fontSize", 117f); Set(Get(first, "textToolSettings"), "fontFamily", "F26 unavailable family");
            Set(Get(first, "textToolSettings"), "fontStyle", 3); Set(Get(first, "textToolSettings"), "justify", true);
            Set(Get(first, "shapeToolSettings"), "kind", 3); Set(Get(first, "shapeToolSettings"), "stroke", true);
            Set(Get(first, "shapeToolSettings"), "strokeWidth", 13f);
            var creation = Get(first, "gradientCreationSettings"); Set(creation, "type", 4);
            ((WhimTexGradient)Get(creation, "gradient")).SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(new Color(2, .3f, .5f), 1) },
                new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.8f, 1) });
            Set(first, "lassoMode", 1); Set(first, "lassoCurveMode", 1); Set(first, "selectionEdgeMode", 1);
            Set(first, "marqueeShape", 1); Set(first, "areaSelectionMode", 2); Set(first, "canvasCropMode", 0); Set(first, "canvasCropAutoPixelScale", false);
            Set(first, "vectorBrushSize", 67f); Set(first, "vectorBrushShape", 2); Set(first, "vectorBrushTolerance", .31f);
            Set(first, "vectorFillColor", new Color(.3f, .5f, 1.7f, .6f)); Set(first, "canvasGuidesSnap", false); Set(first, "uvLineOpacity", .38f);
            Set(first, "canvasGuidesHidden", true); Set(first, "canvasGuidesLocked", true);
            Set(first, "uvLineColor", new Color(.2f, .7f, .9f, .6f)); Set(first, "brushStrokePreviewScale", .5f);
            Set(first, "selectedBrushPreset", prefix + "preset.sebrush"); Set(first, "selectedBrushPresetSnapshot", "F26-snapshot");
            Set(first, "commentFilter", 4);
            Set(first, "canvasTransformReturnTool", 23);
            Set(first, "lastBaseCanvasTool", 17); Set(first, "canvasTool", 14); // A temporary FX tool must not become the startup tool.
            Set(first, "selectedLayerId", "F26-transient-selection");
            Call(first, "SaveToolPreferences"); t.True((bool)Get(Get(first, "toolPreferences"), "queued"), "Window changes queue the shared mechanism");
            string gradient = JsonUtility.ToJson(Get(creation, "gradient"));
            var expectedSections = Snapshot(first);
            // OnDisable must flush even when the scheduled editor callback has not run.
            UnityEngine.Object.DestroyImmediate(first); windows.Remove(first);
            t.True(read("paint").Contains("brushTipGuid") && !read("paint").Contains("instanceID"), "Stored brush references contain no session object identity");
            foreach (var key in keys) t.True(!EditorPrefs.GetString(key).Contains("F26-transient-selection"), "Selection/draft/document state excluded");
            var reopened = Window();
            var restoredSections = Snapshot(reopened);
            t.Equal(expectedSections.Count, restoredSections.Count, "Every registered tool section is restored");
            foreach (var section in expectedSections)
                t.Equal(section.Value, restoredSections[section.Key], "Complete section roundtrip: " + section.Key);
            paint = Get(reopened, "paintSettings");
            t.Equal(173f, (float)Get(paint, "brushSize"), "Brush restored after destroy/recreate");
            t.Equal(9, (int)Get(paint, "pencilSize"), "Pencil restored"); t.Near(.29, (float)Get(paint, "blurFlow"), .00001, "Blur restored");
            t.Near(.41, (float)Get(paint, "smudgeMixing"), .00001, "Smudge restored"); t.Equal(101, (int)Get(paint, "healingSearch"), "Healing restored");
            t.True(!(bool)Get(paint, "fillContiguous"), "Fill restored"); t.Equal(5, (int)Get(Get(paint, "dynamics"), "writeChannels"), "Write channels restored");
            t.True((bool)Get(Get(Get(paint, "dynamics"), "pressureSize"), "enabled"), "Pressure binding restored");
            Call(reopened, "RestoreBrushTipAfterReload"); t.True(tip == (Texture2D)Get(Get(paint, "dynamics"), "tip"), "GUID/local-ID asset restored without import or mutation");
            t.Equal(117f, (float)Get(Get(reopened, "textToolSettings"), "fontSize"), "Text restored");
            t.Equal("F26 unavailable family", (string)Get(Get(reopened, "textToolSettings"), "fontFamily"), "Unavailable font identity retained");
            t.Equal(13f, (float)Get(Get(reopened, "shapeToolSettings"), "strokeWidth"), "Shape restored");
            t.Equal(gradient, JsonUtility.ToJson(Get(Get(reopened, "gradientCreationSettings"), "gradient")), "Gradient keys/HDR/alpha restored");
            foreach (var spec in new[] { ("lassoMode", 1), ("lassoCurveMode", 1), ("selectionEdgeMode", 1), ("marqueeShape", 1), ("areaSelectionMode", 2), ("vectorBrushShape", 2), ("canvasCropMode", 0) })
                t.Equal(spec.Item2, Convert.ToInt32(Get(reopened, spec.Item1)), spec.Item1 + " restored");
            t.Equal(67f, (float)Get(reopened, "vectorBrushSize"), "Vector brush restored");
            t.Equal("AwaitingReview", Get(reopened, "commentFilter").ToString(), "Comment tool filter restored");
            t.Equal(new Color(.3f, .5f, 1.7f, .6f), (Color)Get(reopened, "vectorFillColor"), "Signed/HDR color structure restored");
            t.Near(.31, (float)Get(reopened, "vectorBrushTolerance"), .00001, "Vector precision restored");
            t.Equal("Text", Get(reopened, "canvasTool").ToString(), "Only basic startup tool is restored");
            t.True(!(bool)Get(reopened, "canvasCropAutoPixelScale") && !(bool)Get(reopened, "canvasGuidesSnap"), "Crop/guide preferences restored");
            t.Near(.38, (float)Get(reopened, "uvLineOpacity"), .00001, "UV appearance restored without document mesh");
            t.True(Get(reopened, "vectorBrushEditingDocument") == null && Get(reopened, "areaSelectionOwner") == null, "No active editing session restored");

            // Exercise the production size/keyboard path without showing or focusing UI.
            Call(reopened, "SetVectorBrushSize", 89f);
            t.True((bool)Get(Get(reopened, "toolPreferences"), "queued"), "Size/keyboard route queues common persistence");
            Flush(reopened); t.Equal(89f, (float)Get(reopened, "vectorBrushSize"), "Size path commits before common save");
            var document = (WhimTexDocument)Get(reopened, "activeDocument"); document.width = 71;
            var session = Get(reopened, "toolPreferences"); Call(session, "Reset");
            t.True(ReferenceEquals(document, Get(reopened, "activeDocument")) && document.width == 71, "Settings reset preserves the document and its dimensions");
            t.Equal(24f, (float)Get(reopened, "vectorBrushSize"), "Reset restores vector defaults");
            t.Equal(64f, (float)Get(Get(reopened, "textToolSettings"), "fontSize"), "Reset restores text defaults");
            t.Equal("Resize", Get(reopened, "canvasCropMode").ToString(), "Reset restores crop defaults");
            // Corruption is isolated to one section; unrelated settings remain usable.
            write("text", "["); write("vectorSize", "{\"value\":-900}"); write("vectorShape", "{\"value\":1e80}");
            var damaged = Window(); t.Equal(64f, (float)Get(Get(damaged, "textToolSettings"), "fontSize"), "Malformed section uses defaults");
            t.Equal(1f, (float)Get(damaged, "vectorBrushSize"), "Out-of-range input is normalized");
            t.Equal("Circle", Get(damaged, "vectorBrushShape").ToString(), "Overflowing enum falls back independently");
            t.Equal(13f, (float)Get(Get(damaged, "shapeToolSettings"), "strokeWidth"), "Bad section does not discard good sections");
            // An unavailable tip is a missing reference, not permission to discard settings.
            string previousGuid = (string)Get(Get(damaged, "paintSettings"), "brushTipGuid");
            string unavailable = read("paint").Replace("\"brushTipGuid\":\"" + previousGuid + "\"",
                "\"brushTipGuid\":\"00000000000000000000000000000000\"");
            write("paint", unavailable);
            var missing = Window(); var missingPaint = Get(missing, "paintSettings");
            t.True(!(bool)Call(missingPaint, "TryRestoreBrushTip"), "Missing asset falls back without throwing");
            t.Equal("00000000000000000000000000000000", (string)Get(missingPaint, "brushTipGuid"), "Missing asset identity retained");
            Set(missingPaint, "brushSize", 181f); Flush(missing);
            t.True(read("paint").Contains("00000000000000000000000000000000"), "Editing another option retains missing reference");
            // Missing external inputs retain their source identity without creating files.
            string absentPreset = System.IO.Path.GetFullPath("Temp/WhimTex/" + prefix + "missing.sebrush");
            t.True(!System.IO.File.Exists(absentPreset), "Preset probe owns an absent unique path");
            Set(missingPaint, "brushTipGuid", ""); Set(missingPaint, "brushTipPresetPath", absentPreset); Flush(missing);
            var absent = Window(); var absentPaint = Get(absent, "paintSettings");
            t.True(!(bool)Call(absentPaint, "TryRestoreBrushTip"), "Missing preset safely falls back");
            t.Equal(absentPreset, (string)Get(absentPaint, "brushTipPresetPath"), "Missing preset path survives reopening");
            string absentCacheId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            string absentCachePath = (string)absentPaint.GetType().GetMethod("ClipboardTipPath", F).Invoke(null, new object[] { absentCacheId });
            t.True(!System.IO.File.Exists(absentCachePath), "Clipboard probe owns an absent unique cache ID");
            Set(absentPaint, "brushTipPresetPath", ""); Set(absentPaint, "clipboardTipId", absentCacheId); Flush(absent);
            var cached = Window(); var cachedPaint = Get(cached, "paintSettings");
            t.True(!(bool)Call(cachedPaint, "TryRestoreBrushTip"), "Missing clipboard cache safely falls back");
            t.Equal(absentCacheId, (string)Get(cachedPaint, "clipboardTipId"), "Missing cache identity survives reopening");
            t.Equal(181f, (float)Get(cachedPaint, "brushSize"), "Missing external inputs retain independent brush settings");
            t.True(!System.IO.File.Exists(absentPreset) && !System.IO.File.Exists(absentCachePath), "Fallback never creates missing files");
            Set(missing, "lastBaseCanvasTool", 1); Set(missing, "canvasTool", 1); Flush(missing);
            // Reopened still owns its Text tool; focus sync must not terminate its editor.
            Call(Get(damaged, "toolPreferences"), "Refresh");
            t.Equal("Text", Get(damaged, "canvasTool").ToString(), "Another window cannot switch the active tool on focus");
            var newDefault = Window(); t.Equal("Brush", Get(newDefault, "canvasTool").ToString(), "New window uses latest basic-tool preference");
        }
        finally
        {
            for (int i = windows.Count - 1; i >= 0; i--) if (windows[i] != null) UnityEngine.Object.DestroyImmediate(windows[i]);
            foreach (var session in sessions) session.Dispose();
            foreach (string key in keys) { if (!key.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Foreign preference key."); EditorPrefs.DeleteKey(key); }
        }
    });
}
