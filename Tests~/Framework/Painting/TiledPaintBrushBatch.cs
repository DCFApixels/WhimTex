using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex.TiledPaint
{
    public enum TiledPaintPencilShape { Brush, Circle, Square, Diamond }

    // ShaderColor is straight linear RGBA, with flow already applied to alpha.
    // The caller performs the same color decoding/clamping as the native brush.
    public readonly struct TiledPaintBrushSettings
    {
        public readonly float Size, Hardness;
        public readonly Color ShaderColor;
        public readonly TiledPaintPencilShape Shape;
        public readonly bool StandardColor, HdrBlend;

        public TiledPaintBrushSettings(float size, float hardness, Color shaderColor,
            TiledPaintPencilShape shape = TiledPaintPencilShape.Brush, bool standardColor = false, bool hdrBlend = true)
        {
            Size = size; Hardness = hardness; ShaderColor = shaderColor;
            Shape = shape; StandardColor = standardColor; HdrBlend = hdrBlend;
            Validate();
        }

        internal void Validate()
        {
            if (!float.IsFinite(Size) || Size <= 0 || !float.IsFinite(Hardness) || Hardness < 0 || Hardness > 1 ||
                !float.IsFinite(ShaderColor.r) || !float.IsFinite(ShaderColor.g) || !float.IsFinite(ShaderColor.b) ||
                !float.IsFinite(ShaderColor.a) || ShaderColor.a < 0 || ShaderColor.a > 1 ||
                Mathf.Max(Mathf.Abs(ShaderColor.r),Mathf.Abs(ShaderColor.g),Mathf.Abs(ShaderColor.b))>65504 || (uint)Shape > 3)
                throw new ArgumentOutOfRangeException(nameof(TiledPaintBrushSettings));
        }
    }

    // Keep the four source-UV endpoints: rebuilding xMax from Rect.width changes
    // interpolation rounding. CanvasCenterUv is the native shader's dab center.
    public readonly struct TiledPaintBrushStamp
    {
        public readonly float XMin, YMin, XMax, YMax;
        public readonly Vector2 CanvasCenterUv;

        public TiledPaintBrushStamp(float xMin, float yMin, float xMax, float yMax, Vector2 canvasCenterUv)
        {
            XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax; CanvasCenterUv = canvasCenterUv;
            Validate();
        }

        internal void Validate()
        {
            if (!float.IsFinite(XMin) || !float.IsFinite(YMin) || !float.IsFinite(XMax) || !float.IsFinite(YMax) ||
                XMin >= XMax || YMin >= YMax || !float.IsFinite(CanvasCenterUv.x) || !float.IsFinite(CanvasCenterUv.y))
                throw new ArgumentException("Stamp endpoints must be finite and ordered.");
        }

        public static TiledPaintBrushStamp FromSourcePixels(TiledPaintFrame frame, Vector2 center, TiledPaintBrushSettings settings)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (!frame.SourceToCanvasUv.TryPoint(center, out var canvas))
                throw new ArgumentException("Stamp center crosses the projective horizon.");
            return FromCanvasUv(frame, canvas, settings);
        }

        public static TiledPaintBrushStamp FromSourceUv(TiledPaintFrame frame, Vector2 center, TiledPaintBrushSettings settings)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (!frame.SourceUvToCanvasUv.TryPoint(center, out var canvas))
                throw new ArgumentException("Stamp center crosses the projective horizon.");
            return FromCanvasUv(frame, canvas, settings);
        }

        // Stamps are already spaced, pressure-resolved and (for Pencil) snapped.
        // This helper reproduces the native simple-tip geometry, including AA padding.
        public static TiledPaintBrushStamp FromCanvasUv(TiledPaintFrame frame, Vector2 center, TiledPaintBrushSettings settings)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            settings.Validate();
            float rx = settings.Size / frame.CanvasWidth * .5f, ry = settings.Size / frame.CanvasHeight * .5f;
            if (settings.Shape == TiledPaintPencilShape.Brush)
            {
                float padding = 1f;
                if (frame.CanvasUvToSourceUv.TryPoint(center, out var source) &&
                    frame.SourceUvToCanvasUv.TryPoint(new Double2(source.x + 1d / frame.SourceWidth, source.y), out var cx) &&
                    frame.SourceUvToCanvasUv.TryPoint(new Double2(source.x, source.y + 1d / frame.SourceHeight), out var cy))
                    padding = Mathf.Max(padding,
                        Vector2.Scale((Vector2)cx - center, new Vector2(frame.CanvasWidth, frame.CanvasHeight)).magnitude +
                        Vector2.Scale((Vector2)cy - center, new Vector2(frame.CanvasWidth, frame.CanvasHeight)).magnitude);
                rx += padding / frame.CanvasWidth; ry += padding / frame.CanvasHeight;
            }
            // Float endpoints match native stamp construction before double matrix evaluation.
            double x0 = center.x - rx, y0 = center.y - ry, x1 = center.x + rx, y1 = center.y + ry;
            var inverse = frame.CanvasUvToSourceUv;
            double wa = inverse.m20 * x0 + inverse.m21 * y0 + inverse.m22;
            double wb = inverse.m20 * x1 + inverse.m21 * y0 + inverse.m22;
            double wc = inverse.m20 * x1 + inverse.m21 * y1 + inverse.m22;
            double wd = inverse.m20 * x0 + inverse.m21 * y1 + inverse.m22;
            if (!(wa * wb > 0 && wa * wc > 0 && wa * wd > 0) ||
                !inverse.TryPoint(new Double2(x0, y0), out var a) || !inverse.TryPoint(new Double2(x1, y0), out var b) ||
                !inverse.TryPoint(new Double2(x1, y1), out var c) || !inverse.TryPoint(new Double2(x0, y1), out var d))
                throw new NotSupportedException("An unbounded stamp crossing the projective horizon needs a finite region from its caller.");
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return new TiledPaintBrushStamp(min.x, min.y, max.x, max.y, center);
        }
    }

    // Immutable settings and reusable native resources for sequential updates.
    // One batch lease may be alive at a time. Execute its commands before disposal.
    // Texture tips, per-stamp dynamics, clipping payloads and sequential backdrop
    // sampling require a separate native batch adapter; they are not silently approximated here.
    public sealed class TiledPaintBrushRasterizer : IDisposable
    {
        private const int TileSize = 256;
        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 position;
            public Vector2 uv;
            public Vector3 clipMin, clipMax, clipData;
            public Vector4 tileData;
        }
        private readonly struct Range
        {
            public readonly TiledPaintTile Tile;
            public readonly int First, Count;
            public readonly Rect Scissor;
            public readonly bool Clipped;
            public Range(TiledPaintTile tile, int first, int count, Rect scissor, bool clipped)
            { Tile = tile; First = first; Count = count; Scissor = scissor; Clipped = clipped; }
        }
        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
        };
        private const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly TiledPaintFrame frame;
        private readonly TiledPaintOptions options;
        private TiledPaintBrushStamp[] stamps = Array.Empty<TiledPaintBrushStamp>();
        private Vertex[] vertices = Array.Empty<Vertex>();
        private int[] alternate = Array.Empty<int>(), indices = Array.Empty<int>();
        private readonly List<Range> ranges = new List<Range>();
        private TiledPaintBrushBatch lease;
        private int stampCount;
        private Material material;
        private Mesh mesh;
        private bool disposed;

        public TiledPaintBrushRasterizer(Shader brushShader, TiledPaintFrame frame, TiledPaintBrushSettings settings,
            TiledPaintOptions options = null)
        {
            this.frame = frame ?? throw new ArgumentNullException(nameof(frame));
            settings.Validate();
            // Test-only adapter: keep the production validation contract without
            // widening its public API for this separately compiled fixture.
            try
            {
                this.options = (TiledPaintOptions)typeof(TiledPaintOptions).GetMethod("Copy",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(options ?? new TiledPaintOptions(), null);
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException != null)
            { throw error.InnerException; }
            if (brushShader == null || !brushShader.isSupported) throw new NotSupportedException("The native raster brush shader is unavailable.");
            try
            {
                material = new Material(brushShader) { name = "Tiled paint brush rasterizer", hideFlags = HideFlags.HideAndDontSave };
                mesh = new Mesh { name = "Tiled paint stamp batch", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                mesh.MarkDynamic(); Configure(settings);
            }
            catch { Dispose(); throw; }
        }

        public TiledPaintBrushBatch CreateBatch(IReadOnlyList<TiledPaintBrushStamp> stamps)
            => new TiledPaintBrushBatch(this, stamps, false);

        internal int Acquire(TiledPaintBrushBatch owner, IReadOnlyList<TiledPaintBrushStamp> input)
        {
            EnsureUsable();
            if (lease != null) throw new InvalidOperationException("Dispose the previous batch after executing its commands before creating another.");
            if (input == null) throw new ArgumentNullException(nameof(input));
            int count = input.Count;
            EnsureCapacity(ref stamps, count, options.MaxBatchStamps);
            for (int i = 0; i < count; i++)
            {
                var stamp = input[i]; stamp.Validate();
                if (Math.Abs((double)stamp.XMin * frame.SourceWidth) > options.CoordinateLimit ||
                    Math.Abs((double)stamp.XMax * frame.SourceWidth) > options.CoordinateLimit ||
                    Math.Abs((double)stamp.YMin * frame.SourceHeight) > options.CoordinateLimit ||
                    Math.Abs((double)stamp.YMax * frame.SourceHeight) > options.CoordinateLimit)
                    throw new ArgumentOutOfRangeException(nameof(input), "Stamp exceeds the configured coordinate limit.");
                stamps[i] = stamp;
            }
            stampCount = count; lease = owner; return count;
        }

        internal Rect Bounds(TiledPaintBrushBatch owner, int stamp)
        {
            CheckLease(owner);
            if ((uint)stamp >= (uint)stampCount) throw new ArgumentOutOfRangeException(nameof(stamp));
            var s = stamps[stamp];
            return Rect.MinMaxRect(s.XMin, s.YMin, s.XMax, s.YMax);
        }

        internal void Record(TiledPaintBrushBatch owner, CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame frame)
        {
            CheckLease(owner);
            if (commands == null || pages == null) throw new ArgumentNullException(commands == null ? nameof(commands) : nameof(pages));
            if (!ReferenceEquals(this.frame, frame)) throw new ArgumentException("Batch and source must share the same immutable frame.");
            int references = 0, indexCapacity = 0;
            foreach (var page in pages)
            {
                if (page == null || page.Texture == null || !page.Texture.IsCreated()) throw new TiledPaintResourceLostException();
                if (page.Texture.width % TileSize != 0 || page.Texture.height % TileSize != 0)
                    throw new ArgumentException("Pending pages must contain complete 256-pixel slots.");
                int slots = checked(page.Texture.width / TileSize * (page.Texture.height / TileSize));
                foreach (var tile in page.Tiles)
                {
                    if (tile == null || tile.Page != page || (uint)tile.Index >= (uint)slots)
                        throw new ArgumentException("Invalid pending tile slot.");
                    int previous = -1;
                    foreach (int index in tile.Indices)
                    {
                        if ((uint)index >= (uint)stampCount || index <= previous)
                            throw new ArgumentException("Tile stamp indices must be unique and preserve submission order.");
                        previous = index;
                    }
                    references = checked(references + tile.Indices.Count);
                    if (references > options.MaxBatchTileReferences) throw new ArgumentException("Tile references exceed the configured limit.");
                    long x = (long)tile.Key.x * TileSize, y = (long)tile.Key.y * TileSize;
                    bool partial = x >= 0 && y >= 0 && x < frame.SourceWidth && y < frame.SourceHeight &&
                        (x + TileSize > frame.SourceWidth || y + TileSize > frame.SourceHeight);
                    indexCapacity = checked(indexCapacity + tile.Indices.Count * (partial ? 8 : 4));
                }
            }
            int clippedQuads = 0;
            for (int i = 0; i < stampCount; i++)
            {
                var s = stamps[i];
                if ((s.XMin < 0 || s.YMin < 0 || s.XMax > 1 || s.YMax > 1) &&
                    s.XMin < 1 && s.YMin < 1 && s.XMax > 0 && s.YMax > 0) clippedQuads++;
            }
            EnsureCapacity(ref vertices, checked((stampCount + clippedQuads) * 4), checked(options.MaxBatchStamps * 8));
            EnsureCapacity(ref alternate, stampCount, options.MaxBatchStamps);
            Array.Clear(alternate, 0, stampCount);
            int vertexCount = checked(stampCount * 4);
            for (int i = 0; i < stampCount; i++)
            {
                var s = stamps[i];
                Quad(vertices, i * 4, s.XMin, s.YMin, s.XMax, s.YMax, s.CanvasCenterUv);
                if (s.XMin >= 0 && s.YMin >= 0 && s.XMax <= 1 && s.YMax <= 1) continue;
                float x0 = Mathf.Max(0, s.XMin), y0 = Mathf.Max(0, s.YMin), x1 = Mathf.Min(1, s.XMax), y1 = Mathf.Min(1, s.YMax);
                if (x1 <= x0 || y1 <= y0) { alternate[i] = -1; continue; }
                alternate[i] = vertexCount + 1;
                Quad(vertices, vertexCount, x0, y0, x1, y1, s.CanvasCenterUv); vertexCount += 4;
            }
            // Edge tiles can need one native and one shared raw index range.
            EnsureCapacity(ref indices, indexCapacity, checked(options.MaxBatchTileReferences * 8));
            ranges.Clear();
            int cursor = 0;
            foreach (var page in pages) foreach (var tile in page.Tiles)
            {
                if (tile.Indices.Count == 0) continue;
                long x = (long)tile.Key.x * TileSize, y = (long)tile.Key.y * TileSize;
                int insideWidth = (int)Math.Max(0L, Math.Min(TileSize, frame.SourceWidth - x));
                int insideHeight = (int)Math.Max(0L, Math.Min(TileSize, frame.SourceHeight - y));
                bool native = x >= 0 && y >= 0 && insideWidth > 0 && insideHeight > 0;
                var origin = tile.Origin;
                if (native)
                {
                    int first = cursor;
                    AddIndices(tile, true, alternate, indices, ref cursor);
                    if (cursor > first) ranges.Add(new Range(tile, first, cursor - first,
                        new Rect(origin.x, origin.y, insideWidth, insideHeight), insideWidth != TileSize || insideHeight != TileSize));
                    if (insideWidth == TileSize && insideHeight == TileSize) continue;
                }
                int rawFirst = cursor;
                AddIndices(tile, false, alternate, indices, ref cursor);
                if (!native) ranges.Add(new Range(tile, rawFirst, cursor - rawFirst, default, false));
                else
                {
                    if (insideWidth < TileSize) ranges.Add(new Range(tile, rawFirst, cursor - rawFirst,
                        new Rect(origin.x + insideWidth, origin.y, TileSize - insideWidth, TileSize), true));
                    if (insideHeight < TileSize) ranges.Add(new Range(tile, rawFirst, cursor - rawFirst,
                        new Rect(origin.x, origin.y + insideHeight, insideWidth, TileSize - insideHeight), true));
                }
            }
            if (ranges.Count > 0)
            {
                // Replace native mesh storage before changing buffers still used
                // by previously submitted commands, as the native raster path does.
                mesh.Clear(false);
                mesh.SetVertexBufferParams(vertices.Length, Layout);
                mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, UploadFlags);
                mesh.subMeshCount = ranges.Count;
                for (int i = 0; i < ranges.Count; i++)
                    mesh.SetIndices(indices, ranges[i].First, ranges[i].Count, MeshTopology.Quads, i, false);
                mesh.bounds = new Bounds(new Vector3(.5f, .5f, 0), new Vector3(2, 2, 2));
                TiledPaintPage previousPage = null;
                bool scissor = false;
                commands.DisableScissorRect();
                for (int i = 0; i < ranges.Count; i++)
                {
                    var range = ranges[i]; var tile = range.Tile; var origin = tile.Origin;
                    if (previousPage != tile.Page) { commands.SetRenderTarget(tile.Page.Texture); previousPage = tile.Page; }
                    commands.SetViewport(new Rect(origin.x, origin.y, TileSize, TileSize));
                    float x = tile.Key.x * TileSize / (float)frame.SourceWidth, y = tile.Key.y * TileSize / (float)frame.SourceHeight;
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(x,
                        x + TileSize / (float)frame.SourceWidth, y, y + TileSize / (float)frame.SourceHeight, -1, 1));
                    if (range.Clipped) commands.EnableScissorRect(range.Scissor);
                    else if (scissor) commands.DisableScissorRect();
                    scissor = range.Clipped;
                    commands.DrawMesh(mesh, Matrix4x4.identity, material, i, 0);
                }
                if (scissor) commands.DisableScissorRect();
            }
        }

        private static void AddIndices(TiledPaintTile tile, bool native, int[] alternate, int[] indices, ref int cursor)
        {
            foreach (int stamp in tile.Indices)
            {
                int first = stamp * 4;
                if (native && alternate[stamp] < 0) continue;
                if (native && alternate[stamp] > 0) first = alternate[stamp] - 1;
                indices[cursor++] = first; indices[cursor++] = first + 1;
                indices[cursor++] = first + 2; indices[cursor++] = first + 3;
            }
        }

        private static void Quad(Vertex[] vertices, int first, float x0, float y0, float x1, float y1, Vector2 center)
        {
            vertices[first] = VertexAt(x0, y0, 0, 0, center); vertices[first + 1] = VertexAt(x0, y1, 0, 1, center);
            vertices[first + 2] = VertexAt(x1, y1, 1, 1, center); vertices[first + 3] = VertexAt(x1, y0, 1, 0, center);
        }
        private static Vertex VertexAt(float x, float y, float u, float v, Vector2 center) =>
            new Vertex { position = new Vector3(x, y, 0), uv = new Vector2(u, v), tileData = new Vector4(center.x, center.y, 0, 1) };

        private void Configure(TiledPaintBrushSettings settings)
        {
            material.DisableKeyword("BRUSH_DYNAMICS"); material.DisableKeyword("BRUSH_TEXTURE");
            material.SetVector("_Color", settings.ShaderColor);
            material.SetFloat("_Hardness", settings.Hardness); material.SetFloat("_PencilShape", (int)settings.Shape);
            material.SetFloat("_BrushSize", settings.Size);
            material.SetVector("_CanvasSize", new Vector4(frame.CanvasWidth, frame.CanvasHeight, 0, 0));
            material.SetVector("_PatternCenter", new Vector4(.5f, .5f, 0, 0));
            material.SetVector("_BackdropTransform", new Vector4(1, 1, 0, 0)); material.SetVector("_TipAspect", Vector4.one);
            material.SetTexture("_BrushTip", Texture2D.whiteTexture); material.SetTexture("_BrushSdfGradient", Texture2D.whiteTexture);
            material.SetTexture("_SelectionMask", Texture2D.whiteTexture); material.SetTexture("_StrokeBase", null); material.SetTexture("_Backdrop", null);
            material.SetFloat("_TipStandard", settings.StandardColor ? 1 : 0); material.SetFloat("_HdrBlend", settings.HdrBlend ? 1 : 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_BlendOperation", (float)BlendOp.Add);
            foreach (string name in new[] { "_CoverageMask", "_TipChannel", "_TipSdf", "_TipDecode", "_TipEncodeMask", "_StampBlendEnabled",
                "_PressureOpacity", "_StampBlendMode", "_UseSelection", "_SelectionWrap", "_CanvasWrap", "_PrepareStandard" }) material.SetFloat(name, 0);
            var m = frame.SourceUvToCanvasUv;
            material.SetVector("_PaintRow0", new Vector4((float)m.m00, (float)m.m01, (float)m.m02, 0));
            material.SetVector("_PaintRow1", new Vector4((float)m.m10, (float)m.m11, (float)m.m12, 0));
            material.SetVector("_PaintRow2", new Vector4((float)m.m20, (float)m.m21, (float)m.m22, 0));
        }

        private static void EnsureCapacity<T>(ref T[] buffer, int required, int limit)
        {
            if (required < 0 || required > limit) throw new ArgumentOutOfRangeException(nameof(required), "Brush batch exceeds its configured capacity.");
            if (required > buffer.Length) Array.Resize(ref buffer, Math.Min(limit, Mathf.NextPowerOfTwo(Math.Max(16, required))));
        }
        internal void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Brush rasterizer must be used on its owner thread.");
        }
        private void EnsureUsable()
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(TiledPaintBrushRasterizer));
        }
        private void CheckLease(TiledPaintBrushBatch owner)
        {
            EnsureUsable();
            if (!ReferenceEquals(lease, owner)) throw new ObjectDisposedException(nameof(TiledPaintBrushBatch));
        }
        internal void Release(TiledPaintBrushBatch owner)
        {
            CheckLease(owner); lease = null; stampCount = 0; ranges.Clear();
        }
        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            if (lease != null) throw new InvalidOperationException("Dispose the active brush batch before its rasterizer.");
            disposed = true;
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            mesh = null; material = null;
            vertices = Array.Empty<Vertex>(); stamps = Array.Empty<TiledPaintBrushStamp>();
            alternate = indices = Array.Empty<int>(); ranges.Clear();
        }
    }

    // Immutable, single-use lease. Surface.Append executes its recorded commands
    // synchronously before returning, so the lease can be disposed immediately.
    public sealed class TiledPaintBrushBatch : ITiledPaintRasterBatch, IDisposable
    {
        private readonly TiledPaintBrushRasterizer rasterizer;
        private readonly bool ownsRasterizer;
        private bool recorded, disposed;
        public int Count { get; }

        // Convenience for one batch. Reuse TiledPaintBrushRasterizer for a stroke.
        public TiledPaintBrushBatch(Shader shader, TiledPaintFrame frame, TiledPaintBrushSettings settings,
            IReadOnlyList<TiledPaintBrushStamp> stamps, TiledPaintOptions options = null)
            : this(new TiledPaintBrushRasterizer(shader, frame, settings, options), stamps, true) { }

        internal TiledPaintBrushBatch(TiledPaintBrushRasterizer rasterizer, IReadOnlyList<TiledPaintBrushStamp> stamps, bool ownsRasterizer)
        {
            this.rasterizer = rasterizer; this.ownsRasterizer = ownsRasterizer;
            try { Count = rasterizer.Acquire(this, stamps); }
            catch { if (ownsRasterizer) rasterizer.Dispose(); throw; }
        }
        public Rect Bounds(int stamp) => rasterizer.Bounds(this, stamp);
        public void Record(CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame frame)
        {
            rasterizer.CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(TiledPaintBrushBatch));
            if (recorded) throw new InvalidOperationException("A brush batch can only be recorded once.");
            rasterizer.Record(this, commands, pages, frame); recorded = true;
        }
        public void Dispose()
        {
            rasterizer.CheckThread();
            if (disposed) return;
            rasterizer.Release(this); disposed = true;
            if (ownsRasterizer) rasterizer.Dispose();
        }
    }
}
