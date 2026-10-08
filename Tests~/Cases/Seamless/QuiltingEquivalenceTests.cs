using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static class QuiltingEquivalenceTests
    {
        static string Folder => global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temp + "/";
        static readonly System.Reflection.MethodInfo Current=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless").GetMethod("Render",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        static Color[] Read(RenderTexture rt)
        {
            var old=RenderTexture.active;var t=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
            try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
            finally{RenderTexture.active=old;global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        static Color[] Render(Texture t,int edge,float width,float feather,int quality,int seed,int matching,Vector4 mask,bool current)
        {
            var rt=current ? (RenderTexture)Current.Invoke(null,new object[]{t,t.width,t.height,(MakeSeamlessLayerBehaviour.PoissonEdges)edge,width,feather,(MakeSeamlessLayerBehaviour.QuiltingQuality)quality,seed,(MakeSeamlessLayerBehaviour.QuiltingChannels)matching,mask})
                : PatchQuiltingBaseline.Render(t,t.width,t.height,(MakeSeamlessLayerBehaviour.PoissonEdges)edge,width,feather,(MakeSeamlessLayerBehaviour.QuiltingQuality)quality,seed,(MakeSeamlessLayerBehaviour.QuiltingChannels)matching,mask);
            try{return Read(rt);}finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
        }
        static string ExecuteMain()
        {
            int cases=0;float maxError=0;
            foreach(var size in new[]{new Vector2Int(17,13),new Vector2Int(64,48)})
            {
                var t=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true));var p=new Color[size.x*size.y];
                try
                {
                    for(int fixture=0;fixture<3;fixture++)
                    {
                        for(int y=0;y<size.y;y++)for(int x=0;x<size.x;x++)p[y*size.x+x]=fixture==0 ? new Color(.3f+.4f*Mathf.Sin(x*.27f+y*.31f),.7f+.5f*Mathf.Cos(x*.41f-y*.1f),x*.02f,.4f+.3f*Mathf.Cos(y*.2f)) : fixture==1 ? new Color(x%4,y%3,0,1) : new Color(x*.03f,.5f,.5f,1);
                        t.SetPixels(p);t.Apply();
                        foreach(int q in new[]{0,1,2})foreach(int edge in new[]{0,1,2})foreach(int matching in new[]{0,1})foreach(int seed in new[]{0,19,-2147483648})
                        {
                            float band=seed==0?.2f:seed==19?.45f:.02f;
                            var mask=fixture==2?new Vector4(0,1,1,0):Vector4.one;
                            var a=Render(t,edge,band,16,q,seed,matching,mask,false);var b=Render(t,edge,band,16,q,seed,matching,mask,true);
                            for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)
                            {
                                float error=Math.Abs(a[i][c]-b[i][c]);maxError=Math.Max(maxError,error);
                                global::WhimTex.Tests.UnityC.FixtureContext.Context.True(!(error>1e-6f), $"Mismatch size={size} fixture={fixture} quality={q} edge={edge} matching={matching} seed={seed} at {i}/{c}: {a[i][c]} != {b[i][c]}");
                            }
                            cases++;
                        }
                    }
                }
                finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
            }
            string report=$"Old/new GPU equivalence: {cases} cases, max error {maxError}.";
            return report;
        }
        static string ExecuteManaged()
        {
            bool previous=Unity.Burst.BurstCompiler.Options.EnableBurstCompilation;
            try {Unity.Burst.BurstCompiler.Options.EnableBurstCompilation=false;return ExecuteMain();}
            finally{Unity.Burst.BurstCompiler.Options.EnableBurstCompilation=previous;}
        }
        static string ExecuteBenchmark(Texture2D t)
        {
            string report="Paired warm medians, including output readback, G/B All, width20%, feather16, seed0, no Poisson.\n";
            foreach(int q in new[]{0,1,2})foreach(int matching in new[]{0,1})
            {
                var mask=new Vector4(0,1,1,0);var a=Render(t,0,.2f,16,q,0,matching,mask,false);var b=Render(t,0,.2f,16,q,0,matching,mask,true);
                float error=0;for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)error=Math.Max(error,Math.Abs(a[i][c]-b[i][c]));
                global::WhimTex.Tests.UnityC.FixtureContext.Context.True(!(error>1e-6), "Live noise differs: "+error);
                var oldTimes=new double[3];var newTimes=new double[3];
                for(int run=0;run<3;run++)
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();Render(t,0,.2f,16,q,0,matching,mask,false);watch.Stop();oldTimes[run]=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();Render(t,0,.2f,16,q,0,matching,mask,true);watch.Stop();newTimes[run]=watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(oldTimes);Array.Sort(newTimes);report+=$"quality={q} matching={matching}: old={oldTimes[1]:F2}ms new={newTimes[1]:F2}ms speedup={oldTimes[1]/newTimes[1]:F1}x maxError={error}\n";
            }
            System.IO.File.WriteAllText(Folder+"optimized-timings.txt",report);return report;
        }
        static string ExecuteCurrentTiming(Texture2D t)
        {
            string report=$"Warm current-only {t.width}x{t.height}, 15 samples, no Poisson, Burst={Unity.Burst.BurstCompiler.Options.EnableBurstCompilation}.\n";
            foreach(int q in new[]{1,2})foreach(int matching in new[]{0,1})foreach(float band in new[]{.2f,.45f})
            {
                var mask=new Vector4(0,1,1,0);
                for(int warm=0;warm<3;warm++)Render(t,0,band,16,q,0,matching,mask,true);
                var times=new double[15];
                for(int run=0;run<times.Length;run++)
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();Render(t,0,band,16,q,0,matching,mask,true);
                    watch.Stop();times[run]=watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);report+=$"quality={q} matching={matching} width={band}: min={times[0]:F2} median={times[7]:F2} p90={times[13]:F2}ms\n";
            }
            return report;
        }
    
    public static string Main() => global::WhimTex.Tests.UnityC.FixtureContext.Run("QuiltingEquivalenceTests.Main", () => { ExecuteMain(); });

    public static string Managed() => global::WhimTex.Tests.UnityC.FixtureContext.Run("QuiltingEquivalenceTests.Managed", () => { ExecuteManaged(); });

    public static string Benchmark() => global::WhimTex.Tests.UnityC.ReviewedOracle.Live("QuiltingEquivalenceTests.Benchmark", ExecuteBenchmark);

    public static string CurrentTiming() => global::WhimTex.Tests.UnityC.ReviewedOracle.Live("QuiltingEquivalenceTests.CurrentTiming", ExecuteCurrentTiming, true);
}
    internal static class PatchQuiltingBaseline
    {
        private sealed class Candidate
        {
            internal float score;
            internal int donor;
            internal int[] left, right;
        }

        internal static RenderTexture Render(Texture input, int width, int height,
            MakeSeamlessLayerBehaviour.PoissonEdges edges, float band, float feather,
            MakeSeamlessLayerBehaviour.QuiltingQuality quality, int seed,
            MakeSeamlessLayerBehaviour.QuiltingChannels matching, Vector4 channels)
        {
            var material = (Material)typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials")
                .GetProperty("PatchQuilting",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).GetValue(null);
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Patch Quilting shader is unavailable.");
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            RenderTexture result = null;
            try
            {
                GL.sRGBWrite = false;
                bool independent = matching == MakeSeamlessLayerBehaviour.QuiltingChannels.Independent;
                material.SetInt("_Independent", independent ? 1 : 0);
                material.SetVector("_Channels", channels);
                result = Temporary(width, height);
                Graphics.Blit(input, result, material, 0);
                bool horizontal = edges != MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom && width > 1;
                bool vertical = edges != MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight && height > 1;
                if (horizontal) Replace(false, false);
                // Reuse the first pass as the second donor source. Closed cuts keep their
                // endpoint weights equal so joining Y cannot reopen the X seam.
                if (vertical) Replace(true, horizontal);
                var straight = Temporary(width, height);
                try { Graphics.Blit(result, straight, material, 2); }
                catch { global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(straight); throw; }
                global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(result); result = null;
                return straight;

                void Replace(bool transpose, bool closed)
                {
                    var next = Stitch(result, width, height, transpose, closed, band, feather,
                        quality, seed, independent, channels, material);
                    global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(result); result = next;
                }
            }
            finally
            {
                material.SetTexture("_Paths", null);
                if (result != null) global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(result);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }

        private static RenderTexture Stitch(RenderTexture source, int width, int height,
            bool transpose, bool closed, float fraction, float feather,
            MakeSeamlessLayerBehaviour.QuiltingQuality quality, int seed, bool independent,
            Vector4 channels, Material material)
        {
            int limit = quality == MakeSeamlessLayerBehaviour.QuiltingQuality.Draft ? 96 :
                quality == MakeSeamlessLayerBehaviour.QuiltingQuality.High ? 256 : 160;
            int candidates = quality == MakeSeamlessLayerBehaviour.QuiltingQuality.Draft ? 8 :
                quality == MakeSeamlessLayerBehaviour.QuiltingQuality.High ? 48 : 24;
            int aw = Math.Min(width, limit), ah = Math.Min(height, limit);
            int n = transpose ? ah : aw, rows = transpose ? aw : ah;
            int full = transpose ? height : width;
            int fullBand = Mathf.Clamp(Mathf.RoundToInt(full * fraction), 1, full / 2);
            int b = Mathf.Clamp(Mathf.RoundToInt(n * fullBand / (float)full), 1, n / 2);
            var analysis = Temporary(aw, ah);
            Texture2D readback = null, paths = null;
            RenderTexture output = null;
            try
            {
                Graphics.Blit(source, analysis);
                readback = global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(aw, ah, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave });
                RenderTexture.active = analysis;
                readback.ReadPixels(new Rect(0, 0, aw, ah), 0, 0, false);
                var pixels = readback.GetPixels();
                var map = new Color[rows * 3];
                // Three texture rows: left cut, right cut, full-resolution donor offset.
                // RGBA stores either one shared solution or independent channel solutions.
                int count = independent ? 4 : 1;
                for (int channel = 0; channel < count; channel++)
                {
                    if (independent && channels[channel] == 0) continue;
                    var selected = independent ? Vector4.zero : channels;
                    if (independent) selected[channel] = 1;
                    Candidate best = Search(pixels, aw, ah, transpose, closed, b, candidates,
                        unchecked(seed + channel * 7919 + (transpose ? 104729 : 0)), selected);
                    int donor = n > 2*b ? Mathf.RoundToInt(best.donor * (full - 2*fullBand) / (float)(n - 2*b)) : 0;
                    for (int y = 0; y < rows; y++)
                    {
                        float left = best.left[y] / (float)Math.Max(1, 2*b-1);
                        float right = best.right[y] / (float)Math.Max(1, 2*b-1);
                        for (int c = independent ? channel : 0; c < (independent ? channel + 1 : 4); c++)
                        {
                            map[y][c] = left;
                            map[rows+y][c] = right;
                            map[2*rows+y][c] = donor;
                        }
                    }
                }
                paths = global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rows, 3, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
                paths.SetPixels(map); paths.Apply(false, false);
                material.SetTexture("_Paths", paths);
                material.SetVector("_Size", new Vector4(width, height, 1f/width, 1f/height));
                material.SetInt("_Transpose", transpose ? 1 : 0);
                material.SetFloat("_Band", fullBand);
                // Frozen managed search, evaluated with the current percentage blend contract.
                material.SetFloat("_Feather", Mathf.Clamp01(feather * .01f));
                material.SetInt("_Tiny", b < 4 ? 1 : 0);
                output = Temporary(width, height);
                Graphics.Blit(source, output, material, 1);
                var result = output; output = null; return result;
            }
            finally
            {
                material.SetTexture("_Paths", null);
                global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(analysis);
                if (output != null) global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(output);
                if (readback != null) global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(readback);
                if (paths != null) global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(paths);
            }
        }

        private static Candidate Search(Color[] image, int width, int height, bool transpose,
            bool closed, int band, int count, int seed, Vector4 channels)
        {
            int n = transpose ? height : width, rows = transpose ? width : height;
            // Unwrap the two edge bands into one strip: [right edge | left edge].
            // Copy a contiguous donor across its center, and cut within either overlap.
            int overlap = Math.Max(1, band-2), columns = Math.Max(1, overlap-1);
            var options = new List<Candidate>(count);
            Color Pixel(int x, int y) => image[transpose ? x*width+y : y*width+x];
            double Error(Color a, Color b)
            {
                double sum = 0;
                for (int c = 0; c < 4; c++) if (channels[c] > 0)
                {
                    double d = (double)a[c]-b[c];
                    if (!double.IsNaN(d) && !double.IsInfinity(d)) sum += d*d;
                }
                return sum;
            }
            for (int k = 0; k < count; k++)
            {
                float jitter = (Hash(unchecked((uint)seed + (uint)k*747796405u)) & 65535) / 65536f;
                int donor = k == 0 ? (n-2*band)/2 : Mathf.RoundToInt((k+jitter)/count*(n-2*band));
                var leftCost = new float[rows*columns]; var rightCost = new float[rows*columns];
                for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
                {
                    leftCost[y*columns+x] = Cost(x+1, y);
                    rightCost[y*columns+x] = Cost(2*band-overlap+x, y);
                }
                float Cost(int x, int y)
                {
                    int at = (n-band+x)%n;
                    Color a = Pixel(at,y), d = Pixel(Math.Min(n-1,donor+x),y);
                    int next = Math.Min(rows-1,y+1), prev = Math.Max(0,y-1);
                    double cost = Error(a,d) + 4*Error(Pixel(at,next)-Pixel(at,prev),
                        Pixel(Math.Min(n-1,donor+x),next)-Pixel(Math.Min(n-1,donor+x),prev));
                    return (float)Math.Min(cost,1e30);
                }
                var lp = MinimumCut(leftCost, rows, columns, closed, out float ls);
                var rp = MinimumCut(rightCost, rows, columns, closed, out float rs);
                for (int y = 0; y < rows; y++) { lp[y] += 1; rp[y] += 2*band-overlap; }
                options.Add(new Candidate { donor=donor, score=(ls+rs)/(2*rows), left=lp, right=rp });
            }
            options.Sort((a,b) => a.score.CompareTo(b.score));
            float threshold = options[0].score*1.1f+1e-12f;
            int eligible = 1;
            while (eligible < options.Count && options[eligible].score <= threshold) eligible++;
            return options[(int)(Hash(unchecked((uint)seed ^ 0x9e3779b9u)) % (uint)eligible)];
        }

        internal static int[] MinimumCut(float[] cost, int rows, int columns, bool closed, out float score)
        {
            var previous = new float[columns]; var next = new float[columns];
            var back = new sbyte[rows*columns];
            int anchor = 0, guard = Math.Min(3, Math.Max(1, rows/2));
            if (closed)
            {
                // Bounded cyclic approximation: pick a shared low-cost guard anchor,
                // then solve the interior exactly subject to that fixed boundary.
                double best = double.PositiveInfinity;
                for (int x = 0; x < columns; x++)
                {
                    double value = 0;
                    for (int y = 0; y < guard; y++) value += cost[y*columns+x]+cost[(rows-1-y)*columns+x];
                    if (value < best) { best=value; anchor=x; }
                }
            }
            for (int x = 0; x < columns; x++) previous[x] = closed && x != anchor ? float.PositiveInfinity : cost[x];
            for (int y = 1; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int from = x;
                    if (x > 0 && previous[x-1] < previous[from]) from=x-1;
                    if (x+1 < columns && previous[x+1] < previous[from]) from=x+1;
                    next[x] = closed && (y < guard || y >= rows-guard) && x != anchor
                        ? float.PositiveInfinity : previous[from]+cost[y*columns+x];
                    back[y*columns+x] = (sbyte)(from-x);
                }
                var swap=previous; previous=next; next=swap;
            }
            int last = closed ? anchor : 0;
            if (!closed) for (int x=1;x<columns;x++) if(previous[x]<previous[last]) last=x;
            score=previous[last]; var path=new int[rows]; path[rows-1]=last;
            for(int y=rows-1;y>0;y--) { last+=back[y*columns+last]; path[y-1]=last; }
            return path;
        }

        private static uint Hash(uint x)
        {
            unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); }
        }

        private static RenderTexture Temporary(int width, int height)
        {
            var rt=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
            rt.filterMode=FilterMode.Bilinear; rt.wrapMode=TextureWrapMode.Clamp; return rt;
        }
    }
}
