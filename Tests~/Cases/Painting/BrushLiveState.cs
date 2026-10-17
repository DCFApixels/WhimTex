using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

// Read-only metadata: no rendering, pixel getters, input, setters or user-document writes.
public static class BrushLiveState
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static object Field(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o);
    static string Json(object o) => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { o });
    public static string Inspect() => TestContext.Run("Read current brush settings and document render metadata", t =>
    {
        t.True(AppDomain.CurrentDomain.GetData("WhimTex.Tests.BrushLiveTrace") == null, "No owned live trace remains");
        foreach (var window in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
            t.True(window.GetType().Name != "BrushLiveTraceWindow", "No diagnostic preview window remains");
        var rows = new List<object>();
        foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
        {
            var doc = Field(window, "activeDocument") as WhimTexDocument;
            if (doc == null) continue;
            var layers = new List<object>();
            void Add(Layer layer, int depth)
            {
                var drawing = layer.Behaviour as DrawingLayerBehaviour;
                var pixels = drawing == null ? null : Field(drawing, "pixels") as Texture2D;
                layers.Add(new { id = layer.Id, type = layer.Behaviour?.GetType().Name, depth,
                    layer.enabled, layer.opacity, blend = layer.blendMode.ToString(), layer.colorRange, fx = layer.fx?.Count ?? 0,
                    storedWidth = pixels == null ? 0 : pixels.width, storedHeight = pixels == null ? 0 : pixels.height });
                if (layer.children != null) foreach (var child in layer.children) Add(child, depth + 1);
            }
            foreach (var layer in doc.layers) Add(layer, 0);
            rows.Add(new { doc.width, doc.height, tool = Field(window, "canvasTool")?.ToString(),
                paintSettingsJson = JsonUtility.ToJson(Field(window, "paintSettings")),
                paintingCanvasScale = Field(window, "paintingCanvasScale"), painting = Field(window, "paintingLayer") != null,
                layers });
        }
        t.True(rows.Count > 0, "At least one open WhimTex document");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/brush-live-trace"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-current-state.json"), Json(rows));
    });
}
