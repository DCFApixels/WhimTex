// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// run_script UserSettingsCleanupTests.Run. Restores every touched preference/cache and owns only
// transient windows and a unique Temp directory; never opens or edits user documents/presets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static class UserSettingsCleanupTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("UserSettingsCleanupTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Settings = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings", true);
    static int checks;
    sealed class Preference
    {
        internal string key, kind;
        internal bool exists;
        internal object value;
        internal Preference(string key, string kind)
        {
            this.key = key; this.kind = kind; exists = EditorPrefs.HasKey(key);
            value = kind == "int" ? (object)EditorPrefs.GetInt(key) : kind == "bool" ? EditorPrefs.GetBool(key) :
                kind == "float" ? EditorPrefs.GetFloat(key) : EditorPrefs.GetString(key);
        }
        internal void Restore()
        {
            if (!exists) EditorPrefs.DeleteKey(key);
            else if (kind == "int") EditorPrefs.SetInt(key, (int)value);
            else if (kind == "bool") EditorPrefs.SetBool(key, (bool)value);
            else if (kind == "float") EditorPrefs.SetFloat(key, (float)value);
            else EditorPrefs.SetString(key, (string)value);
        }
    }
    static void Check(bool ok, string message) { context.True(ok, message); }
    static string Key(Type type, string field) => (string)type.GetField(field, F).GetRawConstantValue();
    static object Read(object obj, string field) => obj.GetType().GetField(field, F).GetValue(obj);
    static object Call(object obj, string method, params object[] args) =>
        (obj as Type ?? obj.GetType()).GetMethod(method, F).Invoke(obj is Type ? null : obj, args);
    static string Folder() => (string)Settings.GetProperty("PresetsFolder", F).GetValue(null);
    private static void ExecuteRun()
    {
        checks = 0;
        var preferences = new List<Preference>();
        var windows = new List<WhimTexWindow>();
        var caches = new Dictionary<FieldInfo, object>();
        void Remember(string key, string kind) => preferences.Add(new Preference(key, kind));
        var appearance = new[] {
            ("LightKey", "CheckerLight", "string"), ("DarkKey", "CheckerDark", "string"),
            ("ErrorKey", "InvalidPixels", "string"), ("SizeKey", "CheckerSize", "int"),
            ("ShowMantaKey", "ShowManta", "bool"), ("PostFxBackgroundKey", "PostFxBackground", "string"),
            ("PostFxBackgroundModeKey", "PostFxBackgroundMode", "int")
        };
        string root = fixture.TempFolder();
        string folderKey = Key(Settings, "PresetsFolderKey");
        var windowType = typeof(WhimTexWindow);
        string toolKey = Key(windowType, "CanvasToolPrefKey");
        string returnKey = Key(windowType, "CanvasTransformReturnToolPrefKey");
        string scaleKey = Key(windowType, "PaintingCanvasScalePrefKey");
        string paintKey = Key(windowType, "PaintToolSettingsPrefKey");
        const string oldTool = "DCFApixels.WhimTex.PreviewTool";
        const string oldReturn = "DCFApixels.WhimTex.PreviewTransformReturnTool";
        const string oldScale = "DCFApixels.WhimTex.PaintingPreviewScale";
        const string oldPaint = "DCFApixels.WhimTex.PaintToolSettings";
        foreach (var spec in appearance)
        {
            Remember(Key(Settings, spec.Item1), spec.Item3);
            Remember("DCFApixels.WhimTex.Preview." + spec.Item2, spec.Item3);
        }
        foreach (string key in new[] { toolKey, returnKey, paintKey, oldTool, oldReturn, oldPaint, folderKey,
            Key(Settings, "HealingStrokeColorKey") }) Remember(key, "string");
        Remember(scaleKey, "float"); Remember(oldScale, "float");
        foreach (string name in new[] { "checkerSize", "checkerLight", "checkerDark", "invalidPixels",
            "postFxBackground", "postFxBackgroundMode", "showManta", "healingStrokeColor" })
        {
            var field = Settings.GetField(name, F);
            caches[field] = field.GetValue(null);
        }
        try
        {
            foreach (var spec in appearance)
            {
                string key = Key(Settings, spec.Item1);
                string old = "DCFApixels.WhimTex.Preview." + spec.Item2;
                Check(key == "DCFApixels.WhimTex.CanvasView." + spec.Item2, "Canvas View key: " + spec.Item2);
                if (spec.Item3 == "int") EditorPrefs.SetInt(old, 73);
                else if (spec.Item3 == "bool") EditorPrefs.SetBool(old, false);
                else EditorPrefs.SetString(old, "#112233");
            }
            Check(toolKey == "DCFApixels.WhimTex.Canvas.Tool" &&
                returnKey == "DCFApixels.WhimTex.Canvas.TransformReturnTool" &&
                scaleKey == "DCFApixels.WhimTex.Canvas.PaintingScale" &&
                paintKey == "DCFApixels.WhimTex.Canvas.PaintToolSettings", "Canonical Canvas keys");
            Call(Settings, "ResetCanvasViewAppearance");
            Check((int)Settings.GetProperty("CheckerSize", F).GetValue(null) == 16 &&
                (bool)Settings.GetProperty("ShowManta", F).GetValue(null), "Old appearance values are not restored");
            Settings.GetProperty("CheckerSize", F).SetValue(null, 29);
            Check(EditorPrefs.GetInt(Key(Settings, "SizeKey")) == 29, "Size uses current key");
            Settings.GetProperty("CheckerLight", F).SetValue(null, new Color(.2f, .4f, .6f));
            Check(EditorPrefs.GetString(Key(Settings, "LightKey")) == "#336699", "Color uses current key");
            Settings.GetProperty("ShowManta", F).SetValue(null, false);
            Check(EditorPrefs.HasKey(Key(Settings, "ShowMantaKey")) && !EditorPrefs.GetBool(Key(Settings, "ShowMantaKey")),
                "Background visibility uses current key");
            Call(Settings, "ResetCanvasViewAppearance");
            foreach (var spec in appearance)
            {
                Check(!EditorPrefs.HasKey(Key(Settings, spec.Item1)), "Reset removes current key: " + spec.Item2);
                string old = "DCFApixels.WhimTex.Preview." + spec.Item2;
                Check(spec.Item3 == "int" ? EditorPrefs.GetInt(old) == 73 :
                    spec.Item3 == "bool" ? !EditorPrefs.GetBool(old) : EditorPrefs.GetString(old) == "#112233",
                    "Historical settings not migrated or rewritten: " + spec.Item2);
            }
            EditorPrefs.SetString(oldTool, "Brush"); EditorPrefs.SetString(oldReturn, "Pencil");
            EditorPrefs.SetFloat(oldScale, .125f);
            EditorPrefs.SetString(oldPaint, "{\"brushSize\":913,\"blurOpacity\":0.13}");
            foreach (string key in new[] { toolKey, returnKey, scaleKey, paintKey }) EditorPrefs.DeleteKey(key);
            var window = ScriptableObject.CreateInstance<WhimTexWindow>(); windows.Add(window);
            Check(Read(window, "canvasTool").ToString() == "None" &&
                Read(window, "canvasTransformReturnTool").ToString() == "None", "Old tool selections ignored");
            Check((float)Read(window, "paintingCanvasScale") == 1f, "Old quality setting ignored");
            Check((float)Read(Read(window, "paintSettings"), "brushSize") == 32f, "Old paint settings ignored");
            EditorPrefs.SetString(toolKey, "Brush"); EditorPrefs.SetString(returnKey, "Pencil");
            EditorPrefs.SetFloat(scaleKey, .25f);
            EditorPrefs.SetString(paintKey, "{\"brushSize\":57,\"blurFlow\":0.23}");
            var restored = ScriptableObject.CreateInstance<WhimTexWindow>(); windows.Add(restored);
            Check(Read(restored, "canvasTool").ToString() == "Brush" &&
                Read(restored, "canvasTransformReturnTool").ToString() == "Pencil", "Current tools reload");
            Check((float)Read(restored, "paintingCanvasScale") == .25f, "Current quality reloads");
            var paint = Read(restored, "paintSettings");
            Check((float)Read(paint, "brushSize") == 57f && (float)Read(paint, "blurFlow") == .23f,
                "Current paint settings reload");
            Call(restored, "SavePaintToolSettings");
            string saved = EditorPrefs.GetString(paintKey);
            Check(saved.Contains("\"blurFlow\"") && !saved.Contains("\"blurOpacity\""), "Only current paint fields are written");
            foreach (string source in new[] { "{\"blurOpacity\":0.13}", "[" })
            {
                EditorPrefs.SetString(paintKey, source);
                windowType.GetField("paintSettings", F).SetValue(restored, null);
                Call(restored, "LoadPaintToolSettings");
                Check((float)Read(Read(restored, "paintSettings"), "blurFlow") == 1f,
                    "No blurOpacity migration; malformed settings safely fall back");
            }
            Directory.CreateDirectory(root);
            const string baseline = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Compatibility0125/";
            string brush = Path.Combine(root, "brush.sebrush");
            File.Copy(baseline + "brush.sebrush", brush);
            byte[] bytes = File.ReadAllBytes(brush);
            object[] args = { root, null };
            Check((bool)Settings.GetMethod("TrySetPresetsFolder", F).Invoke(null, args) && args[1] == null && Folder() == root,
                "Explicit library folder remains supported");
            Call(Settings, "ResetCanvasViewAppearance");
            Check(Folder() == root, "Appearance reset does not reset library folder");
            string canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DCFApixels", "WhimTex", "Presets");
            bool existed = Directory.Exists(canonical);
            Call(Settings, "ResetPresetsFolder");
            Check(Folder() == canonical && Directory.Exists(canonical) == existed, "Default path is canonical and not created");
            Check(Convert.ToBase64String(File.ReadAllBytes(brush)) == Convert.ToBase64String(bytes),
                "Reset does not move/delete/rewrite 0.12.5 preset files");
            string missing = Path.Combine(root, "NotCreated");
            args = new object[] { missing, null };
            Check((bool)Settings.GetMethod("TrySetPresetsFolder", F).Invoke(null, args) && !Directory.Exists(missing),
                "Selecting missing absolute directory does not create it");
            args = new object[] { brush, null };
            Check(!(bool)Settings.GetMethod("TrySetPresetsFolder", F).Invoke(null, args) && Folder() == missing,
                "File path rejected without replacing existing setting");
            args = new object[] { "relative-folder", null };
            Check(!(bool)Settings.GetMethod("TrySetPresetsFolder", F).Invoke(null, args) && Folder() == missing,
                "Relative path rejected without replacing existing setting");
            return;
        }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        finally
        {
            foreach (var window in windows) if (window != null) Object.DestroyImmediate(window);
            foreach (var item in caches) item.Key.SetValue(null, item.Value);
            for (int i = preferences.Count - 1; i >= 0; i--) preferences[i].Restore();
            (Settings.GetField("Changed", F).GetValue(null) as Action)?.Invoke();
            // The outer fixture validates and deletes only this invocation's GUID folder.
        }
    }
}
