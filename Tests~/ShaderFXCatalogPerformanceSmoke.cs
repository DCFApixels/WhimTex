using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXCatalogPerformanceSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
        var get = catalog.GetMethod("GetEntries", flags);
        int effects = Resources.FindObjectsOfTypeAll<ShaderFX>().Length;
        int shaders = Resources.FindObjectsOfTypeAll<Shader>().Length;
        var watch = Stopwatch.StartNew();
        var first = (IList)get.Invoke(null, null);
        double cold = watch.Elapsed.TotalMilliseconds;
        if (first.Count == 0) throw new Exception("No FX presets discovered.");
        foreach (object entry in first)
        {
            var type = entry.GetType();
            var hasError = type.GetProperty("HasError", flags);
            bool before = (bool)hasError.GetValue(entry);
            object restored = JsonUtility.FromJson(JsonUtility.ToJson(entry), type);
            if ((bool)hasError.GetValue(restored) != before)
                throw new Exception("Serialization changed preset availability.");
            var error = type.GetField("error", flags);
            error.SetValue(restored, "");
            if ((bool)hasError.GetValue(restored)) throw new Exception("Empty error disables preset.");
            error.SetValue(restored, "Invalid header");
            if (!(bool)hasError.GetValue(restored)) throw new Exception("Real errors must disable presets.");
        }
        watch.Restart();
        for (int i = 0; i < 10; i++)
        {
            var next = (IList)get.Invoke(null, null);
            if (next.Count != first.Count) throw new Exception("Catalog is unstable.");
            for (int n = 0; n < first.Count; n++)
                if (!ReferenceEquals(first[n], next[n])) throw new Exception("Unchanged entry reread.");
        }
        double warm = watch.Elapsed.TotalMilliseconds / 10;
        if (Resources.FindObjectsOfTypeAll<ShaderFX>().Length != effects || Resources.FindObjectsOfTypeAll<Shader>().Length != shaders)
            throw new Exception("Catalog discovery loaded ShaderFX or shader objects.");
        return $"PASS: {first.Count} presets; first scan {cold:F2} ms; warm mean {warm:F2} ms; no FX/shader objects loaded; unchanged entries reused.";
    }
}
