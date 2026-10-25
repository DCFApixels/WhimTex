using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Persistent native hints, bounded image requests and coarse-to-fine contrast cuts.
    // Take/Accept run on the owner; all mask work and graph solves run on one worker.
    internal sealed class QuickSelection : IDisposable
    {
        internal const int TileSize=256;
        internal const byte Unknown=128;
        internal readonly int width,height,columns,rows;
        private readonly float tolerance;
        private readonly bool subtract;
        private Dictionary<int,byte[]> markers=new Dictionary<int,byte[]>();
        private readonly ConcurrentQueue<Brush> strokes=new ConcurrentQueue<Brush>();
        private readonly Queue<Request> requests=new Queue<Request>();
        private QuickSelection previous;
        private byte[] baseline;
        private Color[] coarsePixels;
        private int coarseStep,step,gw,gh;
        private byte[] labels,positive,negative;
        private readonly Dictionary<int,int> nodes=new Dictionary<int,int>();
        private readonly List<int> cells=new List<int>();
        private Color[] colors;
        private float[] links;
        private bool haveAdd,haveSubtract,needStart=true,done;
        private bool flatSource;
        private readonly object inputGate=new object();
        private int inputRevision,calculationRevision;
        private bool previewPass;
        private Result previewResult;
        private int unacceptedTile=-1;
        internal bool Progressive {get;set;}
        internal bool RefinementAllowed {get;set;}=true;
        internal bool WaitingToRefine=>requests.Count>0&&requests.Peek().kind==5;
        internal int InterruptedSolves {get;private set;}
        private int solving;
        internal bool IsSolving=>Volatile.Read(ref solving)!=0;
        internal bool IsRefined=>!needStart&&requests.Count==0&&strokes.IsEmpty&&calculationRevision==InputRevision;
        internal int InputRevision=>Volatile.Read(ref inputRevision);
        internal void Interrupt()=>Interlocked.Increment(ref inputRevision);
        internal bool IsCurrent(Result result)=>result.revision==InputRevision;
        private QuickSelectionRaster.Mask sweep;
        private List<List<int>> partitions;
        private int partition;
        private bool sweepChanged;
        private bool sweepExpanded;
        // Coarse graph is at most 512 squared; larger refinement bands are relaxed
        // in image blocks with shared neighbour labels, until stable.
        internal int GraphNodeBudget {get;set;}=524288;
        private long peakBytes;
        internal double SolveMs {get;private set;}
        internal double BuildMs {get;private set;}
        internal double RefineMs {get;private set;}
        internal double PublishMs {get;private set;}
        internal int MaxNodes {get;private set;}
        internal int BlockSweeps {get;private set;}
        private readonly RectInt[] bounds;
        private readonly List<Vector4>[] outlines,seams;
        private const int OutlineBudget=16384;
        private int outlineStep=1;
        private byte[] workingCoverage;
        private byte[] publishedExisting;
        private RectInt[] outputBounds;
        private byte[] outputUniform;
        internal QuickSelection History=>previous?.History??this;
        internal bool Wrap {get;set;}
        internal int Pending=>requests.Count+(needStart||!strokes.IsEmpty?1:0);
        internal bool IsFinalized=>done&&strokes.IsEmpty;
        internal long MarkerBytes {get{long n=0;foreach(var v in markers.Values)n+=v.LongLength;return n;}}
        internal long DistanceBytes=>peakBytes;
        internal int Processed {get;private set;}
        internal byte[] InitialCoverage {get;private set;}
        private readonly struct Brush
        {
            internal readonly Vector2 point;internal readonly float radius;internal readonly byte hint;
            internal Brush(Vector2 p,float r,byte h){point=p;radius=r;hint=h;}
        }
        internal sealed class Request
        {
            internal int index,step=1,kind;
            internal RectInt tile,source;
            internal bool cleanup;
            internal List<int> samples;
        }
        internal sealed class Result
        {
            internal RectInt region,bounds;
            internal byte[] coverage;
            internal Vector4[] edges;
            internal bool changed;
            internal bool preview;
            internal int revision=-1,previewWidth,previewHeight,previewStep;
        }
        internal QuickSelection(int width,int height,float tolerance,bool subtract)
        {
            if(width<1||height<1||(long)width*height>CanvasSelection.MaximumPixels)throw new ArgumentOutOfRangeException(nameof(width));
            this.width=width;this.height=height;this.tolerance=Mathf.Clamp(tolerance,.01f,1);this.subtract=subtract;
            columns=(width+TileSize-1)/TileSize;rows=(height+TileSize-1)/TileSize;
            bounds=new RectInt[columns*rows];outlines=new List<Vector4>[bounds.Length];seams=new List<Vector4>[bounds.Length];
            coarseStep=1;while((width+coarseStep-1)/coarseStep>512||(height+coarseStep-1)/coarseStep>512)coarseStep*=2;
        }
        internal void ReuseLayout(QuickSelection value)
        {if(value!=null&&value.width==width&&value.height==height)previous=value;}
        internal void Initialize(byte[] existing,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();previous=previous?.History;
            if(previous!=null)
            {
                markers=previous.markers;baseline=previous.baseline;coarsePixels=previous.coarsePixels;
                InitialCoverage=previous.InitialCoverage;previous=null;
            }
            else {baseline=existing==null?null:(byte[])existing.Clone();InitialCoverage=existing==null?new byte[width*height]:null;}
            for(int i=0;i<bounds.Length;i++)
            {
                token.ThrowIfCancellationRequested();var tile=Tile(i);
                bounds[i]=existing==null?default:FindBounds(tile,existing,width,tile.y*width+tile.x);
            }
            RebuildContours((x,y)=>Read(existing,x,y),token);
        }
        internal void FinishInput(CancellationToken token){token.ThrowIfCancellationRequested();if(requests.Count==0&&!needStart&&strokes.IsEmpty)done=true;}
        internal int MarkerAt(int x,int y)
        {
            if(x<0||y<0||x>=width||y>=height)return Unknown;
            int i=y/TileSize*columns+x/TileSize;
            return markers.TryGetValue(i,out var values)?values[(y%TileSize)*Tile(i).width+x%TileSize]:Unknown;
        }
        internal static byte EncodeHint(bool negative,float strength)
        {strength=float.IsFinite(strength)?Mathf.Clamp01(strength):1;return (byte)(128+(negative?-Mathf.RoundToInt(128*strength):Mathf.RoundToInt(127*strength)));}
        internal RectInt Tile(int i)=>new RectInt(i%columns*TileSize,i/columns*TileSize,Math.Min(TileSize,width-i%columns*TileSize),Math.Min(TileSize,height-i/columns*TileSize));
        internal void AddBrush(Vector2 point,float radius)=>AddWeightedBrush(point,radius,subtract,false,1);
        internal void AddWrappedBrush(Vector2 point,float radius)=>AddWeightedBrush(point,radius,subtract,true,1);
        internal void AddStrokeBrush(Vector2 point,float radius,bool negative,bool wrap)=>AddWeightedBrush(point,radius,negative,wrap,1);
        internal void AddWeightedBrush(Vector2 point,float radius,bool negative,bool wrap,float strength)
        {
            byte hint=EncodeHint(negative,strength);radius=Mathf.Clamp(radius,.5f,1024);
            lock(inputGate)
            {
                Interrupt();
                if(!wrap){strokes.Enqueue(new Brush(point,radius,hint));return;}
                point=new Vector2(Mathf.Repeat(point.x,width),Mathf.Repeat(point.y,height));
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)strokes.Enqueue(new Brush(point+new Vector2(x*width,y*height),radius,hint));
            }
        }
        internal Request Take()
        {
            // Finish the bounded preview snapshot even during continuous input. New hints
            // queue the next preview; expensive refinement is always interruptible.
            if(previewPass&&requests.Count>0)return requests.Dequeue();
            if(needStart||!strokes.IsEmpty||calculationRevision!=InputRevision)return Work(0);
            if(WaitingToRefine&&!RefinementAllowed)return null;
            return requests.Count>0?requests.Dequeue():null;
        }
        internal void Accept(Result result) { if(result.changed&&IsCurrent(result))unacceptedTile=-1; }
        private static Request Work(int kind)=>new Request{cleanup=true,kind=kind,index=-1};
        internal Result Grow(Request request,Color[] pixels,byte[] existing,CancellationToken token)=>GrowCore(request,pixels,existing,token,false);
        internal Result GrowRaw(Request request,Color[] pixels,byte[] existing,CancellationToken token)=>GrowCore(request,pixels,existing,token,true);
        private Result GrowCore(Request request,Color[] pixels,byte[] existing,CancellationToken token,bool raw)
        {
            token.ThrowIfCancellationRequested();Processed++;publishedExisting=existing;
            try
            {
            if(request.kind>=2&&!previewPass)CheckCurrent(token);
            if(request.kind==0){Start(token);return new Result();}
            if(request.kind==1)
            {
                coarsePixels=new Color[pixels.Length];for(int i=0;i<pixels.Length;i++)coarsePixels[i]=raw?Feature(pixels[i]):pixels[i];
                BeginCoarse(token,Progressive);return new Result();
            }
            if(request.kind==2){ReadSamples(request,pixels,raw);return new Result();}
            if(request.kind==3){Cut(token);var result=previewResult;previewResult=null;return result??new Result();}
            if(request.kind==4)return Publish(request.index,existing,token);
            if(request.kind==5){BeginCoarse(token,false);return new Result();}
            throw new InvalidOperationException("Unknown selection work request.");
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested)
            {InterruptedSolves++;needStart=true;requests.Clear();previewResult=null;return new Result();}
        }
        private void CheckCurrent(CancellationToken token)
        {token.ThrowIfCancellationRequested();if(calculationRevision!=InputRevision)throw new OperationCanceledException();}
        private void Start(CancellationToken token)
        {
            RepairUnacceptedTile();
            requests.Clear();sweep=null;partitions=null;needStart=false;done=false;previewResult=null;previewPass=false;
            var batch=new List<Brush>();
            lock(inputGate){calculationRevision=InputRevision;while(strokes.TryDequeue(out var value))batch.Add(value);}
            foreach(var brush in batch)
            {
                token.ThrowIfCancellationRequested();int cx=Mathf.FloorToInt(brush.point.x),cy=Mathf.FloorToInt(brush.point.y);
                int left=Math.Max(0,(int)Math.Floor(cx-brush.radius)),right=Math.Min(width-1,(int)Math.Ceiling(cx+brush.radius));
                int bottom=Math.Max(0,(int)Math.Floor(cy-brush.radius)),top=Math.Min(height-1,(int)Math.Ceiling(cy+brush.radius));
                for(int ty=bottom/TileSize;ty<=top/TileSize;ty++)for(int tx=left/TileSize;tx<=right/TileSize;tx++)
                {
                    int index=ty*columns+tx;if(index<0||index>=bounds.Length)continue;var tile=Tile(index);
                    markers.TryGetValue(index,out var values);float rr=brush.radius*brush.radius;
                    for(int y=Math.Max(bottom,tile.y);y<=Math.Min(top,tile.yMax-1);y++)for(int x=Math.Max(left,tile.x);x<=Math.Min(right,tile.xMax-1);x++)
                    {
                        if((x-cx)*(x-cx)+(y-cy)*(y-cy)>rr)continue;
                        if(values==null){if(brush.hint==Unknown)continue;values=new byte[tile.width*tile.height];Array.Fill(values,Unknown);markers[index]=values;}
                        values[(y-tile.y)*tile.width+x-tile.x]=brush.hint;
                    }
                    if(values!=null){bool any=false;foreach(byte v in values)if(v!=Unknown){any=true;break;}if(!any)markers.Remove(index);}
                }
            }
            haveAdd=haveSubtract=false;
            foreach(var tile in markers.Values)foreach(byte v in tile){haveAdd|=v>128;haveSubtract|=v<128;}
            if(!haveAdd&&!haveSubtract){labels=baseline==null?null:(byte[])baseline.Clone();QueuePublish();return;}
            if(!haveAdd&&baseline==null){step=1;gw=width;gh=height;labels=null;QueuePublish();return;}
            if(coarsePixels==null)requests.Enqueue(new Request{kind=1,index=-2,source=new RectInt(0,0,width,height),step=coarseStep});
            else BeginCoarse(token,Progressive);
        }
        private void BuildHints(CancellationToken token)
        {
            positive=negative=null;if(step==1)return;
            positive=new byte[gw*gh];negative=new byte[gw*gh];
            foreach(var pair in markers)
            {
                token.ThrowIfCancellationRequested();var tile=Tile(pair.Key);var data=pair.Value;
                for(int y=0;y<tile.height;y++)for(int x=0;x<tile.width;x++)
                {
                    byte v=data[y*tile.width+x];if(v==128)continue;int cell=(tile.y+y)/step*gw+(tile.x+x)/step;
                    if(v>128)positive[cell]=Math.Max(positive[cell],(byte)(v-128));else negative[cell]=Math.Max(negative[cell],(byte)(128-v));
                }
            }
        }
        private void Hint(int cell,out float add,out float sub)
        {
            if(step>1){add=positive[cell]/127f;sub=negative[cell]/128f;return;}
            int v=MarkerAt(cell%gw,cell/gw);add=v>128?(v-128)/127f:0;sub=v<128?(128-v)/128f:0;
        }
        private void BeginCoarse(CancellationToken token,bool preview)
        {
            previewPass=preview;step=coarseStep;
            if(preview)while((width+step-1)/step>128||(height+step-1)/step>128)step*=2;
            gw=(width+step-1)/step;gh=(height+step-1)/step;labels=new byte[gw*gh];
            BuildHints(token);nodes.Clear();cells.Clear();for(int i=0;i<labels.Length;i++){nodes[i]=cells.Count;cells.Add(i);}
            int cw=(width+coarseStep-1)/coarseStep,ch=(height+coarseStep-1)/coarseStep;
            colors=new Color[labels.Length];int ratio=step/coarseStep;
            for(int y=0;y<gh;y++)for(int x=0;x<gw;x++)
                colors[y*gw+x]=coarsePixels[Math.Min(ch-1,y*ratio+ratio/2)*cw+Math.Min(cw-1,x*ratio+ratio/2)];
            links=new float[cells.Count*4];flatSource=true;
            for(int i=0;i<cells.Count;i++)for(int d=0;d<4;d++)
            {int other=Neighbour(i,d);if(other>=0){float weight=Weight(colors[i],colors[other]);links[i*4+d]=weight;if(weight<.999f)flatSource=false;}}
            requests.Enqueue(Work(3));
        }
        private int Neighbour(int cell,int direction)
        {
            int x=cell%gw,y=cell/gw;
            if(direction==0)x++;else if(direction==1)y++;else if(direction==2)x--;else y--;
            if(Wrap){x=(x+gw)%gw;y=(y+gh)%gh;}
            return x>=0&&x<gw&&y>=0&&y<gh?y*gw+x:-1;
        }
        private float Weight(Color a,Color b)
        {
            float r=a.r-b.r,g=a.g-b.g,blue=a.b-b.b,alpha=a.a-b.a;
            float sigma=Math.Max(.001f,tolerance*.15f),d=(r*r+g*g+blue*blue+alpha*alpha)/(sigma*sigma);
            return .00001f+1f/(1f+d*d);
        }
        internal static Color Feature(Color c)
        {
            float a=float.IsFinite(c.a)?Mathf.Clamp01(c.a):0;
            float F(float x)=>float.IsFinite(x)?x/(1+Mathf.Abs(x)):0;
            return new Color(F(c.r)*a,F(c.g)*a,F(c.b)*a,a*.7f);
        }
        private void AddBand(QuickSelectionRaster.Mask active,int cell,int radius)
        {
            int cx=cell%gw,cy=cell/gw;
            for(int yy=cy-radius;yy<=cy+radius;yy++)for(int xx=cx-radius;xx<=cx+radius;xx++)
            {int x=xx,y=yy;if(Wrap){x=(x%gw+gw)%gw;y=(y%gh+gh)%gh;}if(x>=0&&x<gw&&y>=0&&y<gh)active.Add(y*gw+x);}
        }
        private void Refine(CancellationToken token)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var old=labels;int oldWidth=gw;step/=2;gw=(width+step-1)/step;gh=(height+step-1)/step;
            labels=new byte[gw*gh];var active=new QuickSelectionRaster.Mask(labels.Length);
            QuickSelectionRaster.Refine(old,labels,oldWidth,gw,gh,active,Wrap);token.ThrowIfCancellationRequested();
            BuildHints(token);
            foreach(var pair in markers)
            {
                token.ThrowIfCancellationRequested();var tile=Tile(pair.Key);var data=pair.Value;
                for(int y=0;y<tile.height;y++)for(int x=0;x<tile.width;x++)
                {
                    byte v=data[y*tile.width+x];if(v==128)continue;int cell=(tile.y+y)/step*gw+(tile.x+x)/step;
                    if((v>128)!=(labels[cell]!=0))AddBand(active,cell,2);
                }
            }
            PlanSamples(active,token);RefineMs+=timer.Elapsed.TotalMilliseconds;
        }
        private void PlanSamples(QuickSelectionRaster.Mask active,CancellationToken token)
        {
            var all=new List<int>();foreach(int cell in active.Indices())all.Add(cell);
            if(all.Count>GraphNodeBudget)
            {
                sweep=active;sweepChanged=false;sweepExpanded=false;partition=0;BlockSweeps++;
                int side=Math.Max(16,(int)Math.Sqrt(GraphNodeBudget)),cw=(gw+side-1)/side;
                var groups=new SortedDictionary<int,List<int>>();
                foreach(int cell in all)
                {int key=cell/gw/side*cw+cell%gw/side;if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<int>();list.Add(cell);}
                partitions=new List<List<int>>(groups.Values);PlanNodes(partitions[0],token);return;
            }
            PlanNodes(all,token);
        }
        private void PlanNodes(List<int> active,CancellationToken token)
        {
            nodes.Clear();cells.Clear();var blocks=new Dictionary<int,List<int>>();int bw=(gw+255)/256;
            int left=gw,bottom=gh,rightEdge=0,topEdge=0;
            foreach(int i in active)
            {
                if((i&65535)==0)token.ThrowIfCancellationRequested();nodes[i]=cells.Count;cells.Add(i);
                int px=i%gw,py=i/gw;left=Math.Min(left,px);bottom=Math.Min(bottom,py);rightEdge=Math.Max(rightEdge,px+1);topEdge=Math.Max(topEdge,py+1);
                int b=i/gw/256*bw+i%gw/256;if(!blocks.TryGetValue(b,out var list))blocks[b]=list=new List<int>();list.Add(cells.Count-1);
            }
            colors=new Color[cells.Count];links=new float[cells.Count*4];
            left=Math.Max(0,left-1);bottom=Math.Max(0,bottom-1);rightEdge=Math.Min(gw,rightEdge+1);topEdge=Math.Min(gh,topEdge+1);
            // Compact contours fit in one read, avoiding an idle-editor/GPU round trip
            // for each quadrant. Padding includes the refinement band and sample halo.
            if(cells.Count>0&&rightEdge-left<=528&&topEdge-bottom<=528)
            {
                var samples=new List<int>(cells.Count);for(int i=0;i<cells.Count;i++)samples.Add(i);
                requests.Enqueue(new Request{kind=2,index=-3,step=step,samples=samples,
                    source=new RectInt(left*step,bottom*step,Math.Min(width,rightEdge*step)-left*step,Math.Min(height,topEdge*step)-bottom*step)});
                requests.Enqueue(Work(3));return;
            }
            foreach(var pair in blocks)
            {
                int x=Math.Max(0,pair.Key%bw*256-1),y=Math.Max(0,pair.Key/bw*256-1);
                int right=Math.Min(gw,(pair.Key%bw+1)*256+1),top=Math.Min(gh,(pair.Key/bw+1)*256+1);
                var source=new RectInt(x*step,y*step,Math.Min(width,right*step)-x*step,Math.Min(height,top*step)-y*step);
                requests.Enqueue(new Request{kind=2,index=-3,source=source,step=step,samples=pair.Value});
            }
            requests.Enqueue(Work(3));
        }
        private void ReadSamples(Request request,Color[] pixels,bool raw)
        {
            int rw=(request.source.width+step-1)/step,rh=(request.source.height+step-1)/step;
            if(pixels.Length!=rw*rh)throw new ArgumentException("Invalid graph selection source region.");
            Color At(int x,int y){var c=pixels[y*rw+x];return raw?Feature(c):c;}
            foreach(int node in request.samples)
            {
                int cell=cells[node],x=cell%gw-request.source.x/step,y=cell/gw-request.source.y/step;
                Color center=At(x,y);colors[node]=center;
                for(int d=0;d<4;d++)
                {
                    int nx=x+(d==0?1:d==2?-1:0),ny=y+(d==1?1:d==3?-1:0);
                    if(nx>=0&&nx<rw&&ny>=0&&ny<rh){float weight=Weight(center,At(nx,ny));links[node*4+d]=weight;if(weight<.999f)flatSource=false;}
                }
            }
        }
        private void Cut(CancellationToken token)
        {
            if(!previewPass)CheckCurrent(token);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            int n=cells.Count;var graph=new QuickSelectionCut(n,n*8+8);
            MaxNodes=Math.Max(MaxNodes,n);
            float area=.00005f*step;
            // A constant repeated image has neither a contrast edge nor an outer
            // perimeter. Scale its area prior so a small Add still stays local.
            if(Wrap&&flatSource)area=Math.Max(area,4f/Math.Min(gw,gh));
            for(int i=0;i<n;i++)
            {
                if((i&4095)==0){if(previewPass)token.ThrowIfCancellationRequested();else CheckCurrent(token);}int cell=cells[i];Hint(cell,out float add,out float sub);
                float fg=area,bg=0;
                // Soft hints are finite unary costs; hard endpoints dominate every incident edge.
                if(add>=1&&sub==0)bg+=32;else if(add>0)bg+=4*add*add;
                if(sub>=1&&add==0)fg+=32;else if(sub>0)fg+=4*sub*sub;
                if(baseline!=null&&baseline[Math.Min(height-1,cell/gw*step+step/2)*width+Math.Min(width-1,cell%gw*step+step/2)]>=128)bg+=area*2;
                for(int d=0;d<4;d++)
                {
                    int other=Neighbour(cell,d);
                    // The image rectangle also bounds the selected area; it is a soft
                    // perimeter cost, not a hard background label on edge pixels.
                    if(other<0){fg+=1;continue;}
                    float weight=links[i*4+d];bool active=nodes.TryGetValue(other,out int j);
                    if(Wrap&&(Math.Abs(other%gw-cell%gw)>1||Math.Abs(other/gw-cell/gw)>1)&&active)weight=Weight(colors[i],colors[j]);
                    if(active){if(j>i)graph.Link(i,j,weight);}
                    else if(labels[other]!=0)bg+=weight;else fg+=weight;
                }
                graph.Terminal(i,fg,bg);
            }
            peakBytes=Math.Max(peakBytes,graph.Bytes+labels.LongLength+(positive?.LongLength??0)+(negative?.LongLength??0)+n*64L+(coarsePixels?.LongLength??0)*16);
            BuildMs+=timer.Elapsed.TotalMilliseconds;timer.Restart();
            bool[] selected;
            Volatile.Write(ref solving,1);
            try{selected=graph.Solve(token,previewPass?null:()=>calculationRevision!=InputRevision);}
            finally{Volatile.Write(ref solving,0);}
            SolveMs+=timer.Elapsed.TotalMilliseconds;var expand=new List<int>();
            for(int i=0;i<n;i++)
            {
                int cell=cells[i];byte value=selected[i]?(byte)255:(byte)0;
                if(value!=labels[cell])sweepChanged=true;
                if(value!=labels[cell]&&value!=0&&step<coarseStep)
                {
                    for(int d=0;d<4;d++)
                    {
                        int p=Neighbour(cell,d);if(p<0)continue;int q=Neighbour(p,d);
                        bool Outside(int c)=>c>=0&&(sweep==null?!nodes.ContainsKey(c):(sweep.words[c>>5]&(1u<<(c&31)))==0);
                        if((Outside(p)||Outside(q))&&links[i*4+d]>.15f){expand.Add(cell);break;}
                    }
                }
                labels[cell]=value;
            }
            if(previewPass)
            {
                previewResult=BuildPreview();previewPass=false;
                requests.Enqueue(Work(5));return;
            }
            if(step<coarseStep)ExtendThinCandidates(expand,token);
            if(sweep!=null)
            {
                // Neighbour labels cross block boundaries. Continue full sweeps until
                // no block changes, instead of returning independent tile cuts with seams.
                foreach(int cell in expand)AddBand(sweep,cell,32);sweepExpanded|=expand.Count>0;
                if(++partition<partitions.Count){PlanNodes(partitions[partition],token);return;}
                if(sweepExpanded){var active=sweep;sweep=null;partitions=null;PlanSamples(active,token);return;}
                if(sweepChanged){partition=0;sweepChanged=false;BlockSweeps++;PlanNodes(partitions[0],token);return;}
                sweep=null;partitions=null;
            }
            if(expand.Count>0)
            {
                var active=new QuickSelectionRaster.Mask(labels.Length);foreach(int cell in cells)active.Add(cell);
                foreach(int cell in expand)AddBand(active,cell,32);PlanSamples(active,token);return;
            }
            if(step>1){Refine(token);return;}
            QueuePublish();
        }
        private void ExtendThinCandidates(List<int> expand,CancellationToken token)
        {
            // A narrow continuation clipped by the refinement band can otherwise be
            // cut off at its root: the artificial band edge costs as much as the root.
            // Explore strong-colour paths only to extend ridge-like candidates; flat
            // areas cannot trigger this expansion.
            int n=cells.Count;var reached=new bool[n];var queue=new int[n];int head=0,tail=0;
            for(int i=0;i<n;i++)if(labels[cells[i]]!=0){reached[i]=true;queue[tail++]=i;}
            while(head<tail)
            {
                if((head&4095)==0)token.ThrowIfCancellationRequested();int i=queue[head++],cell=cells[i];
                for(int d=0;d<4;d++)if(links[i*4+d]>.7f)
                {
                    int other=Neighbour(cell,d);if(other<0)continue;
                    if(nodes.TryGetValue(other,out int j)){if(!reached[j]){reached[j]=true;queue[tail++]=j;}continue;}
                    if(sweep!=null&&(sweep.words[other>>5]&(1u<<(other&31)))!=0)continue;
                    int a=d%2==0?1:0,b=d%2==0?3:2;
                    if(links[i*4+a]<.1f&&links[i*4+b]<.1f)expand.Add(cell);
                }
            }
        }
        private void QueuePublish()
        {
            var changed=new byte[bounds.Length];outputBounds=new RectInt[bounds.Length];outputUniform=new byte[bounds.Length];
            QuickSelectionRaster.Publication(labels,baseline,publishedExisting,width,height,haveSubtract,changed,outputBounds,outputUniform);
            for(int i=0;i<bounds.Length;i++)if(changed[i]!=0)requests.Enqueue(new Request{kind=4,cleanup=true,index=i,tile=Tile(i)});
        }
        public void Dispose(){coarsePixels=null;colors=null;links=null;positive=negative=null;labels=null;sweep=null;partitions=null;previewResult=null;nodes.Clear();cells.Clear();requests.Clear();}

        private Result BuildPreview()
        {
            var values=(byte[])labels.Clone();
            if(!haveSubtract&&baseline!=null)for(int y=0;y<gh;y++)for(int x=0;x<gw;x++)
                values[y*gw+x]=Math.Max(values[y*gw+x],baseline[Math.Min(height-1,y*step+step/2)*width+Math.Min(width-1,x*step+step/2)]);
            var edges=new List<Vector4>();
            bool Filled(int x,int y)=>x>=0&&y>=0&&x<gw&&y<gh&&values[y*gw+x]>=128;
            for(int y=0;y<=gh;y++)
            {
                int start=-1;for(int x=0;x<=gw;x++)
                {bool edge=x<gw&&Filled(x,y-1)!=Filled(x,y);if(edge&&start<0)start=x;
                    if(!edge&&start>=0){edges.Add(new Vector4(start*step,Math.Min(height,y*step),Math.Min(width,x*step),Math.Min(height,y*step)));start=-1;}}
            }
            for(int x=0;x<=gw;x++)
            {
                int start=-1;for(int y=0;y<=gh;y++)
                {bool edge=y<gh&&Filled(x-1,y)!=Filled(x,y);if(edge&&start<0)start=y;
                    if(!edge&&start>=0){edges.Add(new Vector4(Math.Min(width,x*step),start*step,Math.Min(width,x*step),Math.Min(height,y*step)));start=-1;}}
            }
            return new Result{preview=true,revision=calculationRevision,coverage=values,previewWidth=gw,previewHeight=gh,previewStep=step,edges=edges.ToArray()};
        }
        private void RepairUnacceptedTile()
        {
            if(unacceptedTile<0)return;
            int i=unacceptedTile;var tile=Tile(i);var existing=publishedExisting;
            bounds[i]=existing==null?default:FindBounds(tile,existing,width,tile.y*width+tile.x);
            outlines[i]=bounds[i].width>0?Outline(tile,(x,y)=>Read(existing,x,y)):null;
            seams[i]=Seams(tile,(x,y)=>Read(existing,x,y));
            if(tile.xMax<width)seams[i+1]=Seams(Tile(i+1),(x,y)=>Read(existing,x,y));
            if(tile.yMax<height)seams[i+columns]=Seams(Tile(i+columns),(x,y)=>Read(existing,x,y));
            if(outlineStep>TileSize||ContourCount()>OutlineBudget)RebuildContours((x,y)=>Read(existing,x,y),CancellationToken.None);
            unacceptedTile=-1;
        }

        private Result Publish(int index,byte[] existing,CancellationToken token)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            token.ThrowIfCancellationRequested();var tile=Tile(index);int count=tile.width*tile.height;
            if(workingCoverage==null||workingCoverage.Length!=count)workingCoverage=new byte[count];
            var result=new Result{region=tile,coverage=workingCoverage,revision=calculationRevision};
            unacceptedTile=index;
            if(labels==null)Array.Clear(workingCoverage,0,count);
            else for(int y=0;y<tile.height;y++)Buffer.BlockCopy(labels,(tile.y+y)*width+tile.x,workingCoverage,y*tile.width,tile.width);
            result.changed=true;
            byte ReadResult(int x,int y)=>tile.Contains(new Vector2Int(x,y))?workingCoverage[(y-tile.y)*tile.width+x-tile.x]:Read(existing,x,y);
            bounds[index]=outputBounds[index];
            outlines[index]=outputUniform[index]==2?Outline(tile,(x,y)=>workingCoverage[(y-tile.y)*tile.width+x-tile.x]):null;
            seams[index]=Seams(tile,ReadResult);
            if(tile.xMax<width)seams[index+1]=Seams(Tile(index+1),ReadResult);
            if(tile.yMax<height)seams[index+columns]=Seams(Tile(index+columns),ReadResult);
            if(outlineStep>TileSize||ContourCount()>OutlineBudget)RebuildContours(ReadResult,token);
            var all=new List<Vector4>(ContourCount());
            int minX=width,minY=height,maxX=0,maxY=0;
            for(int i=0;i<bounds.Length;i++)
            {
                var b=bounds[i];if(b.width>0){minX=Math.Min(minX,b.x);minY=Math.Min(minY,b.y);maxX=Math.Max(maxX,b.xMax);maxY=Math.Max(maxY,b.yMax);}
                if(outlines[i]!=null)all.AddRange(outlines[i]);
                if(seams[i]!=null)all.AddRange(seams[i]);
            }
            result.bounds=maxX>minX?new RectInt(minX,minY,maxX-minX,maxY-minY):default;result.edges=all.ToArray();PublishMs+=timer.Elapsed.TotalMilliseconds;return result;
        }
        private byte Read(byte[] mask, int x, int y) => mask != null && x >= 0 && y >= 0 && x < width && y < height ? mask[y*width+x] : (byte)0;
        private static RectInt FindBounds(RectInt tile, byte[] values, int stride, int offset)
        {
            int x0=tile.width,y0=tile.height,x1=0,y1=0;
            for(int y=0;y<tile.height;y++) for(int x=0;x<tile.width;x++) if(values[offset+y*stride+x]!=0)
            { x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x+1);y1=Math.Max(y1,y+1); }
            return x1>x0 ? new RectInt(tile.x+x0,tile.y+y0,x1-x0,y1-y0) : default;
        }
        private int ContourCount()
        {
            int count=0;foreach(var v in outlines)count+=v?.Count??0;foreach(var v in seams)count+=v?.Count??0;return count;
        }
        private void RebuildContours(Func<int,int,byte> read,CancellationToken token)
        {
            for(;;outlineStep*=2)
            {
                int total=0;
                if(outlineStep>TileSize)
                {
                    token.ThrowIfCancellationRequested();Array.Clear(outlines,0,outlines.Length);Array.Clear(seams,0,seams.Length);
                    var canvas=new RectInt(0,0,width,height);outlines[0]=Outline(canvas,read);seams[0]=Seams(canvas,read);
                    total=outlines[0].Count+seams[0].Count;
                }
                else for(int i=0;i<bounds.Length;i++)
                {
                    token.ThrowIfCancellationRequested();var tile=Tile(i);
                    outlines[i]=bounds[i].width>0?Outline(tile,read):null;seams[i]=Seams(tile,read);
                    total+=(outlines[i]?.Count??0)+seams[i].Count;if(total>OutlineBudget)break;
                }
                if(total<=OutlineBudget)return;
            }
        }
        private List<Vector4> Outline(RectInt tile,Func<int,int,byte> read)
        {
            int step=outlineStep;var edges=new List<Vector4>();
            bool Filled(int x,int y)=>read(Math.Min(width-1,x+step/2),Math.Min(height-1,y+step/2))>=128;
            for(int y=tile.y+step;y<tile.yMax;y+=step)
            {
                int start=-1;
                for(int x=tile.x;x<=tile.xMax;x=Math.Min(tile.xMax,x+step))
                {
                    bool edge=x<tile.xMax&&Filled(x,y-step)!=Filled(x,y);
                    if(edge&&start<0)start=x;
                    if(!edge&&start>=0){edges.Add(new Vector4(start,y,x,y));start=-1;if(edges.Count>OutlineBudget)return edges;}
                    if(x==tile.xMax)break;
                }
            }
            for(int x=tile.x+step;x<tile.xMax;x+=step)
            {
                int start=-1;
                for(int y=tile.y;y<=tile.yMax;y=Math.Min(tile.yMax,y+step))
                {
                    bool edge=y<tile.yMax&&Filled(x-step,y)!=Filled(x,y);
                    if(edge&&start<0)start=y;
                    if(!edge&&start>=0){edges.Add(new Vector4(x,start,x,y));start=-1;if(edges.Count>OutlineBudget)return edges;}
                    if(y==tile.yMax)break;
                }
            }
            return edges;
        }
        private List<Vector4> Seams(RectInt tile, Func<int,int,byte> read)
        {
            int step=outlineStep;var edges = new List<Vector4>();
            bool Filled(int x,int y)=>x>=0&&y>=0&&x<width&&y<height&&read(Math.Min(width-1,x+step/2),Math.Min(height-1,y+step/2))>=128;
            void Horizontal(int y)
            {
                int start=-1;
                int before=y==height?(height-1)/step*step:y-step;
                for(int x=tile.x;x<=tile.xMax;x=Math.Min(tile.xMax,x+step))
                {
                    bool edge=x<tile.xMax && Filled(x,before)!=Filled(x,y);
                    if(edge && start<0)start=x;
                    if(!edge && start>=0){edges.Add(new Vector4(start,y,x,y));start=-1;}
                    if(x==tile.xMax)break;
                }
            }
            void Vertical(int x)
            {
                int start=-1;
                int before=x==width?(width-1)/step*step:x-step;
                for(int y=tile.y;y<=tile.yMax;y=Math.Min(tile.yMax,y+step))
                {
                    bool edge=y<tile.yMax && Filled(before,y)!=Filled(x,y);
                    if(edge && start<0)start=y;
                    if(!edge && start>=0){edges.Add(new Vector4(x,start,x,y));start=-1;}
                    if(y==tile.yMax)break;
                }
            }
            Horizontal(tile.y); Vertical(tile.x);
            if(tile.xMax==width)Vertical(tile.xMax);
            if(tile.yMax==height)Horizontal(tile.yMax);
            return edges;
        }
    }
}
