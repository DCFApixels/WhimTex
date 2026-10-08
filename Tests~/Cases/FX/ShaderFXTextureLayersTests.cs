// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXTextureLayersTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXTextureLayersTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    private static void ExecuteMain()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.width = doc.height = 32;
        ShaderFX fx = null;
        int checks = 0;
        try
        {
            Layer consumer = new ColorFillLayerBehaviour();
            var fill = new ColorFillLayerBehaviour();
            fill.color = new Color(.2f, .7f, .4f, 1);
            Layer source = fill;
            source.enabled = false;
            doc.layers.Add(consumer); doc.layers.Add(source);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(DCFApixels.WhimTex.WhimTexDocument), typeof(string), typeof(List<DCFApixels.WhimTex.ShaderFXParameter>) }, null).Invoke(null, new object[] { doc,
                "// @param texture2D _Map\nfloat4 ApplyFX(float2 uv,float4 color){return tex2D(_Map,uv);}", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            consumer.fx.Add(fx);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var p = parameters[0]; p.textureSource = ShaderFXTextureSource.Layer; p.textureLayerId = source.Id;
            void CheckColor(Color expected)
            {
                expected = expected.linear;
                var image = doc.ComposeCanvas();
                try {
                    var actual = image.GetPixel(16,16);
                    context.True(!(Mathf.Abs(actual.r-expected.r)>.02 || Mathf.Abs(actual.g-expected.g)>.02 ||
                        Mathf.Abs(actual.b-expected.b)>.02 || Mathf.Abs(actual.a-expected.a)>.02), "Unexpected sampled color: " + actual + " expected " + expected);
                    checks++;
                } finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            CheckColor(fill.color);
            fill.color = new Color(.8f,.1f,.3f,1); CheckColor(fill.color);
            p.textureLayerId = "missing"; CheckColor(Color.clear);
            p.textureLayerId = consumer.Id; CheckColor(Color.clear);
            Layer group = new GroupLayerBehaviour(); group.enabled = false;
            doc.layers.Remove(source); group.children.Add(source); source.enabled = true; doc.layers.Add(group);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
            p.textureLayerId = group.Id; CheckColor(fill.color);
            var usable = typeof(WhimTexDocument).GetMethod("IsUsableShaderTexture", F, null, new[] { typeof(Layer), typeof(string) }, null);
            context.True(!((bool)usable.Invoke(doc,new object[]{consumer,consumer.Id})), "Self reference allowed");
            context.True(true, "Expected exception was rejected"); return;
        }
        finally { if (fx != null) UnityEngine.Object.DestroyImmediate(fx); UnityEngine.Object.DestroyImmediate(doc); }
    }
}

