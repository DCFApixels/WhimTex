using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXFormerlySerializedAsSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        var parse = metadata.GetMethod("Parse", F);
        var preserve = metadata.GetMethod("PreserveValues", F);
        List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)parse.Invoke(null, new object[] { source, false, null });
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        var oldParameters = Parse("// @param float _OldValue = 0.2 [0 .. 1]\n");
        oldParameters[0].floatValue = 0.73f;
        string previousId = oldParameters[0].id;
        const string renamed =
            "// @formerlyserializedas(_EarlierValue)\n" +
            "// @formerlyserializedas(_OldValue)\n" +
            "// @param float _NewValue = 0.5 [0 .. 1]\n";
        var next = Parse(renamed);
        Check(next[0].controls[0].formerlySerializedAs.Length == 2 &&
            next[0].controls[0].formerlySerializedAs[1] == "_OldValue", "Former names parse and retain their order");
        preserve.Invoke(null, new object[] { next, oldParameters });
        Check(Mathf.Approximately(next[0].floatValue, 0.73f) && next[0].id == previousId,
            "Rename migration preserves the compatible parameter value and identity");

        foreach (string invalid in new[]
        {
            "// @formerlyserializedas _OldValue\n// @param float _NewValue\n",
            "// @formerlyserializedas(_OldValue)\n",
            "// @formerlyserializedas(_NewValue)\n// @param float _NewValue\n",
            "// @formerlyserializedas(_OldValue)\n// @param float _NewValue\n// @param float _OldValue\n",
            "// @formerlyserializedas(_OldValue)\n// @param float _NewValue\n// @formerlyserializedas(_OldValue)\n// @param float _Other\n"
        })
        {
            bool rejected = false;
            try { Parse(invalid); }
            catch (TargetInvocationException error) when (error.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Accepted invalid former-name metadata: " + invalid);
        }

        var incompatible = Parse("// @formerlyserializedas(_OldColor)\n// @param float _NewValue = 0.5\n");
        var oldColor = Parse("// @param color _OldColor = #FF0000\n");
        oldColor[0].colorValue = Color.blue;
        string oldColorId = oldColor[0].id;
        preserve.Invoke(null, new object[] { incompatible, oldColor });
        Check(incompatible[0].id != oldColorId && Mathf.Approximately(incompatible[0].floatValue, 0.5f),
            "Incompatible former field types do not migrate");

        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        try
        {
            const string fxSource = "// @whimtex-effect Test/Renamed\n" + renamed +
                "float4 ApplyFX(float2 uv, float4 color) { return color * _NewValue; }\n";
            var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            fx = (ShaderFX)create.Invoke(null, new object[] { document, fxSource, Parse(fxSource) });
            var fxParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            fxParameters[0].floatValue = 0.41f;
            string saved = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", F)
                .Invoke(null, new object[] { fx, "Test/Renamed" });
            Check(saved.Contains("// @formerlyserializedas(_EarlierValue)") && saved.Contains("// @formerlyserializedas(_OldValue)"),
                "FX preset saving preserves former-name directives");
            var restored = Parse(saved);
            Check(restored[0].controls[0].formerlySerializedAs.Length == 2, "FX preset alias roundtrip");

            const string brushSource = "// @whimtex-brush Test/Renamed\n" +
                "// @formerlyserializedas(_OldBrushValue)\n" +
                "// @param float _NewBrushValue = 0.4\n" +
                "float4 BrushTip(float2 uv) { return float4(_NewBrushValue, _NewBrushValue, _NewBrushValue, 1); }\n";
            var brushParameters = Parse(brushSource);
            var brushProgram = assembly.GetType("DCFApixels.WhimTex.BrushTipProgram");
            string brushSaved = (string)brushProgram.GetMethod("Export", F).Invoke(null, new object[] { brushSource, brushParameters });
            Check(brushSaved.Contains("// @formerlyserializedas(_OldBrushValue)") &&
                Parse(brushSaved)[0].controls[0].formerlySerializedAs[0] == "_OldBrushValue",
                "Brush preset export preserves former-name directives");
            return "PASS: former-name parsing, compatible migration, identity preservation, validation and FX/brush preset roundtrips.";
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        finally
        {
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
