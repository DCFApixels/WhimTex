using System;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class BlurRegionTests
{
    public static string RunScaledSpacing() => TestContext.Run("Blur covers a canvas path at any layer scale", test =>
    {
        using var disk = new PaintTileHistoryTests.DiskScope();
        Check(1);
        Check(16);

        void Check(int scale)
        {
            const int side = 512, center = 256;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave;
            doc.width = doc.height = side;
            var layer = new DrawingLayerBehaviour();
            var transform = TextureTransform.Default;
            transform.scale = new Vector2(scale, 1);
            transform.pivot = new Vector2(.5f, .5f);
            doc.layers.Add(new Layer(layer) { transform = transform, colorRange = LayerColorRange.HDR,
                blendRange = LayerBlendRange.HDR, filterMode = LayerFilterMode.Point });
            var seed = new Texture2D(side, side, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var mask = new Texture2D(1, side, TextureFormat.R8, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var target = RT(side, side);
            try
            {
                var pattern = new Color[side * side];
                var coverage = mask.GetRawTextureData<byte>();
                for (int y = 0; y < side; y++)
                {
                    var color = (y & 1) == 0 ? new Color(4, -.5f, .25f, 1) : new Color(1.5f, -.25f, 2, 1);
                    Array.Fill(pattern, color, y * side, side);
                    coverage[y] = y < center - 1 ? (byte)0 : y == center - 1 ? (byte)128 : byte.MaxValue;
                }
                seed.SetPixels(pattern); seed.Apply(false, false); mask.Apply(false, false);
                Call(layer, "AdoptStoredTexture", seed);
                Call(doc, "NormalizeModel");
                Call(doc, "GetPaintTransform", layer.Owner);
                Call(doc, "ResetUndoTrackingAfterLoad");
                var start = new Vector2((center + .5f - 128f / scale) / side, (center + .5f) / side);
                var end = new Vector2((center + .5f + 128f / scale) / side, start.y);
                var original = Capture();
                var slow = Paint(16);
                Call(layer, "CancelStroke");
                Exact(original, Capture(), "Cancel returns to the original HDR stripes");

                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                var fast = Paint(1);
                int halfSpan = 128 / scale, expected = 2 * halfSpan + 1;
                int fastCount = 0, slowCount = 0;
                for (int x = center - halfSpan; x <= center + halfSpan; x++)
                {
                    float before = At(original, x, center);
                    if (Math.Abs(At(fast, x, center) - before) > .01f) fastCount++;
                    if (Math.Abs(At(slow, x, center) - before) > .01f) slowCount++;
                }
                test.Equal(expected, slowCount, "Short segments cover every centerline texel at scale " + scale);
                test.Equal(slowCount, fastCount, "Fast motion has the same coverage at scale " + scale);
                int protectedBytes = (center - 1) * side * 8;
                test.True(fast.AsSpan(0, protectedBytes).SequenceEqual(original.AsSpan(0, protectedBytes)), "Protected rows remain bit-exact");
                float full = At(fast, center, center + 1) - At(original, center, center + 1);
                float soft = At(fast, center, center - 1) - At(original, center, center - 1);
                test.True(Math.Abs(full) > .05f, "Full selection changes the HDR stripes");
                test.Near(128f / 255f, soft / full, .015f, "Soft coverage scales the change on a matching stripe");
                test.True(At(fast, center, center) > 1f && At(fast, center, center, 1) < 0f, "Changed texel retains signed HDR");

                Call(layer, "EndStroke");
                Call(layer, "FinishStrokeStorage");
                Call(doc, "MarkChanged");
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                Exact(fast, Capture(), "Commit keeps every live Half component");
                Undo.PerformUndo(); Call(layer, "ReconcilePixelUndo");
                Exact(original, Capture(), "Native Undo restores original Half data");
                Undo.PerformRedo(); Call(layer, "ReconcilePixelUndo");
                Exact(fast, Capture(), "Native Redo restores fast masked stroke");

                byte[] Paint(int segments)
                {
                    Call(layer, "PrepareBufferedStroke", side, side, "Owned scaled Blur");
                    Call(layer, "BeginStroke", start);
                    for (int i = 0; i < segments; i++)
                        Call(layer, "BlurSegment", Vector2.Lerp(start, end, i / (float)segments),
                            Vector2.Lerp(start, end, (i + 1) / (float)segments), side, side, 20f, 1f, 1f, null, false, mask);
                    return Capture();
                }
                byte[] Capture()
                {
                    Call(layer, "EnsureTiledSource", side, side);
                    ((TiledPaintSurface)Get(layer, "tiledSurface")).CopyRegion(new RectInt(0, 0, side, side), target);
                    return Read(target);
                }
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                Call(layer, "CancelStroke");
                PaintTileHistoryTests.ReleaseOwnedHistory(layer);
                Undo.ClearUndo(doc);
                Call(layer, "ReleaseTransientResources");
                doc.layers.Clear();
                Object.DestroyImmediate(doc);
                if (seed != null) Object.DestroyImmediate(seed);
                Object.DestroyImmediate(mask); Object.DestroyImmediate(target);
            }
            static float At(byte[] data, int x, int y, int channel = 0)
                => Mathf.HalfToFloat(BitConverter.ToUInt16(data, ((y * 512 + x) * 4 + channel) * 2));
        }
    });
}
