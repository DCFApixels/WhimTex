using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class GroupFXSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 8;
        ShaderFX fx = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        int checks = 0;
        void Near(Color actual, Color expected, string label)
        {
            for (int i = 0; i < 4; i++)
                if (Mathf.Abs(actual[i] - expected[i]) > .015f || float.IsNaN(actual[i]))
                    throw new Exception(label + ": " + actual + " expected " + expected);
            checks++;
        }
        Color Pixel()
        {
            var texture = doc.Compose();
            try { return texture.GetPixel(4, 4); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        Color Read(RenderTexture rt)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            var old = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                return texture.GetPixel(4, 4);
            }
            finally
            {
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        try
        {
            string code = "float4 ApplyFX(float2 uv, float4 color) { return float4(color.b, color.r, color.g, color.a); }";
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags).Invoke(null,
                new object[] { doc, code, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            Layer group = new GroupLayerBehaviour();
            group.children.Add(new ColorFillLayerBehaviour { color = Color.red });
            doc.layers.Add(group);
            Near(Pixel(), Color.red, "No FX");
            group.modifiers.Add(fx);
            Near(Pixel(), Color.green, "Group FX");
            if ((bool)typeof(Layer).GetProperty("IsPassThrough", flags).GetValue(group)) throw new Exception("Not isolated");
            group.modifiers.Add(fx);
            Near(Pixel(), Color.blue, "FX order");
            group.modifiers.RemoveAt(1);
            Layer outer = new GroupLayerBehaviour(); outer.children.Add(group); outer.modifiers.Add(fx);
            doc.layers.Clear(); doc.layers.Add(outer);
            Near(Pixel(), Color.blue, "Nested FX applied once");
            doc.layers.Clear(); doc.layers.Add(group);
            doc.layers.Add(new ColorFillLayerBehaviour { color = Color.blue });
            group.opacity = .5f;
            var withFx = Pixel();
            group.modifiers.Clear();
            ((ColorFillLayerBehaviour)group.children[0].Behaviour).color = Color.green;
            group.compositing = GroupCompositing.Isolated;
            Near(Pixel(), withFx, "Opacity after FX / backdrop untouched");
            group.compositing = GroupCompositing.PassThrough;
            group.opacity = 1;
            ((ColorFillLayerBehaviour)group.children[0].Behaviour).color = Color.red;
            group.modifiers.Add(fx);
            group.clippingMask = true;
            Near(Pixel(), Color.green, "Clipped group FX");
            group.clippingMask = false;
            Near(Read((RenderTexture)typeof(TextureCompositor).GetMethod("RenderAgentLayerPreview", flags).Invoke(doc, new object[] { group, 8 })), Color.green, "RGBA group preview");
            var exported = (Texture2D)typeof(TextureCompositor).GetMethod("RenderPsdGroupContent", flags).Invoke(doc, new object[] { group });
            try { Near(exported.GetPixel(4, 4), Color.green, "Layered export"); }
            finally { UnityEngine.Object.DestroyImmediate(exported); }
            group.modifiers.Clear();
            if (!(bool)typeof(Layer).GetProperty("IsPassThrough", flags).GetValue(group)) throw new Exception("Pass Through not restored");
            Near(Pixel(), Color.red, "Removed FX");
            var ui = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI");
            var bindingsType = ui.GetNestedType("ValueBindings", BindingFlags.NonPublic);
            var bindings = Activator.CreateInstance(bindingsType, true);
            var root = new VisualElement();
            var fxView = ui.GetMethod("BuildLayerInspectorSections", flags).Invoke(null, new object[] {
                root, group, doc, (Action<string, Action>)((_, action) => action()), bindings,
                (Action<VisualElement>)(_ => throw new Exception("Group-specific properties callback")),
                true, (Action<bool>)(_ => {}), true, (Action<bool>)(_ => {}), true, (Action<bool>)(_ => {}) });
            var mode = root.Q<TextField>("groupCompositing");
            if (fxView == null || mode == null || !mode.isReadOnly || mode.value != "Pass Through" || !mode.enabledInHierarchy)
                throw new Exception("Group inspector availability");
            group.modifiers.Add(fx);
            bindingsType.GetMethod("Refresh").Invoke(bindings, new object[] { true });
            if (mode.value != "Isolated") throw new Exception("Group inspector refresh");
            group.modifiers.Clear();
            bindingsType.GetMethod("Refresh").Invoke(bindings, new object[] { true });
            if (mode.value != "Pass Through") throw new Exception("Group inspector restoration");
            string json = "{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":[{\"type\":\"group\",\"fx\":[{\"code\":\"" + code + "\"}],\"children\":[{\"type\":\"color\",\"properties\":{\"color\":[1,0,0,1]}}]}]}";
            using (var data = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", flags).Invoke(null, new object[] { json, 8, 8 }))
            {
                data.GetType().GetMethod("Compile", flags).Invoke(data, null);
                var pasted = (TextureCompositor)data.GetType().GetField("Document", flags).GetValue(data);
                var texture = pasted.Compose();
                try { Near(texture.GetPixel(4, 4), Color.green, "Clipboard group FX"); }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            return "PASS: " + checks + " group FX rendering checks, isolation and restoration.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
