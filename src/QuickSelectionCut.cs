using System;
using System.Threading;

namespace DCFApixels.WhimTex
{
    // FIFO push/relabel with periodic reverse breadth-first distance updates.
    // All storage and cancellation belong to the background selection worker.
    internal sealed class QuickSelectionCut
    {
        private readonly int[] head, next, to, height, current, queue;
        private readonly float[] capacity, excess;
        private readonly bool[] queued;
        private readonly int source, sink, count;
        private int edges, read, write, queuedCount;
        private const float Epsilon=1e-6f;
        internal long Bytes => (long)(head.Length+next.Length+to.Length+height.Length+current.Length+queue.Length)*4+
            (long)(capacity.Length+excess.Length)*4+queued.Length;
        internal QuickSelectionCut(int nodes,int edgeCapacity)
        {
            source=nodes;sink=nodes+1;count=nodes+2;
            head=new int[count];Array.Fill(head,-1);height=new int[count];current=new int[count];queue=new int[count];
            excess=new float[count];queued=new bool[count];
            next=new int[edgeCapacity];to=new int[edgeCapacity];capacity=new float[edgeCapacity];
        }
        private void Pair(int a,int b,float forward,float reverse)
        {
            if(edges+2>to.Length)throw new InvalidOperationException("Selection graph capacity exceeded.");
            to[edges]=b;capacity[edges]=forward;next[edges]=head[a];head[a]=edges++;
            to[edges]=a;capacity[edges]=reverse;next[edges]=head[b];head[b]=edges++;
        }
        internal void Link(int a,int b,float weight)=>Pair(a,b,weight,weight);
        internal void Terminal(int node,float foregroundCost,float backgroundCost)
        {Pair(source,node,backgroundCost,0);Pair(node,sink,foregroundCost,0);}
        private void Enqueue(int node)
        {
            if(node==source||node==sink||height[node]>=count||queued[node]||excess[node]<=Epsilon)return;
            queued[node]=true;queue[write]=node;write=(write+1)%count;queuedCount++;
        }
        private void RelabelAll(CancellationToken token,Func<bool> obsolete)
        {
            Array.Fill(height,count+1);height[sink]=0;int a=0,b=0;queue[b++]=sink;
            while(a<b)
            {
                if((a&4095)==0)Check(token,obsolete);
                int v=queue[a++];
                for(int e=head[v];e>=0;e=next[e])
                {int u=to[e];if(u!=source&&height[u]==count+1&&capacity[e^1]>Epsilon){height[u]=height[v]+1;queue[b++]=u;}}
            }
            height[source]=count;Array.Copy(head,current,count);Array.Clear(queued,0,count);
            read=write=queuedCount=0;for(int i=0;i<source;i++)Enqueue(i);
        }
        private static void Check(CancellationToken token,Func<bool> obsolete)
        {token.ThrowIfCancellationRequested();if(obsolete!=null&&obsolete())throw new OperationCanceledException();}
        internal bool[] Solve(CancellationToken token,Func<bool> obsolete=null)
        {
            Check(token,obsolete);
            for(int e=head[source];e>=0;e=next[e])
            {float flow=capacity[e];capacity[e]=0;capacity[e^1]+=flow;excess[to[e]]+=flow;}
            RelabelAll(token,obsolete);long work=0;int discharges=0;
            while(queuedCount>0)
            {
                if((discharges++&255)==0)Check(token,obsolete);
                int u=queue[read];read=(read+1)%count;queuedCount--;queued[u]=false;
                while(excess[u]>Epsilon&&height[u]<count)
                {
                    int e=current[u];
                    if(e<0)
                    {
                        int minimum=int.MaxValue;
                        for(int k=head[u];k>=0;k=next[k])if(capacity[k]>Epsilon)minimum=Math.Min(minimum,height[to[k]]);
                        if(minimum==int.MaxValue)throw new InvalidOperationException("Disconnected residual selection graph.");
                        height[u]=minimum+1;current[u]=head[u];work+=8;continue;
                    }
                    work++;int v=to[e];
                    if(capacity[e]>Epsilon&&height[u]==height[v]+1)
                    {
                        float amount=Math.Min(excess[u],capacity[e]);capacity[e]-=amount;capacity[e^1]+=amount;
                        excess[u]-=amount;excess[v]+=amount;Enqueue(v);
                    }
                    else current[u]=next[e];
                }
                if(work>edges*4L){Check(token,obsolete);RelabelAll(token,obsolete);work=0;}
            }
            // A maximum preflow is sufficient for the cut. Excess disconnected from the
            // sink need not travel back across the image to the source. Sink reachability
            // (not source reachability) determines the cut in this formulation.
            RelabelAll(token,obsolete);var reached=new bool[count];
            for(int i=0;i<source;i++)reached[i]=height[i]>=count;
            return reached;
        }
    }
}
