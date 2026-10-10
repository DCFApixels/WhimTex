using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class NoiseApiTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseApiTests", Body);
    static void Body()
    {
        // Opt-in after manual compilation. No assets, imports, rendering or Undo.
        var type = typeof(DCFApixels.WhimTex.WhimTexApi);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var setter = type.GetMethod("SetNoise", flags);
        var snapshot = type.GetMethod("NoiseSnapshot", flags);
        var jsonType = setter.GetParameters()[1].ParameterType;
        object Json(string text) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
        var layer = new DCFApixels.WhimTex.NoiseLayerBehaviour();
        int checks = 0;
        void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
        void Set(string json) => setter.Invoke(null, new object[] { layer, Json(json) });
        void Reject(string json)
        {
            bool rejected = false;
            try { Set(json); }
            catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException?.GetType().Name == "WhimTexApiException"; }
            Check(rejected, "Reject " + json);
        }
        Check(layer.seed == 1337 && layer.scale == 8 && layer.octaves == 3, "Defaults");
        Check(!layer.periodic1D, "1D Seamless defaults off");
        Set("{\"dimensions\":\"OneD\",\"periodic1D\":true,\"periodic\":\"Y\"}");
        Check(layer.periodic1D && layer.periodic == DCFApixels.WhimTex.NoiseLayerBehaviour.PeriodicAxes.Y, "Separate 1D and 2D Seamless settings");
        Reject("{\"periodic1D\":1}");
        Check(layer.WarpScale == UnityEngine.Vector2.one && layer.linkWarpScale, "Warp multiplier defaults");
        Set("{\"warpScale\":3}");
        Check(layer.warpScale == 3 && layer.scale == 8, "Warp Scale independent of Noise Scale");
        Set("{\"scale\":0.5}");
        Check(layer.WarpScale == new UnityEngine.Vector2(3,3), "Noise Scale retains Warp multipliers");
        Set("{\"warpScale\":[2,5],\"linkWarpScale\":true}");
        Check(layer.WarpScale == new UnityEngine.Vector2(2,5), "Literal anisotropic warp multipliers with chain");
        Reject("{\"warpScale\":[1]}"); Reject("{\"warpScale\":[1,1001]}"); Reject("{\"linkWarpScale\":1}");
        Reject("{\"warpScale\":0}"); Reject("{\"warpScale\":1001}");
        Check(layer.encoding == DCFApixels.WhimTex.NoiseLayerBehaviour.OutputEncoding.LinearData && layer.gradient.Mode == DCFApixels.WhimTex.WhimTexGradientMode.Perceptual, "Linear Data output and Perceptual palette defaults");
        Set("{\"encoding\":\"ColorValues\",\"gradient\":[{\"time\":0,\"color\":[1,0,0,0.5]},{\"time\":1,\"color\":[0,0,1,1]}]}");
        Check(layer.encoding == DCFApixels.WhimTex.NoiseLayerBehaviour.OutputEncoding.ColorValues && layer.gradient.Mode == DCFApixels.WhimTex.WhimTexGradientMode.Perceptual, "Supplied stops default to Perceptual");
        var savedGradient = layer.gradient.Clone();
        Set("{\"encoding\":\"Gradient\"}");
        Check(layer.encoding == DCFApixels.WhimTex.NoiseLayerBehaviour.OutputEncoding.Gradient && layer.gradient.Equals(savedGradient), "Output retains palette");
        Reject("{\"encoding\":\"Unknown\"}");
        Set("{\"seed\":2147483647,\"noiseType\":\"Cellular\",\"scale\":12.5,\"offset\":[3,-4],\"fractal\":\"Ridged\",\"octaves\":6,\"lacunarity\":3,\"gain\":0.6,\"weightedStrength\":0.2,\"pingPongStrength\":3,\"cellularDistance\":\"Hybrid\",\"cellularReturn\":\"Distance2Sub\",\"cellularJitter\":0.75,\"warp\":\"BasicGrid\",\"warpStrength\":2,\"encoding\":\"LinearData\",\"inverted\":true}");
        Check(layer.seed == int.MaxValue && layer.offset.y == -4 && layer.inverted, "Set parameters");
        var copy = new DCFApixels.WhimTex.NoiseLayerBehaviour();
        setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
        Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "Settings round trip");
        Set("{\"seed\":-2147483648}");
        Check(layer.seed == int.MinValue && layer.scale == 12.5f, "Partial update and full seed range");
        Set("{\"noiseType\":\"WhiteNoise\",\"grainColor\":\"Color\",\"grainSize\":4}");
        Check(layer.noiseType == DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseType.WhiteNoise &&
            layer.grainColor == DCFApixels.WhimTex.NoiseLayerBehaviour.GrainColor.Color && layer.grainSize == 4,
            "White Noise settings");
        setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
        Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "White Noise settings round trip");
        Reject("{\"grainColor\":\"Unknown\"}");
        Reject("{\"grainSize\":0}");
        Reject("{\"grainSize\":1025}");
        Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("noiseGrainColors"), "White color discovery");
        Set("{\"noiseType\":\"BlueNoise\"}");
        Check(layer.noiseType == DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseType.BlueNoise && layer.grainSize == 4,
            "Blue Noise shares grain settings");
        setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
        Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "Blue Noise settings round trip");
        Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("BlueNoise"), "Blue Noise discovery");
        foreach (string json in new[] { "{\"scale\":0}", "{\"octaves\":9}", "{\"octaves\":1.5}", "{\"offset\":[10001,0]}",
            "{\"seed\":2147483648}", "{\"noiseType\":\"Unknown\"}", "{\"warp\":\"Unknown\"}", "{\"cellularJitter\":2}", "{\"unused\":true}" }) Reject(json);
        Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("noiseDefaults"), "Discovery");
        Set("{\"dimensions\":\"ThreeD\",\"periodic\":\"XY\",\"scale\":[6.3,10.7],\"linkScale\":false,\"offset\":[3,-4,5]}");
        Check(layer.dimensions == DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseDimensions.ThreeD && layer.offset.z == 5 &&
            layer.Scale.y == 10.7f && !layer.linkScale, "3D anisotropic periodic settings");
        setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
        Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "3D settings round trip");
        Set("{\"offset\":[1,2]}");
        Check(layer.offset.z == 5, "XY patch preserves Z");
        Set("{\"scale\":7}");
        Check(layer.Scale.x == 7 && layer.Scale.y == 7, "Scalar scale sets both axes");
        Set("{\"noiseType\":\"Perlin\",\"dimensions\":\"ThreeD\",\"scale\":[4,8,2],\"linkScale\":true}");
        Check(layer.Scale3D==new Vector3(4,8,2),"API XYZ are literal even with chain enabled");
        Set("{\"scale\":[6,9]}");Check(layer.Scale3D==new Vector3(6,9,2),"API XY preserves Scale Z");
        Set("{\"scale\":7}");Check(layer.Scale3D==new Vector3(7,7,7),"3D scalar sets XYZ");
        Set("{\"dimensions\":\"TwoD\",\"scale\":3}");Check(layer.Scale3D==new Vector3(3,3,7),"2D scalar preserves inactive Z");
        setter.Invoke(null,new object[]{copy,snapshot.Invoke(null,new object[]{layer})});
        Check(copy.Scale3D==layer.Scale3D,"Snapshot retains inactive Z");
        foreach(string invalid in new[]{"{\"scale\":[1,2,0]}","{\"scale\":[1,2,1001]}","{\"scale\":[1,2,\"bad\"]}","{\"scale\":[1,2,3,4]}","{\"scaleZ\":2}"})Reject(invalid);
        foreach (string invalid in new[] { "{\"scale\":[1]}", "{\"scale\":[1,1001]}", "{\"offset\":[0,0,10001]}", "{\"offset\":[0,0,0,0]}",
            "{\"periodic\":\"Z\"}", "{\"dimensions\":\"FourD\"}", "{\"linkScale\":1}" }) Reject(invalid);
        var current = new DCFApixels.WhimTex.NoiseLayerBehaviour();
        Check(current.Scale == new UnityEngine.Vector2(8,8), "Runtime defaults store both scale axes explicitly");
        Check(current.Scale3D==new Vector3(8,8,1),"Default Z keeps existing 3D slices unchanged");
        Check(current.WarpScale == UnityEngine.Vector2.one, "Runtime Warp Scale defaults to multiplier 1");
        Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("noisePeriodicAxes"), "Periodicity discovery");
        Set("{\"linkScale\":true,\"scale\":[4,9],\"encoding\":\"LinearData\",\"gradient\":{\"colors\":[{\"time\":0,\"color\":[0,0,0,0.25]},{\"time\":1,\"color\":[2,1,0,1]}],\"mode\":\"Linear\"}}");
        Check(layer.Scale == new UnityEngine.Vector2(4,9), "API axes are literal even with chain enabled");
        Check(layer.encoding == DCFApixels.WhimTex.NoiseLayerBehaviour.OutputEncoding.LinearData &&
            layer.gradient.Mode == DCFApixels.WhimTex.WhimTexGradientMode.Linear, "Palette assignment does not enable Gradient; explicit mode retained");
        Set("{\"gradient\":[{\"time\":0,\"color\":[0,0,0,1]},{\"time\":1,\"color\":[1,1,1,1]}]}");
        Check(layer.gradient.Mode == DCFApixels.WhimTex.WhimTexGradientMode.Perceptual, "Replacement palette uses its own default mode");
        foreach(string invalid in new[]{"{\"seamless\":true}","{\"scaleY\":2}","{\"offsetZ\":1}","{\"useGradient\":true}"}) Reject(invalid);
        var sdf = new DCFApixels.WhimTex.SDFLayerBehaviour();
        Check(sdf.encoding == DCFApixels.WhimTex.SDFLayerBehaviour.OutputEncoding.Gradient &&
            sdf.gradient.Equals(new DCFApixels.WhimTex.NoiseLayerBehaviour().gradient), "SDF Gradient default and identical Noise palette");
        var document = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>());
        document.layers.Add(sdf);
        try
        {
            var setLayer = type.GetMethod("SetLayer", flags);
            void SetSdf(string json) => setLayer.Invoke(null, new object[]{document, (DCFApixels.WhimTex.Layer)sdf, Json(json)});
            SetSdf("{\"encoding\":\"LinearData\",\"inverted\":true}");
            SetSdf("{\"gradient\":{\"colors\":[{\"time\":0,\"color\":[0,0,0,0.25]},{\"time\":1,\"color\":[2,1,0,1]}],\"mode\":\"Fixed\"}}");
            Check(sdf.encoding == DCFApixels.WhimTex.SDFLayerBehaviour.OutputEncoding.LinearData && sdf.inverted &&
                sdf.gradient.Mode == DCFApixels.WhimTex.WhimTexGradientMode.Fixed, "SDF palette assignment retains output/inversion and accepts explicit mode");
            var palette = sdf.gradient.Clone();
            SetSdf("{\"encoding\":\"Gradient\"}");
            Check(sdf.gradient.Equals(palette) && sdf.inverted, "SDF output retains palette/inversion");
            foreach(string invalid in new[]{"{\"encoding\":\"ColorValues\"}","{\"sdf\":{}}","{\"useGradient\":true}"})
            {
                bool rejected=false;
                try{SetSdf(invalid);}catch(System.Reflection.TargetInvocationException e){rejected=e.InnerException?.GetType().Name=="WhimTexApiException";}
                Check(rejected,"Reject SDF "+invalid);
            }
            Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("sdfEncodings"), "SDF output discovery");
        }
        finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document);}
        return;
        
    }
}
