using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using DCFApixels.WhimTex;

// No menus/windows are shown. Files belong only to the fixture's Temp/WhimTex GUID folder.
public static class ShaderFXCatalogPerformanceTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
    static string timings;
    static object Call(string name, params object[] args) => Catalog.GetMethod(name, Flags).Invoke(null, args);
    static object Field(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);

    public static string Run()
    {
        timings = "";
        string json = TestContext.Run("ShaderFXCatalogPerformanceTests.Run", context =>
        {
            using var fixture = new MigrationD();
            try { Check(context, fixture); }
            finally { Call("GetEntries"); }
        });
        var result = JsonUtility.FromJson<TestResult>(json);
        result.message += " " + timings;
        return result.ToJson();
    }

    static void CompleteScan(TestContext context)
    {
        var task = (Task)Catalog.GetField("pendingScan", Flags).GetValue(null);
        context.True(task != null, "Background header task started");
        context.True(task.Wait(10000), "Header scan completed within test timeout");
        Call("FinishRefresh");
    }

    static void Check(TestContext context, MigrationD fixture)
    {
        int effects = Resources.FindObjectsOfTypeAll<ShaderFX>().Length;
        int shaders = Resources.FindObjectsOfTypeAll<Shader>().Length;
        var first = (IList)Call("GetEntries");
        // Drain a startup scan without showing UI or running unrelated Editor callbacks.
        if (Catalog.GetField("pendingScan", Flags).GetValue(null) != null) CompleteScan(context);
        first = (IList)Call("GetEntries");
        context.True(first.Count > 0, "FX presets discovered");
        foreach (object entry in first)
        {
            var type = entry.GetType();
            var hasError = type.GetProperty("HasError", Flags);
            bool before = (bool)hasError.GetValue(entry);
            object restored = JsonUtility.FromJson(JsonUtility.ToJson(entry), type);
            context.Equal(before, (bool)hasError.GetValue(restored), "Session header availability");
            var error = type.GetField("error", Flags);
            error.SetValue(restored, "");
            context.True(!(bool)hasError.GetValue(restored), "Empty error keeps preset enabled");
            error.SetValue(restored, "Invalid header");
            context.True((bool)hasError.GetValue(restored), "Invalid header disables preset");
        }

        Call("DomainReloaded");
        var watch = Stopwatch.StartNew();
        var menu = (UnityEditor.GenericMenu)Call("BuildMenu", new object[] { null });
        double firstMenu = watch.Elapsed.TotalMilliseconds;
        context.True(menu.GetItemCount() >= first.Count, "Menu contains every discovered preset");
        context.True(ReferenceEquals(first, Call("GetMenuEntries")), "Reload retains the ready snapshot without synchronous scanning");
        context.True((bool)Catalog.GetField("refreshProject", Flags).GetValue(null), "Menu leaves reconciliation to background work");
        watch.Restart();
        for (int i = 0; i < 100; i++) Call("BuildMenu", new object[] { null });
        double warmMenu = watch.Elapsed.TotalMilliseconds / 100;

        // Verify the real startup/update path, including rejection of superseded snapshots.
        watch.Restart();
        Call("BeginRefresh");
        double prepare = watch.Elapsed.TotalMilliseconds;
        Call("UserPresetsChanged");
        CompleteScan(context);
        context.True(ReferenceEquals(first, Call("GetMenuEntries")), "Invalidated background result cannot overwrite current headers");
        Call("BeginRefresh");
        watch.Restart();
        CompleteScan(context);
        double complete = watch.Elapsed.TotalMilliseconds;
        context.True(!(bool)Catalog.GetField("refreshProject", Flags).GetValue(null), "Background reconciliation completed");
        var next = (IList)Call("GetMenuEntries");
        context.Equal(first.Count, next.Count, "Same presets after background reconciliation");
        for (int i = 0; i < first.Count; i++) context.True(ReferenceEquals(first[i], next[i]), "Unchanged entry reused");

        Call("BeginRefresh");
        // A source notification invalidates pending headers before the deferred import handler runs.
        Call("AssetsChanged", new object[] { new[] { new[] { "Temp/WhimTex/" + Guid.NewGuid().ToString("N") + ".hlsl" } } });
        CompleteScan(context);
        context.True(ReferenceEquals(next, Call("GetMenuEntries")), "Import notification rejects an in-flight snapshot immediately");
        Call("FlushChanges");

        string folder = fixture.TempFolder();
        string marked = Path.Combine(folder, "Marked.hlsl");
        string unmarked = Path.Combine(folder, "Include.hlsl");
        string invalid = Path.Combine(folder, "Invalid.hlsl");
        File.WriteAllText(marked, "// @whimtex-effect Test/Original\n#error Discovery must not compile this body\n// @param BROKEN");
        File.WriteAllText(unmarked, "// no effect marker\n// @whimtex-effect Wrong/Later\n");
        File.WriteAllText(invalid, "// @whimtex-effect Test//EmptySegment\n");
        IDictionary ReadUserHeaders()
        {
            var read = (Delegate)Call("PrepareScan", folder);
            // Exercise the file-only stage on a real worker, including the user folder iterator.
            object scan = Task.Run(() => read.DynamicInvoke()).GetAwaiter().GetResult();
            return (IDictionary)Field(scan, "entries");
        }
        var headers = ReadUserHeaders();
        context.True(headers.Contains(marked) && !headers.Contains(unmarked), "Only first-line marked files enter catalog");
        context.Equal("Test/Original", (string)Field(headers[marked], "menuPath"), "Category read without parsing invalid source body");
        context.True(string.IsNullOrEmpty((string)Field(headers[marked], "error")), "Invalid body is left for selection-time compilation");
        context.True(!string.IsNullOrEmpty((string)Field(headers[invalid], "error")), "Header validation retained");
        File.WriteAllText(marked, "// @whimtex-effect Test/Renamed\n");
        headers = ReadUserHeaders();
        context.Equal("Test/Renamed", (string)Field(headers[marked], "menuPath"), "External header edits detected");
        File.Move(marked, Path.Combine(folder, "Moved.hlsl"));
        File.Delete(invalid);
        headers = ReadUserHeaders();
        context.True(!headers.Contains(marked) && !headers.Contains(invalid) && headers.Contains(Path.Combine(folder, "Moved.hlsl")), "External moves/deletions detected");

        // Simulate a fresh Editor with no session headers. The first menu must still never scan/wait.
        ((IDictionary)Catalog.GetField("entries", Flags).GetValue(null)).Clear();
        Catalog.GetField("sortedEntries", Flags).SetValue(null, Array.CreateInstance(Catalog.GetNestedType("Entry", BindingFlags.NonPublic), 0));
        Catalog.GetField("initialized", Flags).SetValue(null, false);
        context.Equal(0, ((IList)Call("GetMenuEntries")).Count, "Cold menu returns immediately without scanning");
        menu = (UnityEditor.GenericMenu)Call("BuildMenu", new object[] { null });
        context.True(menu.GetItemCount() > 0, "Cold menu contains loading/folder actions");
        Call("BeginRefresh");
        CompleteScan(context);
        context.Equal(first.Count, ((IList)Call("GetMenuEntries")).Count, "Cold background scan publishes the complete catalog");

        context.Equal(effects, Resources.FindObjectsOfTypeAll<ShaderFX>().Length, "Discovery creates no FX objects");
        context.Equal(shaders, Resources.FindObjectsOfTypeAll<Shader>().Length, "Discovery creates no shaders");
        timings = $"Presets={first.Count}, first menu after reload={firstMenu:F3} ms, repeat menu={warmMenu:F3} ms, background main-thread preparation={prepare:F3} ms, worker wait + publication={complete:F3} ms";
    }
}
