using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // RG carries the two endpoint silhouettes, BA the two corrections in halfway space.
    internal sealed class MorphCorrectionAtlas : IDisposable
    {
        private readonly List<Vector4> records = new(), previous = new();
        private readonly List<float> crossings = new();
        internal Texture2D Texture { get; private set; }
        internal Texture2D Distances { get; private set; }
        private readonly List<Vector4> contour = new(), lastContour = new();
        internal bool Foreground { get; private set; }
        internal bool HasLocalChoice { get; private set; }
        internal bool HasTone { get; private set; }
        internal readonly MorphRasterRenderer.FlowCache front = new(), back = new();
        internal readonly List<MorphAnchor> frontPoints = new(), backPoints = new();
        internal readonly List<MorphSegment> frontEdges = new(), backEdges = new();
        private Vector2Int canvas;

        internal bool Prepare(IReadOnlyList<MorphAnchor> points, IReadOnlyList<MorphSegment> edges, int width, int height)
        {
            records.Clear();
            contour.Clear();
            Foreground = HasLocalChoice = HasTone = false;
            for (int i = 0; i < (points?.Count ?? 0); i++)
            {
                var p = points[i];
                Add(MorphGroupLayerBehaviour.Coordinates(p), MorphGroupLayerBehaviour.Coordinates(p), p.correction, false);
            }
            for (int i = 0; i < (edges?.Count ?? 0); i++)
            {
                var e = edges[i];
                Add(e.Pair(false), e.Pair(true), e.correction, e.foreground);
            }
            if (records.Count == 0) return false;
            bool reuse = Texture != null && canvas.x == width && canvas.y == height && previous.Count == records.Count;
            for (int i = 0; reuse && i < records.Count; i++) reuse = records[i].Equals(previous[i]);
            if (!reuse)
            {
                bool contourChanged = canvas.x != width || canvas.y != height || contour.Count != lastContour.Count;
                for (int i = 0; !contourChanged && i < contour.Count; i++)
                    contourChanged = !contour[i].Equals(lastContour[i]);
                if (contourChanged)
                {
                    front.Clear();
                    back.Clear();
                    lastContour.Clear();
                    lastContour.AddRange(contour);
                }
                float scale = Mathf.Min(1, 512f / Mathf.Max(width, height));
                int w = Mathf.Max(1, Mathf.RoundToInt(width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                if (Texture == null || Texture.width != w || Texture.height != h)
                {
                    if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
                    Texture = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                }
                var pixels = Texture.GetRawTextureData<Color32>();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = default;
                if (Foreground)
                    for (int side = 0; side <= 1; side++)
                        for (int y = 0; y < h; y++)
                        {
                            crossings.Clear();
                            float v = (y + .5f) / h;
                            for (int i = 0; i < records.Count; i += 3)
                            {
                                if (records[i+2].x < 4) continue;
                                Vector2 a = MorphSegment.Side(records[i], side == 0), b = MorphSegment.Side(records[i+1], side == 0);
                                if ((a.y > v) == (b.y > v)) continue;
                                crossings.Add(w * (a.x + (v-a.y) / (b.y-a.y) * (b.x-a.x)));
                            }
                            crossings.Sort();
                            for (int j = 1; j < crossings.Count; j += 2)
                            {
                                float left = crossings[j-1], right = crossings[j];
                                for (int x = Mathf.Max(0, Mathf.FloorToInt(left)); x < Mathf.Min(w, Mathf.CeilToInt(right)); x++)
                                {
                                    var c = pixels[x+y*w];
                                    byte cover = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(Mathf.Min(x+1,right)-Mathf.Max(x,left)));
                                    if (side == 0) c.r = Math.Max(c.r,cover); else c.g = Math.Max(c.g,cover);
                                    pixels[x+y*w] = c;
                                }
                            }
                        }
                if (Foreground && (contourChanged || Distances == null))
                    BuildContourDistances(pixels, w, h);
                Vector2 size = new Vector2(w,h);
                for (int i = 0; i < records.Count; i += 3)
                {
                    var settings = records[i+2];
                    int mode = (int)settings.x % 4;
                    if (mode == 0) continue;
                    Vector2 Mid(Vector4 pair) => Vector2.Scale(.5f * (MorphSegment.Side(pair,true)+MorphSegment.Side(pair,false)),size);
                    Vector2 a = Mid(records[i]), b = Mid(records[i+1]), axis = b-a;
                    float radius = settings.y*Mathf.Min(w,h), feather = Mathf.Max(.5f, radius*settings.z);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius)), x1 = Mathf.Min(w,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius)), y1 = Mathf.Min(h,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius));
                    for (int y=y0; y<y1; y++)
                        for (int x=x0; x<x1; x++)
                        {
                            Vector2 d = new Vector2(x+.5f,y+.5f)-a;
                            float t = Mathf.Clamp01(Vector2.Dot(d,axis)/Mathf.Max(1e-12f,axis.sqrMagnitude));
                            float u = Mathf.Clamp01((radius-(d-t*axis).magnitude)/feather);
                            byte weight = (byte)Mathf.RoundToInt(255*settings.w*u*u*(3-2*u));
                            int index = x+y*w; var c = pixels[index];
                            if (mode == (int)MorphCorrectionMode.LocalChoice) c.b = Math.Max(c.b,weight);
                            else c.a = Math.Max(c.a,weight);
                            pixels[index] = c;
                        }
                }
                Texture.Apply(false,false);
                previous.Clear(); previous.AddRange(records); canvas = new Vector2Int(width,height);
            }
            if (Foreground)
            {
                frontPoints.Clear(); backPoints.Clear(); frontEdges.Clear(); backEdges.Clear();
                for (int i=0; i<(points?.Count??0); i++)
                {
                    var p=points[i]; (Inside(MorphGroupLayerBehaviour.Coordinates(p)) ? frontPoints : backPoints).Add(p);
                }
                for (int i=0; i<(edges?.Count??0);)
                {
                    var first=edges[i]; int end=i+Mathf.Max(1,first.chordCount);
                    bool inside=!first.foreground && Inside(first.pathChord ? MiddlePair(edges, i, end, width, height)
                        : .5f*(first.Pair(false)+edges[end-1].Pair(true)));
                    for (;i<end;i++)
                    {
                        if (first.foreground || inside) frontEdges.Add(edges[i]);
                        if (!first.foreground && !inside) backEdges.Add(edges[i]);
                    }
                }
            }
            return true;
        }

        private bool Inside(Vector4 pair) =>
            Texture.GetPixelBilinear(pair.x,pair.y).r + Texture.GetPixelBilinear(pair.z,pair.w).g >= 1;

        // Classify a whole guide at a point on its paired path. The midpoint of
        // the end-to-end chord can be outside a curved silhouette, or at its seam.
        private static Vector4 MiddlePair(IReadOnlyList<MorphSegment> edges, int start, int end, int width, int height)
        {
            if (end == start + 1) return .5f * (edges[start].Pair(false) + edges[start].Pair(true));
            var size = new Vector4(width, height, width, height);
            float Length(MorphSegment e) => MorphSegment.PairedLength(Vector4.Scale(e.Pair(true) - e.Pair(false), size));
            float total = 0;
            for (int i = start; i < end; i++) total += Length(edges[i]);
            float remaining = .5f * total;
            for (int i = start; i < end; i++)
            {
                var edge = edges[i];
                float length = Length(edge);
                if (length > 0 && remaining <= length)
                    return Vector4.LerpUnclamped(edge.Pair(false), edge.Pair(true), remaining / length);
                remaining -= length;
            }
            return edges[end - 1].Pair(true);
        }
        private void Add(Vector4 a, Vector4 b, MorphGuideCorrection c, bool frontEdge)
        {
            bool active=c.mode != MorphCorrectionMode.None && c.width>0 && c.strength>0;
            if (!active && !frontEdge) return;
            if (frontEdge)
            {
                contour.Add(a);
                contour.Add(b);
            }
            Foreground |= frontEdge;
            HasLocalChoice |= active && c.mode == MorphCorrectionMode.LocalChoice;
            HasTone |= active && c.mode == MorphCorrectionMode.MatchTone;
            records.Add(a); records.Add(b);
            records.Add(new Vector4((frontEdge?4:0)+(active?(int)c.mode:0),c.width,c.feather,c.strength));
        }

        // A two-sweep chamfer distance supports a single interpolated silhouette. Store normalized
        // distances so the full canvas and the downscaled editor preview use the same transition.
        private void BuildContourDistances(Unity.Collections.NativeArray<Color32> pixels, int width, int height)
        {
            if (Distances != null && (Distances.width != width || Distances.height != height))
                UnityEngine.Object.DestroyImmediate(Distances);
            if (Distances == null)
                Distances = new Texture2D(width, height, TextureFormat.RGFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var output = Distances.GetRawTextureData<Vector2>();
            var distances = new float[pixels.Length];
            float shortAxis = Mathf.Min(width, height);
            for (int channel = 0; channel < 2; channel++)
            {
                bool Inside(int index) => (channel == 0 ? pixels[index].r : pixels[index].g) >= 128;
                for (int i = 0; i < distances.Length; i++)
                {
                    int x = i % width, y = i / width;
                    bool inside = Inside(i);
                    bool boundary = x > 0 && Inside(i-1) != inside || x+1 < width && Inside(i+1) != inside ||
                        y > 0 && Inside(i-width) != inside || y+1 < height && Inside(i+width) != inside;
                    distances[i] = boundary ? .5f : width + height;
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    int step = pass == 0 ? 1 : -1;
                    for (int n = 0; n < distances.Length; n++)
                    {
                        int i = pass == 0 ? n : distances.Length-1-n;
                        int x = i % width, y = i / width;
                        float d = distances[i];
                        bool side = (uint)(x-step) < (uint)width;
                        if (side) d = Mathf.Min(d, distances[i-step]+1);
                        if ((uint)(y-step) < (uint)height)
                        {
                            int row = i - width*step;
                            d = Mathf.Min(d, distances[row]+1);
                            if (side) d = Mathf.Min(d, distances[row-step]+1.41421356f);
                            if ((uint)(x+step) < (uint)width) d = Mathf.Min(d, distances[row+step]+1.41421356f);
                        }
                        distances[i] = d;
                    }
                }
                for (int i = 0; i < distances.Length; i++)
                {
                    var pair = output[i];
                    pair[channel] = distances[i] * (Inside(i) ? 1 : -1) / shortAxis;
                    output[i] = pair;
                }
            }
            Distances.Apply(false, false);
        }

        public void Dispose()
        {
            front.Clear(); back.Clear(); previous.Clear();
            if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
            Texture=null;
            if (Distances != null) UnityEngine.Object.DestroyImmediate(Distances);
            Distances = null;
        }
    }
}
