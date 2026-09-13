// Run with Unity Pipeline eval_file. Only transient documents; no user assets are changed.
const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var docType = typeof(DCFApixels.SpriteEditor.TextureCompositor);
var source = ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
var destination = ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
DCFApixels.SpriteEditor.TextureCompositor snapshot = null;
DCFApixels.SpriteEditor.TextureCompositor orphanSnapshot = null;
var clipboard = docType.Assembly.GetType("DCFApixels.SpriteEditor.LayerClipboard", true);
string savedClipboard = GUIUtility.systemCopyBuffer;
Undo.IncrementCurrentGroup();
int testGroup = Undo.GetCurrentGroup();
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Flags).Invoke(owner, args);
System.Collections.Generic.List<DCFApixels.SpriteEditor.Layer> Layers(params DCFApixels.SpriteEditor.Layer[] values) => new System.Collections.Generic.List<DCFApixels.SpriteEditor.Layer>(values);
Texture2D Pixels(DCFApixels.SpriteEditor.Layer layer) => (Texture2D)layer.Behaviour.GetType().GetProperty("StoredTexture", Flags).GetValue(layer.Behaviour);
try
{
    source.hideFlags = destination.hideFlags = HideFlags.HideAndDontSave;
    source.width = 16; source.height = 8;
    destination.width = 32; destination.height = 32;
    var texture = new Texture2D(4, 2, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
    var colors = new Color[] { new Color(1, .25f, 2, .5f), Color.red, Color.green, Color.blue, Color.clear, Color.white, Color.black, Color.yellow };
    texture.SetPixels(colors); texture.Apply(false, false);
    var drawing = (DCFApixels.SpriteEditor.DrawingLayerBehaviour)typeof(DCFApixels.SpriteEditor.DrawingLayerBehaviour).GetMethod("FromMergedTexture", Flags).Invoke(null, new object[] { texture });
    DCFApixels.SpriteEditor.Layer paint = drawing;
    paint.layerName = "Paint"; paint.opacity = .6f; paint.enabled = false;
    paint.colorRange = DCFApixels.SpriteEditor.LayerColorRange.HDR;
    paint.transform.position = new Vector2(.1f, .2f);
    paint.transform.scale = new Vector2(.75f, .5f);
    var paintSurface = (RenderTexture)Call(drawing, "EnsurePaintSurface", 16, 8);
    var previousRenderTarget = RenderTexture.active;
    try
    {
        RenderTexture.active = paintSurface;
        GL.Clear(false, true, new Color(2, .125f, 1, .5f));
        drawing.GetType().GetField("paintSurfaceDirty", Flags).SetValue(drawing, true);
    }
    finally { RenderTexture.active = previousRenderTarget; }
    Check(texture.GetPixel(0, 0).r == 1, "Test contains a fresh GPU stroke not yet in stored pixels");
    var blur = new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.BlurLayerBehaviour { radius = 9 });
    blur.layerName = "Blur";
    var group = new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.GroupLayerBehaviour());
    group.layerName = "Group"; group.children = Layers(blur, paint);
    source.layers = Layers(group);
    Call(source, "NormalizeModel");
    var effect = (DCFApixels.SpriteEditor.ShaderFX)Call(source, "AddEmbeddedShaderFX", paint);
    Call(effect, "SetDraftCode", "float4 ApplyFX(float2 uv, float4 color) { return color; }");
    Check((bool)Call(effect, "Apply"), "Inline FX compiles before copying");
    string originalCode = (string)effect.GetType().GetField("code", Flags).GetValue(effect);
    string originalId = paint.Id;
    int copyUndoGroup = Undo.GetCurrentGroup();
    snapshot = (DCFApixels.SpriteEditor.TextureCompositor)Call(source, "CaptureLayerClipboard", Layers(group, paint));
    Check(Undo.GetCurrentGroup() == copyUndoGroup, "Copy does not add Undo entries");
    Check(snapshot.layers.Count == 1 && snapshot.layers[0].children.Count == 2, "Group and selected child copied once");
    var snapshotPaint = snapshot.layers[0].children[1];
    var snapshotBlur = snapshot.layers[0].children[0];
    Check(snapshotPaint.Id != originalId, "Snapshot identities are independent");
    Check(Pixels(snapshotPaint) != texture, "Snapshot owns its pixels");
    Check(Pixels(snapshotPaint).width == 4 && Pixels(snapshotPaint).height == 2, "Original Drawing resolution preserved");
    Check(Pixels(snapshotPaint).GetPixel(0, 0).r == 4, "HDR pixels preserved");
    Check(snapshotPaint.modifiers[0] != effect, "Embedded FX copied independently");
    Check((string)effect.GetType().GetField("code", Flags).GetValue(snapshotPaint.modifiers[0]) == originalCode, "FX code preserved");
    Check(snapshotPaint.layerName == "Paint" && snapshotPaint.opacity == .6f && !snapshotPaint.enabled, "Common settings preserved");
    Check(snapshotPaint.transform.position == paint.transform.position && snapshotPaint.transform.scale == paint.transform.scale, "Transform preserved");
    Check(((DCFApixels.SpriteEditor.BlurLayerBehaviour)snapshotBlur.Behaviour).radius == 9, "Behaviour settings preserved");

    texture.SetPixel(0, 0, Color.black); texture.Apply(false, false);
    Call(effect, "SetDraftCode", "changed after copy");
    Check(Pixels(snapshotPaint).GetPixel(0, 0).r == 4, "Later source paint does not change clipboard");
    Check((string)effect.GetType().GetField("code", Flags).GetValue(snapshotPaint.modifiers[0]) == originalCode, "Later source FX edits do not change clipboard");

    // A Previous effect copied without its source must not bind to unrelated destination layers.
    orphanSnapshot = (DCFApixels.SpriteEditor.TextureCompositor)Call(source, "CaptureLayerClipboard", Layers(blur));
    var orphanEffect = orphanSnapshot.layers[0].Behaviour;
    Check(orphanEffect.GetType().GetField("inputMode", Flags).GetValue(orphanEffect).ToString() == "Specific", "Uncopied Previous target becomes explicit missing target");
    Check(string.IsNullOrEmpty((string)orphanEffect.GetType().GetProperty("TargetLayerId", Flags).GetValue(orphanEffect)), "Uncopied target cleared");

    // Specific targets inside the copied set follow the new identities.
    var targetProperty = blur.Behaviour.GetType().GetProperty("TargetLayerId", Flags);
    targetProperty.SetValue(blur.Behaviour, paint.Id);
    var inputField = blur.Behaviour.GetType().GetField("inputMode", Flags);
    inputField.SetValue(blur.Behaviour, Enum.Parse(inputField.FieldType, "Specific"));
    UnityEngine.Object.DestroyImmediate(orphanSnapshot);
    orphanSnapshot = (DCFApixels.SpriteEditor.TextureCompositor)Call(source, "CaptureLayerClipboard", Layers(group));
    var remappedBlur = orphanSnapshot.layers[0].children[0].Behaviour;
    Check((string)targetProperty.GetValue(remappedBlur) == orphanSnapshot.layers[0].children[1].Id, "Internal Specific target remapped");

    UnityEngine.Object.DestroyImmediate(source); source = null;
    Check(Pixels(snapshotPaint) != null && Pixels(snapshotPaint).GetPixel(0, 0).r == 4, "Clipboard survives source document destruction");
    Call(destination, "PasteLayers", snapshot);
    Check(destination.layers.Count == 1 && destination.layers[0].layerName == "Group", "Paste inserts original named root");
    var pasted = destination.layers[0].children[1];
    Check(pasted.Id != snapshotPaint.Id, "Paste generates new IDs");
    Check(Pixels(pasted) != Pixels(snapshotPaint) && Pixels(pasted).GetPixel(0, 0).r == 4, "Paste owns independent HDR pixels");
    Check(Pixels(pasted).width == 4 && Pixels(pasted).height == 2, "Different canvas does not resize stored pixels");
    Check(pasted.modifiers[0] != snapshotPaint.modifiers[0], "Paste owns its embedded FX");
    Check((DCFApixels.SpriteEditor.TextureCompositor)pasted.modifiers[0].GetType().GetProperty("EmbeddedOwner", Flags).GetValue(pasted.modifiers[0]) == destination, "Pasted FX owned by destination");
    string firstPasteId = destination.layers[0].Id;
    Undo.PerformUndo();
    Check(destination.layers.Count == 0, "One Undo removes entire paste");
    Undo.PerformRedo();
    Check(destination.layers.Count == 1 && destination.layers[0].Id == firstPasteId, "Redo restores pasted group identity");
    Check(Pixels(destination.layers[0].children[1]).GetPixel(0, 0).r == 4, "Redo restores Drawing pixels");
    Call(destination, "PasteLayers", snapshot);
    Check(destination.layers.Count == 2 && destination.layers[0].Id != destination.layers[1].Id, "Repeated paste creates unique copies");
    Check(Pixels(destination.layers[0].children[1]) != Pixels(destination.layers[1].children[1]), "Repeated pastes do not share Drawing storage");
    Call(destination, "DuplicateLayers", Layers(destination.layers[0]));
    Check(destination.layers.Count == 3, "Existing Duplicate still works");
    Check(destination.layers[0].layerName != destination.layers[1].layerName, "Duplicate still allocates Copy name");

    clipboard.GetMethod("Copy", Flags).Invoke(null, new object[] { destination, Layers(destination.layers[0]) });
    var current = (DCFApixels.SpriteEditor.TextureCompositor)clipboard.GetProperty("Current", Flags).GetValue(null);
    Check(current != null, "Layer clipboard becomes available across windows");
    var ownedPixels = Pixels(current.layers[0].children[1]);
    GUIUtility.systemCopyBuffer = "new external text";
    Check(clipboard.GetProperty("Current", Flags).GetValue(null) == null, "External copy invalidates layer clipboard");
    Check(current == null && ownedPixels == null, "Invalidated clipboard releases owned resources");
    return "Layer clipboard smoke passed: " + checks + " checks";
}
finally
{
    clipboard.GetMethod("Clear", Flags).Invoke(null, null);
    GUIUtility.systemCopyBuffer = savedClipboard;
    Undo.RevertAllDownToGroup(testGroup);
    if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
    if (orphanSnapshot != null) UnityEngine.Object.DestroyImmediate(orphanSnapshot);
    if (source != null) UnityEngine.Object.DestroyImmediate(source);
    if (destination != null) UnityEngine.Object.DestroyImmediate(destination);
    Undo.IncrementCurrentGroup();
}
