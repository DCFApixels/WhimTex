using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class ShaderFXTextureViewTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Assembly = typeof(ShaderFX).Assembly;
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);

    public static string Run() => TestContext.Run("Texture views: independent regions, filtering, HDR, helpers and logical 8K inputs", t =>
    {
        using var f = new Fixture(t);
        string code = @"// @param texture2D _Mask
WhimTexTexture Forward(WhimTexTexture source) { WhimTexTexture copy = source; return copy; }
float4 Read(WhimTexTexture source, PinnedUV uv) { return SampleTexture(Forward(source), uv); }
float4 ApplyFX(PinnedUV uv, float4 color)
{
    float4 same = Read(_MainTex, uv);
    float mask = Read(_Mask, uv).r;
    return float4((color.rgb + same.rgb) * .5, color.a * mask);
}";
        var fx = f.Fx(code);
        t.True(fx.IsPixelLocal, "Forwarding/copying texture views retains locality");
        var full = f.Pattern(64, 32, false);
        var mask = f.Pattern(32, 16, true);
        var crop = f.Crop(full, new RectInt(16, 8, 24, 12));
        var maskCrop = f.Crop(mask, new RectInt(7, 3, 18, 10));
        // Fractional sample phase plus one-pixel guards exercise bilinear filtering
        // at the first/last output pixels, rather than only texel-center lookups.
        var outputRegion = new Rect(17.25f / 64, 9.25f / 32, 20f / 64, 10f / 32);
        foreach (var filter in new[] { FilterMode.Point, FilterMode.Bilinear })
        {
            full.filterMode = mask.filterMode = crop.filterMode = maskCrop.filterMode = filter;
            var expected = f.Render(fx, full, new Rect(0, 0, 1, 1), new Vector2(64, 32),
                mask, new Rect(0, 0, 1, 1), new Vector2(32, 16), outputRegion);
            foreach (bool cropMain in new[] { false, true })
            foreach (bool cropMask in new[] { false, true })
            {
                var actual = f.Render(fx, cropMain ? crop : full,
                    cropMain ? new Rect(16f / 64, 8f / 32, 24f / 64, 12f / 32) : new Rect(0, 0, 1, 1),
                    new Vector2(64, 32), cropMask ? maskCrop : mask,
                    cropMask ? new Rect(7f / 32, 3f / 16, 18f / 32, 10f / 16) : new Rect(0, 0, 1, 1),
                    new Vector2(32, 16), outputRegion);
                f.EqualPixels(expected, actual, filter + " main=" + cropMain + " mask=" + cropMask);
            }
        }
        // Real 8K logical coordinates without a gratuitous 1 GiB allocation.
        // The input texels encode their original global pixel indices.
        var small = f.Own(new Texture2D(32, 16, TextureFormat.RGBAHalf, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        var data = new Color[32 * 16];
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++)
            data[y * 32 + x] = new Color((4600 + x) / 8192f, (2500 + y) / 8192f, -2, .5f);
        small.SetPixels(data); small.Apply();
        var pass = f.Fx(@"float4 Read(WhimTexTexture source, PinnedUV uv) { return SampleTexture(source, uv); }
float4 ApplyFX(PinnedUV uv, float4 color) { return Read(_MainTex, uv); }");
        var region8K = new Rect(4600f / 8192, 2500f / 8192, 32f / 8192, 16f / 8192);
        var result8K = f.Render(pass, small, region8K, new Vector2(8192, 8192), null, default, default, region8K, 32, 16);
        f.EqualPixels(small.GetPixels(), result8K, "8K coordinates preserve stored Half/HDR values");
        var sizes = f.Fx(@"float4 ApplyFX(float4 color) {
return float4(_MainTex.texelSize.z / 8192.0, _MainTex_TexelSize.w / 8192.0,
_MainTex.texelSize.x * 8192.0, _InputSize.x / 8192.0); }");
        var sizePixels = f.Render(sizes, small, region8K, new Vector2(8192, 8192), null, default, default, region8K);
        foreach (var pixel in sizePixels) for (int c = 0; c < 4; c++) t.Near(1, pixel[c], .00001, "Logical size ignores crop size");

        var preview = f.Fx(@"// @param preview(PreviewTexture) texture2D _Mask
float4 ApplyFX(float4 color) { return color; }
#ifdef WHIMTEX_PREVIEW
float4 PreviewTexture(float2 uv, WhimTexTexture source) { return SampleTexture(source, uv); }
#endif");
        var fullRegion = new Rect(0, 0, 1, 1);
        var previewPixels = f.Render(preview, full, fullRegion, new Vector2(64, 32),
            mask, fullRegion, new Vector2(32, 16), fullRegion, 32, 16, preview: true);
        f.EqualPixels(mask.GetPixels(), previewPixels, "Inspector callback receives the texture view argument");

        foreach (string body in new[] {
            "WhimTexTexture source = _MainTex; source.uvMap.z += .2; return SampleTexture(source, uv);",
            "WhimTexTexture source = (WhimTexTexture)0; return SampleTexture(source, uv);",
            "WhimTexTexture source = {0}; return SampleTexture(source, uv);",
            "WhimTexTexture source = _MainTex; source = _MainTex; return SampleTexture(source, uv);"
        })
        {
            bool rejected = false;
            try { f.Fx("float4 ApplyFX(PinnedUV uv, float4 color) {" + body + "}"); }
            catch (TargetInvocationException) { rejected = true; }
            t.True(rejected, "Mutable/fabricated texture view rejected: " + body);
        }
        var bypass = f.Fx("float4 ApplyFX(PinnedUV uv, float4 color) { return tex2D(_MainTex.pixels, float2(uv.x, uv.y)); }");
        t.True(!bypass.IsPixelLocal, "Extracting the physical sampler is unconfirmed");
    });

    sealed class Fixture : IDisposable
    {
        readonly TestContext t;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly WhimTexDocument document;
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        internal Fixture(TestContext t)
        {
            this.t = t;
            document = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            document.width = document.height = 8192;
        }
        internal T Own<T>(T value) where T : UnityEngine.Object
        { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        internal ShaderFX Fx(string code)
        {
            var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, code, new List<ShaderFXParameter>() }));
            Call(fx, "ApplyAgentDraft");
            var shader = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(fx);
            if (shader != null) Own(shader);
            return fx;
        }
        internal Texture2D Pattern(int w, int h, bool mask)
        {
            var texture = Own(new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                pixels[y * w + x] = mask ? new Color(((x + y) % 5) / 4f, 0, 0, 1)
                    : new Color((x % 7) - 2, y / 8f, -x / 64f, (x + y) / 100f);
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
        internal Texture2D Crop(Texture2D source, RectInt rect)
        {
            var texture = Own(new Texture2D(rect.width, rect.height, source.format, false, true) { wrapMode = TextureWrapMode.Clamp });
            texture.SetPixels(source.GetPixels(rect.x, rect.y, rect.width, rect.height)); texture.Apply(); return texture;
        }
        static void BindView(Material material, string name, Texture texture, Rect region, Vector2 size)
        {
            Type type = Assembly.GetType("DCFApixels.WhimTex.ShaderTextureView", true);
            var view = Activator.CreateInstance(type, F, null, new object[] { texture, region, size }, null);
            type.GetMethod("BindMetadata", F).Invoke(view, new object[] { material,
                Shader.PropertyToID("_WhimTex_Texture" + name + "_UvMap"),
                Shader.PropertyToID("_WhimTex_Texture" + name + "_Size") });
            material.SetTexture(name, texture);
        }
        internal Color[] Render(ShaderFX fx, Texture main, Rect region, Vector2 size,
            Texture mask, Rect maskRegion, Vector2 maskSize, Rect outputRegion, int w = 20, int h = 10, bool preview = false)
        {
            var context = Activator.CreateInstance(Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
                document, null, w, h, 1f, true, true, null);
            var material = (Material)Call(fx, "GetMaterial", context);
            t.True(material != null, "Compiled material");
            if (preview)
            {
                var parameter = ((IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(fx))[0];
                string source = (string)Assembly.GetType("DCFApixels.WhimTex.ShaderFXSourceBuilder", true)
                    .GetMethod("BuildPreview", F).Invoke(null, new object[] { fx, parameter.controls[0], parameter });
                var shader = Own(UnityEditor.ShaderUtil.CreateShaderAsset(source, true));
                material = Own(new Material(shader));
                UnityEditor.ShaderUtil.CompilePass(material, 0, true);
                t.True(shader.isSupported && !UnityEditor.ShaderUtil.ShaderHasError(shader), "Texture preview compiles");
            }
            BindView(material, "_MainTex", main, region, size);
            if (mask != null) BindView(material, "_Mask", mask, maskRegion, maskSize);
            material.SetVector("_WhimTex_Viewport", new Vector4(outputRegion.x, outputRegion.y, outputRegion.width, outputRegion.height));
            material.SetVector("_InputSize", new Vector4(size.x, size.y, 1 / size.x, 1 / size.y));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Texture2D read = null;
            var active = RenderTexture.active;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(main, rt, material);
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply();
                return read.GetPixels();
            }
            finally
            {
                RenderTexture.active = active;
                material.SetTexture("_MainTex", null);
                if (mask != null) material.SetTexture("_Mask", null);
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
                RenderTexture.ReleaseTemporary(rt);
            }
        }
        internal void EqualPixels(Color[] expected, Color[] actual, string message)
        {
            t.Equal(expected.Length, actual.Length, message + " length");
            for (int i = 0; i < actual.Length; i++)
                for (int c = 0; c < 4; c++) t.Near(expected[i][c], actual[i][c], .00005, message + " pixel=" + i + " channel=" + c);
        }
        public void Dispose()
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    }
}
