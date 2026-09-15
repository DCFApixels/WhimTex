using System;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
public static class SdfGradientGpuSmoke
{
    public static string Main()
    {
        AssetDatabase.ImportAsset("Packages/com.dcfapixels.whimtex/src/Shaders/SdfGradient.shader", ImportAssetOptions.ForceUpdate);
        var shader = Shader.Find("Hidden/TextureCompositor/SdfGradient");
        foreach (var m in ShaderUtil.GetShaderMessages(shader))
            if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) throw new Exception(m.message);
        var material = new Material(shader);
        var source = new Texture2D(65, 2, TextureFormat.RFloat, false, true);
        var values = new float[130];
        for (int i = 0; i < values.Length; i++) values[i] = (i % 65 - 32) / 4f;
        source.SetPixelData(values, 0); source.Apply();
        var output = RenderTexture.GetTemporary(65, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(65, 2, TextureFormat.RGBAFloat, false, true);
        var old = RenderTexture.active; bool srgb = GL.sRGBWrite;
        using var lut = new WhimTexGradientTexture();
        int checks = 0;
        try
        {
            var gradient = new SDFLayerBehaviour().gradient;
            foreach (WhimTexGradientMode mode in new[] { WhimTexGradientMode.Linear, WhimTexGradientMode.Classic, WhimTexGradientMode.Perceptual, WhimTexGradientMode.Fixed })
            for (int position = 0; position < 4; position++)
            for (int inverted = 0; inverted < 2; inverted++)
            {
                gradient.Mode = mode;
                material.SetTexture("_GradientLut", lut.GetTexture(gradient, ColorSpace.Gamma));
                material.SetFloat("_MaxDistance", 8);
                material.SetInt("_Position", position); material.SetInt("_Inverted", inverted);
                GL.sRGBWrite = false; Graphics.Blit(source, output, material);
                RenderTexture.active = output; read.ReadPixels(new Rect(0,0,65,2),0,0); var pixels = read.GetPixels();
                for(int i=0;i<values.Length;i++)
                {
                    float d=values[i]; if(position==0)d=Mathf.Max(d,0); if(position==1)d=Mathf.Max(-d,0); if(position==2)d=Mathf.Abs(d);
                    float t=Mathf.Clamp01(position==3?(d+8)/16:d/8); if(inverted!=0)t=1-t;
                    Color expected=gradient.Evaluate(t).linear;
                    for(int c=0;c<4;c++)
                    {
                        if(Mathf.Abs(pixels[i][c]-expected[c])>.004f) throw new Exception($"{mode} pos={position} inv={inverted} i={i} c={c} actual={pixels[i][c]} expected={expected[c]} samples={pixels[0]} {pixels[16]} {pixels[32]} {pixels[48]} {pixels[64]} lut={lut.GetTexture(gradient, ColorSpace.Gamma).GetPixel(0,0)} texel={material.GetVector("_GradientLut_TexelSize")}");
                        checks++;
                    }
                }
            }
            return "SDF LUT checks: " + checks;
        }
        finally { RenderTexture.active=old; GL.sRGBWrite=srgb; RenderTexture.ReleaseTemporary(output); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(material); }
    }
}
