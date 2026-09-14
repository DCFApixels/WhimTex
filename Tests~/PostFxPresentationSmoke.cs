// Opt-in live eval_file test after the user's manual compilation/import.
// Only temporary GPU resources. Does not render cameras, edit assets or change a window.
var shader = UnityEngine.Shader.Find("Hidden/TextureCompositor/URPPreviewSurface");
if (shader == null || !shader.isSupported) throw new System.Exception("Post FX surface shader is unavailable.");
var material = new UnityEngine.Material(shader) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
var source = new UnityEngine.Texture2D(8,8,UnityEngine.TextureFormat.RGBAFloat,false,true)
    { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
var readback = new UnityEngine.Texture2D(8,8,UnityEngine.TextureFormat.RGBAFloat,false,true)
    { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
var input = UnityEngine.RenderTexture.GetTemporary(8,8,0,UnityEngine.RenderTextureFormat.ARGBHalf,UnityEngine.RenderTextureReadWrite.Linear);
var output = UnityEngine.RenderTexture.GetTemporary(8,8,0,UnityEngine.RenderTextureFormat.ARGBHalf,UnityEngine.RenderTextureReadWrite.Linear);
var previous = UnityEngine.RenderTexture.active;
bool previousSrgb = UnityEngine.GL.sRGBWrite;
int checks = 0;
void Check(bool value,string message) { if (!value) throw new System.Exception(message); checks++; }
try
{
    Check(material.HasProperty("_MainTex"), "Missing registered _MainTex: Graphics.Blit cannot supply the normalization input.");
    Check(material.HasProperty("_Source"), "Missing composite input.");
    int pass = material.FindPass("NormalizeOutput");
    Check(pass == 2,"Normalization pass index must match the backend's final blit.");
    Check(shader.FindPassTagValue(pass,new UnityEngine.Rendering.ShaderTagId("LightMode")).name == "WhimTexNormalizeOutput",
        "The blit-only pass must not participate in automatic camera drawing.");
    int normals = material.FindPass("DepthNormals");
    Check(normals >= 0, "Missing demand-driven normals pass.");
    Check(shader.FindPassTagValue(normals,new UnityEngine.Rendering.ShaderTagId("LightMode")).name == "DepthNormalsOnly",
        "Forward-only surfaces need DepthNormalsOnly routing in both Forward and Deferred renderers.");
    var pixels = new UnityEngine.Color[64];
    for(int y=0;y<8;y++) for(int x=0;x<8;x++)
        pixels[y*8+x] = new UnityEngine.Color((x+1)/8f,(y+1)/8f,.35f,(x+y+1)/16f);
    source.SetPixels(pixels); source.Apply(false,false);
    input.filterMode = output.filterMode = source.filterMode = UnityEngine.FilterMode.Point;
    UnityEngine.GL.sRGBWrite = false;
    UnityEngine.Graphics.Blit(source,input);
    UnityEngine.Graphics.Blit(input,output,material,pass);
    UnityEngine.RenderTexture.active = output;
    readback.ReadPixels(new UnityEngine.Rect(0,0,8,8),0,0,false);
    var actual = readback.GetPixels();
    for(int i=0;i<pixels.Length;i++)
    {
        var expected = UnityEngine.QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Gamma ? pixels[i].linear : pixels[i];
        for(int channel=0;channel<4;channel++)
            Check(UnityEngine.Mathf.Abs(actual[i][channel]-expected[channel])<.004f,"Post FX normalization must preserve the varying image and alpha (pixel " + i + ", channel " + channel + ").");
    }
    return "Post FX presentation checks passed: " + checks;
}
finally
{
    UnityEngine.RenderTexture.active = previous;
    UnityEngine.GL.sRGBWrite = previousSrgb;
    UnityEngine.RenderTexture.ReleaseTemporary(input);
    UnityEngine.RenderTexture.ReleaseTemporary(output);
    UnityEngine.Object.DestroyImmediate(source);
    UnityEngine.Object.DestroyImmediate(readback);
    UnityEngine.Object.DestroyImmediate(material);
}
