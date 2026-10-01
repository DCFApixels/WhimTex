// Opt-in after manual compilation. No assets, imports, rendering or Undo.
var type = typeof(DCFApixels.WhimTex.WhimTexApi);
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var setter = type.GetMethod("SetNoise", flags);
var snapshot = type.GetMethod("NoiseSnapshot", flags);
var jsonType = setter.GetParameters()[1].ParameterType;
object Json(string text) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
var layer = new DCFApixels.WhimTex.NoiseLayerBehaviour();
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Set(string json) => setter.Invoke(null, new object[] { layer, Json(json) });
void Reject(string json)
{
    bool rejected = false;
    try { Set(json); }
    catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException?.GetType().Name == "WhimTexApiException"; }
    Check(rejected, "Reject " + json);
}
Check(layer.seed == 1337 && layer.scale == 8 && layer.octaves == 3, "Defaults");
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
Set("{\"noiseType\":\"WhiteNoise\",\"whiteNoiseColor\":\"Color\",\"whiteNoiseSize\":4}");
Check(layer.noiseType == DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseType.WhiteNoise &&
    layer.whiteNoiseColor == DCFApixels.WhimTex.NoiseLayerBehaviour.WhiteNoiseColor.Color && layer.whiteNoiseSize == 4,
    "White Noise settings");
setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "White Noise settings round trip");
Reject("{\"whiteNoiseColor\":\"Unknown\"}");
Reject("{\"whiteNoiseSize\":0}");
Reject("{\"whiteNoiseSize\":1025}");
Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("noiseWhiteColors"), "White color discovery");
Set("{\"noiseType\":\"BlueNoise\"}");
Check(layer.noiseType == DCFApixels.WhimTex.NoiseLayerBehaviour.NoiseType.BlueNoise && layer.whiteNoiseSize == 4,
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
foreach (string invalid in new[] { "{\"scale\":[1]}", "{\"scale\":[1,1001]}", "{\"offset\":[0,0,10001]}", "{\"offset\":[0,0,0,0]}",
    "{\"periodic\":\"Z\"}", "{\"dimensions\":\"FourD\"}", "{\"linkScale\":1}" }) Reject(invalid);
var legacy = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.NoiseLayerBehaviour>("{\"scale\":3.25,\"offset\":{\"x\":1,\"y\":2}}");
Check(legacy.Scale.x == 3.25f && legacy.Scale.y == 3.25f && legacy.offset.z == 0, "Existing scalar scale and XY offset retain their appearance");
Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("noisePeriodicAxes"), "Periodicity discovery");
return "Noise API checks passed: " + checks;
