using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class MirrorAutoRadiusSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static string Main()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.width = 48; doc.height = 32;
        var texture = new Texture2D(48, 32, TextureFormat.RGBAFloat, false, true);
        var pixels = new Color[48 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.5f + .4f * Mathf.Sin(i * .37f), .4f + .2f * Mathf.Cos(i * .11f), .3f, .8f);
        texture.SetPixels(pixels); texture.Apply();
        var layer = MakeSeamlessLayerBehaviour.CreateDefault(); layer.mode = MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;
        layer.colorRange = LayerColorRange.HDR;
        var source = new FileLayerBehaviour { sourceTexture = texture, colorRange = LayerColorRange.HDR };
        doc.layers.Add(layer); doc.layers.Add(source);
        var cache = (IDisposable)Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"), true);
        Color[] Render(bool cached, bool thumbnail = false)
        {
            string method = thumbnail ? "RenderThumbnailLayer" : cached ? "RenderCachedPreview" : "RenderAllLayers";
            object[] args = thumbnail ? new object[] { layer.Owner, 48, cache } : cached ? new object[] { 48, cache, false, null } : new object[] { 48, 32 };
            var rt = (RenderTexture)typeof(TextureCompositor).GetMethod(method, F).Invoke(doc, args);
            var previous = RenderTexture.active;
            var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); return read.GetPixels(); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(read); RenderTexture.ReleaseTemporary(rt); }
        }
        void Same(Color[] a, Color[] b)
        {
            Check(a.Length == b.Length, "Dimensions");
            for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++) Check(Math.Abs(a[i][c] - b[i][c]) < .003f, "Auto/manual rendering mismatch");
        }
        try
        {
            typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(doc, null);
            layer.TargetLayerId = source.Id; source.enabled = false;
            Check(layer.mirrorAutoRadius, "Default enabled");
            foreach (bool contrast in new[] { false, true }) foreach (float width in new[] { .001f, .2f, .4f })
            {
                layer.blendWidth = width; layer.mirrorContrastCompensation = contrast;
                layer.mirrorCorrectionRadius = .19f; layer.mirrorAutoRadius = true;
                var automatic = Render(false); Same(automatic, Render(true));
                var autoThumbnail = Render(true, true);
                layer.mirrorAutoRadius = false; layer.mirrorCorrectionRadius = Mathf.Max(.005f, width * .25f);
                Same(automatic, Render(false)); Same(automatic, Render(true)); Same(autoThumbnail, Render(true, true));
            }
            var setter = typeof(WhimTexApi).GetMethod("SetMakeSeamless", F);
            foreach (bool automatic in new[] { true, false })
            {
                var jsonType = setter.GetParameters()[1].ParameterType;
                var settings = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null,
                    new object[] { "{\"mirrorAutoRadius\":" + (automatic ? "true" : "false") + ",\"mirrorCorrectionRadius\":0.13}" });
                setter.Invoke(null, new object[] { layer, settings });
                Check(layer.mirrorAutoRadius == automatic, "API setter");
                var clone = JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(layer));
                Check(clone.mirrorAutoRadius == automatic && clone.mirrorCorrectionRadius == .13f, "Serialized settings");
                string json = (string)typeof(WhimTexApi).GetMethod("WritePortableClipboard", F).Invoke(null, new object[] { doc, doc.layers });
                using (var clip = (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 48, 32 }))
                {
                    var decoded = (TextureCompositor)clip.GetType().GetField("Document", F).GetValue(clip);
                    var restored = (MakeSeamlessLayerBehaviour)decoded.layers[0].Behaviour;
                    Check(restored.mirrorAutoRadius == automatic && restored.mirrorCorrectionRadius == .13f, "Portable roundtrip");
                }
            }
            return $"Passed: {checks} checks, auto/manual render equivalence, compensation on/off, cache invalidation, thumbnails, API and serialization.";
        }
        finally { cache.Dispose(); UnityEngine.Object.DestroyImmediate(doc); UnityEngine.Object.DestroyImmediate(texture); }
    }
}
