using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class MorphRasterRenderer
    {
        internal static RenderTexture RenderGuided(bool raster, RenderTexture from, RenderTexture to, IReadOnlyList<MorphAnchor> anchors, float t,
            FlowCache cache, (Layer, Layer, ulong, ulong, int, int, float) pair, MorphInterpolation interpolation,
            bool classic, bool geometry, IReadOnlyList<MorphSegment> edges, bool stationary, bool visibility, bool three, MorphMaskBlend blend)
        {
            bool authored = false;
            if (raster && !classic && t > 0 && t < 1)
            {
                for (int i=0; !authored && i<(anchors?.Count??0); i++) authored=Active(anchors[i].correction);
                for (int i=0; !authored && i<(edges?.Count??0); i++) authored=edges[i].foreground || Active(edges[i].correction);
            }
            if (!authored)
                return Render(from,to,anchors,t,cache,pair,interpolation,classic,geometry,edges,stationary,visibility,three,blend);

            var parent = cache ?? new FlowCache();
            var atlas = parent.corrections ??= new MorphCorrectionAtlas();
            var oldTarget=RenderTexture.active;
            bool srgb=GL.sRGBWrite;
            var temporaries=new List<RenderTexture>(12);
            try
            {
                atlas.Prepare(anchors,edges,from.width,from.height);
                var baseline=Track(RenderCore(from,to,anchors,t,parent,pair,interpolation,false,geometry,edges,stationary,visibility,three,blend));
                GL.sRGBWrite=false;
                if (atlas.Foreground)
                {
                    var front=Track(Surface(true));
                    var back=Track(Surface(false));
                    material.SetTexture("_FrontLayer",front);
                    material.SetTexture("_BackLayer",back);
                    baseline=Track(Blit(baseline,"CombineMorphLayers"));
                }
                if (atlas.HasLocalChoice || atlas.HasTone)
                {
                    var reference = atlas.HasLocalChoice ? Track(RenderCore(from,to,anchors,t,parent,pair,interpolation,false,geometry,edges,stationary,visibility,three,MorphMaskBlend.Original, Pass("LocalChoiceWarp"), atlas)) : baseline;
                    var coverage=Track(RenderCore(from,to,anchors,t,parent,pair,interpolation,false,geometry,edges,stationary,visibility,three,MorphMaskBlend.Original, Pass("CorrectionCoverage"), atlas));
                    material.SetTexture("_CorrectionReference",reference);
                    material.SetTexture("_CorrectionCoverage",coverage);
                    material.SetFloat("_MorphTime",t);
                    material.SetVector("_RasterSize",new Vector4(from.width,from.height,0,0));
                    if (atlas.HasTone)
                    {
                        var gradual = Track(RenderCore(from, to, anchors, t, parent, pair, interpolation, false,
                            geometry, edges, stationary, visibility, three, MorphMaskBlend.Original, Pass("GradualBlendWarp"), atlas));
                        material.SetTexture("_CorrectionReference", gradual);
                        int w = Mathf.Max(1, (from.width + 3) / 4), h = Mathf.Max(1, (from.height + 3) / 4);
                        float radius = Mathf.Max(1, Mathf.Min(w, h) * .0075f);
                        var difference = Track(Blit(baseline, "MorphToneDifference", w, h));
                        material.SetVector("_BlurStep", new Vector4(radius / w, 0, 0, 0));
                        var horizontal = Track(Blit(difference, "MorphToneBlur", w, h));
                        material.SetVector("_BlurStep", new Vector4(0, radius / h, 0, 0));
                        material.SetTexture("_CorrectionTone", Track(Blit(horizontal, "MorphToneBlur", w, h)));
                        material.SetTexture("_CorrectionReference", reference);
                    }
                    baseline=Track(Blit(baseline,"ApplyMorphCorrection"));
                }
                temporaries.Remove(baseline);
                return baseline;
            }
            finally
            {
                RenderTexture.active=oldTarget; GL.sRGBWrite=srgb;
                if(material!=null)
                    foreach(string id in BoundCorrectionTextures) material.SetTexture(id,null);
                foreach(var rt in temporaries) ProfiledRenderTexture.ReleaseTemporary(rt);
                if(cache==null) parent.Clear();
            }

            RenderTexture Surface(bool foreground)
            {
                material.SetTexture("_GuideAtlas",atlas.Texture);
                material.SetInteger("_FrontSide",foreground?1:0);
                material.SetInteger("_EndpointIndex",0);
                var a=Track(Blit(from,"ExtractMorphSurface"));
                material.SetInteger("_EndpointIndex",1);
                var b=Track(Blit(to,"ExtractMorphSurface"));
                material.SetTexture("_ContourDistance", atlas.Distances);
                material.SetTexture("_SurfaceKeyA", from);
                material.SetTexture("_SurfaceKeyB", to);
                return RenderCore(a,b,foreground?atlas.frontPoints:atlas.backPoints,t,foreground?atlas.front:atlas.back,
                    pair,interpolation,false,geometry,foreground?atlas.frontEdges:atlas.backEdges,stationary,visibility,three,blend,Pass("MorphSurfaceWarp"),atlas);
            }

            RenderTexture Track(RenderTexture rt){temporaries.Add(rt);return rt;}
            RenderTexture Blit(RenderTexture source,string pass,int w=0,int h=0)
            {
                var target=HdrUtility.Temporary(w==0?from.width:w,h==0?from.height:h,
                    from.format==RenderTextureFormat.ARGBFloat || to.format==RenderTextureFormat.ARGBFloat);
                try
                {
                    Graphics.Blit(source, target, material, Pass(pass));
                    return target;
                }
                catch { ProfiledRenderTexture.ReleaseTemporary(target); throw; }
            }
        }

        private static RenderTexture PrepareLocalPreference(RenderTexture from, int width, int height)
        {
            float scale = Mathf.Min(1, 192f / Mathf.Max(width, height));
            int w = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            var votes = HdrUtility.Temporary(w, h, false);
            RenderTexture filtered = null;
            try
            {
                Graphics.Blit(from, votes, material, Pass("MeasureLocalPreference"));
                filtered = HdrUtility.Temporary(w, h, false);
                Graphics.Blit(votes, filtered, material, Pass("SmoothLocalPreference"));
                return filtered;
            }
            catch
            {
                if (filtered != null) ProfiledRenderTexture.ReleaseTemporary(filtered);
                throw;
            }
            finally { ProfiledRenderTexture.ReleaseTemporary(votes); }
        }

        private static int Pass(string name)
        {
            int index = material.FindPass(name);
            return index >= 0 ? index : throw new InvalidOperationException("Missing morph shader pass " + name);
        }

        private static readonly string[] BoundCorrectionTextures={"_ContourDistance","_SurfaceKeyA","_SurfaceKeyB","_GuideAtlas","_FrontLayer","_BackLayer","_CorrectionReference","_CorrectionCoverage","_CorrectionTone"};
        private static bool Active(MorphGuideCorrection c) => c.mode!=MorphCorrectionMode.None && c.width>0 && c.strength>0;
    }
}
