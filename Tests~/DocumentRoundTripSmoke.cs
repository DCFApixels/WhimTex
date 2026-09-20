// Document round trip: build a real document, save it as a native image carrier, load it back and compare
// both the model and the rendered composite. Proves the new storage format end to end.
// Run with Unity Pipeline eval_file; the report is returned as the result string.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var report = new System.Text.StringBuilder();
int checks = 0;
string phase = "setup";
void Check(bool ok, string message) { if (!ok) throw new System.Exception("FAIL[" + phase + "]: " + message); checks++; }
object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
object Field(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
void SetField(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
System.Collections.Generic.List<UnityEngine.Color> Sample(UnityEngine.Texture2D image)
{
    var samples = new System.Collections.Generic.List<UnityEngine.Color>();
    for (int y = 0; y < image.height; y += 2)
        for (int x = 0; x < image.width; x += 2)
            samples.Add(image.GetPixel(x, y));
    return samples;
}

// --- build a document with several behaviour types, a group, an effect target and a drawing layer ---
var doc = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.width = doc.height = 8;
var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour { brushColor = UnityEngine.Color.red, brushSize = 8, brushHardness = 1f };
Call(drawing, "PaintPoint", new UnityEngine.Vector2(.5f, .5f), 8, 8, Call(drawing, "GetStrokeParameters", false));
Call(drawing, "SyncSurfaceToTexture");
doc.layers.Add(drawing);
Call(doc, "NormalizeModel");
var group = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.GroupLayerBehaviour()) { layerName = "Saved group", opacity = .7f };
group.transform.rotation = 37;
group.children.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = UnityEngine.Color.green }));
doc.layers.Add(group);
var outline = new DCFApixels.WhimTex.OutlineLayerBehaviour
{
    inputMode = DCFApixels.WhimTex.EffectInputMode.Specific, TargetLayerId = drawing.Id
};
doc.layers.Add(new DCFApixels.WhimTex.Layer(outline) { layerName = "Outline" });
Call(doc, "NormalizeModel");
var effect = (DCFApixels.WhimTex.ShaderFX)Call(doc, "AddEmbeddedShaderFX", group);
Call(effect, "ApplyAgentDraft");
Call(doc, "MarkChanged");
string[] layerIds = System.Array.ConvertAll(doc.layers.ToArray(), layer => layer.Id);
report.Append("layers=").Append(doc.layers.Count).Append(" drawingPixels=").Append(Field(drawing, "pixels") != null);

// --- save into the new format ---
phase = "save";
var before = doc.Compose();
var beforeSamples = Sample(before);
UnityEngine.Object.DestroyImmediate(before);
string dir = "Assets/WhimTexSpike";
System.IO.Directory.CreateDirectory(dir);
string path = DCFApixels.WhimTex.WhimTexDocumentFile.Save(doc, dir + "/roundtrip-test");
report.Append(" || carrier=").Append(System.IO.Path.GetExtension(path));
Check(System.IO.File.Exists(path), "the carrier file exists");
Check(DCFApixels.WhimTex.WhimTexDocumentFile.IsDocument(path), "the file is recognised as a document");
var imported = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
Check(imported != null, "Unity imported the carrier as a texture");
Check(imported.width == 8 && imported.height == 8, "the carrier is the composite at document size, got " + (imported == null ? "-" : imported.width + "x" + imported.height));
Check(UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter, "the carrier is owned by the native TextureImporter");
var boundComposite = Field(doc, "outputTexture") as UnityEngine.Texture2D;
Check(boundComposite != null && boundComposite == imported, "the saved document is bound to the file image");
Check(UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).Length == 1 || !System.Array.Exists(UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path),
    asset => asset is DCFApixels.WhimTex.TextureCompositor), "the document is not stored as a sub-asset");

// --- load it back ---
phase = "load";
Check(DCFApixels.WhimTex.WhimTexDocumentFile.TryLoad(path, out DCFApixels.WhimTex.TextureCompositor loaded, out string loadError),
    "document loads: " + loadError);
Check(loaded != null && loaded != doc, "a fresh document instance was reconstructed");
Check(!UnityEditor.AssetDatabase.Contains(loaded), "the loaded document is not an asset");
Check(loaded.width == 8 && loaded.height == 8, "canvas size survives");
Check(loaded.layers.Count == doc.layers.Count, "layer count survives, got " + loaded.layers.Count);
for (int i = 0; i < layerIds.Length; i++)
    Check(loaded.layers[i].Id == layerIds[i], "layer id survives at " + i);
