// Opt-in after manual compilation. Transient objects only; no saves, imports or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var type = typeof(DCFApixels.WhimTex.TextureCompositor);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 16;
var texture = new UnityEngine.Texture2D(16,16,UnityEngine.TextureFormat.RGBAFloat,false,true);
texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
var data = new UnityEngine.Color[256];
for (int y=0; y<16; y++) for (int x=0; x<16; x++)
    data[y*16+x] = new UnityEngine.Color(x/15f,y/15f,.4f,x>=4 && x<12 && y>=4 && y<12 ? 1f : 0f);
texture.SetPixels(data); texture.Apply(false,false);
var source = new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = texture };
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Normalize() => type.GetMethod("NormalizeModel",flags).Invoke(document,null);
UnityEngine.Color[] Render(DCFApixels.WhimTex.Layer layer)
{
    var rt = (UnityEngine.RenderTexture)type.GetMethod("RenderLayerPreview",flags).Invoke(document,new object[]{layer,16});
    if (rt == null) return null;
    var previous = UnityEngine.RenderTexture.active;
    var pixels = new UnityEngine.Texture2D(16,16,UnityEngine.TextureFormat.RGBAFloat,false,true);
    try
    {
        UnityEngine.RenderTexture.active = rt;
        pixels.ReadPixels(new UnityEngine.Rect(0,0,16,16),0,0,false);
        return pixels.GetPixels();
    }
    finally
    {
        UnityEngine.RenderTexture.active = previous;
        UnityEngine.RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(pixels);
    }
}
void Same(UnityEngine.Color[] a,UnityEngine.Color[] b,string message)
{
    Check(a != null && b != null,"Rendered " + message);
    for(int i=0;i<a.Length;i++) for(int c=0;c<4;c++)
        Check(UnityEngine.Mathf.Abs(a[i][c]-b[i][c])<.002f,message);
}
try
{
    foreach (var effect in new DCFApixels.WhimTex.TargetedLayerBehaviour[] {
        new DCFApixels.WhimTex.OutlineLayerBehaviour(), new DCFApixels.WhimTex.SDFLayerBehaviour(), new DCFApixels.WhimTex.NormalMapLayerBehaviour() })
    foreach (bool specific in new[]{false,true})
    foreach (bool grouped in new[]{false,true})
    {
        source.enabled = true;
        var group = new DCFApixels.WhimTex.GroupLayerBehaviour(); group.layers.Add(source);
        DCFApixels.WhimTex.Layer target = grouped ? (DCFApixels.WhimTex.Layer)group : source;
        document.layers.Clear(); document.layers.Add(effect); document.layers.Add(target); Normalize();
        effect.inputMode = specific ? DCFApixels.WhimTex.EffectInputMode.Specific : DCFApixels.WhimTex.EffectInputMode.Previous;
        effect.TargetLayerId = specific ? target.Id : null;
        var before = Render(effect);
        target.enabled = false;
        Same(before,Render(effect),effect + " hidden input, specific=" + specific + ", group=" + grouped);
        Check(!target.enabled,"Rendering does not modify target visibility");
        if(grouped)
        {
            var hiddenChild = new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = UnityEngine.Color.white, enabled = false };
            group.layers.Insert(0,hiddenChild); Normalize();
            Same(before,Render(effect),"Hidden children remain excluded");
        }
        effect.enabled = false;
        var composite = document.Compose();
        try { foreach(var pixel in composite.GetPixels()) Check(pixel.a<.001f,"Hidden sources stay out of composition"); }
        finally { UnityEngine.Object.DestroyImmediate(composite); }
        effect.enabled = true; target.enabled = true;
    }
    var sdf = new DCFApixels.WhimTex.SDFLayerBehaviour();
    var normal = new DCFApixels.WhimTex.NormalMapLayerBehaviour();
    document.layers.Clear(); document.layers.Add(normal); document.layers.Add(sdf); document.layers.Add(source);
    source.enabled = true; Normalize();
    var visibleChain = Render(normal);
    source.enabled = false; sdf.enabled = false;
    Same(visibleChain,Render(normal),"Hidden effect chain");
    sdf.inputMode = DCFApixels.WhimTex.EffectInputMode.Specific; sdf.TargetLayerId = normal.Id;
    Check(Render(normal)==null,"Hidden effect cycle remains rejected");
    return "Hidden effect input checks passed: " + checks;
}
finally
{
    UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(texture);
}
