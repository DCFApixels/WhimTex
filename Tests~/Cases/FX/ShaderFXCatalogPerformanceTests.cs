// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXCatalogPerformanceTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXCatalogPerformanceTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    private static void ExecuteMain()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
        var get = catalog.GetMethod("GetEntries", flags);
        int effects = Resources.FindObjectsOfTypeAll<ShaderFX>().Length;
        int shaders = Resources.FindObjectsOfTypeAll<Shader>().Length;
        var watch = Stopwatch.StartNew();
        var first = (IList)get.Invoke(null, null);
        double cold = watch.Elapsed.TotalMilliseconds;
        context.True(!(first.Count == 0), "No FX presets discovered.");
        foreach (object entry in first)
        {
            var type = entry.GetType();
            var hasError = type.GetProperty("HasError", flags);
            bool before = (bool)hasError.GetValue(entry);
            object restored = JsonUtility.FromJson(JsonUtility.ToJson(entry), type);
            context.True(!((bool)hasError.GetValue(restored) != before), "Serialization changed preset availability.");
            var error = type.GetField("error", flags);
            error.SetValue(restored, "");
            context.True(!((bool)hasError.GetValue(restored)), "Empty error disables preset.");
            error.SetValue(restored, "Invalid header");
            context.True(!(!(bool)hasError.GetValue(restored)), "Real errors must disable presets.");
        }
        watch.Restart();
        for (int i = 0; i < 10; i++)
        {
            var next = (IList)get.Invoke(null, null);
            context.True(!(next.Count != first.Count), "Catalog is unstable.");
            for (int n = 0; n < first.Count; n++)
                context.True(!(!ReferenceEquals(first[n], next[n])), "Unchanged entry reread.");
        }
        double warm = watch.Elapsed.TotalMilliseconds / 10;
        context.True(!(Resources.FindObjectsOfTypeAll<ShaderFX>().Length != effects || Resources.FindObjectsOfTypeAll<Shader>().Length != shaders), "Catalog discovery loaded ShaderFX or shader objects.");
        return;
    }
}

