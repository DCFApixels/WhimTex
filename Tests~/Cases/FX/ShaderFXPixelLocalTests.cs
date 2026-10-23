using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class ShaderFXPixelLocalTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly A = typeof(ShaderFX).Assembly;
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static Type Type(string name) => A.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Values(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);

    public static string Run() => TestContext.Run("ApplyFX signatures, immutable PinnedUV, mask sampling and pixel-local queries", t =>
    {
        using (var state = new MigrationD())
        using (var f = new Fixture(t))
        {
            Signatures(t, f);
            Arithmetic(t, f);
            Rejections(t, f);
            Masks(t, f);
            Persistence(t, f);
            Bypasses(t, f);
            BundledPresets(t, f);
            AuthoringExamples(t, f);
        }
    });

    static void Signatures(TestContext t, Fixture f)
    {
        var color = f.Fx("float4 ApplyFX(float4 color) { return color * float4(2, 1, .5, 1); }");
        t.True(color.IsPixelLocal, "Color-only contract is pixel-local");
        var sample = new Color(.2f, .4f, 2, .25f);
        f.Pixel(f.Render(color, sample), new Color(.4f, .4f, 1, .25f), "Color-only dispatch retains signed/HDR and alpha");
        var spatial = f.Fx("float4 ApplyFX(PinnedUV uv, float4 color) { float2 p=uv; return SampleInput(p); }");
        t.True(!spatial.IsPixelLocal, "float2 declaration does not claim pixel locality");
        f.Pixel(f.Render(spatial, sample), sample, "Existing spatial dispatch");
        var pinned = f.Fx(@"
float4 ReadHere(PinnedUV coordinate) { return SampleInput(coordinate); }
float4 ApplyFX(PinnedUV uv, float4 color)
{
    PinnedUV copy = uv;
    float2 p = float2(copy.x, copy.y);
    return float4(p, ReadHere(copy).b, color.a);
}");
        t.True(pinned.IsPixelLocal, "Pinned inputs, copies and helpers remain pixel-local");
        f.Pixel(f.Render(pinned, sample, 2, 1), new Color(2.5f / Fixture.Width, 1.5f / Fixture.Height, 2, .25f),
            "PinnedUV fields contain current normalized coordinates");
        var qualifiers = f.Fx("float4 ApplyFX(const in PinnedUV coordinate, in float4 source) { return SampleInput(coordinate); }");
        t.True(qualifiers.IsPixelLocal, "Const/in qualifiers and arbitrary argument names");
        f.Pixel(f.Render(qualifiers, sample), sample, "Qualified input dispatch");
        var mainTexture = f.Fx("float4 ApplyFX(PinnedUV uv, float4 color) { return SampleTexture(_MainTex, uv); }");
        t.True(mainTexture.IsPixelLocal, "Incoming sampler passed to SampleTexture remains pixel-local");
        t.True(!((string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(mainTexture)).Contains("IsPixelLocal is false"), "Safe MainTex sampling has no bypass warning");
        f.Pixel(f.Render(mainTexture, sample), sample, "SampleTexture MainTex matches SampleInput without changing HDR or alpha");
        var throughSampler = f.Fx("float4 ReadHere(WhimTexTexture source, PinnedUV coordinate) { return SampleTexture(source, coordinate); }\nfloat4 ApplyFX(PinnedUV uv, float4 color) { return ReadHere(_MainTex, uv); }");
        t.True(throughSampler.IsPixelLocal, "Passing MainTex to a pinned sampler helper is not a bypass");
        f.Pixel(f.Render(throughSampler, sample), sample, "Pinned sampler helper matches incoming texture reads");
        var mainSpatial = f.Fx("float4 ApplyFX(PinnedUV uv, float4 color) { return SampleTexture(_MainTex, (float2)uv); }");
        t.True(!mainSpatial.IsPixelLocal, "MainTex sampling with a spatial signature does not claim pixel locality");
        var dimensions = f.Fx("float4 ApplyFX(float4 color) { return color * (_MainTex_TexelSize.z / _InputSize.x); }");
        t.True(dimensions.IsPixelLocal, "Texture dimension metadata is not an image read");
        var include = f.Fx("#include \"Packages/com.dcfapixels.whimtex/Tests~/Fixtures/FX/PinnedUVHelper.hlsl\"\nfloat4 ApplyFX(PinnedUV uv,float4 color) {return ReadPinnedInput(uv);}");
        t.True(include.IsPixelLocal, "Pinned helper in an expanded include retains its contract");
        f.Pixel(f.Render(include, sample), sample, "Pinned type is available in included helpers");

        var layer = new Layer(new ColorFillLayerBehaviour());
        t.True(layer.AreActiveFxPixelLocal(), "Empty chain is pixel-local");
        layer.fx.Add(null); layer.fx.Add(color); layer.fx.Add(pinned);
        t.True(layer.AreActiveFxPixelLocal(), "Null slots do not affect active FX query");
        layer.fx.Add(spatial);
        t.True(!layer.AreActiveFxPixelLocal(), "Active spatial FX prevents all-local answer");
        spatial.Active = false;
        t.True(layer.AreActiveFxPixelLocal(), "Disabled FX does not participate");
        var unapplied = f.Fx("float4 ApplyFX(float4 color) { return color; }", false);
        layer.fx.Add(unapplied);
        t.True(!unapplied.IsPixelLocal && !layer.AreActiveFxPixelLocal(), "Unapplied active FX is not confirmed local");
        unapplied.Active = false;
        t.True(layer.AreActiveFxPixelLocal(), "Disabled unapplied FX is ignored");
        Call(color, "SetDraftCode", "float4 ApplyFX(PinnedUV uv, float4 color) { return SampleInput(uv + 0); }");
        t.True(color.IsPixelLocal, "Pending source does not change the applied contract");
        Call(color, "ApplyAgentDraft");
        t.True(!color.IsPixelLocal, "Applying the draft replaces the contract");
        Call(pinned, "SetDraftCode", "float4 ApplyFX(PinnedUV uv, float4 color) { uv.x = 0; return color; }");
        try { Call(pinned, "ApplyAgentDraft"); } catch (TargetInvocationException) { }
        t.True(!pinned.IsPixelLocal, "Failed Apply cannot leave a stale true flag");
    }

    static void Arithmetic(TestContext t, Fixture f)
    {
        var sample = new Color(-.2f, .4f, 2, .25f);
        foreach (string expression in new[] { "uv", "(float2)uv", "float2(uv)", "uv.xy", "uv.yx.yx", "uv+0", "0+uv", "uv-0", "uv*1", "1*uv", "uv/1", "-(-uv)", "lerp(uv, uv, .5)", "min(uv, 1)", "float2(uv[0],uv[1])" })
        {
            var fx = f.Fx("float4 ApplyFX(PinnedUV uv,float4 color) {float2 p=" + expression + "; return float4(p,color.b,color.a);}");
            t.True(fx.IsPixelLocal, "Numeric computation without image reads remains local: " + expression);
            f.Pixel(f.Render(fx, sample, 2, 1), new Color(2.5f/Fixture.Width, 1.5f/Fixture.Height, 2, .25f), "Numeric conversion: " + expression);
            var read = f.Fx("float4 ApplyFX(PinnedUV uv,float4 color) {return SampleInput(" + expression + ");}");
            t.Equal(expression == "uv", read.IsPixelLocal, "Sampling uses the expression type: " + expression);
            f.Pixel(f.Render(read, sample), sample, "Sampling retains HDR/alpha: " + expression);
        }
        foreach (var item in new[] {
            (true, "PinnedUV Identity(PinnedUV p) {return p;} float4 ApplyFX(PinnedUV uv,float4 color) {return SampleInput(Identity((uv)));}"),
            (false, "float2 Identity(float2 p) {return p;} float4 ApplyFX(PinnedUV uv,float4 color) {return SampleInput(Identity(uv));}"),
            (false, "float4 Read(float2 p) {return SampleInput(p);} float4 ApplyFX(PinnedUV uv,float4 color) {return Read(uv);}"),
            (true, "float4 Read(PinnedUV p) {return SampleInput(p);} float4 Read(float2 p) {return SampleInput(p);} float4 ApplyFX(PinnedUV uv,float4 color) {return Read(uv);}"),
            (false, "float4 Read(PinnedUV p) {return SampleInput(p);} float4 Read(float2 p) {return SampleInput(p);} float4 ApplyFX(PinnedUV uv,float4 color) {return Read(uv+0);}"),
            (true, "float4 Unused(float2 p) {return SampleInput(p);} float4 ApplyFX(PinnedUV uv,float4 color) {PinnedUV copy=uv; return SampleInput(copy);}"),
            (false, "float4 ApplyFX(PinnedUV uv,float4 color) {return SampleInput(uv,0);}"),
            (true, "float4 ApplyFX(PinnedUV uv,float4 color) {for(int i=0;i<2;i++){float2 p=uv; p+=i;} return SampleInput(uv);}"),
            (false, "float4 ApplyFX(PinnedUV uv,float4 color) {float4 v=0; for(int i=0;i<2;i++){float2 p=uv; v+=SampleInput(p)*.5;} return v;}"),
            (false, "float4 Read(float choose,PinnedUV p) {return SampleInput(p);} float4 Read(bool choose,PinnedUV p) {return SampleInput(p+0);} float4 ApplyFX(PinnedUV uv,float4 color) {return Read(uv.x>.5,uv);}"),
            (false, "#define FETCH Read\nfloat4 Read(float2 p) {return SampleInput(p);}\nfloat4 ApplyFX(PinnedUV uv,float4 color) {return FETCH(uv);}"),
            (false, "#define FETCH(p) \\\n SampleInput(p)\nfloat4 ApplyFX(PinnedUV uv,float4 color) {return FETCH(uv);}"),
            (false, "float4 ApplyFX(PinnedUV uv,float4 color) {\n#if 1\nfloat2 p=uv;\n#else\nPinnedUV p=uv;\n#endif\nreturn SampleInput(p);}"),
            (false, "float4 ApplyFX(float4 color) {return SampleInput(float2(.5,.5));}")
        })
        {
            var fx = f.Fx(item.Item2);
            t.Equal(item.Item1, fx.IsPixelLocal, "Reachable sampling contract: " + item.Item2);
            f.Pixel(f.Render(fx, sample), sample, "Helper/loop sampling parity");
        }
    }

    static void Rejections(TestContext t, Fixture f)
    {
        foreach (string code in new[] {
            "float4 ApplyFX(float2 uv) { return 1; }",
            "float3 ApplyFX(float4 color) { return color.rgb; }",
            "float4 ApplyFX(float4 color, float2 uv) { return color; }",
            "float4 ApplyFX(inout float4 color) { return color; }",
            "float4 ApplyFX(float4 color) { return color; } float4 ApplyFX(float2 uv, float4 color) { return color; }",
            "float4 ApplyFX(float2 uv, float4 color) { return color; }",
            "float4 ApplyFX(PinnedUV uv, float4 color) { uv.y++; return color; }",
            "float4 ApplyFX(PinnedUV uv, float4 color) { PinnedUV p=uv; p.x=0; return color; }",
            "float4 ApplyFX(PinnedUV uv, float4 color) { uv=uv; return color; }",
            "float4 ApplyFX(PinnedUV uv, float4 color) { PinnedUV p=uv*2; return color; }",
            "void Move(inout PinnedUV p) {} float4 ApplyFX(PinnedUV uv, float4 color) { Move(uv); return color; }",
            "float4 ApplyFX(PinnedUV uv, float4 color) { uv.xy += 1; return color; }",
            "PinnedUV Wrong(float2 p) {return p;} float4 ApplyFX(PinnedUV uv,float4 color) {return SampleInput(Wrong(uv));}",
            "float4 Read(PinnedUV p) {return SampleInput(p);} float4 ApplyFX(PinnedUV uv,float4 color) {return Read((float2)uv);}",
            "void Move(inout float2 p) {p+=1;} float4 ApplyFX(PinnedUV uv,float4 color) {Move(uv); return color;}",
            "float4 ApplyFX(PinnedUV uv,float4 color) {PinnedUV p=p; return SampleInput(p);}",
            "float4 ApplyFX(PinnedUV uv, float4 color) { PinnedUV p=(PinnedUV)float2(0,0); return SampleInput(p); }",
            "float4 ApplyFX(PinnedUV uv, float4 color) { PinnedUV p={0,0}; return SampleInput(p); }",
            "struct PinnedUV { float x; float y; }; float4 ApplyFX(PinnedUV uv,float4 color) { return color; }",
            "#define Alias PinnedUV\nfloat4 ApplyFX(PinnedUV uv,float4 color) { return color; }",
            "#define Alias \\\n PinnedUV\nfloat4 ApplyFX(PinnedUV uv,float4 color) { return color; }"
        })
        {
            var fx = f.Fx(code, false);
            bool rejected = false;
            try { Call(fx, "ApplyAgentDraft"); }
            catch (TargetInvocationException) { rejected = true; }
            t.True(rejected && !fx.IsPixelLocal, "Invalid contract rejected: " + code);
            t.True(!string.IsNullOrEmpty((string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(fx)), "Failure has diagnostics");
        }
    }

    const string MaskCode = @"
// @param texture2D _Mask = self
float ReadMask(WhimTexTexture mask, PinnedUV coordinate) { return SampleTexture(mask,coordinate).r; }
float4 ApplyFX(PinnedUV uv, float4 color) { color.a *= ReadMask(_Mask,uv); return color; }
";

    static void Masks(TestContext t, Fixture f)
    {
        var mask = f.Fx(MaskCode);
        t.True(mask.IsPixelLocal, "Texture parameter with pinned sampling is pixel-local");
        var p = Values(mask)[0];
        var source = new Color(.4f, .2f, 3, .5f);
        f.Pixel(f.Render(mask, source), new Color(.4f, .2f, 3, .2f), "Self mask reads before this FX");
        p.textureSource = ShaderFXTextureSource.None;
        f.Pixel(f.Render(mask, source), new Color(.4f, .2f, 3, 0), "None mask is transparent black");
        var texture = f.Own(new Texture2D(4, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
        var pixels = new Color[8];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color((i + 1) / 8f, 0, 0, 1);
        texture.SetPixels(pixels); texture.Apply();
        p.textureSource = ShaderFXTextureSource.Texture; p.textureValue = texture;
        for (int y = 0; y < Fixture.Height; y++) for (int x = 0; x < Fixture.Width; x++)
            f.Pixel(f.Render(mask, source, x, y), new Color(source.r, source.g, source.b, source.a * pixels[y / 2 * 4 + x / 2].r),
                "Mask of another resolution at corresponding normalized point");
        var maskLayer = f.Add(new ColorFillLayerBehaviour { color = new Color(.5f, .5f, .5f, 1) });
        p.textureSource = ShaderFXTextureSource.Layer; p.textureLayerId = maskLayer.Id;
        f.Pixel(f.Render(mask, source), new Color(source.r, source.g, source.b, source.a * Mathf.GammaToLinearSpace(.5f)), "Layer-backed pinned mask");
        p.textureLayerId = "missing-layer";
        f.Pixel(f.Render(mask, source), new Color(source.r, source.g, source.b, 0), "Missing mask layer binds transparent input");
        t.True(mask.IsPixelLocal, "Switching Texture/Self/Layer/None does not change pinned contract");

        var spatial = f.Fx("// @param texture2D _Mask\nfloat4 ApplyFX(PinnedUV uv,float4 color) { return SampleTexture(_Mask,1-uv); }");
        Values(spatial)[0].textureValue = texture;
        f.Pixel(f.Render(spatial, source, 0, 0), pixels[7], "Spatial SampleTexture keeps arbitrary UV support");
        t.True(!spatial.IsPixelLocal, "Transformed sampling does not claim pixel locality");
        var preset = f.Fx(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Mask.hlsl"));
        t.True(!preset.IsPixelLocal, "Existing transformable Mask retains spatial signature");
        var neutral = new Color(.3f, .4f, .6f, .5f);
        f.Pixel(f.Render(preset, neutral), new Color(neutral.r, neutral.g, neutral.b, neutral.a * neutral.r), "Built-in Mask primary sampling parity");
    }

    static void Persistence(TestContext t, Fixture f)
    {
        var fx = f.Fx(MaskCode);
        string native = EditorJsonUtility.ToJson(fx);
        EditorJsonUtility.FromJsonOverwrite(native, fx);
        if (!(bool)typeof(ShaderFX).GetProperty("HasAppliedShader", F).GetValue(fx))
            t.True(!fx.IsPixelLocal, "A snapshot without its transient shader is not confirmed local yet");
        Call(fx, "RestoreDocumentShader");
        t.True(fx.IsPixelLocal, "Native source snapshot restores its compiled pixel-local contract");
        string exported = (string)Type("ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { fx, "Tests/Pinned Mask" });
        t.True(exported.Contains("ApplyFX(PinnedUV uv") && exported.Contains("SampleTexture(mask,coordinate)"), "Preset keeps author-facing signatures and helpers");
        var copy = f.Fx(exported); t.True(copy.IsPixelLocal, "Exported preset reapplies its contract");
        var layer = f.Consumer; layer.fx.Clear(); layer.fx.Add(fx);
        var json = WhimTexDocumentJson.Write(f.Document);
        using (var restored = WhimTexDocumentJson.Read(json.Json))
        {
            var restoredFx = restored.Document.layers[0].fx[0];
            var previousShader = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(restoredFx);
            if (previousShader != null) f.Own(previousShader);
            Call(restoredFx, "ApplyAgentDraft");
            t.True(restoredFx.IsPixelLocal && restored.Document.layers[0].AreActiveFxPixelLocal(), "JSON reconstructs the code-derived contract");
        }
        layer.fx.Clear();
        layer.fx.Add(f.Own(ScriptableObject.CreateInstance<ShaderFX>()));
        using (var restored = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(f.Document).Json))
        {
            var restoredFx = restored.Document.layers[0].fx[0];
            t.True(restoredFx.IsPixelLocal, "Omitted/default FX source restores the current color-only contract");
        }
        layer.fx.Clear();
        foreach (string form in new[] { "float4 color", "PinnedUV uv, float4 color" })
        {
            var previewFx = f.Fx("// @param preview(Show) float _Amount=.5 [0 .. 1]\n#ifdef WHIMTEX_PREVIEW\nfloat4 Show(float2 uv,float value) { return SampleInput(uv)*value; }\n#endif\nfloat4 ApplyFX(" + form + ") { return color; }");
            var parameter = Values(previewFx)[0];
            string shaderSource = (string)Type("ShaderFXSourceBuilder").GetMethod("BuildPreview", F).Invoke(null, new object[] { previewFx, parameter.controls[0], parameter });
            var shader = f.Own(ShaderUtil.CreateShaderAsset(shaderSource, true));
            var material = f.Own(new Material(shader)); ShaderUtil.CompilePass(material, 0, true);
            t.True(shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Preview callbacks retain float2 sampling for " + form);
        }
    }

    static void Bypasses(TestContext t, Fixture f)
    {
        foreach (string body in new[] {
            "return tex2D(_MainTex,float2(uv.x,uv.y));",
            "return tex2Dlod(_MainTex,float4(uv.x,uv.y,0,0));",
            "return tex2Dgrad(_MainTex,float2(uv.x,uv.y),float2(0,0),float2(0,0));",
            "return SampleTexture(_MainTex,uv) + tex2D(_MainTex,float2(uv.x,uv.y));",
            "return color + ddx(color);"
        })
        {
            var fx = f.Fx("float4 ApplyFX(PinnedUV uv,float4 color) { " + body + " }");
            t.True(!fx.IsPixelLocal, "Bypass/derivative loses pixel-local confirmation");
            t.True(((string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(fx)).Contains("IsPixelLocal is false"), "Bypass warning is visible through shared diagnostics");
        }
        var helper = f.Fx("float4 Other(float2 p) {return tex2D(_MainTex,p);}\nfloat4 ApplyFX(float4 color) {return Other(float2(.5,.5));}");
        t.True(!helper.IsPixelLocal, "Helper functions cannot hide obvious bypasses");
        foreach (string operation in new[] { "Image.Sample(State, float2(uv.x, uv.y))", "Image.Load(int3(0,0,0))" })
        {
            object contract = Type("ShaderFXEntryPoint").GetMethod("Parse", F).Invoke(null,
                new object[] { "float4 ApplyFX(PinnedUV uv, float4 color) { return " + operation + "; }" });
            t.True(!(bool)contract.GetType().GetField("verified", F).GetValue(contract), "Classifier treats direct resource operation as a bypass: " + operation);
        }
        var lut = f.Fx("// @param gradient _Colors\nfloat4 ApplyFX(float4 color) {return _Colors_Sample(color.r);}");
        t.True(lut.IsPixelLocal, "Generated gradient LUT is not a document-image read");
        var comment = f.Fx("// WhimTex pixel-local v1\nfloat4 ApplyFX(PinnedUV uv,float4 color) {return SampleInput(uv+0);}");
        t.True(!comment.IsPixelLocal, "A user comment cannot forge the generated contract header");
    }

    static void BundledPresets(TestContext t, Fixture f)
    {
        var local = new HashSet<string>(StringComparer.Ordinal) {
            "BrightnessContrast", "ColorBalance", "ColorFilter", "Gain", "GradientMap", "HueSaturation",
            "Levels", "Negative", "Normalize", "Step", "Threshold", "NormalLighting", "Posterize", "ChromaKey", "DomainWarp"
        };
        int localCount = 0;
        var paths = Directory.GetFiles("Packages/com.dcfapixels.whimtex/src/FXPresets", "*.hlsl");
        Array.Sort(paths, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path), code = File.ReadAllText(path);
            var fx = f.Fx(code);
            t.Equal(local.Contains(name), fx.IsPixelLocal, name + " declares the correct pixel-local contract");
            if (!local.Contains(name)) continue;
            localCount++;
            string baseline = System.Text.RegularExpressions.Regex.Replace(code,
                @"ApplyFX\((?:PinnedUV uv, )?float4 color\)\s*\{", "ApplyFX(PinnedUV pinned, float4 color) { float2 uv = pinned;")
                .Replace("floor(float2(uv.x, uv.y) *", "floor(uv *")
                .Replace("SampleTexture(_Normals, uv)", "tex2D(_Normals, uv)");
            var original = f.Fx(baseline);
            foreach (var effect in new[] { fx, original })
            {
                foreach (var p in Values(effect))
                {
                    if (p.name == "_Opacity") p.floatValue = .7f;
                    else if (p.name == "_Brightness") p.floatValue = 35;
                    else if (p.name == "_Contrast") p.floatValue = 25;
                    else if (p.name == "_MasterHue") p.floatValue = 31;
                    else if (p.name == "_MasterSaturation") p.floatValue = 1.6f;
                    else if (p.name == "_MasterLightness") p.floatValue = 12;
                    else if (p.name == "_Gain") p.floatValue = 1.7f;
                    else if (p.name == "_Dither") p.floatValue = 2;
                    else if (p.name == "_Gamma") p.floatValue = 1.3f;
                }
            }
            foreach (var source in new[] { new Color(.13f, .43f, .71f, .65f), new Color(-.2f, 1.7f, .37f, .4f) })
                foreach (int x in new[] { 0, 3, 7 })
                    f.Pixel(f.Render(fx, source, x, 1), f.Render(original, source, x, 1), name + " retains its original pixels at " + x);
        }
        t.Equal(local.Count, localCount, "All migrated built-in presets were checked");
    }

    [Serializable] sealed class CodeValue { public string code; }

    static void AuthoringExamples(TestContext t, Fixture f)
    {
        int count = 0;
        var codePattern = new System.Text.RegularExpressions.Regex("\"code\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")");
        foreach (string folder in new[] { "Samples~/AgentTextures", "Documentation~/Examples/Clipboard" })
            foreach (string file in Directory.GetFiles("Packages/com.dcfapixels.whimtex/" + folder, "*.json"))
                foreach (System.Text.RegularExpressions.Match match in codePattern.Matches(File.ReadAllText(file)))
                {
                    string code = JsonUtility.FromJson<CodeValue>("{\"code\":" + match.Groups[1].Value + "}").code;
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    try { f.Fx(code); }
                    catch (Exception error) { throw new Exception("Authoring example failed: " + file, error); }
                    count++;
                }
        t.True(count > 30, "Current authoring examples compile: " + count);
    }

    sealed class Fixture : IDisposable
    {
        internal const int Width = 8, Height = 4;
        readonly TestContext t;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        internal readonly WhimTexDocument Document;
        internal readonly Layer Consumer;
        readonly RenderTexture target, self;
        readonly Texture2D input, readback;
        readonly object context;
        internal Fixture(TestContext t)
        {
            this.t = t;
            try
            {
                Document = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); Document.width = Width; Document.height = Height;
                Consumer = Add(new ColorFillLayerBehaviour());
                input = Own(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
                readback = Own(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true));
                target = Own(new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                self = Own(new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                t.True(target.Create() && self.Create(), "Owned render targets created");
                context = Activator.CreateInstance(Type("LayerRenderContext"), Document, null, Width, Height, 1f, true, true, null);
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
                throw;
            }
        }
        internal T Own<T>(T value) where T : UnityEngine.Object { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        internal Layer Add(LayerBehaviour behaviour)
        {
            var layer = new Layer(behaviour); Call(layer, "AssignNewId"); Document.layers.Add(layer); return layer;
        }
        internal ShaderFX Fx(string code, bool apply = true)
        {
            var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { Document, code, new List<ShaderFXParameter>() }));
            if (apply)
            {
                Call(fx, "ApplyAgentDraft");
                Own((Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(fx));
            }
            return fx;
        }
        internal Color Render(ShaderFX fx, Color color, int x = 4, int y = 2)
        {
            var pixels = new Color[Width * Height]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            input.SetPixels(pixels); input.Apply();
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            object bindings = null;
            try
            {
                GL.sRGBWrite = false; Graphics.Blit(input, self);
                var material = (Material)Call(fx, "GetMaterial", context);
                t.True(material != null, "Applied effect has a material");
                bindings = Call(Document, "BindShaderTextureLayers", fx, Consumer, context, self);
                if (bindings != null) Call(bindings, "Apply", material);
                Graphics.Blit(input, target, material); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); readback.Apply(); return readback.GetPixel(x, y);
            }
            finally { (bindings as IDisposable)?.Dispose(); RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        internal void Pixel(Color actual, Color expected, string message)
        { for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], .0006, message + " channel " + c); }
        public void Dispose()
        {
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception e) { errors.Add(e); } }
            if (Document != null)
            {
                foreach (var layer in Document.layers) Attempt(() => Call(layer, "ReleaseTransientResources"));
                Document.layers.Clear();
            }
            if (target != null) Attempt(() => target.Release());
            if (self != null) Attempt(() => self.Release());
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var value = owned[i];
                if (value != null) Attempt(() => UnityEngine.Object.DestroyImmediate(value));
            }
            owned.Clear();
            if (errors.Count > 0) throw new AggregateException("Owned pixel-local fixture cleanup failed", errors);
        }
    }
}
