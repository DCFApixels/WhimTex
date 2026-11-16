using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex.TiledPaint
{
    // CPU transactions publish immutable GPU tile versions. No editor/document hooks.
    public sealed partial class TiledPaintSurface:IDisposable
    {
        public const int TileSize=256;
        public readonly TiledPaintFrame Frame;
        readonly TiledPaintOptions options;
        readonly TiledPaintShaders shaders;
        readonly TiledPagePool pool;
        readonly Material composite;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly CommandBuffer commands;
        readonly List<TiledPaintPage> pending=new List<TiledPaintPage>();
        readonly List<TiledPaintPage> insidePages=new List<TiledPaintPage>(),outsidePages=new List<TiledPaintPage>(),active=new List<TiledPaintPage>();
        readonly Dictionary<Vector2Int,TiledPaintTile> slots=new Dictionary<Vector2Int,TiledPaintTile>();
        readonly HashSet<Vector2Int> dirty=new HashSet<Vector2Int>();
        TiledSnapshotState source;
        TiledStrokeSettings settings;
        ProjectiveMatrix? canvasMapping;
        public void SetCanvasMapping(ProjectiveMatrix sourcePixelToCanvasUv)
        { CheckMutation(); if(Pending)throw new InvalidOperationException("Change the canvas mapping between strokes."); canvasMapping=sourcePixelToCanvasUv; }
        FrozenTexture selection,originalStraight;
        int insideCount,outsideCount;
        bool disposed,appending;
        public bool Pending {get;private set;}
        public int PageSize=>pool.PageSize;
        public long AllocatedBytes=>pool.AllocatedBytes;
        public int TileCount {get {Check();return source.Tiles.Count;}}

        public TiledPaintSurface(TiledPaintFrame frame,TiledPaintShaders shaders,TiledPaintOptions options=null)
        {
            Frame=frame??throw new ArgumentNullException(nameof(frame));
            this.shaders=shaders??throw new ArgumentNullException(nameof(shaders));
            this.options=(options??new TiledPaintOptions()).Copy();
            if(shaders.ComputeCommit==null||!SystemInfo.supportsComputeShaders||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf)||!shaders.ComputeCommit.IsSupported(0))
                this.options.ComputeCommit=false;
            int size=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(frame.SourceWidth,frame.SourceHeight)),256,1024);
            pool=new TiledPagePool(size,this.options);
            try
            {
                composite=new Material(shaders.RasterCommit){hideFlags=HideFlags.HideAndDontSave,name="Tiled paint composite"};
                commands=new CommandBuffer{name="Tiled paint transaction"};
                source=new TiledSnapshotState(frame,new Dictionary<Vector2Int,TiledVersion>(),pool);
            }
            catch {commands?.Release();if(composite!=null)Object.DestroyImmediate(composite);pool.Dispose();throw;}
        }

        public void Prepare(int freePages){CheckMutation();if(Pending)throw new InvalidOperationException("Prepare between strokes.");pool.Prepare(freePages);}
        public void Begin(TiledStrokeSettings stroke)
        {
            CheckMutation();if(Pending)throw new InvalidOperationException("A stroke is already active.");source.Validate();
            // A default struct is deliberately valid (zero-opacity, no channels).
            if(!float.IsFinite(stroke.Opacity)||stroke.Opacity<0||stroke.Opacity>1)throw new ArgumentException("Invalid opacity.");
            FrozenTexture mask=null,straight=null;
            try
            {
                mask=FrozenTexture.Capture(pool,stroke.Selection);
                straight=FrozenTexture.Capture(pool,stroke.OriginalStraight);
                settings=stroke;selection=mask;originalStraight=straight;Pending=true;
            }
            catch {mask?.Dispose();straight?.Dispose();throw;}
        }

        public void Append(ITiledPaintRasterBatch batch)
        {
            CheckMutation();CheckStroke();appending=true;
            try {AppendCore(batch);} finally {appending=false;}
        }
        Rect[] appendBounds=Array.Empty<Rect>();
        RectInt[] appendTiles=Array.Empty<RectInt>();
        TiledPaintTile reservedTile;
        void AppendCore(ITiledPaintRasterBatch batch)
        {
            CheckStroke();if(batch==null)throw new ArgumentNullException(nameof(batch));
            if((settings.StampAccumulation||settings.PressureOpacity)&&(!(batch is ITiledPaintBackdropBatch backdrop)||settings.PressureOpacity&&!backdrop.SupportsPressureOpacity))
                throw new NotSupportedException("This stroke requires a native backdrop-accumulating raster batch.");
            int count=batch.Count;
            if(count<0||count>options.MaxBatchStamps)throw new ArgumentOutOfRangeException(nameof(batch),"Batch stamp limit exceeded.");
            // Complete validation precedes allocation/mutation; bounds are evaluated once.
            if(appendBounds.Length<count)Array.Resize(ref appendBounds,count);
            if(appendTiles.Length<count)Array.Resize(ref appendTiles,count);
            long references=0;
            for(int i=0;i<count;i++)
            {
                var r=batch.Bounds(i);ValidateBounds(r);appendBounds[i]=r;
                var tiles=appendTiles[i]=TileBounds(r,true);
                references=checked(references+(long)tiles.width*tiles.height);
                if(references>options.MaxBatchTileReferences)throw new ArgumentOutOfRangeException(nameof(batch),"Batch tile-reference limit exceeded.");
            }
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                // Plain pending ink does not read committed tiles. Validate the
                // whole source at Begin/Commit, not on every pointer update.
                // Backdrop batches do read it and retain the stronger check.
                if(settings.StampAccumulation)source.Validate();
                selection?.Validate();originalStraight?.Validate();
                foreach(var page in pending)page.Allocation.Validate();
                commands.Clear();active.Clear();
                foreach(var page in pending)foreach(var tile in page.tiles)tile.indices.Clear();
                reservedTile=null;RectInt previousChanged=default;
                for(int i=0;i<count;i++)
                {
                    var r=appendBounds[i];if(r.width<=0||r.height<=0)continue;
                    var changed=TileBounds(r,false);
                    // Dense paths repeatedly visit the same tile rectangle. Its
                    // union is unchanged, while every dab still reaches the tile.
                    if(changed!=previousChanged)
                    {
                        int right=changed.x+changed.width,top=changed.y+changed.height;
                        for(int y=changed.y;y<top;y++)for(int x=changed.x;x<right;x++)dirty.Add(new Vector2Int(x,y));
                        previousChanged=changed;
                    }
                    float left=r.x, bottom=r.y, rightUv=r.xMax, topUv=r.yMax;
                    // Native in-frame slot order is independent of outside dabs.
                    if(left>=0&&bottom>=0&&rightUv<=1&&topUv<=1)Reserve(appendTiles[i],i,true);
                    else
                    {
                        float x0=Mathf.Max(0,left),y0=Mathf.Max(0,bottom),x1=Mathf.Min(1,rightUv),y1=Mathf.Min(1,topUv);
                        if(x1>x0&&y1>y0)Reserve(TileBounds(Rect.MinMaxRect(x0,y0,x1,y1),true),i,true);
                        Reserve(appendTiles[i],i,false);
                    }
                }
                foreach(var page in pending)
                {
                    bool used=false;foreach(var tile in page.tiles)used|=tile.indices.Count>0;
                    if(!used)continue;active.Add(page);
                    if(page.NeedsClear){commands.SetRenderTarget(page.Texture);commands.SetViewport(new Rect(0,0,PageSize,PageSize));commands.ClearRenderTarget(false,true,Color.clear);}
                }
                if(active.Count>0)batch.Record(commands,active,Frame);
                foreach(var page in active)page.Allocation.Cleared=false;
                GL.sRGBWrite=false;GL.PushMatrix();
                try {GL.LoadOrtho();Graphics.ExecuteCommandBuffer(commands);} finally {GL.PopMatrix();}
                foreach(var page in active)page.NeedsClear=false;
            }
            catch {ClearStroke();throw;}
            finally {commands.Clear();RenderTexture.active=previous;GL.sRGBWrite=srgb;}
        }

        void Reserve(RectInt r,int dab,bool inside)
        {
            int right=r.x+r.width,top=r.y+r.height;
            int columns=(Frame.SourceWidth+255)/256,rows=(Frame.SourceHeight+255)/256;
            for(int y=r.y;y<top;y++)for(int x=r.x;x<right;x++)
            {
                bool contained=x>=0&&y>=0&&x<columns&&y<rows;
                if(contained==inside||(!inside&&contained&&((x+1)*256>Frame.SourceWidth||(y+1)*256>Frame.SourceHeight)))
                {
                    var tile=GetSlot(new Vector2Int(x,y),inside);
                    if(tile.indices.Count==0||tile.indices[tile.indices.Count-1]!=dab)tile.indices.Add(dab);
                }
            }
        }
        TiledPaintTile GetSlot(Vector2Int key,bool inside)
        {
            if(reservedTile!=null&&reservedTile.Key==key)return reservedTile;
            if(slots.TryGetValue(key,out var tile))return reservedTile=tile;
            var pages=inside?insidePages:outsidePages;int count=inside?insideCount:outsideCount;
            int capacity=pool.SlotsPerPage;
            if(count%capacity==0)
            {
                var allocation=pool.RentPending();
                try {var page=new TiledPaintPage(allocation,!allocation.Cleared);pending.Add(page);pages.Add(page);}
                catch {if(!pending.Exists(p=>p.Allocation==allocation))pool.ReleasePending(allocation);throw;}
            }
            var owner=pages[count/capacity];tile=new TiledPaintTile(key,owner,count%capacity);
            owner.tiles.Add(tile);slots.Add(key,tile);
            if(inside)insideCount++;else outsideCount++;
            return reservedTile=tile;
        }

        public TiledPaintDelta Commit()
        {
            CheckMutation();CheckStroke();var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            var rented=new List<TiledVersion>(dirty.Count);TiledSnapshotState next=null;TiledPaintDelta delta=null;
            try
            {
                Validate();commands.Clear();ConfigureComposite();
                if(options.ComputeCommit&&dirty.Count>16)ConfigureCompute();
                var map=new Dictionary<Vector2Int,TiledVersion>(source.Tiles);
                var keys=new Vector2Int[dirty.Count];dirty.CopyTo(keys);Array.Sort(keys,CompareKeys);
                foreach(var key in dirty)if(!slots.ContainsKey(key))throw new InvalidOperationException("A changed tile has no pending storage.");
                foreach(var page in pending)
                {
                    foreach(var tile in page.tiles)
                    {
                        if(!dirty.Contains(tile.Key))continue;
                        source.Tiles.TryGetValue(tile.Key,out var before);
                        var after=pool.RentVersion(before?.Page);rented.Add(after);map[tile.Key]=after;
                        RecordCommit(tile,before,after);
                    }
                    // Its reads precede reuse in this command buffer. Source/history slots
                    // remain pinned and are never candidates for output writes.
                    pool.ReleasePending(page.Allocation);
                }
                next=new TiledSnapshotState(Frame,map,pool);
                delta=new TiledPaintDelta(source,next,keys);
                GL.sRGBWrite=false;Graphics.ExecuteCommandBuffer(commands);
                var old=source;source=next;next=null;old.Release();
                ClearStroke();
                return delta;
            }
            catch {delta?.Dispose();next?.Release();Cancel();throw;}
            finally
            {
                foreach(var version in rented)version.Release();
                commands.Clear();RenderTexture.active=previous;GL.sRGBWrite=srgb;
            }
        }

        void ConfigureComposite() => ConfigureComposite(composite);
        void ConfigureComposite(Material composite)
        {
            composite.SetFloat("_StrokeOpacity",settings.Opacity);composite.SetFloat("_StrokeMode",(int)settings.Mode);
            composite.SetFloat("_HdrBlend",settings.HDRBlend?1:0);composite.SetFloat("_BrushStandard",settings.StandardColor?1:0);
            composite.SetFloat("_BrushErase",settings.Erase?1:0);
            composite.SetFloat("_BrushStampAccumulation",settings.StampAccumulation?1:0);composite.SetFloat("_BrushPressureOpacity",settings.PressureOpacity?1:0);
            composite.SetFloat("_BrushUseSelection",selection!=null?1:0);composite.SetFloat("_BrushSelectionWrap",settings.SelectionWrap?1:0);
            composite.SetTexture("_BrushSelection",(Texture)selection?.Texture??Texture2D.blackTexture);
            composite.SetTexture("_BrushOriginalStraight",Texture2D.blackTexture);
            composite.SetTexture("_OriginalStraight",(Texture)originalStraight?.Texture??Texture2D.blackTexture);
            composite.SetFloat("_HasOriginalStraight",originalStraight!=null?1:0);composite.SetFloat("_OriginalStraightGamma",settings.OriginalStraightGamma?1:0);
            composite.SetVector("_WriteChannels",new Vector4((settings.WriteChannels&1)!=0?1:0,(settings.WriteChannels&2)!=0?1:0,(settings.WriteChannels&4)!=0?1:0,(settings.WriteChannels&8)!=0?1:0));
            composite.SetVector("_BrushWriteChannels",Vector4.one);
            composite.SetVector("_BrushSurfaceSize",new Vector4(PageSize,PageSize,1f/PageSize,1f/PageSize));
            composite.SetVector("_BrushDirtyRect",new Vector4(0,0,1,1));
            var matrix=canvasMapping ?? Frame.SourceToCanvasUv;
            composite.SetVector("_BrushCanvasRow0",new Vector4((float)matrix.m00,(float)matrix.m01,(float)matrix.m02,0));
            composite.SetVector("_BrushCanvasRow1",new Vector4((float)matrix.m10,(float)matrix.m11,(float)matrix.m12,0));
            composite.SetVector("_BrushCanvasRow2",new Vector4((float)matrix.m20,(float)matrix.m21,(float)matrix.m22,0));
        }
        void RecordCommit(TiledPaintTile pendingTile,TiledVersion before,TiledVersion after)
        {
            var dst=after.Origin;var src=pendingTile.Origin;var old=before?.Origin??Vector2Int.zero;
            var logical=pendingTile.Key*256;
            var beforeOffset=new Vector4(old.x-dst.x,old.y-dst.y,0,0);
            var logicalOffset=new Vector4(logical.x-dst.x,logical.y-dst.y,1f/Frame.SourceWidth,1f/Frame.SourceHeight);
            var strokeMap=new Vector4(1,1,(src.x-dst.x)/(float)PageSize,(src.y-dst.y)/(float)PageSize);
            // Raster pending and raster composite share the exact native blend path.
            // Compute commit remains an optional identical arithmetic kernel.
            bool compute=options.ComputeCommit&&after.Page.Texture.enableRandomWrite&&dirty.Count>16;
            if(compute)
            {
                var shader=shaders.ComputeCommit;int kernel=0;
                commands.SetComputeVectorParam(shader,"_BeforeOffset",beforeOffset);
                commands.SetComputeVectorParam(shader,"_LogicalOffset",logicalOffset);
                commands.SetComputeVectorParam(shader,"_BrushStrokeMap",strokeMap);
                commands.SetComputeFloatParam(shader,"_HasBefore",before!=null?1:0);
                commands.SetComputeTextureParam(shader,kernel,"_Before",(Texture)before?.Page.Texture??Texture2D.blackTexture);
                commands.SetComputeTextureParam(shader,kernel,"_BrushStroke",pendingTile.Page.Texture);
                commands.SetComputeTextureParam(shader,kernel,"_BrushSurface",after.Page.Texture);
                commands.SetComputeVectorParam(shader,"_BrushCommitRect",new Vector4(dst.x,dst.y,256,256));
                commands.DispatchCompute(shader,kernel,32,32,1);
            }
            else
            {
                properties.Clear();properties.SetFloat("_HasBefore",before!=null?1:0);
                properties.SetTexture("_Before",(Texture)before?.Page.Texture??Texture2D.blackTexture);
                properties.SetVector("_BeforeOffset",beforeOffset);
                properties.SetVector("_LogicalOffset",logicalOffset);properties.SetVector("_BrushStrokeMap",strokeMap);
                properties.SetTexture("_BrushStroke",pendingTile.Page.Texture);
                commands.SetRenderTarget(after.Page.Texture);var viewport=new Rect(dst.x,dst.y,256,256);
                commands.SetViewport(viewport);commands.EnableScissorRect(viewport);
                commands.DrawProcedural(Matrix4x4.identity,composite,0,MeshTopology.Triangles,3,1,properties);commands.DisableScissorRect();
            }
        }
        static readonly string[] FloatProperties={"_StrokeOpacity","_StrokeMode","_HdrBlend","_BrushStandard","_BrushErase","_BrushStampAccumulation","_BrushPressureOpacity","_BrushUseSelection","_BrushSelectionWrap","_HasOriginalStraight","_OriginalStraightGamma"};
        static readonly string[] CommonVectorProperties={"_WriteChannels","_BrushWriteChannels","_BrushSurfaceSize","_BrushDirtyRect","_BrushCanvasRow0","_BrushCanvasRow1","_BrushCanvasRow2"};
        void ConfigureCompute()
        {
            var shader=shaders.ComputeCommit;
            foreach(string name in FloatProperties)commands.SetComputeFloatParam(shader,name,composite.GetFloat(name));
            foreach(string name in CommonVectorProperties)commands.SetComputeVectorParam(shader,name,composite.GetVector(name));
            commands.SetComputeTextureParam(shader,0,"_BrushSelection",(Texture)selection?.Texture??Texture2D.blackTexture);
            commands.SetComputeTextureParam(shader,0,"_BrushOriginalStraight",Texture2D.blackTexture);
            commands.SetComputeTextureParam(shader,0,"_OriginalStraight",(Texture)originalStraight?.Texture??Texture2D.blackTexture);
        }

        public TiledPaintSnapshot AcquireSnapshot(){Check();source.Validate();return new TiledPaintSnapshot(source);}
        // Borrowed pending ink (before opacity/mask/commit). Valid only until the
        // next Append, Commit, Cancel or Dispose. Use CopyRegion for final preview.
        public IEnumerable<TiledPaintTileView> PendingTiles
        {
            get
            {
                CheckStroke();
                foreach(var pair in slots)
                {
                    CheckStroke();pair.Value.Page.Allocation.Validate();
                    if(dirty.Contains(pair.Key))yield return new TiledPaintTileView(pair.Value);
                }
            }
        }
        public void TrimIdle(){CheckMutation();if(Pending)throw new InvalidOperationException("Trim between strokes.");pool.TrimIdle();}
        public void Restore(TiledPaintSnapshot snapshot)
        {
            CheckMutation();if(Pending)throw new InvalidOperationException("Cancel the active stroke before restoring.");
            if(snapshot==null||snapshot.Frame!=Frame)throw new ArgumentException("Snapshot belongs to another source frame.");
            var state=snapshot.State;if(state.OwnerToken!=pool)throw new ArgumentException("Snapshot belongs to another surface.");
            state.Validate();state.Retain();var old=source;source=state;old.Release();
        }
        public void CopyRegion(RectInt region,RenderTexture target)
        {
            CheckMutation();source.Validate();
            ValidateRegion(region);RequireExactCopy(null);
            if(region.width<1||region.height<1||target==null||target.antiAliasing!=1||target.dimension!=TextureDimension.Tex2D||
                target.width!=region.width||target.height!=region.height||target.graphicsFormat!=GraphicsFormat.R16G16B16A16_SFloat)
                throw new ArgumentException("CopyRegion requires a matching-size linear RGBAHalf target.");
            if(pool.OwnsTexture(target)||target==selection?.Texture||target==originalStraight?.Texture)throw new ArgumentException("Cannot overwrite source/history storage or frozen inputs.");
            if(!target.IsCreated()&&!target.Create())throw new InvalidOperationException("Cannot allocate tiled copy target.");
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                commands.Clear();commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,target.width,target.height));commands.ClearRenderTarget(false,true,Color.clear);
                foreach(var pair in source.Tiles)
                {
                    var logical=new RectInt(pair.Key*256,new Vector2Int(256,256));
                    int x=Math.Max(region.xMin,logical.xMin),y=Math.Max(region.yMin,logical.yMin),r=Math.Min(region.xMax,logical.xMax),t=Math.Min(region.yMax,logical.yMax);
                    if(r<=x||t<=y)continue;var v=pair.Value;var origin=v.Origin;
                    commands.CopyTexture(v.Page.Texture,0,0,origin.x+x-logical.x,origin.y+y-logical.y,r-x,t-y,target,0,0,x-region.x,y-region.y);
                }
                GL.sRGBWrite=false;Graphics.ExecuteCommandBuffer(commands);
            }
            finally {commands.Clear();RenderTexture.active=previous;GL.sRGBWrite=srgb;}
        }
        public TiledPaintDelta ImportTiles(IReadOnlyList<TiledPaintImportTile> tiles,bool replace=false)
        {
            CheckMutation();if(Pending)throw new InvalidOperationException("Import between strokes.");if(tiles==null)throw new ArgumentNullException(nameof(tiles));
            if(tiles.Count>options.MaxBatchTileReferences)throw new ArgumentOutOfRangeException(nameof(tiles));
            var unique=new HashSet<Vector2Int>();
            foreach(var tile in tiles)
            {
                var r=tile.SourceRect;long x=(long)tile.Key.x*256,y=(long)tile.Key.y*256;
                if(!unique.Add(tile.Key)||Math.Abs(x)>options.CoordinateLimit||Math.Abs(y)>options.CoordinateLimit||tile.Texture==null||tile.Texture.dimension!=TextureDimension.Tex2D||
                    tile.Texture.graphicsFormat!=GraphicsFormat.R16G16B16A16_SFloat||r.width!=256||r.height!=256||r.x<0||r.y<0||(long)r.x+256>tile.Texture.width||(long)r.y+256>tile.Texture.height)
                    throw new ArgumentException("Import requires unique bounded tile keys and exact 256x256 linear RGBAHalf source regions.");
                if(tile.Texture is RenderTexture rt)
                {
                    if(!rt.IsCreated())throw new TiledPaintResourceLostException();
                    if(rt.antiAliasing!=1)throw new NotSupportedException("Import requires a single-sample source texture.");
                }
                RequireExactCopy(tile.Texture);
            }
            var rented=new List<TiledVersion>(tiles.Count);TiledSnapshotState next=null;TiledPaintDelta delta=null;
            try
            {
                if(!replace)source.Validate();commands.Clear();
                var map=replace?new Dictionary<Vector2Int,TiledVersion>():new Dictionary<Vector2Int,TiledVersion>(source.Tiles);
                foreach(var tile in tiles)
                {
                    var v=pool.RentVersion(pool.FindAllocation(tile.Texture));rented.Add(v);map[tile.Key]=v;
                    var r=tile.SourceRect;var o=v.Origin;commands.CopyTexture(tile.Texture,0,0,r.x,r.y,256,256,v.Page.Texture,0,0,o.x,o.y);
                }
                if(replace)foreach(var key in source.Tiles.Keys)unique.Add(key);
                var changed=new Vector2Int[unique.Count];unique.CopyTo(changed);Array.Sort(changed,CompareKeys);
                next=new TiledSnapshotState(Frame,map,pool);delta=new TiledPaintDelta(source,next,changed);
                Graphics.ExecuteCommandBuffer(commands);var old=source;source=next;next=null;old.Release();return delta;
            }
            catch {delta?.Dispose();next?.Release();throw;}
            finally {foreach(var v in rented)v.Release();commands.Clear();}
        }

        public void Validate(){Check();source.Validate();selection?.Validate();originalStraight?.Validate();foreach(var p in pending)if(p.Texture==null||!p.Texture.IsCreated())throw new TiledPaintResourceLostException();}
        public void Cancel(){CheckMutation();ClearStroke();}
        void ClearStroke()
        {
            foreach(var page in pending)if(page.Allocation.PendingPin)pool.ReleasePending(page.Allocation);
            pending.Clear();insidePages.Clear();outsidePages.Clear();active.Clear();slots.Clear();dirty.Clear();insideCount=outsideCount=0;reservedTile=null;
            selection?.Dispose();originalStraight?.Dispose();selection=originalStraight=null;Pending=false;
        }
        void Check(){pool.CheckThread();if(disposed)throw new ObjectDisposedException(nameof(TiledPaintSurface));}
        void CheckMutation(){Check();if(appending)throw new InvalidOperationException("A raster batch cannot re-enter its surface.");}
        void CheckStroke(){Check();if(!Pending)throw new InvalidOperationException("Begin a stroke first.");}
        void ValidateRegion(RectInt r)
        {
            if(r.width<1||r.height<1||Math.Abs((long)r.x)>options.CoordinateLimit||Math.Abs((long)r.y)>options.CoordinateLimit||
                Math.Abs((long)r.x+r.width)>options.CoordinateLimit||Math.Abs((long)r.y+r.height)>options.CoordinateLimit)
                throw new ArgumentOutOfRangeException(nameof(r));
        }
        static void RequireExactCopy(Texture source)
        {
            var required=CopyTextureSupport.Basic;
            if(source!=null&&!(source is RenderTexture))required|=CopyTextureSupport.TextureToRT;
            if((SystemInfo.copyTextureSupport&required)!=required)throw new NotSupportedException("This device cannot copy paint inputs without conversion.");
        }
        void ValidateBounds(Rect r)
        {
            float x=r.x,y=r.y,right=r.xMax,top=r.yMax;
            if(!float.IsFinite(x)||!float.IsFinite(right)||!float.IsFinite(y)||!float.IsFinite(top)||r.width<0||r.height<0||
                Math.Max(Math.Abs((double)x*Frame.SourceWidth),Math.Abs((double)right*Frame.SourceWidth))>options.CoordinateLimit||
                Math.Max(Math.Abs((double)y*Frame.SourceHeight),Math.Abs((double)top*Frame.SourceHeight))>options.CoordinateLimit)
                throw new ArgumentOutOfRangeException(nameof(r),"Non-finite or excessive source bounds.");
        }
        RectInt TileBounds(Rect r,bool guard)
        {
            int x=Mathf.FloorToInt((Mathf.Floor(r.xMin*Frame.SourceWidth)-(guard?1:0))/256);
            int y=Mathf.FloorToInt((Mathf.Floor(r.yMin*Frame.SourceHeight)-(guard?1:0))/256);
            int right=guard?Mathf.FloorToInt(Mathf.Ceil(r.xMax*Frame.SourceWidth)/256)+1:Mathf.CeilToInt(r.xMax*Frame.SourceWidth/256);
            int top=guard?Mathf.FloorToInt(Mathf.Ceil(r.yMax*Frame.SourceHeight)/256)+1:Mathf.CeilToInt(r.yMax*Frame.SourceHeight/256);
            return new RectInt(x,y,Math.Max(0,right-x),Math.Max(0,top-y));
        }
        static int CompareKeys(Vector2Int a,Vector2Int b){int y=a.y.CompareTo(b.y);return y!=0?y:a.x.CompareTo(b.x);}
        public void Dispose()
        {
            pool.CheckThread();if(disposed)return;CheckMutation();ClearStroke();disposed=true;source.Release();commands.Release();Object.DestroyImmediate(composite);pool.Dispose();
        }

        sealed class FrozenTexture:IDisposable
        {
            public readonly RenderTexture Texture;
            readonly TiledPagePool owner;readonly long bytes;bool disposed;
            FrozenTexture(TiledPagePool owner,Texture texture)
            {
                this.owner=owner;
                RequireExactCopy(texture);
                if(texture.dimension!=TextureDimension.Tex2D||texture.width<1||texture.height<1||
                    !SystemInfo.IsFormatSupported(texture.graphicsFormat,GraphicsFormatUsage.Render))throw new NotSupportedException("The input cannot be frozen as an exact 2D texture.");
                if(texture is RenderTexture rt&&(!rt.IsCreated()||rt.antiAliasing!=1))throw new TiledPaintResourceLostException();
                bytes=checked((long)texture.width*texture.height*GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat));
                owner.ReserveExternal(bytes);
                try
                {
                    Texture=new RenderTexture(new RenderTextureDescriptor(texture.width,texture.height){graphicsFormat=texture.graphicsFormat,depthBufferBits=0,msaaSamples=1,useMipMap=false})
                        {hideFlags=HideFlags.HideAndDontSave,filterMode=texture.filterMode,wrapModeU=texture.wrapModeU,wrapModeV=texture.wrapModeV,name="Frozen paint input"};
                    if(!Texture.Create())throw new InvalidOperationException("Failed to freeze paint input.");
                    Graphics.CopyTexture(texture,0,0,Texture,0,0);
                }
                catch {if(Texture!=null){Texture.Release();Object.DestroyImmediate(Texture);}owner.ReleaseExternal(bytes);throw;}
            }
            public static FrozenTexture Capture(TiledPagePool owner,Texture texture)
            {
                if(ReferenceEquals(texture,null))return null;if(texture==null)throw new TiledPaintResourceLostException();return new FrozenTexture(owner,texture);
            }
            public void Validate(){if(disposed||Texture==null||!Texture.IsCreated())throw new TiledPaintResourceLostException();}
            public void Dispose(){if(disposed)return;disposed=true;Texture.Release();Object.DestroyImmediate(Texture);owner.ReleaseExternal(bytes);}
        }
    }
}
