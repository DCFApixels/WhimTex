// Pipeline eval_file. Transient objects only: no project assets, scenes, imports or Undo changes.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var doc = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.hideFlags = HideFlags.HideAndDontSave;
doc.width = doc.height = 8;
var temporary = new List<UnityEngine.Object> { doc };
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
var layerType = typeof(DCFApixels.WhimTex.Layer);
var behaviourType = typeof(DCFApixels.WhimTex.LayerBehaviour);
object Value(object instance, Type type, string name) => type.GetField(name, Hidden).GetValue(instance);
DCFApixels.WhimTex.Layer Find(DCFApixels.WhimTex.TextureCompositor document, string id) =>
    (DCFApixels.WhimTex.Layer)document.GetType().GetMethod("FindLayer", Hidden).Invoke(document, new object[] { id });
try
{
    var child = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.red });
    var group = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.GroupLayerBehaviour()) { layerName = "Keep group", opacity = .7f };
    group.children.Add(child);
    doc.layers.Add(group);
    typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("NormalizeModel", Hidden).Invoke(doc, null);
    string id = child.Id;
    child.layerName = "Keep name";
    child.opacity = .35f;
    child.transform.rotation = 25;
    child.swizzle[0] = DCFApixels.WhimTex.SwizzleChannel.B;
    var previous = child.Behaviour;
    var next = new DCFApixels.WhimTex.NoiseLayerBehaviour { seed = 913 };
    child.SetBehaviour(next);
    Check(ReferenceEquals(Find(doc, id), child), "Replacement retains node identity and lookup");
    Check(child.Id == id && child.layerName == "Keep name" && child.opacity == .35f && child.transform.rotation == 25,
        "Replacement retains common settings");
    Check(ReferenceEquals(next.Owner, child), "Behaviour is bound to its wrapper");
    bool rejected = false;
    try { new DCFApixels.WhimTex.Layer(next); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "A behaviour cannot belong to two layers");

    var json = JsonUtility.ToJson(group);
    var copy = JsonUtility.FromJson<DCFApixels.WhimTex.Layer>(json);
    Check(copy.Behaviour is DCFApixels.WhimTex.GroupLayerBehaviour && copy.children.Count == 1, "JSON restores group structure");
    Check(copy.children[0].Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour noise && noise.seed == 913, "JSON restores polymorphic content");
    Check(copy.children[0].layerName == child.layerName && copy.children[0].opacity == child.opacity, "JSON restores wrapper settings");
    Check(ReferenceEquals(copy.children[0].Behaviour.Owner, copy.children[0]), "Deserialized behaviour binds to cloned wrapper");
    Check(!ReferenceEquals(copy.children[0].Behaviour, next), "Clone does not share behaviours");
    var clone = UnityEngine.Object.Instantiate(doc);
    temporary.Add(clone);
    Check(clone.layers[0].children[0].Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour n && n.seed == 913, "Native Unity clone preserves behaviour");

    string recoveryId = (string)Value(group, layerType, "behaviourId");
    group.SetBehaviour(null);
    Check(group.children[0] == child && group.layerName == "Keep group", "Missing group retains identity and descendants");
    Check((string)Value(group, layerType, "behaviourId") == recoveryId, "Missing behaviour retains its recovery link");
    rejected = false;
    try { group.SetBehaviour(new DCFApixels.WhimTex.FileLayerBehaviour()); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected && group.children.Count == 1, "Replacing a populated group cannot silently erase its children");
    var missingClone = UnityEngine.Object.Instantiate(doc);
    temporary.Add(missingClone);
    Check(missingClone.layers[0].Behaviour == null && Find(missingClone, id) != null, "Missing group remains traversable after serialization");
    var image = doc.Compose(); temporary.Add(image);
    Check(image.GetPixel(4,4).a == 0, "Missing group does not render descendants implicitly");
    group.SetBehaviour(new DCFApixels.WhimTex.GroupLayerBehaviour());
    Check(group.children[0] == child && group.opacity == .7f, "Group recovery preserves children and compositing settings");

    var recovery = layerType.Assembly.GetType("DCFApixels.WhimTex.MissingLayerRecovery");
    var recordType = recovery.GetNestedType("Record", Hidden);
    var record = Activator.CreateInstance(recordType, true);
    var parse = layerType.Assembly.GetType("DCFApixels.WhimTex.MissingLayerData").GetMethod("Parse", Hidden);
    recordType.GetField("Data", Hidden).SetValue(record, parse.Invoke(null, new object[] {
        "recoveryId: 00000000000000000000000000000012\ntargetLayerId: target\nradius: 51.4\nstrength: 3.413\nlayerName: must-not-copy\noldSetting: 9\n" }));
    var blur = new DCFApixels.WhimTex.BlurLayerBehaviour();
    string freshId = (string)Value(blur, behaviourType, "recoveryId");
    var report = recovery.GetMethod("Copy", Hidden).Invoke(null, new object[] { record, blur, doc });
    Check(blur.TargetLayerId == "target" && Mathf.Abs(blur.radius - 51.4f) < .0001f && Mathf.Abs(blur.strength - 3.413f) < .0001f,
        "Recovery transfers compatible public and inherited private behaviour fields");
    Check((string)Value(blur, behaviourType, "recoveryId") == freshId && blur.layerName != "must-not-copy", "Recovery cannot overwrite identity/common settings");
    var skipped = (List<string>)report.GetType().GetField("Skipped", Hidden).GetValue(report);
    Check(skipped.Contains("layerName") && skipped.Contains("oldSetting"), "Incompatible fields reported");

    // Exercise content adoption used by agent completion and rasterization on the actual C# implementation.
    var pixels = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
    temporary.Add(pixels);
    var prepared = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.DrawingLayerBehaviour());
    var drawing = (DCFApixels.WhimTex.DrawingLayerBehaviour)prepared.Behaviour;
    typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetField("pixels", Hidden).SetValue(drawing, pixels);
    prepared.transform.rotation = 73;
    child.enabled = false;
    layerType.GetMethod("AdoptContent", Hidden).Invoke(child, new object[] { prepared });
    Check(ReferenceEquals(Find(doc, id), child) && child.Id == id && child.layerName == "Keep name" && !child.enabled,
        "Adoption keeps wrapper identity and user-owned name/visibility");
    Check(ReferenceEquals(child.Behaviour, drawing) && ReferenceEquals(drawing.Owner, child) && child.transform.rotation == 73,
        "Adoption transfers prepared behaviour and transform");
    Check(prepared.Behaviour == null, "Adoption detaches the donor instead of sharing its behaviour");
    return new { success = true, checks };
}
finally { for (int i = temporary.Count - 1; i >= 0; i--) if (temporary[i] != null) UnityEngine.Object.DestroyImmediate(temporary[i]); }
