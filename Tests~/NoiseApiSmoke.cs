// Opt-in after manual compilation. No assets, imports, rendering or Undo.
var type = typeof(DCFApixels.SpriteEditor.SpriteEditorApi);
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var setter = type.GetMethod("SetNoise", flags);
var snapshot = type.GetMethod("NoiseSnapshot", flags);
var jsonType = setter.GetParameters()[1].ParameterType;
object Json(string text) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
var layer = new DCFApixels.SpriteEditor.NoiseLayerBehaviour();
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Set(string json) => setter.Invoke(null, new object[] { layer, Json(json) });
void Reject(string json)
{
    bool rejected = false;
    try { Set(json); }
    catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException?.GetType().Name == "SpriteEditorApiException"; }
    Check(rejected, "Reject " + json);
}
Check(layer.seed == 1337 && layer.scale == 8 && layer.octaves == 3, "Defaults");
Set("{\"seed\":2147483647,\"noiseType\":\"Cellular\",\"scale\":12.5,\"offset\":[3,-4],\"fractal\":\"Ridged\",\"octaves\":6,\"lacunarity\":3,\"gain\":0.6,\"weightedStrength\":0.2,\"pingPongStrength\":3,\"cellularDistance\":\"Hybrid\",\"cellularReturn\":\"Distance2Sub\",\"cellularJitter\":0.75,\"warp\":\"BasicGrid\",\"warpStrength\":2,\"encoding\":\"LinearData\",\"inverted\":true}");
Check(layer.seed == int.MaxValue && layer.offset.y == -4 && layer.inverted, "Set parameters");
var copy = new DCFApixels.SpriteEditor.NoiseLayerBehaviour();
setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "Settings round trip");
Set("{\"seed\":-2147483648}");
Check(layer.seed == int.MinValue && layer.scale == 12.5f, "Partial update and full seed range");
Set("{\"noiseType\":\"WhiteNoise\",\"whiteNoiseColor\":\"Color\",\"whiteNoiseSize\":4}");
Check(layer.noiseType == DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType.WhiteNoise &&
    layer.whiteNoiseColor == DCFApixels.SpriteEditor.NoiseLayerBehaviour.WhiteNoiseColor.Color && layer.whiteNoiseSize == 4,
    "White Noise settings");
setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "White Noise settings round trip");
Reject("{\"whiteNoiseColor\":\"Unknown\"}");
Reject("{\"whiteNoiseSize\":0}");
Reject("{\"whiteNoiseSize\":1025}");
Check(DCFApixels.SpriteEditor.SpriteEditorApi.Describe().Contains("noiseWhiteColors"), "White color discovery");
Set("{\"noiseType\":\"BlueNoise\"}");
Check(layer.noiseType == DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType.BlueNoise && layer.whiteNoiseSize == 4,
    "Blue Noise shares grain settings");
setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { layer }) });
Check(snapshot.Invoke(null, new object[] { layer }).ToString() == snapshot.Invoke(null, new object[] { copy }).ToString(), "Blue Noise settings round trip");
Check(DCFApixels.SpriteEditor.SpriteEditorApi.Describe().Contains("BlueNoise"), "Blue Noise discovery");
foreach (string json in new[] { "{\"scale\":0}", "{\"octaves\":9}", "{\"octaves\":1.5}", "{\"offset\":[10001,0]}",
    "{\"seed\":2147483648}", "{\"noiseType\":\"Unknown\"}", "{\"warp\":\"Unknown\"}", "{\"cellularJitter\":2}", "{\"unused\":true}" }) Reject(json);
Check(DCFApixels.SpriteEditor.SpriteEditorApi.Describe().Contains("noiseDefaults"), "Discovery");
return "Noise API checks passed: " + checks;