Check(loaded.layers[1].layerName == "Saved group" && System.Math.Abs(loaded.layers[1].opacity - .7f) < .0001f, "name and opacity survive");
Check(System.Math.Abs(loaded.layers[1].transform.rotation - 37d) < 1e-9, "double precision rotation survives");
Check(loaded.layers[1].children.Count == 1 && loaded.layers[1].children[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "child layer survives");
Check(loaded.layers[2].Behaviour is DCFApixels.WhimTex.OutlineLayerBehaviour outlineLoaded &&
    outlineLoaded.TargetLayerId == drawing.Id, "effect target layer id survives");
DCFApixels.WhimTex.Layer drawingLoaded = loaded.layers[0];
Check(drawingLoaded.Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour, "drawing behaviour type survives");
var drawingLoadedBehaviour = (DCFApixels.WhimTex.DrawingLayerBehaviour)drawingLoaded.Behaviour;
// GetPreviewTexture returns a texture owned by the layer: reading pixels is fine, destroying it breaks rendering.
var sourcePreview = drawing.GetPreviewTexture(8);
var loadedPreview = drawingLoadedBehaviour.GetPreviewTexture(8);
Check(loadedPreview != null, "drawing pixels were restored from a container block");
Check(sourcePreview.GetPixel(4, 4).r > .9f && loadedPreview.GetPixel(4, 4).r > .9f, "drawing pixels match after the round trip");
Check(System.Math.Abs(sourcePreview.GetPixel(4, 4).a - loadedPreview.GetPixel(4, 4).a) < .01f, "drawing alpha matches");

// --- object identity: one effect, one instance ---
phase = "identity";
var modifiers = (System.Collections.Generic.List<UnityEngine.Object>)loaded.layers[1].modifiers;
Check(modifiers != null && modifiers.Count == 1 && modifiers[0] is DCFApixels.WhimTex.ShaderFX, "embedded effect survives on its layer");
var embeddedList = (System.Collections.Generic.IList<DCFApixels.WhimTex.ShaderFX>)Field(loaded, "embeddedShaderFX");
Check(embeddedList != null && embeddedList.Count == 1, "embedded effect list survives, got " + (embeddedList == null ? "-" : embeddedList.Count.ToString()));
Check(ReferenceEquals(embeddedList[0], modifiers[0]), "the effect is one shared instance, not two copies");

// --- the composite renders the same ---
phase = "compose";
var after = loaded.Compose();
var afterSamples = Sample(after);
UnityEngine.Object.DestroyImmediate(after);
Check(beforeSamples.Count == afterSamples.Count, "composite sample count");
float worst = 0f;
for (int i = 0; i < beforeSamples.Count; i++)
{
    var a = beforeSamples[i];
    var b = afterSamples[i];
    worst = System.Math.Max(worst, System.Math.Max(System.Math.Abs(a.r - b.r),
        System.Math.Max(System.Math.Abs(a.g - b.g), System.Math.Max(System.Math.Abs(a.b - b.b), System.Math.Abs(a.a - b.a)))));
}
Check(worst < .01f, "composite matches after the round trip, worst channel delta " + worst);
report.Append(" || worstDelta=").Append(worst.ToString("F5"));

// --- plain images are not documents, and the old path is untouched ---
phase = "guards";
var plain = new UnityEngine.Texture2D(4, 4, UnityEngine.TextureFormat.RGBA32, false, false);
plain.Apply();
System.IO.File.WriteAllBytes(dir + "/plain.png", plain.EncodeToPNG());
UnityEditor.AssetDatabase.Refresh();
Check(!DCFApixels.WhimTex.WhimTexDocumentFile.IsDocument(dir + "/plain.png"), "a plain PNG is not a document");
Check(!DCFApixels.WhimTex.WhimTexDocumentFile.TryLoad(dir + "/plain.png", out _, out string plainError) && !string.IsNullOrEmpty(plainError),
    "loading a plain PNG fails with a reason");
Check(!DCFApixels.WhimTex.WhimTexDocumentFile.TryLoad(dir + "/missing.png", out _, out _), "a missing file fails cleanly");
Check(DCFApixels.WhimTex.WhimTexDocumentFile.Save(doc, dir + "/roundtrip-second").EndsWith("roundtrip-second" + System.IO.Path.GetExtension(path)),
    "saving twice keeps the same carrier extension");
var second = DCFApixels.WhimTex.WhimTexDocumentFile.Load(dir + "/roundtrip-second" + System.IO.Path.GetExtension(path));
Check(second != null && second.layers.Count == doc.layers.Count, "the second save also loads");
return "PASS: document round trip checks=" + checks + ", " + report;
