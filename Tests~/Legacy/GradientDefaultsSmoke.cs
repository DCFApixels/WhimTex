using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class GradientDefaultsSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    public static string Main()
    {
        checks = 0;
        var assembly = typeof(WhimTexGradient).Assembly;
        var clipboard = assembly.GetType("DCFApixels.WhimTex.WhimTexGradientClipboard");
        WhimTexGradient Read(string json) => (WhimTexGradient)clipboard.GetMethod("Read", F).Invoke(null, new object[] { json });
        var read = typeof(WhimTexApi).GetMethod("ReadGradient", F);
        Check((WhimTexGradientMode)read.GetParameters()[1].DefaultValue == WhimTexGradientMode.Perceptual, "API default");
        Check(new WhimTexGradient().Mode == WhimTexGradientMode.Perceptual, "Constructor default");
        Check(GradientUtility.WhiteToBlack.Mode == WhimTexGradientMode.Perceptual, "Shared ramp default");
        Check(GradientUtility.Create((WhimTexGradient)null).Mode == WhimTexGradientMode.Perceptual, "Null fallback");
        Check(new GradientLayerBehaviour().gradient.Mode == WhimTexGradientMode.Perceptual, "Gradient layer default");
        Check(new SDFLayerBehaviour().gradient.Mode == WhimTexGradientMode.Perceptual, "SDF defaults to Perceptual");
        var sdfGradient = new SDFLayerBehaviour().gradient;
        var noiseGradient = new NoiseLayerBehaviour().gradient;
        Check(sdfGradient.Equals(noiseGradient), "SDF and Noise share identical default gradient values");
        foreach (var gradient in new[] { sdfGradient, noiseGradient })
        {
            Check(gradient.Mode == WhimTexGradientMode.Perceptual, "SDF/Noise default Perceptual");
            Check(gradient.ColorKeys.Length == 2, "Two default color keys");
            Check(gradient.ColorKeys[0].time == 0f && gradient.ColorKeys[0].color == Color.black, "Default key 0 is black");
            Check(gradient.ColorKeys[1].time == 1f && gradient.ColorKeys[1].color == Color.white, "Default key 1 is white");
            Check(gradient.Evaluate(0).a == 1f && gradient.Evaluate(1).a == 1f, "Opaque default endpoints");
        }
        Check(GradientUtility.CreateLinearWhiteToBlack().Mode == WhimTexGradientMode.Linear, "Explicit Linear helper");
        var brushType = assembly.GetType("DCFApixels.WhimTex.BrushDynamics");
        var brush = Activator.CreateInstance(brushType, true);
        Check(((WhimTexGradient)brushType.GetField("tipGradient", F).GetValue(brush)).Mode == WhimTexGradientMode.Perceptual, "Brush tip default");
        Check(((WhimTexGradient)brushType.GetField("tintGradient", F).GetValue(brush)).Mode == WhimTexGradientMode.Perceptual, "Brush tint default");
        var patternType = assembly.GetType("DCFApixels.WhimTex.FillPatternSettings");
        var pattern = Activator.CreateInstance(patternType, true);
        foreach (string field in new[] { "gradient", "palette" })
            Check(((WhimTexGradient)patternType.GetField(field, F).GetValue(pattern)).Mode == WhimTexGradientMode.Linear, "Pattern stays Linear: " + field);
        var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        var parameters = (List<ShaderFXParameter>)metadata.GetMethod("Parse", F).Invoke(null, new object[] { "// @param gradient _Ramp", false, null });
        Check(parameters[0].gradientValue.Mode == WhimTexGradientMode.Perceptual, "FX default");
        const string stops = "[{\"time\":0,\"color\":[1,0,0,1]},{\"time\":1,\"color\":[0,0,1,1]}]";
        Check(Read(stops).Mode == WhimTexGradientMode.Perceptual, "Clipboard stops default");
        Check(Read("{\"colors\":" + stops + "}").Mode == WhimTexGradientMode.Perceptual, "Clipboard object default");
        CheckBrushInput(stops, WhimTexGradientMode.Perceptual, WhimTexGradientMode.Perceptual);
        CheckBrushInput("{\"colors\":" + stops + "}", WhimTexGradientMode.Perceptual, WhimTexGradientMode.Perceptual);
        foreach (WhimTexGradientMode mode in Enum.GetValues(typeof(WhimTexGradientMode)))
        {
            CheckBrushInput("{\"colors\":" + stops + ",\"mode\":\"" + mode + "\",\"smoothness\":0.35}", mode, mode);
            var gradient = Read("{\"colors\":" + stops + ",\"mode\":\"" + mode + "\"}");
            Check(gradient.Mode == mode, "Explicit clipboard mode preserved: " + mode);
            Check(gradient.Clone().Mode == mode, "Clone preserves mode");
            Check(JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(gradient)).Mode == mode, "Serialized mode preserved");
            var document = ScriptableObject.CreateInstance<TextureCompositor>();
            try
            {
                document.layers.Add(new GradientLayerBehaviour { gradient = gradient.Clone() });
                typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(document, null);
                var json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact }).Json;
                using var restored = WhimTexDocumentJson.Read(json);
                Check(((GradientLayerBehaviour)restored.Document.layers[0].Behaviour).gradient.Equals(gradient), "Compact document roundtrip: " + mode);
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }
        return "PASS: " + checks + " gradient defaults, explicit overrides and roundtrip checks.";
    }

    private static void CheckBrushInput(string gradient, WhimTexGradientMode tipMode, WhimTexGradientMode tintMode)
    {
        // Both paths operate on fresh managed values only, without painting or touching a document.
        string settingsJson = "{\"tipGradient\":" + gradient + ",\"tintGradient\":" + gradient + "}";
        var layer = new DrawingLayerBehaviour();
        var setBrush = typeof(WhimTexApi).GetMethod("SetBrush", F);
        var jsonType = setBrush.GetParameters()[2].ParameterType;
        var settings = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { settingsJson });
        setBrush.Invoke(null, new object[] { null, layer, settings });
        var dynamics = typeof(DrawingLayerBehaviour).GetField("brushDynamics", F).GetValue(layer);
        WhimTexGradient Get(object value, string name) => (WhimTexGradient)value.GetType().GetField(name, F).GetValue(value);
        var tip = Get(dynamics, "tipGradient");
        var tint = Get(dynamics, "tintGradient");
        Check(tip.Mode == tipMode, "API brush tip mode: expected " + tipMode + ", got " + tip.Mode);
        Check(tint.Mode == tintMode, "API brush tint mode: " + tintMode);
        var args = new object[] { "{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"settings\":" + settingsJson + "}", null };
        var clipboard = typeof(WhimTexApi).GetMethod("ReadBrushClipboard", F).Invoke(null, args);
        var clipboardDynamics = clipboard.GetType().GetField("dynamics", F).GetValue(clipboard);
        Check(tip.Equals(Get(clipboardDynamics, "tipGradient")), "Tip matches brush clipboard");
        Check(tint.Equals(Get(clipboardDynamics, "tintGradient")), "Tint matches brush clipboard");
        setBrush.Invoke(null, new object[] { null, layer, Activator.CreateInstance(jsonType) });
        Check(tip.Equals(Get(dynamics, "tipGradient")) && tint.Equals(Get(dynamics, "tintGradient")), "Omitted gradients remain unchanged");
    }
}
