using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXTextureLayersSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
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
            typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(doc, null);
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F).Invoke(null, new object[] { doc,
                "// @param texture2D _Map\nfloat4 ApplyFX(float2 uv,float4 color){return tex2D(_Map,uv);}", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            consumer.modifiers.Add(fx);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var p = parameters[0]; p.textureSource = ShaderFXTextureSource.Layer; p.textureLayerId = source.Id;
            void CheckColor(Color expected)
            {
                expected = expected.linear;
                var image = doc.Compose();
                try {
                    var actual = image.GetPixel(16,16);
                    if (Mathf.Abs(actual.r-expected.r)>.02 || Mathf.Abs(actual.g-expected.g)>.02 ||
                        Mathf.Abs(actual.b-expected.b)>.02 || Mathf.Abs(actual.a-expected.a)>.02)
                        throw new Exception("Unexpected sampled color: " + actual + " expected " + expected);
                    checks++;
                } finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            CheckColor(fill.color);
            fill.color = new Color(.8f,.1f,.3f,1); CheckColor(fill.color);
            p.textureLayerId = "missing"; CheckColor(Color.clear);
            p.textureLayerId = consumer.Id; CheckColor(Color.clear);
            Layer group = new GroupLayerBehaviour(); group.enabled = false;
            doc.layers.Remove(source); group.children.Add(source); source.enabled = true; doc.layers.Add(group);
            typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(doc, null);
            p.textureLayerId = group.Id; CheckColor(fill.color);
            var usable = typeof(TextureCompositor).GetMethod("IsUsableShaderTexture", F, null, new[] { typeof(Layer), typeof(string) }, null);
            if ((bool)usable.Invoke(doc,new object[]{consumer,consumer.Id})) throw new Exception("Self reference allowed");
            checks++;
            return "PASS: " + checks + " checks: hidden source, updates, missing/cyclic fallback, hidden full-color group.";
        }
        finally { if (fx != null) UnityEngine.Object.DestroyImmediate(fx); UnityEngine.Object.DestroyImmediate(doc); }
    }
}
