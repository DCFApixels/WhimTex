using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;

public static class AllBelowInputSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, F).Invoke(obj, args);
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static Color[] Read(RenderTexture rt)
    {
        Check(rt != null, "Rendered image exists");
        var active = RenderTexture.active;
        var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); return cpu.GetPixels(); }
        finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(cpu); RenderTexture.ReleaseTemporary(rt); }
    }
    static void Same(Color[] a, Color[] b, string label)
    {
        Check(a.Length == b.Length, label + " dimensions");
        float max = 0;
        for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++)
        { Check(float.IsFinite(a[i][c]) && float.IsFinite(b[i][c]), label + " finite"); max = Mathf.Max(max, Mathf.Abs(a[i][c] - b[i][c])); }
        Check(max < .004f, label + " maximum error " + max);
    }
    public static string Main()
    {
        checks = 0;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 32;
        var cache = (IDisposable)Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"), true);
        var textures = new List<Texture2D>();
        var effects = new List<TargetedLayerBehaviour>();
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D MakeTexture(int variant)
        {
            var t = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[1024];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                bool inside = variant == 0 ? x > 3 && x < 19 && y > 4 && y < 26 : x > 12 && x < 28 && y > 9 && y < 23;
                pixels[y * 32 + x] = new Color(x / 31f, variant == 0 ? .2f : .8f, y / 31f, inside ? (variant == 0 ? .8f : .65f) : 0);
            }
            t.SetPixels(pixels); t.Apply(); textures.Add(t); return t;
        }
        Layer lower = new FileLayerBehaviour { sourceTexture = MakeTexture(0), opacity = .8f };
        Layer upper = new FileLayerBehaviour { sourceTexture = MakeTexture(1), opacity = .9f, blendMode = BlendMode.Screen };
        Layer hidden = new ColorFillLayerBehaviour { color = Color.magenta, enabled = false };
        void Normalize() => Call(doc, "NormalizeModel");
        Color[] Preview(Layer layer) => Read((RenderTexture)Call(doc, "RenderLayerPreview", layer, 32));
        Color[] Cached() => Read((RenderTexture)Call(doc, "RenderCachedPreview", 32, cache, false, null));
        Color[] Fresh() => Read((RenderTexture)Call(doc, "RenderAllLayers", 32, 32));
        ulong Stamp(Layer layer) { Call(cache, "BeginFrame", doc, null); return (ulong)Call(cache, "Stamp", layer); }
        Texture2D Snapshot()
        {
            var tex = doc.Compose(); textures.Add(tex); return tex;
        }
        try
        {
            Check((int)EffectInputMode.Previous == 0 && (int)EffectInputMode.Specific == 1 && (int)EffectInputMode.AllBelow == 2, "Stable enum values");
            Check(new OutlineLayerBehaviour().inputMode == EffectInputMode.Previous, "Existing default unchanged");
            doc.layers.Add(upper); doc.layers.Add(hidden); doc.layers.Add(lower); Normalize();
            var combined = Snapshot();
            Layer reference = new FileLayerBehaviour { sourceTexture = combined, enabled = false, colorRange = LayerColorRange.HDR };
            effects.Add(new OutlineLayerBehaviour());
            effects.Add(new SDFLayerBehaviour());
            effects.Add(new BlurLayerBehaviour { radius = 2 });
            effects.Add(new SharpenLayerBehaviour());
            effects.Add(new NormalMapLayerBehaviour());
            effects.Add(new MakeSeamlessLayerBehaviour { mode = MakeSeamlessLayerBehaviour.SeamlessMode.Mirror, mirrorSeamCorrection = false });
            foreach (var effect in effects)
            {
                effect.inputMode = EffectInputMode.AllBelow;
                doc.layers.Insert(0, effect); doc.layers.Add(reference); Normalize();
                var standalone = Preview(effect);
                var composite = Cached();
                Same(composite, Fresh(), effect + " cached/export parity");
                var args = new object[] { effect.Owner, null };
                Check((bool)Call(doc, "TryGetCachedMiniPreview", args), effect + " main cache supplies mini preview");
                var copy = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit((RenderTexture)args[1], copy);
                Same(standalone, Read(copy), effect + " main/standalone parity");
                Same(standalone, Read((RenderTexture)Call(doc, "RenderThumbnailLayer", effect.Owner, 32, cache)), effect + " thumbnail parity");
                effect.inputMode = EffectInputMode.Specific; effect.TargetLayerId = reference.Id;
                Same(standalone, Preview(effect), effect + " equals effect on composited snapshot");
                Same(composite, Fresh(), effect + " main effect blend unchanged");
                doc.layers.Remove(effect); doc.layers.Remove(reference);
            }

            var blur = (BlurLayerBehaviour)effects[2]; blur.inputMode = EffectInputMode.AllBelow;
            doc.layers.Insert(0, blur); Normalize();
            int publishes = 0;
            Action<Layer, RenderTexture> observer = (layer, pixels) => { if (layer == lower || layer == upper) publishes++; };
            var publication = typeof(TextureCompositor).GetEvent("MiniPreviewRendered", F);
            publication.GetAddMethod(true).Invoke(doc, new object[] { observer });
            try { cache.Dispose(); Cached(); Check(publishes == 2, "Main path reuses accumulator without rendering lower sources twice"); }
            finally { publication.GetRemoveMethod(true).Invoke(doc, new object[] { observer }); }
            ulong before = Stamp(blur);
            ((ColorFillLayerBehaviour)hidden.Behaviour).color = Color.cyan;
            Check(Stamp(blur) == before, "Hidden lower edits do not invalidate cache");
            lower.opacity = .3f; Check(Stamp(blur) != before, "Deep source opacity invalidates cache"); Same(Cached(), Fresh(), "Opacity cache invalidation");
            before = Stamp(blur); upper.blendMode = BlendMode.Multiply;
            Check(Stamp(blur) != before, "Source blend invalidates cache"); Same(Cached(), Fresh(), "Blend cache invalidation");
            before = Stamp(blur); hidden.enabled = true;
            Check(Stamp(blur) != before, "Source visibility invalidates cache"); Same(Cached(), Fresh(), "Visibility cache invalidation");
            hidden.enabled = false;
            before = Stamp(blur); doc.layers.Remove(lower); doc.layers.Insert(1, lower);
            Check(Stamp(blur) != before, "Source order invalidates cache"); Same(Cached(), Fresh(), "Order cache invalidation");
            before = Stamp(blur); doc.layers.Insert(0, new ColorFillLayerBehaviour { color = Color.green }); Normalize();
            Check(Stamp(blur) == before, "Changes above effect do not invalidate its input"); doc.layers.RemoveAt(0);

            doc.layers.Clear(); doc.layers.Add(upper); doc.layers.Add(lower);
            var processor = new ShaderProcessorLayerBehaviour(); processor.swizzle[0] = SwizzleChannel.B; processor.swizzle[2] = SwizzleChannel.R;
            doc.layers.Insert(0, processor); Normalize();
            Layer processedReference = new FileLayerBehaviour { sourceTexture = Snapshot(), enabled = false, colorRange = LayerColorRange.HDR };
            doc.layers.Insert(0, blur); doc.layers.Add(processedReference); Normalize();
            var processed = Preview(blur); var processedMain = Cached();
            blur.inputMode = EffectInputMode.Specific; blur.TargetLayerId = processedReference.Id;
            Same(processed, Preview(blur), "Lower processor included in input"); Same(processedMain, Fresh(), "Processor accumulator parity");
            blur.inputMode = EffectInputMode.AllBelow;

            var seamless = (MakeSeamlessLayerBehaviour)effects[5];
            seamless.inputMode = EffectInputMode.AllBelow; seamless.mode = MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting;
            doc.layers.Clear(); doc.layers.Add(seamless); doc.layers.Add(upper); doc.layers.Add(lower); Normalize();
            Cached(); Call(cache, "BeginFrame", doc, null);
            ulong quiltStamp = (ulong)Call(cache, "QuiltingStamp", seamless, doc);
            lower.opacity = .55f; Call(cache, "BeginFrame", doc, null);
            Check((ulong)Call(cache, "QuiltingStamp", seamless, doc) != quiltStamp, "Raw Quilting cache tracks entire input stack");
            Same(Cached(), Fresh(), "Quilting lower-stack invalidation");

            doc.layers.Clear(); doc.layers.Add(upper); doc.layers.Add(lower); upper.clippingMask = true; Normalize();
            Layer clippingReference = new FileLayerBehaviour { sourceTexture = Snapshot(), enabled = false, colorRange = LayerColorRange.HDR };
            doc.layers.Insert(0, blur); doc.layers.Add(clippingReference); Normalize();
            var clippedStack = Preview(blur); var clippedMain = Cached();
            blur.inputMode = EffectInputMode.Specific; blur.TargetLayerId = clippingReference.Id;
            Same(clippedStack, Preview(blur), "Input composites clipping chain"); Same(clippedMain, Fresh(), "Main clipping-stack parity");
            blur.inputMode = EffectInputMode.AllBelow; blur.clippingMask = true;
            Same(Cached(), Fresh(), "Clipped AllBelow effect remains stable");
            blur.clippingMask = upper.clippingMask = false;

            doc.layers.Clear(); doc.layers.Add(upper); doc.layers.Add(lower); Normalize();
            Layer groupReference = new FileLayerBehaviour { sourceTexture = Snapshot(), enabled = false, colorRange = LayerColorRange.HDR };
            Layer group = new GroupLayerBehaviour(); group.children.Add(blur); group.children.Add(upper); group.children.Add(lower);
            doc.layers.Clear(); doc.layers.Add(group); doc.layers.Add(new ColorFillLayerBehaviour { color = Color.yellow }); doc.layers.Add(groupReference); Normalize();
            foreach (var mode in new[] { GroupCompositing.Isolated, GroupCompositing.PassThrough })
            {
                ((GroupLayerBehaviour)group.Behaviour).compositing = mode;
                blur.inputMode = EffectInputMode.AllBelow;
                var local = Preview(blur); var main = Cached();
                blur.inputMode = EffectInputMode.Specific; blur.TargetLayerId = groupReference.Id;
                Same(local, Preview(blur), mode + " stays inside group"); Same(main, Fresh(), mode + " ignores external backdrop for input");
            }
            blur.inputMode = EffectInputMode.AllBelow;
            var serialized = JsonUtility.ToJson(doc);
            var roundtrip = ScriptableObject.CreateInstance<TextureCompositor>();
            try { JsonUtility.FromJsonOverwrite(serialized, roundtrip); Check(((TargetedLayerBehaviour)roundtrip.layers[0].children[0].Behaviour).inputMode == EffectInputMode.AllBelow, "Serialization preserves new mode"); }
            finally { UnityEngine.Object.DestroyImmediate(roundtrip); }

            doc.layers.Clear(); doc.layers.Add(blur); Normalize(); blur.radius = 0;
            var empty = Preview(blur); foreach (var pixel in empty) Check(pixel.a == 0, "Empty stack is transparent");
            var cycle = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = blur.Id };
            effects.Add(cycle); doc.layers.Add(cycle); Normalize();
            Check(!(bool)Call(doc, "IsUsableEffectTarget", cycle, blur.Id), "Specific cannot target AllBelow consuming itself");
            Check(!(bool)Call(doc, "HasUsableEffectInput", blur, doc.layers, 0), "AllBelow cycle reported");
            Fresh();
            doc.layers.Clear(); doc.layers.Add(blur); doc.layers.Add(lower); Normalize();
            var api = typeof(WhimTexApi);
            var operation = api.GetMethod("ApplyOperation", F);
            var jsonType = operation.GetParameters()[1].ParameterType;
            object Parse(string json) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
            operation.Invoke(null, new object[] { doc, Parse("{\"op\":\"target\",\"layer\":\"" + blur.Id + "\",\"input\":\"AllBelow\"}"), new Dictionary<string, Layer>(), false });
            Check(blur.inputMode == EffectInputMode.AllBelow && blur.TargetLayerId == null, "API sets stack input without target");
            var readClipboard = api.GetMethod("ReadProceduralClipboard", F);
            var clipboard = readClipboard.Invoke(null, new object[] { "{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":[{\"type\":\"blur\",\"input\":\"AllBelow\"},{\"type\":\"color\"}]}", 32, 32 });
            try
            {
                var pasted = (TextureCompositor)clipboard.GetType().GetField("Document", F).GetValue(clipboard);
                Check(((TargetedLayerBehaviour)pasted.layers[0].Behaviour).inputMode == EffectInputMode.AllBelow, "Portable clipboard retains stack input");
            }
            finally { ((IDisposable)clipboard).Dispose(); }
            var copied = (TextureCompositor)Call(doc, "CaptureLayerClipboard", new List<Layer> { blur });
            try { Check(((TargetedLayerBehaviour)copied.layers[0].Behaviour).inputMode == EffectInputMode.AllBelow, "Native copy keeps stack-relative mode without copying sources"); }
            finally { UnityEngine.Object.DestroyImmediate(copied); }
            string portable = (string)api.GetMethod("WritePortableClipboard", F).Invoke(null, new object[] { doc, new List<Layer> { blur } });
            var restored = readClipboard.Invoke(null, new object[] { portable, 32, 32 });
            try
            {
                var pasted = (TextureCompositor)restored.GetType().GetField("Document", F).GetValue(restored);
                Check(((TargetedLayerBehaviour)pasted.layers[0].Behaviour).inputMode == EffectInputMode.AllBelow, "Portable copy roundtrip does not turn stack input into a specific target");
            }
            finally { ((IDisposable)restored).Dispose(); }
            foreach (string invalid in new[] {
                "{\"type\":\"blur\",\"input\":\"AllBelow\",\"target\":\"x\"}",
                "{\"type\":\"blur\",\"input\":\"Specific\"}",
                "{\"type\":\"color\",\"input\":\"AllBelow\"}" })
            {
                bool rejected = false;
                try
                {
                    var bad = readClipboard.Invoke(null, new object[] { "{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":[" + invalid + "]}", 32, 32 });
                    ((IDisposable)bad).Dispose();
                }
                catch (TargetInvocationException ex) { rejected = ex.GetBaseException().GetType().Name == "WhimTexApiException"; }
                Check(rejected, "Invalid clipboard input rejected");
            }
            var bindings = Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"), true);
            var view = Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.EffectTargetSettingsView"), F, null,
                new object[] { doc, (Action<string, Action>)((name, apply) => apply()), bindings }, null);
            var root = new VisualElement(); Call(view, "Build", root, blur);
            Check(root.Q<EnumField>().value.Equals(EffectInputMode.AllBelow), "Shared inspector shows All Below");
            var targetField = root.Q<PopupField<string>>();
            Check(targetField.ClassListContains("whimtex-hidden"), "Target hidden for All Below");
            blur.inputMode = EffectInputMode.Previous; Call(bindings, "Refresh", true);
            Check(!targetField.ClassListContains("whimtex-hidden"), "Previous target UI unchanged");
            blur.inputMode = EffectInputMode.Specific; blur.TargetLayerId = lower.Id; Call(bindings, "Refresh", true);
            Check(!targetField.ClassListContains("whimtex-hidden"), "Specific target available");
            Check(WhimTexApi.Describe().Contains("AllBelow"), "API advertises mode");
            return "Passed " + checks + " checks: six effects, merged input, main reuse, cache, mini preview, thumbnails, export, clipping, groups, cycles, serialization and API.";
        }
        finally
        {
            cache.Dispose();
            foreach (var effect in effects) Call(effect, "ReleaseTransientResources");
            UnityEngine.Object.DestroyImmediate(doc);
            foreach (var texture in textures) UnityEngine.Object.DestroyImmediate(texture);
            RenderTexture.active = active; GL.sRGBWrite = srgb;
        }
    }
}
