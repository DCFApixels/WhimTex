using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class PaintTileHistoryTests
{
    public sealed class HistoryParameter : ScriptableObject { public int value; }
    public static string RunBoundary() => TestContext.Run("Retired pixel records leave parameter Undo and Redo intact", t =>
    {
        using var scope = new DiskScope(); var mask = Mask();
        var parameter = ScriptableObject.CreateInstance<HistoryParameter>(); parameter.hideFlags = HideFlags.HideAndDontSave;
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            Call(scope.disk, "SetLimits", (int?)8, (int?)2);
            using var f = new Fixture(513, true);
            void Param(int value)
            {
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(parameter, "Owned parameter " + value);
                parameter.value = value; EditorUtility.SetDirty(parameter); Undo.IncrementCurrentGroup();
            }
            void Stroke(float x) => f.Stroke(new[] { new Vector2(x, .5f) }, mask);
            var rect = new RectInt(0, 0, 513, 513);
            Param(1); Stroke(.2f); Param(2);
            int mixed = Undo.GetCurrentGroup(); Stroke(.4f); Param(3);
            Undo.CollapseUndoOperations(mixed); Undo.IncrementCurrentGroup();
            var boundary = Probe(f.surface, rect);
            Stroke(.6f); var afterC = Probe(f.surface, rect); Param(4);
            Stroke(.8f); var afterD = Probe(f.surface, rect);
            Static("PaintHistoryStore", "FlushWrites");
            var cursor = Get(f.layer, "PixelUndoObject");
            t.True((bool)Static("PaintHistoryStore", "RetireOldest", (string)Get(cursor, "DocumentIdentity"), true), "Start retirement of A");
            Call(cursor, "StartCheckpointPage");
            t.True((bool)Type("PaintHistoryStore").GetProperty("MaintenanceBusy", S).GetValue(null), "Background checkpoint is pending publication");
            Call(f.layer, "InvalidatePaintSurface");
            Exact(t, afterD, Probe((RenderTexture)Call(f.layer, "EnsurePaintSurface", 513, 513), rect), "Half rebuild during checkpoint preparation");
            Undo.PerformUndo(); Exact(t, afterC, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Undo while checkpoint is being prepared");
            Undo.PerformRedo(); Exact(t, afterD, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Redo while checkpoint is being prepared");
            SettleRetention(cursor, 2, out _, out _);
            Undo.PerformUndo(); Exact(t, afterC, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Undo D");
            t.Equal(4, parameter.value, "Pixel Undo does not skip parameter");
            Undo.PerformUndo(); t.Equal(3, parameter.value, "Undo parameter 4");
            Undo.PerformUndo(); Exact(t, boundary, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Undo C reaches boundary");
            Undo.PerformUndo(); t.Equal(2, parameter.value, "Mixed retired stroke group retains its parameter with one Undo");
            Exact(t, boundary, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Mixed group does not restore retired pixels");
            Undo.PerformUndo(); t.Equal(1, parameter.value, "Parameter before retired stroke remains one step");
            Undo.PerformUndo(); t.Equal(0, parameter.value, "No empty native stroke record before oldest parameter");
            Undo.PerformRedo(); t.Equal(1, parameter.value, "Redo parameter 1");
            Undo.PerformRedo(); t.Equal(2, parameter.value, "Redo parameter 2");
            Undo.PerformRedo(); t.Equal(3, parameter.value, "Redo mixed parameter");
            Undo.PerformRedo(); Exact(t, afterC, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Redo C");
            Undo.PerformRedo(); t.Equal(4, parameter.value, "Redo parameter 4");
            Undo.PerformRedo(); Exact(t, afterD, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Redo D");
            Call(cursor, "ClearHistory");
            for (int value = 3; value >= 0; value--) { Undo.PerformUndo(); t.Equal(value, parameter.value, "Clear removes pixel records only"); }
            Exact(t, afterD, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Parameter Undo after clear preserves current pixels");
        }
        finally { Undo.ClearUndo(parameter); Object.DestroyImmediate(parameter); Object.DestroyImmediate(mask); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });
    public static string RunMaintenanceBenchmark(int size, string label) => TestContext.Run("History threshold and limit reduction", t =>
    {
        using var scope = new DiskScope();
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite; var mask = Mask();
        try
        {
            Call(scope.disk, "SetLimits", (int?)8, (int?)50);
            using var f = new Fixture(size, false);
            var frame = new[] { new Vector2(.005f, .005f), new Vector2(.995f, .005f),
                new Vector2(.995f, .995f), new Vector2(.005f, .995f), new Vector2(.005f, .005f) };
            var report = new List<object>();
            var cursor = Get(f.layer, "PixelUndoObject");
            for (int i = 0; i < 51; i++)
            {
                f.Stroke(i < 4 ? frame : new[] { new Vector2(.45f + i * .0003f, .5f) }, mask);
                Static("PaintHistoryStore", "FlushWrites");
                cursor = Get(f.layer, "PixelUndoObject");
                double elapsed = SettleRetention(cursor, 50, out double maximumSlice, out int slices);
                report.Add(new { action = "stroke", index = i + 1, milliseconds = elapsed, maximumSlice, slices, peakStage = retentionPeakStage });
            }
            int Count() { int n = 0; foreach (var s in (IList)Static("PaintHistoryStore", "Snapshot"))
                if ((string)Get(s, "owner") == (string)Get(cursor, "Identity")) n++; return n; }
            t.Equal(50, Count(), "Threshold keeps 50 pixel operations");
            var clock = Stopwatch.StartNew(); Call(scope.disk, "SetLimits", (int?)null, (int?)5);
            report.Add(new { action = "set-limit", milliseconds = clock.Elapsed.TotalMilliseconds });
            double shrink = SettleRetention(cursor, 5, out double maxShrinkSlice, out int shrinkSlices);
            report.Add(new { action = "shrink", milliseconds = shrink, maximumSlice = maxShrinkSlice, slices = shrinkSlices, peakStage = retentionPeakStage });
            t.Equal(5, Count(), "Reduction keeps five pixel operations");
            t.True(Type("PaintHistoryStore").GetProperty("LastMaintenanceError", S).GetValue(null) == null, "No maintenance error");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-undo-maintenance"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, size + "-" + label + ".json"), Json(report));
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });
}
