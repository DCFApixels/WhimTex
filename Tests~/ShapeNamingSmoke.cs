// Unity Pipeline eval_file. Transient documents only; no scene/asset or Undo changes.
var type = typeof(DCFApixels.SpriteEditor.TextureCompositor);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var allocate = type.GetMethod("AllocateLayerName", flags);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
DCFApixels.SpriteEditor.TextureCompositor restored = null;
int checks = 0;
string Add(DCFApixels.SpriteEditor.TextureCompositor doc, string kind, bool tool = true)
{
    var shape = new DCFApixels.SpriteEditor.ShapeLayerBehaviour
    { kind = (DCFApixels.SpriteEditor.ShapeLayerBehaviour.ShapeKind)System.Enum.Parse(typeof(DCFApixels.SpriteEditor.ShapeLayerBehaviour.ShapeKind), kind) };
    var layer = new DCFApixels.SpriteEditor.Layer(shape);
    layer.layerName = (string)allocate.Invoke(doc, new object[] { layer, tool ? kind : null });
    doc.layers.Add(layer);
    return layer.layerName;
}
void Check(string actual, string expected)
{
    if (actual != expected) throw new System.Exception("Expected " + expected + ", got " + actual);
    checks++;
}
try
{
    Check(Add(document, "Line"), "Line 1");
    Check(Add(document, "Rectangle", false), "Shape 2");
    Check(Add(document, "Star"), "Star 3");
    Check(Add(document, "Polygon"), "Polygon 4");
    Check(Add(document, "Ellipse"), "Ellipse 5");
    Check(Add(document, "Rectangle"), "Rectangle 6");
    var fill = new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.ColorFillLayerBehaviour());
    Check((string)allocate.Invoke(document, new object[] { fill, null }), "Color Fill 1");
    var group = new DCFApixels.SpriteEditor.GroupLayerBehaviour();
    document.layers.Add(new DCFApixels.SpriteEditor.Layer(group));
    group.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.ShapeLayerBehaviour
    { kind = DCFApixels.SpriteEditor.ShapeLayerBehaviour.ShapeKind.Star }) { layerName = "Line 40" });
    Check(Add(document, "Line"), "Line 41");
    group.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.ShapeLayerBehaviour()) { layerName = "Shape 45" });
    Check(Add(document, "Ellipse"), "Ellipse 46");
    restored = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
    UnityEditor.EditorJsonUtility.FromJsonOverwrite(UnityEditor.EditorJsonUtility.ToJson(document), restored);
    Check(Add(restored, "Polygon"), "Polygon 47");
    type.GetField("layerNameCounters", flags).SetValue(restored, null);
    Check(Add(restored, "Star"), "Star 48");
    document.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.FileLayerBehaviour()) { layerName = "Line 900" });
    Check(Add(document, "Line"), "Line 47");
    return "Shape naming passed: " + checks + " checks (shared counter, menu default, all kinds, nested/renamed kinds and serialization).";
}
finally
{
    UnityEngine.Object.DestroyImmediate(document);
    if (restored != null) UnityEngine.Object.DestroyImmediate(restored);
}
