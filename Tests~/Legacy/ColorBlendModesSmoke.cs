using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// run_script entry ColorBlendModesSmoke.Run. Transient GPU resources; no assets/windows changed.
public static class ColorBlendModesSmoke
{
    static float Lum(Color c) => .3f * c.r + .59f * c.g + .11f * c.b;
    static float Sat(Color c) => Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b);
    static Color SetSat(Color c, float saturation)
    {
        int[] indices = { 0, 1, 2 };
        Array.Sort(indices, (a, b) => c[a].CompareTo(c[b]));
        int lo = indices[0], mid = indices[1], hi = indices[2];
        if (c[hi] > c[lo]) { c[mid] = (c[mid] - c[lo]) * saturation / (c[hi] - c[lo]); c[hi] = saturation; }
        else c[mid] = c[hi] = 0;
        c[lo] = 0;
        return c;
    }
    static Color SetLum(Color c, float l, bool hdr)
    {
        float d = l - Lum(c);
        for (int j = 0; j < 3; j++) c[j] += d;
        if (hdr) return c;
        float low = Mathf.Min(c.r, c.g, c.b), high = Mathf.Max(c.r, c.g, c.b);
        if (low < 0) for (int j = 0; j < 3; j++) c[j] = l + (c[j] - l) * l / (l - low);
        if (high > 1) for (int j = 0; j < 3; j++) c[j] = l + (c[j] - l) * (1 - l) / (high - l);
        return c;
    }
    static Color Blend(Color b, Color s, BlendMode mode, bool hdr)
    {
        switch (mode)
        {
            case BlendMode.Hue: return SetLum(SetSat(s, Sat(b)), Lum(b), hdr);
            case BlendMode.Saturation: return SetLum(SetSat(b, Sat(s)), Lum(b), hdr);
            case BlendMode.Color: return SetLum(s, Lum(b), hdr);
            default: return SetLum(b, Lum(s), hdr);
        }
    }
    static Color Convert(Color c, bool encode)
    {
        for (int j = 0; j < 3; j++)
        {
            float v = Mathf.Abs(c[j]);
            c[j] = Mathf.Sign(c[j]) * (encode ? v <= .0031308f ? 12.92f * v : 1.055f * Mathf.Pow(v, 1 / 2.4f) - .055f
                : v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f));
        }
        return c;
    }
    static Color Expected(Color b, Color s, BlendMode mode, bool hdr, float opacity, bool clipped)
    {
        float a = s.a * opacity, ba = b.a;
        if (a <= 0 || clipped && ba <= 0) return b;
        if (!clipped && ba <= 0) return new Color(s.r, s.g, s.b, a);
        if (!hdr) { b = Convert(b, true); s = Convert(s, true); }
        Color mixed = Blend(b, s, mode, hdr);
        float outputAlpha = clipped ? ba : a + ba * (1 - a);
        for (int j = 0; j < 3; j++)
            mixed[j] = clipped ? Mathf.Lerp(b[j], mixed[j], a)
                : (a * (1 - ba) * s[j] + a * ba * mixed[j] + (1 - a) * ba * b[j]) / outputAlpha;
        if (!hdr) mixed = Convert(mixed, false);
        mixed.a = outputAlpha;
        return mixed;
    }
    public static string Run()
    {
        int checks = 0;
        void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
        var assembly = typeof(TextureCompositor).Assembly;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var menuType = assembly.GetType("DCFApixels.WhimTex.BlendModeMenu", true);
        var groups = (BlendMode[][])menuType.GetField("Groups", flags).GetValue(null);
        var seen = new HashSet<BlendMode>();
        foreach (var group in groups) foreach (var mode in group) Check(seen.Add(mode), "Duplicate menu entry");
        Check(seen.Count == Enum.GetValues(typeof(BlendMode)).Length && groups.Length == 6, "Menu covers all modes");
        Check(groups[1][0] == BlendMode.Lighten && groups[2][0] == BlendMode.Darken && groups[3][0] == BlendMode.Overlay,
            "Lighten/darken/contrast order");
        Check(groups[5][0] == BlendMode.Hue && groups[5][3] == BlendMode.Luminosity, "Color group last");
        var menu = (GenericMenu)menuType.GetMethod("Create", flags).Invoke(null,
            new object[] { (BlendMode?)BlendMode.Color, false, (Action<BlendMode>)(_ => { }), null, null });
        Check(menu.GetItemCount() == seen.Count + 5, "Five native separators and no submenu entries");
        var field = new EnumField(BlendMode.Color);
        menuType.GetMethod("Attach", flags, null, new[] { typeof(EnumField) }, null).Invoke(null, new object[] { field });
        menuType.GetMethod("Attach", flags, null, new[] { typeof(EnumField) }, null).Invoke(null, new object[] { field });
        Check((BlendMode)field.value == BlendMode.Color, "Attaching twice preserves value");
        var psd = assembly.GetType("DCFApixels.WhimTex.WhimTexPsdExporter").GetMethod("BlendKey", flags);
        BlendMode[] modes = { BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity };
        string[] keys = { "hue ", "sat ", "colr", "lum " };
        for (int i = 0; i < modes.Length; i++)
        {
            var args = new object[] { modes[i], false };
            Check((string)psd.Invoke(null, args) == keys[i] && !(bool)args[1], "PSD color key " + modes[i]);
        }
        var shader = Shader.Find("Hidden/TextureCompositor/Blend");
        Check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Blend shader supported");
        var brushShader = Shader.Find("Hidden/TextureCompositor/PaintBrush");
        Check(brushShader != null && !ShaderUtil.ShaderHasError(brushShader), "Brush shader supported");
        var material = new Material(shader);
        var baseTex = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var sourceTex = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        bool oldWrite = GL.sRGBWrite;
        var random = new System.Random(714);
        float Random() => (float)random.NextDouble();
        try
        {
            GL.sRGBWrite = false;
            material.SetTexture("_Blend", sourceTex);
            foreach (bool hdr in new[] { false, true })
            foreach (BlendMode mode in modes)
            for (int sample = 0; sample < 24; sample++)
            {
                var b = new Color(Random(), Random(), Random(), Random());
                var s = new Color(Random(), Random(), Random(), Random());
                if (hdr) { b.r = 2.4f; b.b -= .7f; s.g = 3.1f; s.r -= .8f; }
                if (sample < 6)
                {
                    Color[] edges = { Color.black, Color.white, Color.gray, Color.red, Color.green, Color.blue };
                    b = edges[sample]; s = edges[(sample + 2) % 6];
                }
                if (sample == 6) b.a = 0;
                if (sample == 7) s.a = 0;
                float opacity = sample == 8 ? 0 : sample < 6 ? 1 : .63f;
                for (int path = 0; path < 3; path++)
                {
                    Color bb = b, ss = s;
                    if (path == 2) for (int j = 0; j < 3; j++) { bb[j] *= b.a; ss[j] *= s.a; }
                    baseTex.SetPixel(0, 0, bb); baseTex.Apply();
                    sourceTex.SetPixel(0, 0, ss); sourceTex.Apply();
                    material.SetFloat("_Mode", (float)mode); material.SetFloat("_HdrBlend", hdr ? 1 : 0);
                    material.SetFloat("_Opacity", opacity); material.SetFloat("_PreserveAlpha", path == 1 ? 1 : 0);
                    material.SetFloat("_BrushStandard", hdr ? 0 : 1);
                    Graphics.Blit(baseTex, target, material, path == 2 ? 1 : 0);
                    RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); read.Apply();
                    Color actual = read.GetPixel(0, 0), expected = Expected(b, s, mode, hdr, opacity, path == 1);
                    if (path == 2) for (int j = 0; j < 3; j++) expected[j] *= expected.a;
                    for (int j = 0; j < 4; j++) Check(!float.IsNaN(actual[j]) && !float.IsInfinity(actual[j]) &&
                        Mathf.Abs(actual[j] - expected[j]) < .0003f,
                        mode + " hdr=" + hdr + " path=" + path + " sample=" + sample + ": " + actual.ToString("F6") + " != " + expected.ToString("F6"));
                }
            }
            return "PASS: " + checks + " checks; grouped menu, PSD keys, shader compilation, GPU-vs-CPU color modes across standard/HDR, alpha edges, clipping and premultiplied brush passes.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = oldWrite;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(baseTex);
            UnityEngine.Object.DestroyImmediate(sourceTex); UnityEngine.Object.DestroyImmediate(read);
        }
    }
}
