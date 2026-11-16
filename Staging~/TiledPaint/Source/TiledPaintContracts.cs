using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex.TiledPaint
{
    // Kept outside src until the editor's source, persistence and history adapters are connected.
    public sealed class TiledPaintFrame
    {
        public readonly int SourceWidth, SourceHeight, CanvasWidth, CanvasHeight;
        public readonly ProjectiveMatrix SourceToCanvasPixels, CanvasToSourcePixels, SourceToCanvasUv, SourceUvToCanvasUv, CanvasUvToSourceUv;
        public TiledPaintFrame(int sourceWidth, int sourceHeight, int canvasWidth, int canvasHeight, ProjectiveMatrix sourceToCanvasPixels)
        {
            if(sourceWidth<1||sourceHeight<1||canvasWidth<1||canvasHeight<1||sourceWidth>32768||sourceHeight>32768||canvasWidth>32768||canvasHeight>32768)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth),"Dimensions must be 1..32768.");
            if(!sourceToCanvasPixels.IsFinite||!sourceToCanvasPixels.TryInverse(out var inverse))throw new ArgumentException("The source frame must be finite and invertible.");
            SourceWidth=sourceWidth;SourceHeight=sourceHeight;CanvasWidth=canvasWidth;CanvasHeight=canvasHeight;
            SourceToCanvasPixels=sourceToCanvasPixels;CanvasToSourcePixels=inverse;
            SourceToCanvasUv=ProjectiveMatrix.Scale(1d/canvasWidth,1d/canvasHeight)*sourceToCanvasPixels;
            SourceUvToCanvasUv=SourceToCanvasUv*ProjectiveMatrix.Scale(sourceWidth,sourceHeight);
            if(!SourceUvToCanvasUv.TryInverse(out CanvasUvToSourceUv))throw new ArgumentException("Invalid normalized frame.");
        }
        public Vector2 SourceUv(Vector2 sourcePixel) => new Vector2(sourcePixel.x/SourceWidth,sourcePixel.y/SourceHeight);
    }

    public sealed class TiledPaintOptions
    {
        public long GpuBudgetBytes=1024L*1024*1024;
        public long IdleBudgetBytes=128L*1024*1024;
        public int MaxBatchStamps=65536, MaxBatchTileReferences=1048576;
        public int CoordinateLimit=1048576;
        public bool ComputeCommit=true;
        internal TiledPaintOptions Copy()
        {
            if(GpuBudgetBytes<16L*1024*1024||GpuBudgetBytes>16L*1024*1024*1024||IdleBudgetBytes<0||IdleBudgetBytes>GpuBudgetBytes||
               MaxBatchStamps<1||MaxBatchStamps>1048576||MaxBatchTileReferences<1||MaxBatchTileReferences>4194304||CoordinateLimit<256||CoordinateLimit>4194304)
                throw new ArgumentOutOfRangeException(nameof(TiledPaintOptions));
            return (TiledPaintOptions)MemberwiseClone();
        }
    }

    public sealed class TiledPaintShaders
    {
        public readonly Shader Brush, RasterCommit;
        public readonly ComputeShader ComputeCommit;
        public TiledPaintShaders(Shader brush,Shader rasterCommit,ComputeShader computeCommit=null)
        {
            if(brush==null||!brush.isSupported||rasterCommit==null||!rasterCommit.isSupported)throw new NotSupportedException("Required raster shaders are unavailable.");
            Brush=brush;RasterCommit=rasterCommit;ComputeCommit=computeCommit;
        }
    }

    public readonly struct TiledStrokeSettings
    {
        public readonly float Opacity;
        public readonly BlendMode Mode;
        public readonly bool StandardColor,HDRBlend,Erase,SelectionWrap,StampAccumulation,PressureOpacity;
        public readonly int WriteChannels;
        public readonly Texture Selection, OriginalStraight;
        public readonly bool OriginalStraightGamma;
        public TiledStrokeSettings(float opacity=1,BlendMode mode=BlendMode.Normal,bool standardColor=false,bool hdrBlend=true,
            bool erase=false,int writeChannels=15,Texture selection=null,bool selectionWrap=false,bool stampAccumulation=false,bool pressureOpacity=false,
            Texture originalStraight=null,bool originalStraightGamma=false)
        {
            if(!float.IsFinite(opacity)||opacity<0||opacity>1||!Enum.IsDefined(typeof(BlendMode),mode)||writeChannels<0||writeChannels>15)
                throw new ArgumentOutOfRangeException(nameof(opacity));
            if(pressureOpacity&&!stampAccumulation)throw new ArgumentException("Pressure accumulation requires a prepared backdrop batch.");
            Opacity=opacity;Mode=mode;StandardColor=standardColor;HDRBlend=hdrBlend;Erase=erase;WriteChannels=writeChannels;
            Selection=selection;SelectionWrap=selectionWrap;StampAccumulation=stampAccumulation;PressureOpacity=pressureOpacity;
            OriginalStraight=originalStraight;OriginalStraightGamma=originalStraightGamma;
        }
    }

    // Bounds use the immutable source UV frame, never occupied bounds. The backend
    // preserves submission order. Record may only write the supplied pending targets.
    public interface ITiledPaintRasterBatch
    {
        int Count { get; }
        Rect Bounds(int stamp);
        void Record(CommandBuffer commands,IReadOnlyList<TiledPaintPage> pages,TiledPaintFrame frame);
    }

    // Explicit contract for native batches that initialize/read the committed
    // backdrop and produce a complete accumulated result, rather than plain ink.
    public interface ITiledPaintBackdropBatch:ITiledPaintRasterBatch
    {
        bool SupportsPressureOpacity {get;}
    }

    public sealed class TiledPaintTile
    {
        public readonly Vector2Int Key;
        public readonly TiledPaintPage Page;
        public readonly int Index;
        internal readonly List<int> indices=new List<int>();
        public IReadOnlyList<int> Indices=>indices;
        public Vector2Int Origin=>new Vector2Int(Index%(Page.Texture.width/256)*256,Index/(Page.Texture.width/256)*256);
        internal TiledPaintTile(Vector2Int key,TiledPaintPage page,int index){Key=key;Page=page;Index=index;}
    }

    public sealed class TiledPaintPage
    {
        internal readonly TiledPageAllocation Allocation;
        internal readonly List<TiledPaintTile> tiles=new List<TiledPaintTile>();
        public RenderTexture Texture=>Allocation.Texture;
        public IReadOnlyList<TiledPaintTile> Tiles=>tiles;
        internal bool NeedsClear;
        internal TiledPaintPage(TiledPageAllocation allocation,bool clear){Allocation=allocation;NeedsClear=clear;}
    }

    public readonly struct TiledPaintTileView
    {
        public readonly Vector2Int Key;
        public readonly RenderTexture Texture;
        public readonly RectInt PhysicalRect;
        internal TiledPaintTileView(Vector2Int key,TiledVersion version){Key=key;Texture=version.Page.Texture;PhysicalRect=new RectInt(version.Origin,new Vector2Int(256,256));}
        internal TiledPaintTileView(TiledPaintTile tile){Key=tile.Key;Texture=tile.Page.Texture;PhysicalRect=new RectInt(tile.Origin,new Vector2Int(256,256));}
    }

    public sealed class TiledPaintResourceLostException:InvalidOperationException
    { public TiledPaintResourceLostException():base("Tiled paint GPU content was lost. Restore a retained CPU/document snapshot before continuing."){} }

    public readonly struct TiledPaintImportTile
    {
        public readonly Vector2Int Key;
        public readonly Texture Texture;
        public readonly RectInt SourceRect;
        public TiledPaintImportTile(Vector2Int key,Texture texture,RectInt sourceRect)
        { Key=key;Texture=texture;SourceRect=sourceRect; }
    }

    // The editor, not the renderer, decides retention and undo branching.
    public sealed class TiledPaintDelta:IDisposable
    {
        public readonly TiledPaintSnapshot Before,After;
        public readonly IReadOnlyList<Vector2Int> ChangedTiles;
        internal TiledPaintDelta(TiledSnapshotState before,TiledSnapshotState after,Vector2Int[] changed)
        {
            ChangedTiles=Array.AsReadOnly(changed);
            Before=new TiledPaintSnapshot(before);
            try { After=new TiledPaintSnapshot(after); } catch { Before.Dispose();throw; }
        }
        public void Dispose(){Before.Dispose();After.Dispose();}
    }
}
