using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using Object = UnityEngine.Object;
using BlendMode = DCFApixels.WhimTex.BlendMode;

// Native-only test oracle extracted without arithmetic changes from the retained
// Pages.Run.cs, SHA256 c6d77198696a66117a1d5c4a2c7947249388dac314459cf47acd7aa2fba3c775.
// Kept: native stamp/material/commit adapter, fixture path/mask and region readback.
// Omitted: every experimental raster/gather/storage implementation and benchmark.
// Reflected types are WhimTex-owned. There is no Unity-internal API dependency.
public static class TiledPaintNativeOracle
{
    public const int CanvasSize = 8192;
    private const int Side = CanvasSize;
    private const long DenseSourceBytes = (long)Side * Side * 8;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Product = typeof(DrawingLayerBehaviour).Assembly;
    private static readonly Vector4 IdentityMap = new Vector4(1, 1, 0, 0);
    private static Type ProductType(string name) => Product.GetType("DCFApixels.WhimTex." + name, true);
    public sealed class Sample
    {
        public long SourceBytes, StrokeBytes, TexturePeakBytes, CombinedPeakBytes;
        public bool BufferAccountingAvailable;
        public int TileCount;
    }

    public sealed class Style
    {
        public float Size = 64, Hardness = .5f, Opacity = 1, Flow = 1;
        public Color Color = UnityEngine.Color.white;
        public bool Pencil, Hdr;
        public Texture Mask;
        public BlendMode Mode = BlendMode.Normal;
    }

