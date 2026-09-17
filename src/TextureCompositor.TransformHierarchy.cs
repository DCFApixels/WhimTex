using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [NonSerialized] private ulong transformVersion;

        internal void RefreshTransformHierarchy()
        {
            VisitTransforms(layers, null, ProjectiveMatrix.Identity, 0);
        }

        internal void PlaceCanvasTransform(Layer layer, List<Layer> destination)
        {
            RefreshTransformHierarchy();
            if (!TryFindParentGroup(destination, out Layer group, out _, out _)) return;
            var value = layer.transform;
            if (!value.TrySetMatrix(group.transformCache.inverse * value.ToMatrix(width, height)))
                throw new InvalidOperationException("Cannot place a layer through an invalid parent transform.");
            layer.transform = value;
        }

        private void VisitTransforms(List<Layer> list, Layer parent, ProjectiveMatrix parentMatrix, ulong parentVersion)
        {
            if (list == null) return;
            foreach (var layer in list)
            {
                if (layer == null) continue;
                var cache = layer.transformCache;
                if (cache == null || cache.document != this || cache.parent != parent ||
                    cache.parentVersion != parentVersion || cache.width != width || cache.height != height ||
                    !cache.local.Equals(layer.transform))
                {
                    cache ??= layer.transformCache = new LayerTransformCache();
                    cache.document = this; cache.parent = parent; cache.parentVersion = parentVersion;
                    cache.width = width; cache.height = height; cache.local = layer.transform;
                    cache.parentMatrix = parentMatrix;
                    parentMatrix.TryInverse(out cache.parentInverse);
                    cache.world = parentMatrix * layer.transform.ToMatrix(width, height);
                    cache.world.TryInverse(out cache.inverse);
                    cache.inverseGpu = new LayerTransformCache.GpuRows(cache.inverse);
                    cache.inputInverseGpu = new LayerTransformCache.GpuRows(parentMatrix * cache.inverse);
                    cache.version = ++transformVersion;
                }
                if (layer.IsGroup) VisitTransforms(layer.children, layer, cache.world, cache.version);
            }
        }

        internal TextureTransform GetCanvasTransform(Layer layer)
        {
            RefreshTransformHierarchy();
            return layer.CanvasTransform;
        }

        internal bool SetCanvasTransform(Layer layer, TextureTransform value)
        {
            RefreshTransformHierarchy();
            var cache = layer.transformCache;
            if (cache == null || cache.parent == null) { layer.transform = value; return true; }
            var local = layer.transform;
            local.pivot = value.pivot;
            if (!local.TrySetMatrix(cache.parentInverse * value.ToMatrix(width, height))) return false;
            layer.transform = local;
            return true;
        }

        internal void PreserveTransformForMove(Layer layer, List<Layer> destination)
        {
            RefreshTransformHierarchy();
            ProjectiveMatrix parent = ProjectiveMatrix.Identity;
            if (TryFindParentGroup(destination, out Layer group, out _, out _))
                parent = group.transformCache.world;
            var cache = layer.transformCache;
            if (cache == null || cache.parentMatrix.Equals(parent)) return;
            var value = layer.transform;
            if (!parent.TryInverse(out var inverse) || !value.TrySetMatrix(inverse * cache.world))
                throw new InvalidOperationException("Cannot move a layer through a singular or invalid parent transform.");
            layer.transform = value;
        }
    }

    internal sealed class LayerTransformCache
    {
        internal TextureCompositor document;
        internal Layer parent;
        internal ulong version, parentVersion;
        internal int width, height;
        internal TextureTransform local;
        internal ProjectiveMatrix parentMatrix, parentInverse, world, inverse;
        internal GpuRows inverseGpu, inputInverseGpu;
        internal readonly struct GpuRows
        {
            private readonly Vector4 a, b, c;
            internal GpuRows(ProjectiveMatrix m)
            {
                a=new Vector4((float)m.m00,(float)m.m01,(float)m.m02,0);
                b=new Vector4((float)m.m10,(float)m.m11,(float)m.m12,0);
                c=new Vector4((float)m.m20,(float)m.m21,(float)m.m22,0);
            }
            internal void Set(Material material, string prefix)
            {
                material.SetVector(prefix+"0",a); material.SetVector(prefix+"1",b); material.SetVector(prefix+"2",c);
            }
        }
    }
}
