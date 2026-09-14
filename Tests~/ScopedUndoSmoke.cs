// Opt-in eval body after manual compilation. Uses temporary objects and Editor Undo.
// Do not run during editing. No assets are saved; tool preferences are restored.

const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var documentType = typeof(DCFApixels.WhimTex.TextureCompositor);
var windowType = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var changed = documentType.GetEvent("Changed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
int checks = 0, firstNotifications = 0, secondNotifications = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}
int Begin(string name)
{
    Undo.FlushUndoRecordObjects();
    Undo.IncrementCurrentGroup();
    Undo.SetCurrentGroupName(name);
    return Undo.GetCurrentGroup();
}
void End(int group)
{
    Undo.FlushUndoRecordObjects();
    Undo.CollapseUndoOperations(group);
    Undo.IncrementCurrentGroup();
    firstNotifications = secondNotifications = 0;
}
const string Preferences = "DCFApixels.WhimTex.PaintToolSettings";
bool hadPreferences = EditorPrefs.HasKey(Preferences);
string preferences = EditorPrefs.GetString(Preferences, "");
var first = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
var second = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
var unrelated = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
Texture2D pixels = null;
DCFApixels.WhimTex.TextureCompositorWindow window = null;
DCFApixels.WhimTex.ShaderFX effect = null;
Action<DCFApixels.WhimTex.TextureCompositor> handler = document =>
{
    if (document == first) firstNotifications++;
    if (document == second) secondNotifications++;
};
changed.GetAddMethod(true).Invoke(null, new object[] { handler });
try
{
    first.hideFlags = second.hideFlags = HideFlags.HideAndDontSave;
    Call(first, "MarkChanged");
    Call(second, "MarkChanged");
    int group = Begin("Scoped document edit");
    Undo.RecordObject(first, "Scoped document edit");
    first.width = 123;
    Call(first, "MarkChanged");
    End(group);
    Undo.PerformUndo();
    Check(first.width == 512 && firstNotifications == 1 && secondNotifications == 0,
        "Undo refreshes only the changed document, once");
    firstNotifications = secondNotifications = 0;
    Undo.PerformRedo();
    Check(first.width == 123 && firstNotifications == 1 && secondNotifications == 0,
        "Redo refreshes only the changed document, once");

    group = Begin("Unrelated texture edit");
    Undo.RegisterCompleteObjectUndo(unrelated, "Unrelated texture edit");
    unrelated.name = "Unrelated change";
    End(group);
    Undo.PerformUndo();
    Check(firstNotifications == 0 && secondNotifications == 0,
        "Unrelated Undo does not notify either document");

    pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
    pixels.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
    pixels.Apply();
    var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour();
    typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetField("pixels", Hidden).SetValue(drawing, pixels);
    first.layers.Add(drawing);
    Call(first, "MarkChanged");
    group = Begin("Texture-only legacy edit");
    Undo.RegisterCompleteObjectUndo(pixels, "Texture-only legacy edit");
    pixels.SetPixel(0, 0, Color.green);
    pixels.Apply();
    EditorUtility.SetDirty(pixels);
    Call(first, "MarkChanged");
    End(group);
    Undo.PerformUndo();
    Check(pixels.GetPixel(0, 0).r > 0.99f && firstNotifications == 1 && secondNotifications == 0,
        "Texture-only Undo refreshes its owner without changing other documents");
    firstNotifications = secondNotifications = 0;
    Undo.PerformRedo();
    Check(pixels.GetPixel(0, 0).g > 0.99f && firstNotifications == 1 && secondNotifications == 0,
        "Texture-only Redo refreshes its owner once");

    effect = ScriptableObject.CreateInstance<DCFApixels.WhimTex.ShaderFX>();
    effect.hideFlags = HideFlags.HideAndDontSave;
    drawing.modifiers.Add(effect);
    Call(first, "MarkChanged");
    group = Begin("Scoped effect edit");
    Undo.RecordObject(effect, "Scoped effect edit");
    effect.name = "Changed effect";
    End(group);
    Undo.PerformUndo();
    Check(firstNotifications == 1 && secondNotifications == 0, "FX Undo notifies only consuming documents");

    window = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
    object settings = windowType.GetField("paintSettings", Hidden).GetValue(window);
    group = Begin("Edit before tool settings");
    Undo.RecordObject(first, "Edit before tool settings");
    first.height = 77;
    Call(first, "MarkChanged");
    End(group);
    string nextSettings = "{\"brushSize\":79,\"brushHardness\":0.3,\"brushSpacing\":0.42,\"fillTolerance\":91,\"fillContiguous\":false,\"fillAntialias\":true,\"fillExpand\":3,\"fillSampleMode\":1}";
    Action change = () => JsonUtility.FromJsonOverwrite(nextSettings, settings);
    Call(window, "ApplyPaintToolChange", change);
    string expectedSettings = JsonUtility.ToJson(settings);
    Undo.PerformUndo();
    Check(first.height == 512, "Tool settings do not insert an Undo step");
    Check(JsonUtility.ToJson(windowType.GetField("paintSettings", Hidden).GetValue(window)) == expectedSettings,
        "Tool settings remain unchanged after document Undo");
    Undo.PerformRedo();
    Check(first.height == 77, "Redo still restores the document edit");

    group = Begin("Window snapshot");
    Undo.RegisterCompleteObjectUndo(window, "Window snapshot");
    windowType.GetField("settingsPaneWidth", Hidden).SetValue(window, 444f);
    End(group);
    Call(window, "ApplyPaintToolChange", (Action)(() => settings.GetType().GetField("brushSize").SetValue(settings, 101f)));
    expectedSettings = JsonUtility.ToJson(settings);
    Undo.PerformUndo();
    Check(JsonUtility.ToJson(windowType.GetField("paintSettings", Hidden).GetValue(window)) == expectedSettings,
        "Window Undo snapshots cannot restore tool settings");
}
finally
{
    changed.GetRemoveMethod(true).Invoke(null, new object[] { handler });
    if (window != null)
    {
        Undo.ClearUndo(window);
        window.DiscardChanges();
        UnityEngine.Object.DestroyImmediate(window);
    }
    Undo.ClearUndo(first);
    Undo.ClearUndo(second);
    Undo.ClearUndo(unrelated);
    if (pixels != null) Undo.ClearUndo(pixels);
    if (effect != null) Undo.ClearUndo(effect);
    UnityEngine.Object.DestroyImmediate(first);
    UnityEngine.Object.DestroyImmediate(second);
    UnityEngine.Object.DestroyImmediate(unrelated);
    if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
    if (effect != null) UnityEngine.Object.DestroyImmediate(effect);
    if (hadPreferences) EditorPrefs.SetString(Preferences, preferences);
    else EditorPrefs.DeleteKey(Preferences);
}
return $"Scoped Undo checks passed: {checks}.";