    private static readonly Type StampType = typeof(DrawingLayerBehaviour).GetNestedType("PaintStamp", BindingFlags.NonPublic);
    private static readonly Type StampListType = typeof(List<>).MakeGenericType(StampType);
    private static readonly FieldInfo StampCenter = StampType.GetField("center");
    private static readonly FieldInfo StampPressure = StampType.GetField("opacityPressure");
    private static readonly MethodInfo Draw = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic)
        .GetMethod("Draw", BindingFlags.Public | BindingFlags.Static);
    private static readonly object Circle = Enum.ToObject(typeof(DrawingLayerBehaviour).Assembly
        .GetType("DCFApixels.WhimTex.PencilShape", true), 0);

    internal static IList NewStamps() => (IList)Activator.CreateInstance(StampListType);


    internal static object MakeStamp(Vector2 documentPoint)
    {
        object stamp = Activator.CreateInstance(StampType);
        StampCenter.SetValue(stamp, documentPoint / CanvasSize);
        StampPressure.SetValue(stamp, 1f);
        return stamp;
    }

    public static Vector2 SnapPencil(Vector2 point, float size)
    {
        float offset = (Mathf.RoundToInt(size) & 1) == 0 ? 0 : .5f;
        return new Vector2(Mathf.Floor(point.x + .5f - offset) + offset, Mathf.Floor(point.y + .5f - offset) + offset);
    }

    internal static void DrawStamps(RenderTexture target, IList stamps, Style style, ProjectiveMatrix transform, object nativeStroke = null)
    {
        Color color = style.Color;
        color.a *= style.Flow;
        var args = new object[] { target, stamps, style.Size, style.Hardness, style.Pencil, Circle,
            color, false, CanvasSize, CanvasSize, Vector2.one * .5f, false,
            new TextureTransform { storage = TransformStorage.Projective, matrix = transform },
            !style.Hdr, false, null, null, !style.Hdr, false, style.Hdr, null, null, nativeStroke };
        var parameters = Draw.GetParameters();
        int assigned = args.Length;
        Array.Resize(ref args, parameters.Length);
        for (int i = assigned; i < args.Length; i++) args[i] = parameters[i].DefaultValue;
        Draw.Invoke(null, args);
    }

    internal static void Configure(Material material, Style style, ProjectiveMatrix matrix, Texture stroke, RectInt target)
    {
        material.SetFloat("_PendingBrush", 1);
        material.SetTexture("_BrushStroke", stroke);
        material.SetTexture("_BrushSelection", style.Mask);
        material.SetFloat("_BrushUseSelection", style.Mask != null ? 1 : 0);
        material.SetFloat("_StrokeOpacity", style.Opacity);
        material.SetFloat("_StrokeMode", (int)style.Mode);
        material.SetFloat("_BrushStandard", style.Hdr ? 0 : 1);
        material.SetFloat("_HdrBlend", style.Hdr ? 1 : 0);
        material.SetFloat("_BrushGamma", QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0);
        material.SetVector("_BrushWriteChannels", Vector4.one);
        material.SetVector("_BrushStrokeMap", IdentityMap);
        material.SetVector("_BrushDirtyRect", new Vector4(0, 0, 1, 1));
        material.SetVector("_BrushCanvasRow0", new Vector4((float)matrix.m00, (float)matrix.m01, (float)matrix.m02, 0));
        material.SetVector("_BrushCanvasRow1", new Vector4((float)matrix.m10, (float)matrix.m11, (float)matrix.m12, 0));
        material.SetVector("_BrushCanvasRow2", new Vector4((float)matrix.m20, (float)matrix.m21, (float)matrix.m22, 0));
    }

    internal static RenderTexture Create(int width, int height, bool randomWrite)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            enableRandomWrite = randomWrite
        };
        if (!texture.Create())
        {
            Object.DestroyImmediate(texture);
            throw new InvalidOperationException($"Unable to allocate {width}×{height} RGBAHalf.");
        }
        return texture;
    }

    internal static void Clear(RenderTexture texture)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = previous;
    }

    internal static void Destroy(RenderTexture texture)
    {
        if (texture == null) return;
        texture.Release();
        Object.DestroyImmediate(texture);
    }

    private static readonly Vector2[] Knots =
    {
        new Vector2(2400, 3500), new Vector2(3500, 4500),
        new Vector2(4600, 3500), new Vector2(5800, 4300)
    };

    private static Vector2[][] MakePath(int diameter, float relative, bool pencil, bool firstDab)
    {
        var points = new List<Vector2>();
        float distance = Mathf.Max(.25f, diameter * relative);
        for (int i = 1; i < Knots.Length; i++)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((Knots[i] - Knots[i - 1]).magnitude / distance));
            for (int j = i == 1 ? 0 : 1; j <= steps; j++)
            {
                var point = Vector2.Lerp(Knots[i - 1], Knots[i], j / (float)steps);
                if (pencil) point = TiledPaintNativeOracle.SnapPencil(point, diameter);
                if (points.Count == 0 || points[points.Count - 1] != point) points.Add(point);
            }
        }
        int count = Math.Min(10, points.Count);
        var result = new Vector2[count][];
        for (int i = 0; i < count; i++)
        {
            int first = points.Count * i / count, last = points.Count * (i + 1) / count;
            if (firstDab && count > 1)
            {
                first = i == 0 ? 0 : 1 + (points.Count - 1) * (i - 1) / (count - 1);
                last = i == 0 ? 1 : 1 + (points.Count - 1) * i / (count - 1);
            }
            result[i] = new Vector2[last - first];
            points.CopyTo(first, result[i], 0, last - first);
        }
        return result;
    }

    private static Texture2D SoftMask()
    {
        var mask = new Texture2D(64, 1, TextureFormat.R8, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var bytes = mask.GetRawTextureData<byte>();
        for (int i = 0; i < 64; i++) bytes[i] = (byte)(56 + i * 3);
        mask.Apply(false, true); return mask;
    }

    public static void Bounds(TiledPaintNativeOracle.Style style, Vector2 center,
        out float x0,out float y0,out float x1,out float y1)
    {
        const int canvas=8192;
        float radius=style.Size/Mathf.Max(1f,canvas)*.5f;
        if(!style.Pencil)
        {
            float padding=1f;
            var identity=ProjectiveMatrix.Identity;
            if(identity.TryPoint(new Double2(center.x,center.y),out var sourceCenter)&&
               identity.TryPoint(new Double2(sourceCenter.x+1d/canvas,sourceCenter.y),out var canvasX)&&
               identity.TryPoint(new Double2(sourceCenter.x,sourceCenter.y+1d/canvas),out var canvasY))
                padding=Mathf.Max(padding,
                    Vector2.Scale((Vector2)canvasX-center,new Vector2(canvas,canvas)).magnitude+
                    Vector2.Scale((Vector2)canvasY-center,new Vector2(canvas,canvas)).magnitude);
            radius+=padding/canvas;
        }
        x0=center.x-radius; y0=center.y-radius; x1=center.x+radius; y1=center.y+radius;
    }

    private sealed class Dense : IDisposable
    {
        private readonly Type strokeType = ProductType("PaintStrokeBuffer");
        private readonly IList[] stamps;
        private readonly TiledPaintNativeOracle.Style style;
        private readonly object[] commitArgs = new object[7];
        private readonly MethodInfo commit, getTile, toArray;
        private readonly PropertyInfo strokeBytes;
        private readonly FieldInfo touched;
        private RenderTexture source, fenceTarget;
        private Texture2D fencePixel;
        private Material composite;
        private IDisposable stroke;
        private long strokePeak;
        private int tileCount;
        private bool drawing;

        internal Dense(TiledPaintNativeOracle.Style settings, Vector2[][] batches)
        {
            try
            {
                style = settings;
                commit = ProductType("BrushStrokeCompute").GetMethod("TryCommitTiles", Static);
                strokeBytes = strokeType.GetProperty("Bytes", Instance);
                touched = strokeType.GetField("touched", Instance);
                toArray = touched.FieldType.GetMethod("ToArray", Instance);
                getTile = strokeType.GetMethod("Get", Instance);
                source = TiledPaintNativeOracle.Create(Side, Side, true);
                fenceTarget = TiledPaintNativeOracle.Create(1, 1, false);
                fencePixel = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                composite = new Material(Shader.Find("Hidden/WhimTex/Blend")) { hideFlags = HideFlags.HideAndDontSave };
                TiledPaintNativeOracle.Clear(source);
                stamps = new IList[batches.Length];
                for (int i = 0; i < batches.Length; i++)
                {
                    stamps[i] = TiledPaintNativeOracle.NewStamps();
                    foreach (var point in batches[i]) stamps[i].Add(TiledPaintNativeOracle.MakeStamp(point));
                }
                commitArgs[0] = source; commitArgs[2] = composite;
            }
            catch { Dispose(); throw; }
        }
        internal void Begin()
        {
            stroke = (IDisposable)Activator.CreateInstance(strokeType, Instance, null, new object[] { source }, null);
            strokePeak = 0; tileCount = 0; drawing = false;
        }
        internal void Add(int batch)
        {
            TiledPaintNativeOracle.DrawStamps(source, stamps[batch], style, ProjectiveMatrix.Identity, stroke);
            drawing = true;
        }
        internal void Commit()
        {
            var tiles = (RectInt[])toArray.Invoke(touched.GetValue(stroke), null);
            if (tiles.Length == 0) throw new InvalidOperationException("Native stroke did not accumulate tiles.");
            tileCount = tiles.Length; drawing = false;
            TiledPaintNativeOracle.Configure(composite, style, ProjectiveMatrix.Identity, null, new RectInt(0, 0, Side, Side));
            commitArgs[1] = tiles; commitArgs[3] = stroke; commitArgs[4] = style.Mask;
            if (!(bool)commit.Invoke(null, commitArgs)) throw new InvalidOperationException("Native commit did not execute.");
        }
        internal void Memory(Sample sample)
        {
            strokePeak = Math.Max(strokePeak, (long)strokeBytes.GetValue(stroke));
            sample.SourceBytes = DenseSourceBytes; sample.StrokeBytes = strokePeak;
            sample.TexturePeakBytes = sample.CombinedPeakBytes = DenseSourceBytes + strokePeak;
            sample.BufferAccountingAvailable = true; sample.TileCount = tileCount;
        }
        internal void Fence()
        {
            RenderTexture target = source;
            if (drawing)
            {
                var tiles = (RectInt[])toArray.Invoke(touched.GetValue(stroke), null);
                if (tiles.Length > 0) target = (RenderTexture)getTile.Invoke(stroke, new object[] { tiles[tiles.Length - 1] });
            }
            var previous = RenderTexture.active;
            try
            {
                Graphics.CopyTexture(target, 0, 0, 0, 0, 1, 1, fenceTarget, 0, 0, 0, 0);
                RenderTexture.active = fenceTarget;
                fencePixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            }
            finally { RenderTexture.active = previous; }
        }
        internal Color[] Read(RectInt rect)
        {
            var target = TiledPaintNativeOracle.Create(rect.width, rect.height, false);
            var reader = new Texture2D(rect.width, rect.height, TextureFormat.RGBAHalf, false, true);
            var previous = RenderTexture.active;
            try
            {
                Graphics.CopyTexture(source, 0, 0, rect.x, rect.y, rect.width, rect.height, target, 0, 0, 0, 0);
                RenderTexture.active = target; reader.ReadPixels(new Rect(0, 0, rect.width, rect.height), 0, 0, false);
                return reader.GetPixels();
            }
            finally { RenderTexture.active = previous; TiledPaintNativeOracle.Destroy(target); Object.DestroyImmediate(reader); }
        }
        public void Dispose()
        {
            stroke?.Dispose(); TiledPaintNativeOracle.Destroy(source); TiledPaintNativeOracle.Destroy(fenceTarget);
            if (fencePixel != null) Object.DestroyImmediate(fencePixel);
            if (composite != null) Object.DestroyImmediate(composite);
        }
    }

}
