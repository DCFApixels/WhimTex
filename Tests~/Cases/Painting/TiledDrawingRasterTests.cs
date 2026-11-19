using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Both paths use the native brush material/mesh builder. The reference retains
// its original bounded PaintStrokeBuffer and compute composite, independently
// of the new sparse native adapter and its raster/compute commit implementation.
public static class TiledDrawingRasterTests
{
    const int Side = 512;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(DrawingLayerBehaviour).Assembly;
    static readonly Type StampType = typeof(DrawingLayerBehaviour).GetNestedType("PaintStamp", BindingFlags.NonPublic);
    static readonly MethodInfo Draw = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic).GetMethod("Draw", F);
    static Type Type(string name) => Product.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object value, string name) => value.GetType().GetField(name, F)?.GetValue(value) ?? value.GetType().GetProperty(name, F)?.GetValue(value);
    static void Set(object value, string name, object item) => value.GetType().GetField(name, F).SetValue(value, item);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);

    public static string Run(string scenario = "all") => TestContext.Run("Native tiled Drawing raster: " + scenario, t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var diagnostics = new List<string>();
        void CaptureLog(string message, string stack, LogType type)
        { if (type != LogType.Log) diagnostics.Add(type + ": " + message); }
        Application.logMessageReceived += CaptureLog;
        try
        {
            GL.sRGBWrite = false;
            foreach (string name in new[] { "brush81", "brush2048", "pencil1", "pencilSquare", "pencilDiamond",
                "tip", "pressure", "stamp", "stampPressure", "outside", "outsidePressure", "mask", "locked", "projective",
                "dynamic", "outsideDynamicPressure", "partialPressure", "outsideDynamicStampPressure2048",
                "partialDynamicStampPressure2048", "projectiveDynamicStampPressure2048" })
            {
                if (scenario != "all" && scenario != name) continue;
                using var fixture = new Fixture(name);
                fixture.Verify(t);
            }
        }
        finally { Application.logMessageReceived -= CaptureLog; RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        if (t.Checks == 0) throw new ArgumentException("Unknown raster scenario: " + scenario);
        t.True(diagnostics.Count == 0, "No console diagnostics, including fixture cleanup: " + string.Join("\n", diagnostics));
    });

    public static string RunRaw(string scenario = "projective") => TestContext.Run("Native tiled Drawing pending ink: " + scenario, t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try { GL.sRGBWrite = false; using var fixture = new Fixture(scenario); fixture.VerifyPendingInk(t); }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });

    sealed class Fixture : IDisposable
    {
        readonly string name;
        readonly bool pencil, stampBlend, pressure, dynamicTip, largeSequential;
        readonly int shape, channels, side;
        readonly float diameter;
        readonly ProjectiveMatrix transform;
        readonly object dynamics, nativeStroke;
        readonly IList stamps;
        readonly Texture2D seed, mask, tip;
        readonly RenderTexture native, candidate;
        readonly Material composite;
        readonly TiledPaintSurface raster;
        readonly List<Object> owned = new List<Object>();
        readonly Color paint = new Color(-.48f, 2f, .9f, .37f);

        internal Fixture(string name)
        {
            this.name = name;
            bool partial = name == "partialPressure" || name == "partialDynamicStampPressure2048";
            bool projective = name == "projective" || name == "projectiveDynamicStampPressure2048";
            largeSequential = name == "outsideDynamicStampPressure2048" || name == "partialDynamicStampPressure2048" ||
                name == "projectiveDynamicStampPressure2048";
            side = partial ? 509 : Side;
            try
            {
            pencil = name.StartsWith("pencil", StringComparison.Ordinal);
            shape = name == "pencilSquare" ? 1 : name == "pencilDiamond" ? 2 : 0;
            diameter = name == "pencil1" ? 1 : pencil ? 33 : name == "brush2048" || largeSequential ? 2048 : 81;
            dynamicTip = name == "dynamic" || name == "outsideDynamicPressure" || largeSequential;
            pressure = name == "pressure" || name == "stampPressure" || name == "outsidePressure" || name == "outsideDynamicPressure" || name == "partialPressure" || largeSequential;
            stampBlend = name == "stamp" || name == "stampPressure" || name == "outsidePressure" || name == "outsideDynamicPressure" || name == "partialPressure" || largeSequential;
            channels = name == "locked" || projective && largeSequential ? 5 : 15;
            transform = ProjectiveMatrix.Identity;
            if (projective) transform = new ProjectiveMatrix
                { m00 = .72, m01 = .17, m02 = .12, m10 = -.09, m11 = .87, m12 = .08, m20 = .11, m21 = -.07, m22 = 1 };
            native = Own(RT(side, side, true)); candidate = Own(RT(side, side, false));
            seed = Own(new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var initial = new Color[256 * 256]; Array.Fill(initial, new Color(-.04f, .15f, 1.2f, .35f));
            seed.SetPixels(initial); seed.Apply(); Graphics.Blit(seed, native);
            var frame = new TiledPaintFrame(side, side, side, side,
                ProjectiveMatrix.Scale(side, side) * transform * ProjectiveMatrix.Scale(1d / side, 1d / side));
            raster = new TiledPaintSurface(frame, new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"),
                Shader.Find("Hidden/WhimTex/TiledPaintCommit"), AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.compute")));
            var imports = new List<TiledPaintImportTile>();
            for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
                imports.Add(new TiledPaintImportTile(new Vector2Int(x, y), seed, new RectInt(0, 0, 256, 256)));
            using (raster.ImportTiles(imports)) { }
            if (name == "mask" || name == "locked" || projective && largeSequential)
            {
                mask = Own(new Texture2D(64, 32, TextureFormat.R8, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
                var bytes = mask.GetRawTextureData<byte>();
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((i * 47 + 17) % 256);
                mask.Apply();
            }
            dynamics = Activator.CreateInstance(Type("BrushDynamics"), true);
            Set(dynamics, "blend", stampBlend ? BlendMode.Multiply : BlendMode.Normal);
            if (pressure)
            {
                Set(dynamics, "pressure", true);
                Set(Get(dynamics, "pressureOpacity"), "enabled", true);
            }
            if (name == "tip")
            {
                tip = Own(new Texture2D(17, 9, TextureFormat.RGBAHalf, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
                var data = new Color[17 * 9];
                for (int i = 0; i < data.Length; i++) data[i] = new Color(.6f, .3f, .8f, (i % 17) / 16f * (i / 17) / 8f);
                tip.SetPixels(data); tip.Apply(); Set(dynamics, "tip", tip);
                Set(dynamics, "angleOffset", 31f);
            }
            if (dynamicTip)
            {
                Set(dynamics, "tipChannel", Enum.Parse(Type("BrushTipChannel"), "Color"));
                const string code = "// @whimtex-brush Tests/Tiled Dynamic\n" +
                    "float4 BrushTip(float2 uv, BrushSequence s, BrushMotion m) { " +
                    "float a=saturate(1-length(uv*2-1)); " +
                    "return float4(-.25+s.distance/256,1.25+uv.x+m.deltaPixels.x/64," +
                    ".5+(s.stampIndex%7u)/7.0,a*(.6+.4*uv.y)); }";
                Call(dynamics, "ApplyHlsl", code, new List<ShaderParameter>(), 64);
            }
            nativeStroke = Activator.CreateInstance(Type("PaintStrokeBuffer"), F, null, new object[] { native }, null);
            Call(nativeStroke, "Reset", stampBlend, pressure);
            composite = Own(new Material(Shader.Find("Hidden/WhimTex/Blend")) { hideFlags = HideFlags.HideAndDontSave });
            stamps = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(StampType));
            raster.Begin(new TiledStrokeSettings(opacity: .76f, mode: stampBlend ? BlendMode.Multiply : BlendMode.Normal,
                hdrBlend: true, writeChannels: channels, selection: mask, stampAccumulation: stampBlend, pressureOpacity: pressure));
            for (int batch = 0; batch < 3; batch++)
            {
                stamps.Clear();
                for (int i = 0; i < 5; i++)
                {
                    object stamp = Activator.CreateInstance(StampType);
                    float x = name.StartsWith("outside", StringComparison.Ordinal)
                        ? -.12f + (batch * 5 + i) * .09f : .18f + (batch * 5 + i) * .042f;
                    var point = new Vector2(x, .42f + .15f * Mathf.Sin((batch * 5 + i) * .6f));
                    if (partial) point = new Vector2(.86f + (batch * 5 + i) * .023f, .97f + .07f * Mathf.Sin(i));
                    if (pencil)
                    {
                        float offset = (Mathf.RoundToInt(diameter) & 1) == 0 ? 0 : .5f;
                        point = new Vector2((Mathf.Floor(point.x * side) + offset) / side, (Mathf.Floor(point.y * side) + offset) / side);
                    }
                    Set(stamp, "center", point); Set(stamp, "size", diameter); Set(stamp, "color", paint);
                    Set(stamp, "opacityPressure", .23f + (batch * 5 + i) * .045f);
                    Set(stamp, "rotation", name == "tip" ? .5410521f : 0f);
                    if (dynamicTip)
                        Set(stamp, "dynamicContext", Activator.CreateInstance(Type("BrushStampContext"), F, null,
                            new object[] { 2147483647u, 2u, (uint)(batch * 5 + i), (double)(batch * 5 + i) * 11,
                                80d + (batch * 5 + i) * 11, new Vector2(4 + i, -2 + batch) }, null));
                    stamps.Add(stamp);
                }
                InvokeDraw(native, nativeStroke, null);
                InvokeDraw(null, null, raster);
            }
            ConfigureComposite();
            }
            catch { Dispose(); throw; }
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }

        void InvokeDraw(RenderTexture target, object stroke, TiledPaintSurface sparse)
        {
            Draw.Invoke(null, new object[] { target, stamps, diameter, .71f, pencil, Enum.ToObject(Type("PencilShape"), shape),
                paint, false, side, side, Vector2.one * .5f, false,
                new TextureTransform { storage = TransformStorage.Projective, matrix = transform },
                false, false, null, dynamics, false, stampBlend, true, null, native, stroke, sparse, null });
        }

        void ConfigureComposite()
        {
            // These uniforms are not ShaderLab properties; set the reference's inactive modes explicitly.
            foreach (string property in new[] { "_BrushErase", "_BrushStandard", "_BrushSelectionWrap", "_BrushStraightFallback", "_BrushGamma" })
                composite.SetFloat(property, 0);
            composite.SetFloat("_PendingBrush", 1); composite.SetFloat("_StrokeOpacity", .76f);
            composite.SetFloat("_StrokeMode", (int)(stampBlend ? BlendMode.Multiply : BlendMode.Normal));
            composite.SetFloat("_HdrBlend", 1); composite.SetFloat("_BrushStampAccumulation", stampBlend ? 1 : 0);
            composite.SetFloat("_BrushPressureOpacity", pressure ? 1 : 0);
            composite.SetTexture("_BrushSelection", mask); composite.SetFloat("_BrushUseSelection", mask != null ? 1 : 0);
            composite.SetVector("_BrushDirtyRect", new Vector4(0, 0, 1, 1));
            composite.SetVector("_BrushWriteChannels", new Vector4((channels & 1) != 0 ? 1 : 0,
                (channels & 2) != 0 ? 1 : 0, (channels & 4) != 0 ? 1 : 0, (channels & 8) != 0 ? 1 : 0));
            composite.SetVector("_BrushCanvasRow0", new Vector4((float)transform.m00, (float)transform.m01, (float)transform.m02, 0));
            composite.SetVector("_BrushCanvasRow1", new Vector4((float)transform.m10, (float)transform.m11, (float)transform.m12, 0));
            composite.SetVector("_BrushCanvasRow2", new Vector4((float)transform.m20, (float)transform.m21, (float)transform.m22, 0));
        }

        internal void Verify(TestContext t)
        {
            if (largeSequential)
            {
                int pendingTiles = 0;
                foreach (var tile in raster.PendingTiles) pendingTiles++;
                t.True(pendingTiles >= 64, name + ": large sequential stamps span at least 64 source tiles");
            }
            if (dynamicTip)
            {
                var program = Get(dynamics, "hlslProgram");
                t.True((bool)Get(dynamics, "DynamicTip") && (bool)Get(program, "UsesSequence") && (bool)Get(program, "UsesMotion"),
                    name + ": live HLSL tip keeps sequence and motion vertex data");
                var material = (Material)Get(program, "DynamicMaterial");
                t.True(material.HasProperty("_StrokeBaseTransform") && material.HasProperty("_StrokeBasePresent"),
                    name + ": generated dynamic template carries sparse source mapping");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    t.True(message.severity.ToString() != "Error" && message.severity.ToString() != "Warning", name + ": " + message.message);
            }
            var tiles = (RectInt[])Call(Get(nativeStroke, "touched"), "ToArray");
            bool committed = (bool)Type("BrushStrokeCompute").GetMethod("TryCommitTiles", F)
                .Invoke(null, new object[] { native, tiles, composite, nativeStroke, mask, null, null });
            t.True(committed, name + ": reference native compute commit available");
            raster.CopyRegion(new RectInt(0, 0, side, side), candidate, true);
            EqualPixels(t, native, candidate, name + " pending");
            using var pendingView = new TemporaryRT(side, side, RenderTextureFormat.ARGBFloat);
            using var committedView = new TemporaryRT(side, side, RenderTextureFormat.ARGBFloat);
            var view = Own(new Material(Shader.Find("Hidden/WhimTex/TiledPaintView")) { hideFlags = HideFlags.HideAndDontSave });
            var toView = ProjectiveMatrix.Translate(1d / 3, 1d / 3) * ProjectiveMatrix.Scale(1d / 3, 1d / 3);
            raster.Render(pendingView.Texture, view, toView, FilterMode.Bilinear, false, null, false);
            using var delta = raster.Commit();
            raster.Render(committedView.Texture, view, toView, FilterMode.Bilinear, false, null, false);
            EqualPixels(t, pendingView.Texture, committedView.Texture, name + " Float bilinear pending/committed view with outside frame");
            raster.CopyRegion(new RectInt(0, 0, side, side), candidate);
            EqualPixels(t, native, candidate, name + " committed");
            raster.Restore(delta.Before);
            raster.CopyRegion(new RectInt(0, 0, side, side), candidate);
            using (var baseline = new TemporaryRT(side, side))
            { Graphics.Blit(seed, baseline.Texture); EqualPixels(t, baseline.Texture, candidate, name + " undo"); }
            raster.Restore(delta.After);
            raster.CopyRegion(new RectInt(0, 0, side, side), candidate);
            EqualPixels(t, native, candidate, name + " redo");
            if (dynamicTip)
            {
                var image = Read(candidate);
                try
                {
                    var raw = image.GetRawTextureData<ushort>(); var initial = seed.GetRawTextureData<ushort>();
                    bool changed = false;
                    for (int i = 0; i < raw.Length; i++) changed |= raw[i] != initial[i % 4];
                    t.True(changed, name + ": dynamic tip actually painted");
                }
                finally { Object.DestroyImmediate(image); }
            }
            if (name.StartsWith("outside", StringComparison.Ordinal))
            {
                using var outside = new TemporaryRT(256, 256);
                raster.CopyRegion(new RectInt(-256, 0, 256, 256), outside.Texture);
                var image = Read(outside.Texture);
                try
                {
                    var raw = image.GetRawTextureData<ushort>(); bool painted = false;
                    for (int i = 3; i < raw.Length; i += 4) painted |= raw[i] != 0;
                    t.True(painted, name + ": source outside Transform stores paint");
                }
                finally { Object.DestroyImmediate(image); }
            }
        }

        internal void VerifyPendingInk(TestContext t)
        {
            using var expected = new TemporaryRT(side, side);
            using var actual = new TemporaryRT(side, side);
            RenderTexture.active = expected.Texture; GL.Clear(false, true, Color.clear);
            RenderTexture.active = actual.Texture; GL.Clear(false, true, Color.clear);
            RenderTexture.active = null;
            var tiles = (RectInt[])Call(Get(nativeStroke, "touched"), "ToArray");
            foreach (var tile in tiles)
            {
                var page = (RenderTexture)Call(nativeStroke, "Get", tile);
                var physical = (RectInt)Call(nativeStroke, "Slot", tile);
                Graphics.CopyTexture(page, 0, 0, physical.x, physical.y, tile.width, tile.height,
                    expected.Texture, 0, 0, tile.x, tile.y);
            }
            foreach (var tile in raster.PendingTiles)
            {
                int x = tile.Key.x * 256, y = tile.Key.y * 256;
                if (x < 0 || y < 0 || x >= side || y >= side) continue;
                Graphics.CopyTexture(tile.Texture, 0, 0, tile.PhysicalRect.x, tile.PhysicalRect.y, Math.Min(256, side - x), Math.Min(256, side - y),
                    actual.Texture, 0, 0, x, y);
            }
            EqualPixels(t, expected.Texture, actual.Texture, name + " raw pending ink");
        }

        public void Dispose()
        {
            raster?.Dispose(); (nativeStroke as IDisposable)?.Dispose();
            if (dynamicTip && dynamics != null) Call(dynamics, "ReleaseHlsl");
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null)
            {
                if (owned[i] == RenderTexture.active) RenderTexture.active = null;
                Object.DestroyImmediate(owned[i]);
            }
        }
    }

    static void EqualPixels(TestContext t, RenderTexture a, RenderTexture b, string message)
    {
        var format = a.format == RenderTextureFormat.ARGBFloat ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;
        var left = Read(a, format); var right = Read(b, format);
        try
        {
            var x = left.GetRawTextureData<ushort>(); var y = right.GetRawTextureData<ushort>();
            int first = -1, mismatches = 0;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) { if (first < 0) first = i; mismatches++; }
            t.True(mismatches == 0, message + ": bit differences=" + mismatches + ", first=" + first +
                (first >= 0 ? ", expected=" + x[first] + ", actual=" + y[first] : ""));
        }
        finally { Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
    }

    static Texture2D Read(RenderTexture source, TextureFormat format = TextureFormat.RGBAHalf)
    {
        var previous = RenderTexture.active;
        var result = new Texture2D(source.width, source.height, format, false, true);
        try { RenderTexture.active = source; result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); return result; }
        catch { Object.DestroyImmediate(result); throw; }
        finally { RenderTexture.active = previous; }
    }
    static RenderTexture RT(int width, int height, bool randomWrite, RenderTextureFormat format = RenderTextureFormat.ARGBHalf)
    {
        var rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = randomWrite };
        if (!rt.Create()) { Object.DestroyImmediate(rt); throw new InvalidOperationException("Cannot allocate raster test texture."); }
        return rt;
    }
    sealed class TemporaryRT : IDisposable
    {
        internal readonly RenderTexture Texture;
        internal TemporaryRT(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGBHalf) { Texture = RT(width, height, false, format); }
        public void Dispose()
        {
            if (RenderTexture.active == Texture) RenderTexture.active = null;
            Object.DestroyImmediate(Texture);
        }
    }
}
