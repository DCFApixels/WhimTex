// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXActivityTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXActivityTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    private static void ExecuteMain()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.width = doc.height = 16;
        ShaderFX fx = null, copy = null;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(DCFApixels.WhimTex.TextureCompositor), typeof(string), typeof(List<DCFApixels.WhimTex.ShaderFXParameter>) }, null).Invoke(null, new object[] { doc,
                "float4 ApplyFX(float2 uv,float4 c){return float4(0,0,0,1);}", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            Layer layer = new ColorFillLayerBehaviour();
            doc.layers.Add(layer); layer.modifiers.Add(fx);
            float Render()
            {
                var image = doc.ComposeCanvas();
                try { return image.GetPixel(8,8).r; }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            context.True(!(!fx.Active || Render() > .01f), "Default active FX");
            fx.Active = false;
            context.True(!(Render() < .99f), "Disabled FX must bypass");
            copy = (ShaderFX)typeof(ShaderFX).GetMethod("CloneForDocument", F).Invoke(fx, new object[] { doc });
            context.True(!(copy.Active), "Clone must retain activity");
            fx.Active = true;
            context.True(!(Render() > .01f || copy.Active), "Independent reactivation");
            Layer group = new GroupLayerBehaviour();
            group.modifiers.Add(fx);
            var has = typeof(Layer).GetProperty("HasModifiers", F);
            context.True(!(!(bool)has.GetValue(group)), "Active group FX");
            fx.Active = false;
            context.True(!((bool)has.GetValue(group)), "Disabled group FX must not force isolation");
            return;
        }
        finally
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}

