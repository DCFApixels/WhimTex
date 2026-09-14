// Opt-in live-Editor eval after manual compilation. Transient documents only; no asset saves/imports.
var type = typeof(DCFApixels.WhimTex.TextureCompositor);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var method = type.GetMethod("MergeLayers", flags);
if (method == null) throw new Exception("Manually compile the merge implementation first.");
var documents = new List<DCFApixels.WhimTex.TextureCompositor>();
var owned = new List<UnityEngine.Object>();
int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
DCFApixels.WhimTex.TextureCompositor Doc(params DCFApixels.WhimTex.Layer[] layers)
{
    var doc = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
    doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 8;
    doc.layers.AddRange(layers); type.GetMethod("NormalizeModel", flags).Invoke(doc, null);
    documents.Add(doc); return doc;
}
DCFApixels.WhimTex.ColorFillLayerBehaviour Fill(Color color) => new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = color };
DCFApixels.WhimTex.DrawingLayerBehaviour Merge(DCFApixels.WhimTex.TextureCompositor doc, bool copy,
    params DCFApixels.WhimTex.Layer[] layers)
{
    var result = (DCFApixels.WhimTex.DrawingLayerBehaviour)method.Invoke(doc,
        new object[] { new List<DCFApixels.WhimTex.Layer>(layers), copy });
    owned.Add(result.GetPreviewTexture(8)); return result;
}
Color Pixel(DCFApixels.WhimTex.TextureCompositor doc)
{
    var pixels = doc.Compose();
    try { return pixels.GetPixel(4, 4); }
    finally { UnityEngine.Object.DestroyImmediate(pixels); }
}
void Near(Color a, Color b, string message) => Check(
    Mathf.Abs(a.r - b.r) < .015f && Mathf.Abs(a.g - b.g) < .015f &&
    Mathf.Abs(a.b - b.b) < .015f && Mathf.Abs(a.a - b.a) < .015f, message);
try
{
    var top = Fill(new Color(.8f, .2f, .4f, .5f)); var bottom = Fill(new Color(.2f, .5f, .7f, .6f));
    var doc = Doc(top, bottom); string originalId = top.Id;
    Color before = Pixel(doc);
    var merged = Merge(doc, false, bottom, top, top);
    Check(doc.layers.Count == 1 && doc.layers[0] == merged.Owner, "Replacement deduplicates and uses document order");
    Near(Pixel(doc), before, "Normal layers preserve their merged RGBA");
    Check(merged.modifiers.Count == 0 && merged.opacity == 1 && merged.transform.Equals(DCFApixels.WhimTex.TextureTransform.Default),
        "Result has baked settings and identity transform");
    Check(merged.GetPreviewTexture(8).format == TextureFormat.RGBAHalf, "Merged pixels retain half precision");
    Undo.PerformUndo();
    Check(doc.layers.Count == 2 && doc.layers[0].Id == originalId, "One Undo restores source structure");
    Near(Pixel(doc), before, "Undo restores source pixels");
    Undo.PerformRedo();
    Check(doc.layers.Count == 1 && doc.layers[0]?.Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour,
        "One Redo restores merged layer: count=" + doc.layers.Count + ", type=" + doc.layers[0]?.Behaviour?.GetType().Name);
    owned.Add(doc.layers[0].GetPreviewTexture(8));

    top = Fill(Color.red); bottom = Fill(Color.blue); doc = Doc(top, bottom);
    merged = Merge(doc, true, top, bottom);
    Check(doc.layers.Count == 3 && doc.layers[0] == merged.Owner && doc.layers[1] == top.Owner && doc.layers[2] == bottom.Owner,
        "Copy inserts above originals without removing them");
    Check(top.enabled && bottom.enabled && merged.Id != top.Id, "Copy retains source visibility and has an independent ID");
    Check(merged.layerName.Contains(" Copy "), "Copy uses the shared copy naming convention");

    var group = new DCFApixels.WhimTex.GroupLayerBehaviour { opacity = .4f };
    top = Fill(Color.red); bottom = Fill(Color.blue); group.layers.Add(top); group.layers.Add(bottom);
    doc = Doc(group); before = Pixel(doc);
    merged = Merge(doc, false, group, top);
    Check(doc.layers.Count == 1, "Selected group and selected child are merged once");
    Near(Pixel(doc), before, "Group opacity is baked once");

    group = new DCFApixels.WhimTex.GroupLayerBehaviour { opacity = .4f };
    top = Fill(Color.red); bottom = Fill(Color.blue); group.layers.Add(top); group.layers.Add(bottom);
    doc = Doc(group); before = Pixel(doc);
    merged = Merge(doc, false, top, bottom);
    Check(doc.layers[0] == group.Owner && group.layers.Count == 1 && group.layers[0] == merged.Owner, "Sibling merge stays inside its parent");
    Near(Pixel(doc), before, "Unselected parent opacity is not baked twice");

    var left = new DCFApixels.WhimTex.GroupLayerBehaviour(); var right = new DCFApixels.WhimTex.GroupLayerBehaviour();
    top = Fill(Color.red); bottom = Fill(Color.blue); var retained = Fill(Color.green);
    left.layers.Add(top); left.layers.Add(retained); right.layers.Add(bottom);
    doc = Doc(left, right); merged = Merge(doc, false, top, bottom);
    Check(doc.layers[0] == merged.Owner && left.layers.Count == 1 && left.layers[0] == retained.Owner && right.layers.Count == 0,
        "Cross-group merge retains unselected content and uses the common parent");

    top = Fill(Color.red); bottom = Fill(Color.blue);
    var effect = new DCFApixels.WhimTex.OutlineLayerBehaviour();
    doc = Doc(effect, top, bottom); Merge(doc, true, top, bottom);
    Check(effect.inputMode == DCFApixels.WhimTex.EffectInputMode.Specific && effect.TargetLayerId == top.Id,
        "Copy insertion does not steal an existing Previous input");
    merged = Merge(doc, false, top, bottom);
    Check(effect.TargetLayerId == merged.Id, "References to removed layers follow the merged result");

    var bright = Fill(new Color(2, .2f, .3f, 1)); bright.colorRange = DCFApixels.WhimTex.LayerColorRange.HDR;
    doc = Doc(bright); before = Pixel(doc); merged = Merge(doc, false, bright);
    Near(Pixel(doc), before, "HDR survives merging");
    Check(Pixel(doc).r > 1, "Merge ignores the UI color-input preference");
    int count = doc.layers.Count;
    try { Merge(doc, false, Fill(Color.white)); throw new Exception("Foreign selection was accepted"); }
    catch (System.Reflection.TargetInvocationException ex)
    { Check(ex.InnerException is InvalidOperationException && doc.layers.Count == count, "Invalid selection leaves document untouched"); }
    return $"Merge layers: {checks} checks passed. Persistent save/reopen remains a separate opt-in check.";
}
finally
{
    foreach (var texture in owned) if (texture != null) Undo.ClearUndo(texture);
    foreach (var doc in documents) { Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc); }
    foreach (var texture in owned) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
}
