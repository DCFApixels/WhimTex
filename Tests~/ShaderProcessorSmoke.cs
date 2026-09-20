// Opt-in only after the user has compiled Unity. Transient objects; no imports or asset saves.
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 8;
var texture = new UnityEngine.Texture2D(8, 8, UnityEngine.TextureFormat.RGBAFloat, false, true) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
var source = new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = texture, colorRange = DCFApixels.WhimTex.LayerColorRange.HDR, blendRange = DCFApixels.WhimTex.LayerBlendRange.HDR };
var processor = new DCFApixels.WhimTex.ShaderProcessorLayerBehaviour();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new System.Exception(message); checks++; }
void SetPixels(UnityEngine.Color value)
{
    var pixels = new UnityEngine.Color[64];
    System.Array.Fill(pixels, value); texture.SetPixels(pixels); texture.Apply(false, false);
}
UnityEngine.Color Render()
{
    var output = document.Compose();
    try { return output.GetPixel(4, 4); }
    finally { UnityEngine.Object.DestroyImmediate(output); }
}
void Near(UnityEngine.Color actual, UnityEngine.Color expected, string message)
{
    for (int channel = 0; channel < 4; channel++) Check(UnityEngine.Mathf.Abs(actual[channel] - expected[channel]) < .006f, message);
}
try
{
    SetPixels(new UnityEngine.Color(4, 0, 0, .25f));
    document.layers.Add(processor); document.layers.Add(source);
    Near(Render(), new UnityEngine.Color(4, 0, 0, .25f), "Identity preserves HDR and partial alpha");
    processor.opacity = .5f;
    Near(Render(), new UnityEngine.Color(4, 0, 0, .25f), "Identity at partial opacity does not accumulate alpha");
    SetPixels(new UnityEngine.Color(1, 0, 0, .25f));
    processor.swizzle[0] = DCFApixels.WhimTex.SwizzleChannel.Zero;
    processor.swizzle[2] = DCFApixels.WhimTex.SwizzleChannel.R;
    processor.swizzle[3] = DCFApixels.WhimTex.SwizzleChannel.One;
    Near(Render(), new UnityEngine.Color(.2f, 0, .8f, .625f), "Before/after opacity uses premultiplied interpolation");
    processor.opacity = 1;
    Near(Render(), UnityEngine.Color.blue, "Full opacity replaces RGB and alpha");
    processor.enabled = false;
    Near(Render(), new UnityEngine.Color(1, 0, 0, .25f), "Hidden processor bypasses processing");
    processor.enabled = true;
    processor.opacity = 0;
    Near(Render(), new UnityEngine.Color(1, 0, 0, .25f), "Zero opacity bypasses processing");
    processor.opacity = 1;
    processor.swizzle[3] = DCFApixels.WhimTex.SwizzleChannel.A;

    SetPixels(UnityEngine.Color.red);
    var blur = new DCFApixels.WhimTex.BlurLayerBehaviour { radius = 0 };
    document.layers.Insert(0, blur);
    Near(Render(), UnityEngine.Color.blue, "Effect reads the active processor output");
    processor.enabled = false;
    Near(Render(), UnityEngine.Color.red, "Effect cannot reactivate a hidden processor");
    blur.inputMode = DCFApixels.WhimTex.EffectInputMode.Specific;
    // Normalize IDs before assigning an explicit effect target.
    typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("NormalizeModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(document, null);
    blur.TargetLayerId = processor.Id;
    Near(Render(), UnityEngine.Color.red, "Explicit target also bypasses a hidden processor");
    processor.enabled = true;
    Near(Render(), UnityEngine.Color.blue, "Reenabled processor processes the explicit effect input");
    document.layers.Remove(blur);
    SetPixels(new UnityEngine.Color(1, 0, 0, .25f));

    var group = new DCFApixels.WhimTex.GroupLayerBehaviour(); group.layers.Add(processor);
    document.layers[0] = group;
    Near(Render(), new UnityEngine.Color(0, 0, 1, .25f), "Pass Through processor sees external backdrop");
    group.compositing = DCFApixels.WhimTex.GroupCompositing.Isolated;
    Near(Render(), new UnityEngine.Color(1, 0, 0, .25f), "Isolated processor does not touch external backdrop");
    document.layers.Remove(source); group.layers.Add(source);
    Near(Render(), new UnityEngine.Color(0, 0, 1, .25f), "Isolated processor sees lower children");

    document.layers.Clear(); document.layers.Add(processor); document.layers.Add(source);
    var second = new DCFApixels.WhimTex.ShaderProcessorLayerBehaviour();
    second.swizzle[0] = DCFApixels.WhimTex.SwizzleChannel.B; second.swizzle[2] = DCFApixels.WhimTex.SwizzleChannel.Zero;
    document.layers.Insert(0, second);
    Near(Render(), new UnityEngine.Color(1, 0, 0, .25f), "Processor chain follows stack order");
    string serialized = UnityEngine.JsonUtility.ToJson(document);
    var copy = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
    try
    {
        UnityEngine.JsonUtility.FromJsonOverwrite(serialized, copy);
        Check(copy.layers[0]?.Behaviour is DCFApixels.WhimTex.ShaderProcessorLayerBehaviour && copy.layers[1]?.Behaviour is DCFApixels.WhimTex.ShaderProcessorLayerBehaviour, "Processor type survives document serialization");
    }
    finally { UnityEngine.Object.DestroyImmediate(copy); }
    return "Shader Processor checks passed: " + checks;
}
finally
{
    UnityEngine.Object.DestroyImmediate(document);
    UnityEngine.Object.DestroyImmediate(texture);
}
