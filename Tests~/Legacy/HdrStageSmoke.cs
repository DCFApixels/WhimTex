// Opt-in live-Editor eval after manual shader import. No source import, compilation or saved assets.
var shader = Shader.Find("Hidden/TextureCompositor/Hdr");
if (shader == null || !shader.isSupported) throw new Exception("HDR shader is unavailable.");
var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
var source = new Texture2D(3, 3, TextureFormat.RGBAFloat, false, true)
{ hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
var read = new Texture2D(3, 3, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
var target = RenderTexture.GetTemporary(3, 3, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
target.filterMode = FilterMode.Point;
target.wrapMode = TextureWrapMode.Clamp;
var previous = RenderTexture.active;
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
Color Sample(int pass, int x, int y)
{
    // Deliberately do not call SetTexture("_MainTex"): production relies on Blit's source binding.
    Graphics.Blit(source, target, material, pass);
    RenderTexture.active = target;
    read.ReadPixels(new Rect(0, 0, 3, 3), 0, 0, false);
    return read.GetPixel(x, y);
}
try
{
    Check(material.HasProperty("_MainTex"), "HDR shader must declare the Blit source property");
    Color[] pixels = new Color[9];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(2, -.125f, .25f, .75f);
    source.SetPixels(pixels); source.Apply(false, false);
    Color value = Sample(0, 1, 1);
    Check(Mathf.Abs(value.r - 2) < .001f && Mathf.Abs(value.g + .125f) < .001f &&
        Mathf.Abs(value.b - .25f) < .001f && Mathf.Abs(value.a - .75f) < .001f, "Clean pass must preserve finite signed HDR RGBA");
    Check(Sample(1, 1, 1).r == 0, "Finite HDR must not populate the error mask");
    material.SetFloat("_Saturate", 1);
    value = Sample(0, 1, 1);
    Check(value.r == 1 && value.g == 0 && Mathf.Abs(value.a - .75f) < .001f, "Standard clamps RGB without replacing alpha");
    material.SetFloat("_Saturate", 0);
    pixels[8] = new Color(float.NaN, float.PositiveInfinity, 70000, 1);
    source.SetPixels(pixels); source.Apply(false, false);
    value = Sample(0, 2, 2);
    Check(value.r == 0 && value.g == 0 && value.b == 65504 && value.a == 1, "Nonfinite components become zero; finite overflow clamps to half range");
    Check(Sample(1, 2, 2).r == 1, "Invalid components populate the mask before clamping");
    RenderTexture reduced = null;
    try
    {
        Texture input = target;
        do
        {
            var next = RenderTexture.GetTemporary(Mathf.Max(1, (input.width + 1) / 2),
                Mathf.Max(1, (input.height + 1) / 2), 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            next.filterMode = FilterMode.Point; next.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(input, next, material, 2);
            if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
            input = reduced = next;
        } while (reduced.width > 1 || reduced.height > 1);
        RenderTexture.active = reduced;
        read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        Check(read.GetPixel(0, 0).r == 1, "Reduction must retain errors on odd-sized image edges");
    }
    finally { if (reduced != null) RenderTexture.ReleaseTemporary(reduced); }
    return $"HDR stage checks passed: {checks}.";
}
finally
{
    RenderTexture.active = previous;
    RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(read);
    UnityEngine.Object.DestroyImmediate(source);
    UnityEngine.Object.DestroyImmediate(material);
}
