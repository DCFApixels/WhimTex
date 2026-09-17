using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class MultiLayerTransform
    {
        private readonly TextureCompositor document;
        private readonly Layer[] selected;
        private readonly ulong[] versions;
        private readonly List<Layer> roots = new List<Layer>();
        private readonly int width, height;
        private TextureTransform[] originals, candidates;
        private ProjectiveMatrix[] worlds, parentInverses;
        private ProjectiveMatrix startInverse;
        private TextureTransform startFrame;
        internal TextureTransform Frame { get; private set; }

        internal MultiLayerTransform(TextureCompositor document, IReadOnlyList<string> ids)
        {
            this.document = document;
            width = document.width; height = document.height;
            document.RefreshTransformHierarchy();
            selected = new Layer[ids.Count];
            versions = new ulong[ids.Count];
            double minX=double.PositiveInfinity, minY=minX, maxX=double.NegativeInfinity, maxY=maxX;
            var set = new HashSet<Layer>();
            for (int i=0; i<ids.Count; i++)
            {
                var layer = document.FindLayer(ids[i]);
                if (layer == null) throw new InvalidOperationException("Selection changed.");
                selected[i] = layer; set.Add(layer);
                var matrix = layer.transformCache.world;
                if (!matrix.ValidUnitQuad()) throw new InvalidOperationException("Invalid selected transform.");
                for (int corner=0; corner<4; corner++)
                {
                    var p = matrix.Point(new Double2(corner & 1, corner >> 1));
                    minX=Math.Min(minX,p.x); maxX=Math.Max(maxX,p.x);
                    minY=Math.Min(minY,p.y); maxY=Math.Max(maxY,p.y);
                }
            }
            foreach (var layer in selected)
            {
                bool inherited=false;
                for (var parent=layer.transformCache.parent; parent!=null; parent=parent.transformCache.parent)
                    if (set.Contains(parent)) { inherited=true; break; }
                if (!inherited) roots.Add(layer);
            }
            var frame=TextureTransform.Default;
            frame.scale=new Double2(Math.Max(maxX-minX,1e-5),Math.Max(maxY-minY,1e-5));
            frame.position=new Double2(((minX+maxX)*.5-.5)*width,((minY+maxY)*.5-.5)*height);
            Frame=frame;
            CaptureVersions();
        }

        internal bool Matches(TextureCompositor owner, IReadOnlyList<string> ids)
        {
            if (owner != document || owner.width!=width || owner.height!=height || ids.Count!=selected.Length) return false;
            document.RefreshTransformHierarchy();
            for (int i=0;i<selected.Length;i++)
                if (ids[i]!=selected[i].Id || document.FindLayer(ids[i])!=selected[i] ||
                    selected[i].transformCache.version!=versions[i]) return false;
            return true;
        }

        internal void Begin()
        {
            startFrame=Frame;
            if (!Frame.ToMatrix(width,height).TryInverse(out startInverse))
                throw new InvalidOperationException("Invalid selection frame.");
            originals=new TextureTransform[roots.Count];
            candidates=new TextureTransform[roots.Count];
            worlds=new ProjectiveMatrix[roots.Count];
            parentInverses=new ProjectiveMatrix[roots.Count];
            for(int i=0;i<roots.Count;i++)
            {
                originals[i]=roots[i].transform;
                worlds[i]=roots[i].transformCache.world;
                parentInverses[i]=roots[i].transformCache.parentInverse;
            }
        }

        internal bool Apply(TextureTransform frame, bool pivotOnly=false)
        {
            if (!pivotOnly)
            {
                var delta=frame.ToMatrix(width,height)*startInverse;
                for(int i=0;i<roots.Count;i++)
                {
                    candidates[i]=originals[i];
                    if (!candidates[i].TrySetMatrix(parentInverses[i]*delta*worlds[i])) return false;
                }
                for(int i=0;i<roots.Count;i++) roots[i].transform=candidates[i];
            }
            Frame=frame;
            CaptureVersions();
            return true;
        }

        internal void Cancel()
        {
            for(int i=0;i<roots.Count;i++) roots[i].transform=originals[i];
            Frame=startFrame;
            CaptureVersions();
        }

        private void CaptureVersions()
        {
            document.RefreshTransformHierarchy();
            for(int i=0;i<selected.Length;i++) versions[i]=selected[i].transformCache.version;
        }
    }
}
