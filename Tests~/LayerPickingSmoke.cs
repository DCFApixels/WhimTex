// Run through Unity Pipeline eval_file. Transient document only; no user windows, assets or preferences are changed.
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 32;
var method = document.GetType().GetMethod("PickLayerAtPixel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new System.Exception(message); checks++; }
DCFApixels.SpriteEditor.Layer Pick(int x = 16, int y = 16, float threshold = .1f, bool deep = false, DCFApixels.SpriteEditor.Layer selected = null) =>
    (DCFApixels.SpriteEditor.Layer)method.Invoke(document, new object[] { x, y, threshold, deep, selected });
DCFApixels.SpriteEditor.Layer NewLayer(DCFApixels.SpriteEditor.LayerBehaviour behaviour)
{
    var layer = new DCFApixels.SpriteEditor.Layer(behaviour);
    typeof(DCFApixels.SpriteEditor.Layer).GetMethod("AssignNewId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(layer, null);
    return layer;
}
DCFApixels.SpriteEditor.Layer Fill(float alpha) => NewLayer(new DCFApixels.SpriteEditor.ColorFillLayerBehaviour
    { color = new UnityEngine.Color(1f, 1f, 1f, alpha) });
var back = Fill(1f); var front = Fill(.1f);
document.layers.Add(front); document.layers.Add(back);
try
{
    Check(Pick() == front, "10% inclusive, including half-float roundoff");
    Check(Pick(threshold: .11f) == back, "alpha below threshold passes through");
    front.enabled = false; Check(Pick() == back, "hidden layer ignored"); front.enabled = true;
    front.opacity = .5f; Check(Pick() == back, "layer opacity included"); front.opacity = 1f;
    front.blendMode = DCFApixels.SpriteEditor.BlendMode.None; Check(Pick() == back, "None blend ignored");
    front.blendMode = DCFApixels.SpriteEditor.BlendMode.Normal;
    Check(Pick(-1, 0) == null && Pick(32, 0) == null, "outside canvas");
    ((DCFApixels.SpriteEditor.ColorFillLayerBehaviour)front.Behaviour).color = UnityEngine.Color.clear;
    Check(Pick(threshold: 0f) == back, "zero threshold still skips transparent pixels");
    front.swizzle = new DCFApixels.SpriteEditor.LayerSwizzle();
    ((DCFApixels.SpriteEditor.ColorFillLayerBehaviour)front.Behaviour).color = UnityEngine.Color.white;
    var transform = DCFApixels.SpriteEditor.TextureTransform.Default;
    transform.scale = new UnityEngine.Vector2(.25f, .5f); transform.rotation = 90f;
    front.transform = transform;
    Check(Pick() == front && Pick(2, 2) == back, "transformed shape coverage");
    Check(Pick(threshold: 1f) == front, "fully opaque at 100%");
    var swizzle = new DCFApixels.SpriteEditor.LayerSwizzle();
    swizzle[3] = DCFApixels.SpriteEditor.SwizzleChannel.Zero; front.swizzle = swizzle;
    Check(Pick() == back, "Swizzle alpha is part of coverage"); front.swizzle = default;
    front.transform = DCFApixels.SpriteEditor.TextureTransform.Default;
    var group = NewLayer(new DCFApixels.SpriteEditor.GroupLayerBehaviour());
    ((DCFApixels.SpriteEditor.GroupLayerBehaviour)group.Behaviour).layers.Add(front);
    document.layers[0] = group;
    Check(Pick() == group, "default picks group");
    Check(Pick(deep: true) == front, "Ctrl picks nested child");
    group.opacity = .05f;
    Check(Pick(deep: true) == back, "group opacity gates nested children");
    group.opacity = .5f; front.opacity = .15f;
    Check(Pick(deep: true) == back, "parent and child opacity multiply");
    front.opacity = .2f; Check(Pick(deep: true) == front, "combined 10% inclusive");
    group.enabled = false; Check(Pick(deep: true) == back, "hidden parent gates children"); group.enabled = true;
    group.opacity = front.opacity = 1f;
    group.clippingMask = true;
    var mask = Fill(.5f);
    document.layers.Insert(1, mask);
    front.opacity = .15f;
    Check(Pick(deep: true) == mask, "clipped group applies base alpha to descendants");
    Check(Pick(selected: group) == mask, "repeat group picking includes clipping alpha");
    front.opacity = .2f; Check(Pick(deep: true) == front, "clipped group exact threshold");
    Check(Pick(selected: group) == front, "repeat group picking includes exact clipped threshold");
    mask.enabled = false; Check(Pick(deep: true) == back, "hidden clipping base suppresses chain"); mask.enabled = true;
    mask.transform = transform; Check(Pick(2, 2, deep: true) == back, "clipped group outside base shape");
    mask.transform = DCFApixels.SpriteEditor.TextureTransform.Default;
    mask.opacity = .1f; Check(Pick(deep: true) == back, "base opacity included"); mask.opacity = 1f;
    group.clippingMask = false; document.layers.Remove(mask);
    front.opacity = 1f;
    var childGroup = NewLayer(new DCFApixels.SpriteEditor.GroupLayerBehaviour());
    ((DCFApixels.SpriteEditor.GroupLayerBehaviour)childGroup.Behaviour).layers.Add(front);
    ((DCFApixels.SpriteEditor.GroupLayerBehaviour)group.Behaviour).layers[0] = childGroup;
    Check(Pick(deep: true) == front && Pick() == group, "nested groups");
    Check(Pick(selected: group) == childGroup, "repeat click enters only one group level");
    Check(Pick(selected: childGroup) == front, "next click enters nested group");
    Check(Pick(selected: front) == front, "selected child does not bounce back to its parent");
    Check(Pick(selected: back) == group, "unrelated selection does not enter group");
    Check(Pick(selected: NewLayer(new DCFApixels.SpriteEditor.GroupLayerBehaviour())) == group, "detached selection ignored");
    front.enabled = false;
    Check(Pick(selected: childGroup) == back, "repeat picking skips hidden content"); front.enabled = true;
    group.opacity = .05f;
    Check(Pick(selected: childGroup) == back, "repeat picking respects ancestor opacity"); group.opacity = 1f;
    front.clippingMask = true; Check(Pick(deep: true) == back, "orphan clip is invisible"); front.clippingMask = false;
    string before = UnityEditor.EditorJsonUtility.ToJson(document);
    for (int i = 0; i < 5; i++) Check(Pick(deep: true) == front, "repeatable picking");
    Check(before == UnityEditor.EditorJsonUtility.ToJson(document), "picking does not mutate document");
    var outline = new DCFApixels.SpriteEditor.OutlineLayerBehaviour { inputMode = DCFApixels.SpriteEditor.EffectInputMode.Specific,
        TargetLayerId = front.Id, outlineWidth = 3f, outlineSoftness = 0f };
    var outlineLayer = NewLayer(outline);
    document.layers.Insert(0, outlineLayer);
    front.transform = transform; front.enabled = false;
    Check(Pick(6, 16) == outlineLayer, "effect pixels outside a hidden target are selectable");
    outlineLayer.enabled = false; Check(Pick(6, 16) == back, "hidden effect is ignored");
    document.layers.Remove(outlineLayer);
    front.enabled = true; front.transform = DCFApixels.SpriteEditor.TextureTransform.Default;
    document.layers.Clear(); Check(Pick() == null, "empty document");
    document.layers.Add(group); document.layers.Add(back);
    return "Layer picking: " + checks + " GPU checks passed.";
}
finally { UnityEngine.Object.DestroyImmediate(document); }
