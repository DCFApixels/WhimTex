using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    public static string RunQuotaRelease() => TestContext.Run("Raster quota-release failure keeps signed masked HDR history usable", test =>
    {
        using var disk = new PaintTileHistoryTests.DiskScope();
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 256;
        var drawing = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var mask = new Texture2D(32, 32, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
        Object cursor = null;
        Texture2D original = null;
        string fault = System.IO.Path.Combine((string)Get(disk.disk, "Root"), "session.json.tmp");
        try
        {
            var colors = new Color32[32 * 32]; Array.Fill(colors, new Color32(128, 0, 0, 255));
            mask.SetPixels32(colors); mask.Apply(false, false);
            document.layers.Add(drawing); Call(document, "NormalizeModel");
            Call(drawing, "InitializeCanvas", 256, 256);
            original = (Texture2D)Get(drawing, "StoredTexture");
            var originalBytes = original.GetRawTextureData<byte>().ToArray();
            var region = new RectInt(-256, -256, 768, 768);
            var baseline = Probe(drawing, region);
            var baselineTiles = StorageBytes(drawing);

            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            var settings = Settings(new Color(-.5f, 8f, .75f, 1f));
            object parameters = Call(Call(settings, "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", mask);
            Call(drawing, "PrepareBufferedStroke", 256, 256, "Owned Raster quota failure");
            Call(drawing, "BeginStroke", new Vector2(-.05f, .4f));
            Call(drawing, "PaintPoint", new Vector2(-.05f, .4f), 256, 256, parameters);
            Call(drawing, "EndStroke");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            cursor = (Object)Get(drawing, "PixelUndoObject");
            string revision = (string)Get(cursor, "Revision");
            var step = Static("PaintHistoryStore", "Get", revision);
            string reservation = System.IO.Path.Combine((string)Static("PaintHistoryStore", "StepDirectory", revision), "reservation");
            test.True((bool)Get(step, "sparse") && System.IO.File.Exists(reservation), "Production Raster reserved sparse history before readback");
            long reservedUsage = (long)Call(disk.disk, "Usage");
            System.IO.Directory.CreateDirectory(fault);
            byte[] painted;
            Dictionary<Vector2Int, byte[]> paintedTiles;
            try
            {
                Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
                var pages = (Array)Get(step, "payload");
                test.True(pages.Length > 0, "Raster published real page data");
                foreach (object page in pages)
                {
                    var task = (System.Threading.Tasks.Task)Get(page, "write");
                    test.True(task != null && task.IsCompletedSuccessfully && Get(page, "native") == null,
                        "Quota metadata failure leaves successful Raster writers and releases native payload");
                    test.True(System.IO.File.Exists((string)Get(page, "path")), "Raster pixel page remains available for replay");
                }
                test.Equal(0L, (long)Product("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null), "No Raster writer capacity remains pinned");
                test.True(!(bool)Get(step, "reservationReleased") && System.IO.File.Exists(reservation), "Raster failed quota release remains retryable");
                test.True((long)Call(disk.disk, "Usage") >= reservedUsage, "Raster payload remains conservatively charged");
                paintedTiles = StorageBytes(drawing); painted = Probe(drawing, region);
                bool negative = false, hdr = false;
                foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
                {
                    negative |= ((Vector2Int)Get(tile, "key")).x < 0;
                    foreach (var value in ((Texture2D)Get(tile, "texture")).GetRawTextureData<Unity.Mathematics.half4>())
                    { Unity.Mathematics.float4 pixel = value; hdr |= pixel.x < 0f && pixel.y > 1f; }
                }
                test.True(negative && hdr && !EqualBytes(baseline, painted), "Partial-mask stroke produces actual signed-coordinate HDR pixels");
                Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo");
                ExactTiles(test, baselineTiles, StorageBytes(drawing), "Raster quota-fault Undo");
                Exact(test, baseline, Probe(drawing, region), "Raster quota-fault GPU Undo");
                Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo");
                ExactTiles(test, paintedTiles, StorageBytes(drawing), "Raster quota-fault Redo");
                Exact(test, painted, Probe(drawing, region), "Raster quota-fault GPU Redo");
            }
            finally { if (System.IO.Directory.Exists(fault)) System.IO.Directory.Delete(fault); }

            for (int i = 0; i < 16 && !(bool)Get(step, "reservationReleased"); i++) Static("PaintHistoryStore", "Maintenance", true);
            test.True((bool)Get(step, "reservationReleased") && !System.IO.File.Exists(reservation), "Maintenance releases the recovered Raster reservation");
            test.True((long)Call(disk.disk, "Usage") < reservedUsage, "Recovered Raster completion releases unused quota");
            Stroke(drawing, new Vector2(1.03f, .7f), new Color(3f, -.4f, 2f, .8f));
            Call(drawing, "FlushTiledStorage"); Static("PaintHistoryStore", "FlushWrites");
            test.True((string)Get(cursor, "Revision") != revision, "Raster accepts another stroke after metadata recovery");
            var finalTiles = StorageBytes(drawing); var final = Probe(drawing, region);
            Undo.PerformUndo(); Call(drawing, "ReconcilePixelUndo");
            ExactTiles(test, paintedTiles, StorageBytes(drawing), "Next Raster stroke Undo");
            Exact(test, painted, Probe(drawing, region), "Next Raster stroke GPU Undo");
            Undo.PerformRedo(); Call(drawing, "ReconcilePixelUndo");
            ExactTiles(test, finalTiles, StorageBytes(drawing), "Next Raster stroke Redo");
            Exact(test, final, Probe(drawing, region), "Next Raster stroke GPU Redo");
            Exact(test, originalBytes, original.GetRawTextureData<byte>().ToArray(), "Raster quota failure keeps the original anchor immutable");
        }
        finally
        {
            if (System.IO.Directory.Exists(fault)) System.IO.Directory.Delete(fault);
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            if (original != null) Undo.ClearUndo(original);
            Undo.ClearUndo(document); Call(drawing, "ReleaseTransientResources"); document.layers.Clear();
            Object.DestroyImmediate(document); Object.DestroyImmediate(mask);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });
}
