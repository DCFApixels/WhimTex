// Unity Pipeline eval_file. Transient documents/textures/window only; no asset files or user documents are changed.
var type = typeof(DCFApixels.SpriteEditor.TextureCompositor);
var assembly = type.Assembly;
var instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var outputField = type.GetField("outputTexture", instance);
var notify = type.GetMethod("NotifyOutputTextureChanged", instance);
var changeType = assembly.GetType("DCFApixels.SpriteEditor.CompositorOutputChange", true);
var changeCtor = changeType.GetConstructor(instance, null, new[] { type }, null);
var shouldRefresh = changeType.GetMethod("ShouldRefresh", instance);
var docs = new System.Collections.Generic.List<DCFApixels.SpriteEditor.TextureCompositor>();
var textures = new System.Collections.Generic.List<UnityEngine.Texture2D>();
DCFApixels.SpriteEditor.TextureCompositor NewDocument()
{
    var doc = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
    doc.hideFlags = UnityEngine.HideFlags.HideAndDontSave; doc.width = doc.height = 16;
    var texture = new UnityEngine.Texture2D(16, 16, UnityEngine.TextureFormat.RGBAFloat, false, true);
    texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    var pixels = new UnityEngine.Color[256];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = UnityEngine.Color.black;
    texture.SetPixels(pixels); texture.Apply();
    outputField.SetValue(doc, texture); docs.Add(doc); textures.Add(texture); return doc;
}
DCFApixels.SpriteEditor.Layer File(DCFApixels.SpriteEditor.TextureCompositor source) =>
    new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.FileLayerBehaviour { sourceTexture = source.OutputTexture });
bool Accept(DCFApixels.SpriteEditor.TextureCompositor source, DCFApixels.SpriteEditor.TextureCompositor consumer) =>
    (bool)shouldRefresh.Invoke(changeCtor.Invoke(new object[] { source }), new object[] { consumer });
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new System.Exception(label); checks++; }
var a = NewDocument(); var b = NewDocument(); var c = NewDocument();
var previousFocus = UnityEditor.EditorWindow.focusedWindow;
DCFApixels.SpriteEditor.TextureCompositorWindow window = null;
System.IDisposable session = null;
UnityEngine.RenderTexture gpu = null;
var previousTarget = UnityEngine.RenderTexture.active;
var sample = new UnityEngine.Texture2D(1, 1, UnityEngine.TextureFormat.RGBAFloat, false, true);
sample.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
try
{
    b.layers.Add(File(a)); c.layers.Add(File(b));
    Check(Accept(a, b), "direct dependency");
    Check(!Accept(a, c), "downstream waits for intermediate publication");
    Check(Accept(b, c), "chain dependency");
    Check(!Accept(a, a), "self notification ignored");
    a.layers.Add(File(c));
    Check(!Accept(a, b) && !Accept(b, c) && !Accept(c, a), "three-document cycle blocked");
    a.layers.Clear(); a.layers.Add(File(b));
    Check(!Accept(a, b), "two-document cycle blocked"); a.layers.Clear();
    var group = new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.GroupLayerBehaviour());
    group.children.Add(File(a)); group.enabled = false;
    b.layers.Clear(); b.layers.Add(group);
    Check(Accept(a, b), "nested hidden sources included for effects");
    b.layers.Clear(); b.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.BlurLayerBehaviour { radius = 2f }));
    b.layers.Add(File(a));
    var windowType = typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
    window = (DCFApixels.SpriteEditor.TextureCompositorWindow)windowType.GetMethod("OpenReferencedDocument", statics).Invoke(null, new object[] { b });
    var dirty = windowType.GetField("outputDependencyDirty", instance);
    var requested = windowType.GetField("previewRequested", instance);
    var render = windowType.GetMethod("UpdatePreview", instance);
    var preview = windowType.GetField("previewTexture", instance);
    UnityEngine.Color RenderPixel()
    {
        render.Invoke(window, null);
        var target = (UnityEngine.RenderTexture)preview.GetValue(window);
        Check(target != null, "preview render succeeded");
        UnityEngine.RenderTexture.active = target;
        sample.ReadPixels(new UnityEngine.Rect(target.width / 2, target.height / 2, 1, 1), 0, 0, false);
        UnityEngine.RenderTexture.active = previousTarget;
        return sample.GetPixel(0, 0);
    }
    Check(RenderPixel().r < .01f, "initial cached blur is black");
    requested.SetValue(window, false);
    string original = UnityEditor.EditorJsonUtility.ToJson(b);
    var liveType = assembly.GetType("DCFApixels.SpriteEditor.LiveOutputSession", true);
    session = (System.IDisposable)liveType.GetConstructor(instance, null, new[] { typeof(UnityEngine.Texture2D) }, null).Invoke(new object[] { a.OutputTexture });
    gpu = UnityEngine.RenderTexture.GetTemporary(16, 16, 0, UnityEngine.RenderTextureFormat.ARGBFloat, UnityEngine.RenderTextureReadWrite.Linear);
    UnityEngine.RenderTexture.active = gpu; UnityEngine.GL.Clear(false, true, UnityEngine.Color.red);
    UnityEngine.RenderTexture.active = previousTarget;
    liveType.GetMethod("Publish", instance).Invoke(session, new object[] { gpu });
    for (int i = 0; i < 5; i++) notify.Invoke(a, null);
    Check((bool)dirty.GetValue(window) && (bool)requested.GetValue(window), "notifications queue refresh");
    Check(UnityEditor.EditorJsonUtility.ToJson(b) == original, "notifications do not edit receiver");
    var red = RenderPixel();
    Check(red.r > .99f && red.g < .01f, "new GPU pixels reach File layer and cached blur");
    Check(!(bool)dirty.GetValue(window), "cache invalidation consumed once");
    session.Dispose(); session = null; notify.Invoke(a, null);
    Check(RenderPixel().r < .01f, "stop restores saved CPU pixels through receiver cache");
    var blue = new UnityEngine.Color[256];
    for (int i = 0; i < blue.Length; i++) blue[i] = UnityEngine.Color.blue;
    a.OutputTexture.SetPixels(blue); a.OutputTexture.Apply(); notify.Invoke(a, null);
    var saved = RenderPixel();
    Check(saved.b > .99f && saved.r < .01f, "updated CPU output reaches receiver");
    return "Output dependencies: " + checks + " Unity/GPU checks passed.";
}
finally
{
    session?.Dispose();
    UnityEngine.RenderTexture.active = previousTarget;
    if (gpu != null) UnityEngine.RenderTexture.ReleaseTemporary(gpu);
    if (window != null) window.Close();
    foreach (var doc in docs) if (doc != null) UnityEngine.Object.DestroyImmediate(doc);
    foreach (var texture in textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
    UnityEngine.Object.DestroyImmediate(sample);
    if (previousFocus != null) previousFocus.Focus();
}
