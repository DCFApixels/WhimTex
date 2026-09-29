using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class PatchQuiltingSeamless
    {
        private struct Candidate : IComparable<Candidate>
        {
            internal float score;
            internal int donor, index;
            public int CompareTo(Candidate other) => score.CompareTo(other.score);
        }

        private struct SearchTask
        {
            internal int donor, channel, side;
            internal float shift;
        }

        // Main-thread scratch only, never a result cache. One idle workspace is shared
        // across documents; a nested render gets separate storage. Jobs finish before return.
        private static Workspace spare;
        private static double spareSince;
        private const long WorkspaceBudget = 64L * 1024 * 1024;
        private const double WorkspaceIdleSeconds = 30;

        static PatchQuiltingSeamless()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearWorkspace;
            EditorApplication.quitting += ClearWorkspace;
            EditorApplication.update += TrimWorkspace;
        }

        private static Workspace RentWorkspace()
        {
            var result = spare;
            spare = null;
            return result ?? new Workspace();
        }

        private static void ReturnWorkspace(Workspace workspace)
        {
            if (workspace.Bytes > WorkspaceBudget) { workspace.Dispose(); return; }
            ClearWorkspace();
            spare = workspace;
            spareSince = EditorApplication.timeSinceStartup;
        }

        private static void TrimWorkspace()
        {
            if (spare != null && EditorApplication.timeSinceStartup - spareSince >= WorkspaceIdleSeconds)
                ClearWorkspace();
        }

        private static void ClearWorkspace()
        {
            var old = spare;
            spare = null;
            old?.Dispose();
        }

        private sealed class Workspace : IDisposable
        {
            internal NativeArray<float4> values, gradients, map;
            internal NativeArray<float> costs, work, scores;
            internal NativeArray<sbyte> back;
            internal NativeArray<int> cuts, aliases;
            internal NativeArray<SearchTask> tasks;
            internal NativeArray<Candidate> options;
            internal Texture2D readback, paths, alternatePaths;
            internal PatchQuiltingContrast contrast;

            internal long Bytes => Size(values) + Size(gradients) + Size(map) + Size(costs) + Size(work)
                + Size(scores) + Size(back) + Size(cuts) + Size(aliases) + Size(tasks) + Size(options)
                // RGBAFloat CPU and GPU storage; retained textures have no mipmaps.
                + (readback != null ? (long)readback.width * readback.height * 32 : 0)
                + (paths != null ? (long)paths.width * paths.height * 32 : 0)
                + (alternatePaths != null ? (long)alternatePaths.width * alternatePaths.height * 32 : 0)
                + (contrast?.Bytes ?? 0);

            internal void Prepare(int width, int height, int rows, int candidates, int channelCount)
            {
                Grow(ref values, width * height); Grow(ref gradients, width * height);
                Grow(ref map, rows * 3);
                Grow(ref aliases, candidates * channelCount);
                Grow(ref tasks, candidates * channelCount * 2);
                Grow(ref options, candidates);
                Texture(ref readback, width, height);
                // Non-square renders alternate two path lengths on every evaluation.
                if (paths != null && paths.width != rows)
                {
                    var old = paths; paths = alternatePaths; alternatePaths = old;
                }
                Texture(ref paths, rows, 3);
            }

            internal void PrepareSearch(int taskCount, int rows, int columns)
            {
                Grow(ref costs, taskCount * rows * columns);
                Grow(ref back, taskCount * rows * columns);
                Grow(ref work, taskCount * 2 * columns);
                Grow(ref cuts, taskCount * rows);
                Grow(ref scores, taskCount);
            }

            private static void Texture(ref Texture2D texture, int width, int height)
            {
                if (texture != null && texture.width == width && texture.height == height) return;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
            }

            private static long Size<T>(NativeArray<T> array) where T : struct =>
                array.IsCreated ? (long)array.Length * UnsafeUtility.SizeOf<T>() : 0;

            private static void Grow<T>(ref NativeArray<T> array, int length) where T : struct
            {
                if (array.IsCreated && array.Length >= length) return;
                Release(ref array);
                array = new NativeArray<T>(Math.Max(1, length), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            private static void Release<T>(ref NativeArray<T> array) where T : struct
            {
                if (array.IsCreated) array.Dispose();
                array = default;
            }

            public void Dispose()
            {
                Release(ref values); Release(ref gradients); Release(ref map);
                Release(ref costs); Release(ref work); Release(ref scores); Release(ref back);
                Release(ref cuts); Release(ref aliases); Release(ref tasks); Release(ref options);
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (paths != null) UnityEngine.Object.DestroyImmediate(paths);
                if (alternatePaths != null) UnityEngine.Object.DestroyImmediate(alternatePaths);
                readback = paths = alternatePaths = null;
                contrast?.Dispose(); contrast=null;
            }
        }

        internal static RenderTexture Render(Texture input, int width, int height,
            MakeSeamlessLayerBehaviour.PoissonEdges edges, float band, float feather,
            MakeSeamlessLayerBehaviour.QuiltingQuality quality, int seed,
            MakeSeamlessLayerBehaviour.QuiltingChannels matching, Vector4 channels)
            => RenderCompensated(input,width,height,edges,band,feather,quality,seed,matching,channels,0);

        internal static RenderTexture RenderCompensated(Texture input, int width, int height,
            MakeSeamlessLayerBehaviour.PoissonEdges edges, float band, float feather,
            MakeSeamlessLayerBehaviour.QuiltingQuality quality, int seed,
            MakeSeamlessLayerBehaviour.QuiltingChannels matching, Vector4 channels, float contrast)
            => RenderShifted(input,width,height,edges,band,feather,quality,seed,matching,channels,contrast,0);

        internal static RenderTexture RenderShifted(Texture input, int width, int height,
            MakeSeamlessLayerBehaviour.PoissonEdges edges, float band, float feather,
            MakeSeamlessLayerBehaviour.QuiltingQuality quality, int seed,
            MakeSeamlessLayerBehaviour.QuiltingChannels matching, Vector4 channels, float contrast, float alongSearch)
        {
            var material = WhimTexMaterials.PatchQuilting;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Patch Quilting shader is unavailable.");
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            RenderTexture result = null;
            try
            {
                GL.sRGBWrite = false;
                if (edges == MakeSeamlessLayerBehaviour.PoissonEdges.None)
                {
                    result = Temporary(width,height);
                    Graphics.Blit(input,result);
                    var copy=result; result=null; return copy;
                }
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
                catch { RenderTexture.ReleaseTemporary(straight); throw; }
                RenderTexture.ReleaseTemporary(result); result = null;
                return straight;

                void Replace(bool transpose, bool closed)
                {
                    var next = Stitch(result, width, height, transpose, closed, band, feather,
                        quality, seed, independent, channels, material, contrast, alongSearch);
                    RenderTexture.ReleaseTemporary(result); result = next;
                }
            }
            finally
            {
                material.SetTexture("_Paths", null);
                material.SetTexture("_Tables", null);material.SetTexture("_Gaussian", null);material.SetFloat("_Contrast",0);
                material.SetVector("_AlongShift",Vector4.zero);
                if (result != null) RenderTexture.ReleaseTemporary(result);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }

        private static RenderTexture Stitch(RenderTexture source, int width, int height,
            bool transpose, bool closed, float fraction, float feather,
            MakeSeamlessLayerBehaviour.QuiltingQuality quality, int seed, bool independent,
            Vector4 channels, Material material, float contrast, float alongSearch)
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
            var workspace = RentWorkspace();
            JobHandle pending = default;
            bool reusable = false;
            RenderTexture output = null, gaussian = null;
            try
            {
                int count = independent ? 4 : 1;
                workspace.Prepare(aw, ah, rows, candidates, count);
                Graphics.Blit(source, analysis);
                RenderTexture.active = analysis;
                workspace.readback.ReadPixels(new Rect(0, 0, aw, ah), 0, 0, false);
                // Borrow texture storage; never dispose it independently of the texture.
                var pixels = workspace.readback.GetPixelData<float4>(0);
                var map = workspace.map.GetSubArray(0, rows * 3);
                // Disabled components must not inherit paths from a previous render.
                for (int i = 0; i < map.Length; i++) map[i] = default;
                int columns = Math.Max(1, b-3);
                float search=float.IsFinite(alongSearch)?Mathf.Clamp(alongSearch,0,.25f):0;
                int taskCount = BuildShiftedSearchTasks(workspace, n, b, candidates, seed, transpose, independent, channels,rows,search);
                workspace.PrepareSearch(taskCount, rows, columns);
                pending = new PrepareJob { source=pixels, values=workspace.values, gradients=workspace.gradients,
                    width=aw, n=n, rows=rows, transpose=transpose }.Schedule(aw*ah,64);
                // All channels and both cut sides share one batch and one main-thread wait.
                pending = new SearchJob { values=workspace.values, gradients=workspace.gradients,
                    costs=workspace.costs, work=workspace.work, back=workspace.back, cuts=workspace.cuts,
                    tasks=workspace.tasks, scores=workspace.scores, n=n, rows=rows, band=b,
                    columns=columns, closed=closed, channels=channels
                }.Schedule(taskCount,1,pending);
                pending.Complete();
                var options = workspace.options.GetSubArray(0, candidates);
                float4 selectedDonors=default, selectedShifts=default;
                // Three texture rows: left cut, right cut, full-resolution donor offset.
                // RGBA stores either one shared solution or independent channel solutions.
                for (int channel = 0; channel < count; channel++)
                {
                    if (independent && channels[channel] == 0) continue;
                    int searchSeed = unchecked(seed + channel*7919 + (transpose ? 104729 : 0));
                    // Expand aliases in the original candidate order, including duplicates.
                    // Their multiplicity and tie order still participate in seeded selection.
                    for (int k = 0; k < candidates; k++)
                    {
                        int index = workspace.aliases[channel*candidates+k];
                        options[k] = new Candidate { donor=workspace.tasks[index].donor, index=index,
                            score=(float)(((double)workspace.scores[index]+workspace.scores[index+1])/(2*rows)) };
                    }
                    options.Sort();
                    float threshold=options[0].score*1.1f+1e-12f;
                    int eligible=1;
                    while(eligible<candidates && options[eligible].score<=threshold) eligible++;
                    Candidate best=options[(int)(Hash(unchecked((uint)searchSeed ^ 0x9e3779b9u))%(uint)eligible)];
                    if(independent)selectedDonors[channel]=best.donor;else selectedDonors=new float4(best.donor);
                    float shift=workspace.tasks[best.index].shift;
                    if(independent)selectedShifts[channel]=shift;else selectedShifts=new float4(shift);
                    int donor = n > 2*b ? Mathf.RoundToInt(best.donor * (full - 2*fullBand) / (float)(n - 2*b)) : 0;
                    for (int y = 0; y < rows; y++)
                    {
                        int cutOffset=best.index*rows;
                        float left = (workspace.cuts[cutOffset+y]+1) / (float)Math.Max(1, 2*b-1);
                        float right = (workspace.cuts[cutOffset+rows+y]+2*b-Math.Max(1,b-2)) / (float)Math.Max(1, 2*b-1);
                        float4 leftValue=map[y], rightValue=map[rows+y], donorValue=map[2*rows+y];
                        for (int c = independent ? channel : 0; c < (independent ? channel + 1 : 4); c++)
                        {
                            leftValue[c] = left;
                            rightValue[c] = right;
                            donorValue[c] = donor;
                        }
                        map[y]=leftValue; map[rows+y]=rightValue; map[2*rows+y]=donorValue;
                    }
                }
                workspace.paths.SetPixelData(map,0); workspace.paths.Apply(false, false);
                material.SetTexture("_Paths", workspace.paths);
                material.SetVector("_Size", new Vector4(width, height, 1f/width, 1f/height));
                material.SetInt("_Transpose", transpose ? 1 : 0);
                material.SetVector("_AlongShift",(Vector4)selectedShifts);
                material.SetFloat("_ShiftGuard",rows>5?2f/(rows-1):.5f);
                material.SetFloat("_Band", fullBand);
                material.SetFloat("_Feather", Mathf.Clamp01(feather * .01f));
                material.SetInt("_Tiny", b < 4 ? 1 : 0);
                float compensation=feather>0 && fullBand>1 ? Mathf.Clamp01(contrast) : 0;
                material.SetFloat("_Contrast",compensation);
                if(compensation>0)
                {
                    workspace.contrast ??= new PatchQuiltingContrast();
                    workspace.contrast.ConfigureShifted(material,workspace.values,n,rows,b,selectedDonors,selectedShifts);
                    gaussian=Temporary(width,height);
                    Graphics.Blit(source,gaussian,material,3);
                    material.SetTexture("_Gaussian",gaussian);
                }
                output = Temporary(width, height);
                Graphics.Blit(source, output, material, 1);
                reusable = true;
                var result = output; output = null; return result;
            }
            finally
            {
                try { pending.Complete(); }
                finally
                {
                    material.SetTexture("_Paths", null);
                    material.SetTexture("_Tables", null);material.SetTexture("_Gaussian", null);material.SetFloat("_Contrast",0);
                    RenderTexture.ReleaseTemporary(analysis);
                    if(gaussian!=null)RenderTexture.ReleaseTemporary(gaussian);
                    if (output != null) RenderTexture.ReleaseTemporary(output);
                    if (reusable) ReturnWorkspace(workspace); else workspace.Dispose();
                }
            }
        }

        private static int BuildSearchTasks(Workspace workspace, int n, int band, int candidates,
            int seed, bool transpose, bool independent, Vector4 channels)
            => BuildShiftedSearchTasks(workspace,n,band,candidates,seed,transpose,independent,channels,1,0);

        private static int BuildShiftedSearchTasks(Workspace workspace, int n, int band, int candidates,
            int seed, bool transpose, bool independent, Vector4 channels,int rows,float alongSearch)
        {
            int taskCount = 0;
            for (int channel = 0; channel < (independent ? 4 : 1); channel++)
            {
                if (independent && channels[channel] == 0) continue;
                int searchSeed = unchecked(seed + channel*7919 + (transpose ? 104729 : 0));
                int firstTask = taskCount;
                for (int k = 0; k < candidates; k++)
                {
                    float jitter = (Hash(unchecked((uint)searchSeed + (uint)k*747796405u)) & 65535) / 65536f;
                    bool expanded=alongSearch>0&&rows>5;
                    int half=expanded?candidates/2:candidates;
                    int slot=expanded?k%half:k;
                    int donor = slot == 0 ? (n-2*band)/2 : (int)math.round((slot+jitter)/half*(n-2*band));
                    float shift=0;
                    if(expanded&&k>=half)
                    {
                        uint key=Hash(unchecked((uint)searchSeed ^ (uint)k*2246822519u));
                        float amount=.25f+.75f*(key&65535)/65535f;
                        shift=(k%2==0?-1:1)*alongSearch*amount;
                    }
                    int index = firstTask;
                    while (index < taskCount && (workspace.tasks[index].donor != donor || workspace.tasks[index].shift != shift)) index += 2;
                    if (index == taskCount)
                    {
                        for (int side = 0; side < 2; side++)
                            workspace.tasks[taskCount++] = new SearchTask
                                { donor=donor, channel=independent ? channel : -1, side=side, shift=shift };
                    }
                    workspace.aliases[channel*candidates+k] = index;
                }
            }
            return taskCount;
        }

        internal static float DonorRow(float row,int rows,float shift)
        {
            if(shift==0||rows<=5)return row;
            float span=rows-5,t=math.saturate((row-2)/span);
            float q=t*(1-t);
            return row+shift*span*16*q*q;
        }

        internal static float4 ShiftedSample(NativeArray<float4> source,int n,int rows,int column,float row,float shift)
        {
            float position=DonorRow(row,rows,shift);
            int lo=math.clamp((int)math.floor(position),0,rows-1),hi=math.min(lo+1,rows-1);
            return math.lerp(source[lo*n+column],source[hi*n+column],position-lo);
        }

        [BurstCompile(FloatMode=FloatMode.Strict)]
        private struct PrepareJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> source;
            [WriteOnly] public NativeArray<float4> values, gradients;
            public int width, n, rows;
            public bool transpose;
            public void Execute(int index)
            {
                int x=index%n, y=index/n;
                int next=Math.Min(rows-1,y+1), previous=Math.Max(0,y-1);
                values[index]=source[transpose ? x*width+y : y*width+x];
                gradients[index]=source[transpose ? x*width+next : next*width+x]
                    -source[transpose ? x*width+previous : previous*width+x];
            }
        }

        [BurstCompile(FloatMode=FloatMode.Strict)]
        private struct SearchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> values, gradients;
            // Each job owns [index*stride, (index+1)*stride); no slices overlap.
            [NativeDisableParallelForRestriction] public NativeArray<float> costs, work;
            [NativeDisableParallelForRestriction] public NativeArray<sbyte> back;
            [NativeDisableParallelForRestriction] public NativeArray<int> cuts;
            [ReadOnly] public NativeArray<SearchTask> tasks;
            [WriteOnly] public NativeArray<float> scores;
            public int n, rows, band, columns;
            public bool closed;
            public Vector4 channels;

            public void Execute(int k)
            {
                var task=tasks[k];
                int costOffset=k*rows*columns, workOffset=k*2*columns, pathOffset=k*rows;
                int start=task.side==0 ? 1 : 2*band-Math.Max(1,band-2);
                for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
                {
                    int strip=start+x;
                    int sourceColumn=n-band+strip;
                    if(sourceColumn>=n)sourceColumn-=n;
                    int a=y*n+sourceColumn, b=y*n+Math.Min(n-1,task.donor+strip);
                    float4 donorValue=values[b],donorGradient=gradients[b];
                    if(task.shift!=0)
                    {
                        int column=Math.Min(n-1,task.donor+strip);
                        donorValue=ShiftedSample(values,n,rows,column,y,task.shift);
                        donorGradient=ShiftedSample(values,n,rows,column,Math.Min(rows-1,y+1),task.shift)
                            -ShiftedSample(values,n,rows,column,Math.Max(0,y-1),task.shift);
                    }
                    double error=Error(values[a],donorValue,task.channel)+4*Error(gradients[a],donorGradient,task.channel);
                    costs[costOffset+y*columns+x]=(float)Math.Min(error,1e30);
                }
                scores[k]=MinimumCut(costs,work,back,cuts,rows,columns,closed,costOffset,workOffset,pathOffset);
            }
            private double Error(float4 a,float4 b,int singleChannel)
            {
                if(singleChannel>=0) return Square(a[singleChannel],b[singleChannel]);
                double sum=0;
                if(channels.x>0) sum+=Square(a.x,b.x);
                if(channels.y>0) sum+=Square(a.y,b.y);
                if(channels.z>0) sum+=Square(a.z,b.z);
                if(channels.w>0) sum+=Square(a.w,b.w);
                return sum;
            }
            private static double Square(float a,float b)
            {
                double d=(double)a-b;
                return math.isfinite(d) ? d*d : 0;
            }
        }

        internal static float MinimumCut(NativeArray<float> cost, NativeArray<float> work,
            NativeArray<sbyte> back, NativeArray<int> path, int rows, int columns, bool closed,
            int costOffset, int workOffset, int pathOffset)
        {
            int previous=workOffset, next=workOffset+columns;
            int anchor = 0, guard = Math.Min(3, Math.Max(1, rows/2));
            if (closed)
            {
                // Bounded cyclic approximation: pick a shared low-cost guard anchor,
                // then solve the interior exactly subject to that fixed boundary.
                double best = double.PositiveInfinity;
                for (int x = 0; x < columns; x++)
                {
                    double value = 0;
                    for (int y = 0; y < guard; y++) value += cost[costOffset+y*columns+x]+cost[costOffset+(rows-1-y)*columns+x];
                    if (value < best) { best=value; anchor=x; }
                }
            }
            for (int x = 0; x < columns; x++) work[previous+x] = closed && x != anchor ? float.PositiveInfinity : cost[costOffset+x];
            for (int y = 1; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int from = x;
                    if (x > 0 && work[previous+x-1] < work[previous+from]) from=x-1;
                    if (x+1 < columns && work[previous+x+1] < work[previous+from]) from=x+1;
                    work[next+x] = closed && (y < guard || y >= rows-guard) && x != anchor
                        ? float.PositiveInfinity : work[previous+from]+cost[costOffset+y*columns+x];
                    back[costOffset+y*columns+x] = (sbyte)(from-x);
                }
                var swap=previous; previous=next; next=swap;
            }
            int last = closed ? anchor : 0;
            if (!closed) for (int x=1;x<columns;x++) if(work[previous+x]<work[previous+last]) last=x;
            float score=work[previous+last]; path[pathOffset+rows-1]=last;
            for(int y=rows-1;y>0;y--) { last+=back[costOffset+y*columns+last]; path[pathOffset+y-1]=last; }
            return score;
        }

        private static uint Hash(uint x)
        {
            unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); }
        }

        private static RenderTexture Temporary(int width, int height)
        {
            var rt=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            rt.filterMode=FilterMode.Bilinear; rt.wrapMode=TextureWrapMode.Clamp; return rt;
        }
    }
}
