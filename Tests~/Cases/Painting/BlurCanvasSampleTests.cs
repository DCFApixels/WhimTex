using System;
using System.IO;
using System.Linq;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class BlurRegionTests
{
    public static string RunCanvasSample(bool allLayers, bool projective) => TestContext.Run("Transformed Blur with a canvas snapshot", test =>
    {
        const int side = 256;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = side;
        WhimTexDocument loaded = null;
        RenderTexture snapshot = null;
        Texture2D selection = null;
        string path = Path.GetFullPath("Temp/WhimTex/test-runs/blur-canvas-" + Guid.NewGuid().ToString("N") + ".tiff");
        var lower = new Color(3.25f, -.25f, .75f, 1);
        var upper = new Color(5, -.5f, .25f, 1);
        try
        {
            var painted = new DrawingLayerBehaviour();
            var transform = TextureTransform.Default;
            if (projective)
                test.True(transform.TrySetMatrix(new ProjectiveMatrix
                    { m00 = .85, m01 = .10, m02 = .31, m10 = -.08, m11 = .9, m12 = .075, m20 = .08, m21 = -.05, m22 = 1 }), "Projective fixture");
            else transform.positionF = new Vector2(96, 0);
            AddBackdrop(upper);
            doc.layers.Add(new Layer(painted) { transform = transform, colorRange = LayerColorRange.HDR,
                blendRange = LayerBlendRange.HDR, filterMode = LayerFilterMode.Point });
            AddBackdrop(lower);
            Call(doc, "NormalizeModel");
            Call(painted, "InitializeCanvas", side, side);
            Call(doc, "ResetUndoTrackingAfterLoad");
            byte[] before = Preview(doc);

            var sourceCenter = new Vector2(.32f, .5f);
            var canvasCenter = transform.Map(sourceCenter, new Vector2(side, side)) * side;
            int x = Mathf.FloorToInt(canvasCenter.x), y = Mathf.FloorToInt(canvasCenter.y);
            test.True(x > side / 2 + 20, "Expected canvas sample is on the opposite side from its source UV");
            selection = new Texture2D(side, side, TextureFormat.RFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var selectionPixels = selection.GetRawTextureData<float>();
            for (int row = 0; row < side; row++)
                for (int col = 0; col < side; col++) selectionPixels[row * side + col] = row < y - 2 ? 0 : row > y + 2 ? 1 : .5f;
            selection.Apply(false, false);

            const string label = "Owned transformed Blur";
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RecordObject(doc, label);
            Call(painted, "PrepareBufferedStroke", side, side, label);
            Call(painted, "BeginStroke", sourceCenter);
            snapshot = (RenderTexture)(allLayers ? Call(doc, "RenderCanvasAtSize", side, side)
                : Call(doc, "RenderLayerAndBelow", painted.Owner, side, side));
            var target = RenderTexture.active;
            Call(painted, "BlurSegment", sourceCenter, sourceCenter, side, side, 44f, 1f, 1f, snapshot, false, selection);
            test.Equal(target, RenderTexture.active, "Canvas gathering restores the target binding");
            test.Equal(TextureWrapMode.Clamp, snapshot.wrapMode, "Canvas snapshot keeps its addressing");
            byte[] pending = Preview(doc);
            var expected = allLayers ? upper : lower;
            NearPixel(pending, x, y, expected, .5f);
            NearPixel(pending, x, y + 8, expected, 1);
            test.Equal(0f, Component(pending, x, y - 8, 3), "Unselected pixels remain transparent");

            Call(painted, "EndStroke");
            Call(painted, "FinishStrokeStorage");
            Call(doc, "MarkChanged");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
            ComparePreview(pending, doc, "Committed Blur equals live preview");
            Undo.PerformUndo(); ComparePreview(before, doc, "Exact visual Undo on transformed Drawing");
            Undo.PerformRedo(); ComparePreview(pending, doc, "Exact visual Redo on transformed Drawing");

            Call(doc, "SyncDrawingLayerTextures");
            using (var container = new WhimTexDocumentContainer())
            {
                byte[] model = (byte[])T("WhimTexDocumentSerializer").GetMethod("Serialize", F).Invoke(null, new object[] { doc, container });
                container.Set("document", model, System.IO.Compression.CompressionLevel.Fastest);
                var composite = doc.ComposeCanvas();
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    T("WhimTexTiffCarrier").GetMethod("WriteTo", F).Invoke(null,
                        new object[] { stream, container, composite, (bool?)false, WhimTexOutputPrecision.Float32 });
                }
                finally { Object.DestroyImmediate(composite); }
            }
            loaded = WhimTexDocumentFile.Load(path);
            ComparePreview(pending, loaded, "Native TIFF reopen keeps transformed masked HDR pixels");

            void ComparePreview(byte[] expectedBytes, WhimTexDocument document, string label)
                => test.True(expectedBytes.SequenceEqual(Preview(document)), label);

            void AddBackdrop(Color right)
            {
                var drawing = new DrawingLayerBehaviour();
                doc.layers.Add(new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR });
                var texture = new Texture2D(side, side, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color[side * side];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = i % side >= side / 2 ? right : new Color(.125f, 2, 3, 1);
                texture.SetPixels(pixels); texture.Apply(false, false);
                Call(drawing, "AdoptStoredTexture", texture);
            }

            void NearPixel(byte[] data, int px, int py, Color color, float alpha)
            {
                for (int channel = 0; channel < 3; channel++)
                    test.Near(color[channel], Component(data, px, py, channel), .012, "Expected signed HDR channel " + channel);
                test.Near(alpha, Component(data, px, py, 3), .012, "Canvas-space mask coverage");
            }
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (snapshot != null) T("ProfiledRenderTexture").GetMethod("ReleaseTemporary", F).Invoke(null, new object[] { snapshot });
            if (selection != null) Object.DestroyImmediate(selection);
            foreach (var entry in doc.layers)
                if (entry.Behaviour is DrawingLayerBehaviour drawing)
                {
                    Call(drawing, "CancelStroke");
                    if (Get(drawing, "PixelUndoObject") is Object cursor) Undo.ClearUndo(cursor);
                    Call(drawing, "ReleaseTransientResources");
                }
            Undo.ClearUndo(doc);
            doc.layers.Clear(); Object.DestroyImmediate(doc);
            if (loaded != null)
            {
                foreach (var entry in loaded.layers) Call(entry.Behaviour, "ReleaseTransientResources");
                loaded.layers.Clear(); Object.DestroyImmediate(loaded);
            }
            if (File.Exists(path)) File.Delete(path);
        }

        static float Component(byte[] data, int x, int y, int c)
            => Mathf.HalfToFloat(BitConverter.ToUInt16(data, ((y * side + x) * 4 + c) * 2));

        static byte[] Preview(WhimTexDocument document)
        {
            var texture = (RenderTexture)Call(document, "RenderLayerPreview", document.layers[1], side);
            try { return Read(texture); }
            finally { T("ProfiledRenderTexture").GetMethod("ReleaseTemporary", F).Invoke(null, new object[] { texture }); }
        }
    });
}
