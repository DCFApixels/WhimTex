var windowType = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var type = windowType.Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
var settings = System.Activator.CreateInstance(type, true);
int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new System.Exception(message);
    checks++;
}
T Read<T>(object source, string name) => (T)type.GetField(name).GetValue(source);
var first = new DCFApixels.WhimTex.DrawingLayerBehaviour();
var second = new DCFApixels.WhimTex.DrawingLayerBehaviour();
first.brushSize = 9;
second.brushSize = 91;
first.repeatCount = 3;
second.repeatCount = 8;
string beforeFirst = UnityEngine.JsonUtility.ToJson(first);
string beforeSecond = UnityEngine.JsonUtility.ToJson(second);
Check(Read<float>(settings, "brushSize") == 32f, "Shared brush starts with its own default size");
Check(Read<int>(settings, "pencilSize") == 1, "Pencil starts at one pixel");
Check(type.GetField("pencilShape").GetValue(settings).ToString() == "Circle", "Pencil starts with a circular tip");
var sizeStep = type.GetMethod("GetSizeShortcutStep", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
foreach (int size in new[] { 1, 5, 15, 19, 20, 50, 100, 500, 4096 })
{
    float actual = (float)sizeStep.Invoke(null, new object[] { (float)size });
    Check(actual == System.Math.Max(1, size / 10), "Size shortcut step scales gradually: " + size);
}
type.GetField("pencilSize").SetValue(settings, 5);
Check(Read<bool>(settings, "fillContiguous"), "Shared fill defaults to contiguous");
Check(Read<DCFApixels.WhimTex.FillSampleMode>(settings, "fillSampleMode") ==
    DCFApixels.WhimTex.FillSampleMode.CurrentLayer, "Shared fill defaults to current-layer sampling");
type.GetField("brushSize").SetValue(settings, 47f);
type.GetField("fillTolerance").SetValue(settings, 123);
type.GetField("brushColor").SetValue(settings, UnityEngine.Color.red);
type.GetMethod("SwapBrushColors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
    .Invoke(settings, null);
Check(Read<UnityEngine.Color>(settings, "secondaryBrushColor") == UnityEngine.Color.red, "Color swap uses shared settings");
var restored = UnityEngine.JsonUtility.FromJson(UnityEngine.JsonUtility.ToJson(settings), type);
Check(Read<float>(restored, "brushSize") == 47f && Read<int>(restored, "fillTolerance") == 123,
    "Shared brush/fill settings survive preference serialization");
Check(Read<int>(restored, "pencilSize") == 5, "Independent pencil size survives preference serialization");
Check(UnityEngine.JsonUtility.ToJson(first) == beforeFirst && UnityEngine.JsonUtility.ToJson(second) == beforeSecond,
    "Changing shared tools does not overwrite either layer's legacy or local settings");
Check(type.GetField("repeatMode") == null && type.GetField("transform") == null,
    "Shared settings do not own layer repetition or transforms");
var field = windowType.GetField("paintSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
Check(field.FieldType == type && System.Attribute.IsDefined(field, typeof(System.NonSerializedAttribute)),
    "Shared settings are excluded from window Undo snapshots");
var toolType = windowType.GetNestedType("PreviewTool", System.Reflection.BindingFlags.NonPublic);
var parseTool = windowType.GetMethod("ParsePreviewTool", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
foreach (string name in System.Enum.GetNames(toolType))
    Check(parseTool.Invoke(null, new object[] { name }).ToString() == name,
        "Tool preference round-trips: " + name);
foreach (string invalid in new[] { null, "", "RemovedTool", "999", "-1" })
    Check(parseTool.Invoke(null, new object[] { invalid }).ToString() == "None",
        "Unknown tool preference falls back to None: " + invalid);
foreach (string name in new[] { "previewTool", "previewSettingsTool", "previewTransformReturnTool" })
    Check(System.Attribute.IsDefined(windowType.GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
        typeof(System.NonSerializedAttribute)), "Tool selection is excluded from Undo: " + name);
return "Paint tool settings checks passed: " + checks + ". No assets, windows or GPU resources created.";
