using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXActivitySmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.width = doc.height = 16;
        ShaderFX fx = null, copy = null;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F).Invoke(null, new object[] { doc,
                "float4 ApplyFX(float2 uv,float4 c){return float4(0,0,0,1);}", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            Layer layer = new ColorFillLayerBehaviour();
            doc.layers.Add(layer); layer.modifiers.Add(fx);
            float Render()
            {
                var image = doc.Compose();
                try { return image.GetPixel(8,8).r; }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            if (!fx.Active || Render() > .01f) throw new Exception("Default active FX");
            fx.Active = false;
            if (Render() < .99f) throw new Exception("Disabled FX must bypass");
            copy = (ShaderFX)typeof(ShaderFX).GetMethod("CloneForDocument", F).Invoke(fx, new object[] { doc });
            if (copy.Active) throw new Exception("Clone must retain activity");
            fx.Active = true;
            if (Render() > .01f || copy.Active) throw new Exception("Independent reactivation");
            Layer group = new GroupLayerBehaviour();
            group.modifiers.Add(fx);
            var has = typeof(Layer).GetProperty("HasModifiers", F);
            if (!(bool)has.GetValue(group)) throw new Exception("Active group FX");
            fx.Active = false;
            if ((bool)has.GetValue(group)) throw new Exception("Disabled group FX must not force isolation");
            return "PASS: default activity, bypass, reactivation, independent clone and group isolation.";
        }
        finally
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
