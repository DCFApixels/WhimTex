using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class EffectCacheStampTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Flags).Invoke(owner, args);
    static void Set(ShaderFX fx, string name, object value) => typeof(ShaderFX).GetField(name, Flags).SetValue(fx, value);
    static Type CacheType => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache", true);
    static FixtureScope S => FixtureContext.Scope;
    static WhimTex.Tests.TestContext T => FixtureContext.Context;

    public static string Benchmark() => FixtureContext.Diagnostic("FX cache fingerprint timing", () =>
    {
        var doc = S.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var cache = S.OwnDisposable((IDisposable)Activator.CreateInstance(CacheType, true));
        Layer processor = ShaderLayerBehaviour.CreateCompositeFilter();
        doc.layers.Add(processor);
        for (int i = 0; i < 30; i++)
        {
            Layer layer = new ColorFillLayerBehaviour { enabled = i % 2 == 0 };
            var fx = S.Own(ScriptableObject.CreateInstance<ShaderFX>());
            string source = "// " + new string((char)('a' + i % 20), 100000) + "\nfloat4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color; }";
            Set(fx, "draftCode", source); Set(fx, "workingProgramSource", source); Set(fx, "workingShaderSource", source);
            layer.fx.Add(fx); doc.layers.Add(layer);
        }
        Call(doc, "NormalizeModel");
        void Frame() { Call(cache, "BeginFrame", doc, null); Call(cache, "Stamp", processor); }
        Frame();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) Frame();
        return "30 FX with 100K-character immutable sources, half invisible; warm frame ms=" +
            (clock.Elapsed.TotalMilliseconds / 10).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    });

    public static string Run() => FixtureContext.Run("FX fingerprints and cache dependency invalidation", () =>
    {
        var doc = S.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var cache = S.OwnDisposable((IDisposable)Activator.CreateInstance(CacheType, true));
        var fx = S.Own(ScriptableObject.CreateInstance<ShaderFX>());
        Layer layer = new ColorFillLayerBehaviour(); layer.fx.Add(fx); doc.layers.Add(layer);
        Call(doc, "NormalizeModel");
        ulong Stamp() { Call(cache, "BeginFrame", doc, null); return (ulong)Call(cache, "Stamp", layer); }
        ulong previous = Stamp();
        T.Equal(previous, Stamp(), "Unchanged FX has a stable fingerprint");
        void Changed(Action edit, string label)
        {
            edit(); ulong next = Stamp(); T.True(next != previous, label); previous = next;
            T.Equal(next, Stamp(), "Fingerprint settles after " + label);
        }
        string source = "// long source " + new string('x', 100000);
        Changed(() => Set(fx, "draftCode", source), "Draft source change invalidates");
        Changed(() => Set(fx, "workingProgramSource", source), "Applied code change invalidates");
        Changed(() => Set(fx, "workingShaderSource", source), "Expanded source change invalidates");
        Set(fx, "draftCode", new string(source.ToCharArray()));
        T.Equal(previous, Stamp(), "Equal code with a new string identity keeps its content fingerprint");
        var value = new ShaderParameter { name = "_Value", type = ShaderParameterType.Float, floatValue = .25f };
        Changed(() => Set(fx, "parameters", new List<ShaderParameter> { value }), "Draft parameter structure invalidates");
        Changed(() => value.floatValue = .75f, "Direct value edits invalidate without requiring SetDirty");
        var applied = new ShaderParameter { name = "_OldValue", type = ShaderParameterType.Color };
        Changed(() => Set(fx, "workingParameters", new List<ShaderParameter> { applied }), "Applied fallback parameters invalidate");
        Changed(() => applied.colorValue = Color.red, "Applied fallback value edits invalidate");
        value.type = ShaderParameterType.Gradient; value.gradientValue = new WhimTexGradient(); previous = Stamp();
        Changed(() => value.gradientValue.ColorMode = WhimTexGradientColorMode.Linear, "In-place gradient edits invalidate");
        value.type = ShaderParameterType.Curve; value.curveValue = AnimationCurve.Linear(0, 0, 1, 1); previous = Stamp();
        Changed(() => value.curveValue.MoveKey(1, new Keyframe(1, .25f)), "In-place curve edits invalidate");
        var texture = S.Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
        value.type = ShaderParameterType.Texture2D; value.textureValue = texture; previous = Stamp();
        Changed(() => { texture.SetPixel(0, 0, Color.red); texture.Apply(); }, "External texture updates invalidate");
        value.controls.Add(new ShaderParameterControl { label = "UI label", tooltip = "UI-only help" });
        T.Equal(previous, Stamp(), "UI-only declarations do not change the render fingerprint");
        value.type = ShaderParameterType.Transform2D; value.transformValue = ShaderTransform.Default; previous = Stamp();
        Changed(() => { var transform = value.transformValue; transform.storage = TransformStorage.Projective;
            transform.matrix = ProjectiveMatrix.Identity; transform.matrix.m01 = .2; value.transformValue = transform; },
            "Projective parameter transforms invalidate");
        foreach (var type in new[] { ShaderParameterType.Toggle, ShaderParameterType.Enum, ShaderParameterType.Vector,
            ShaderParameterType.Vector2, ShaderParameterType.Vector3, ShaderParameterType.Normal, ShaderParameterType.Point })
        {
            value.type = type; previous = Stamp();
            Changed(() => { value.floatValue += 1; value.vectorValue.x += .25f; }, "Value invalidation for " + type);
        }
        Changed(() => Set(fx, "lastCompileFailed", true), "Apply failure state invalidates");
        Changed(() => fx.Active = false, "Active state invalidates");
        Changed(() => fx.Active = true, "Re-enabling invalidates");
        Changed(() => Set(fx, "compiledShader", Shader.Find("Hidden/WhimTex/SmudgeBrush")), "Compiled shader change invalidates");

        Layer processor = ShaderLayerBehaviour.CreateCompositeFilter();
        Layer hidden = new ColorFillLayerBehaviour { enabled = false };
        doc.layers.Clear(); doc.layers.Add(processor); doc.layers.Add(hidden); doc.layers.Add(layer);
        Call(doc, "NormalizeModel");
        ulong ProcessorStamp() { Call(cache, "BeginFrame", doc, null); return (ulong)Call(cache, "Stamp", processor); }
        ulong before = ProcessorStamp();
        ((ColorFillLayerBehaviour)hidden.Behaviour).color = Color.green;
        T.Equal(before, ProcessorStamp(), "Invisible ordinary stack content does not invalidate processor input");
        hidden.enabled = true;
        T.True(before != ProcessorStamp(), "Enabling hidden content invalidates processor input");
        hidden.enabled = false;
        var target = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = hidden.Id };
        doc.layers.Insert(0, target); Call(doc, "NormalizeModel");
        Call(cache, "BeginFrame", doc, null); ulong targetStamp = (ulong)Call(cache, "Stamp", target.Owner);
        ((ColorFillLayerBehaviour)hidden.Behaviour).color = Color.blue;
        Call(cache, "BeginFrame", doc, null);
        T.True(targetStamp != (ulong)Call(cache, "Stamp", target.Owner), "Explicit hidden Target sources still invalidate");
        var sampler = new ShaderParameter { name = "_Input", type = ShaderParameterType.Texture2D,
            textureSource = ShaderTextureSource.Layer, textureLayerId = hidden.Id };
        Set(fx, "parameters", new List<ShaderParameter> { sampler });
        Set(fx, "workingParameters", new List<ShaderParameter> { sampler });
        Call(cache, "BeginFrame", doc, null); ulong textureLayerStamp = (ulong)Call(cache, "Stamp", layer);
        ((ColorFillLayerBehaviour)hidden.Behaviour).color = Color.red;
        Call(cache, "BeginFrame", doc, null);
        T.True(textureLayerStamp != (ulong)Call(cache, "Stamp", layer), "Explicit hidden FX texture inputs still invalidate");

        var renderFx = S.Own(ScriptableObject.CreateInstance<ShaderFX>());
        Set(renderFx, "code", "// @param float _Amount = 1 [0 .. 4]\nfloat4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color * float4(_Amount, 1, 1, 1); }");
        Call(renderFx, "CompileAgentDraft");
        Layer color = new ColorFillLayerBehaviour { color = new Color(.25f, .5f, .75f, 1), colorRange = LayerColorRange.HDR };
        color.fx.Add(renderFx); doc.layers.Clear(); doc.layers.Add(color); doc.width = doc.height = 16;
        Call(doc, "NormalizeModel");
        var baseImage = S.Own(doc.ComposeCanvas()); var baseColor = baseImage.GetPixel(8, 8);
        var read = S.Own(new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true));
        Color Render()
        {
            var targetTexture = S.Temporary((RenderTexture)Call(doc, "RenderCanvasWithCache", 16, cache, false, null));
            var active = RenderTexture.active;
            try { RenderTexture.active = targetTexture; read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, false); return read.GetPixel(8, 8); }
            finally { RenderTexture.active = active; S.Release(targetTexture); }
        }
        T.Near(baseColor.r, Render().r, .001, "Compiled FX renders before caching");
        T.Near(baseColor.r, Render().r, .001, "Cache hit renders the same pixels");
        var renderValues = (List<ShaderParameter>)typeof(ShaderFX).GetField("parameters", Flags).GetValue(renderFx);
        renderValues[0].floatValue = 3;
        T.Near(baseColor.r * 3, Render().r, .001, "Direct parameter edit updates actual cached GPU output");
        renderValues[0].floatValue = 1;
        T.Near(baseColor.r, Render().r, .001, "Restored parameter values update cached GPU output");
        Set(renderFx, "code", "// @param float _Amount = 1 [0 .. 4]\nfloat4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color * float4(1, _Amount, 1, 1); }");
        Call(renderFx, "CompileAgentDraft"); renderValues = (List<ShaderParameter>)typeof(ShaderFX).GetField("parameters", Flags).GetValue(renderFx);
        renderValues[0].floatValue = 2;
        var edited = Render();
        T.Near(baseColor.r, edited.r, .001, "Reapplying code changes the affected channel");
        T.Near(baseColor.g * 2, edited.g, .001, "Reapplied shader updates cached GPU output");
        var fresh = S.Own(doc.ComposeCanvas()); var expected = fresh.GetPixel(8, 8);
        T.Near(expected.r, edited.r, .001, "Cached FX matches uncached composition, red");
        T.Near(expected.g, edited.g, .001, "Cached FX matches uncached composition, green");
        T.True(!JsonUtility.ToJson(renderFx).Contains("renderCacheValues"), "Fingerprint state is never serialized into files");
    });
}
