// Unity Pipeline eval_file: unshown temporary windows, not the user's open documents.
const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var type = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var clipboard = type.Assembly.GetType("DCFApixels.WhimTex.LayerClipboard", true);
var first = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
var second = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
var source = (DCFApixels.WhimTex.TextureCompositor)type.GetField("compositor", Flags).GetValue(first);
var target = (DCFApixels.WhimTex.TextureCompositor)type.GetField("compositor", Flags).GetValue(second);
string savedClipboard = GUIUtility.systemCopyBuffer;
object savedArea = type.GetField("areaClipboard", Flags).GetValue(null);
int checks = 0;
Undo.IncrementCurrentGroup();
int testGroup = Undo.GetCurrentGroup();
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Flags).Invoke(owner, args);
void Command(DCFApixels.WhimTex.TextureCompositorWindow window, string name)
{
    using var evt = UnityEngine.UIElements.ExecuteCommandEvent.GetPooled(name);
    evt.target = window.rootVisualElement;
    Call(window, "ExecuteAreaCommand", evt);
}
void Key(DCFApixels.WhimTex.TextureCompositorWindow window, KeyCode key, bool shift = false, UnityEngine.UIElements.VisualElement targetElement = null)
{
    var systemEvent = new Event { type = EventType.KeyDown, keyCode = key, modifiers = EventModifiers.Control | (shift ? EventModifiers.Shift : EventModifiers.None) };
    using var evt = UnityEngine.UIElements.KeyDownEvent.GetPooled(systemEvent);
    evt.target = targetElement ?? window.rootVisualElement;
    Call(window, "OnToolkitKeyDown", evt);
}
try
{
    source.width = source.height = target.width = target.height = 8;
    var layer = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.red });
    layer.layerName = "Red";
    source.layers.Add(layer);
    Call(source, "NormalizeModel");
    Call(first, "SelectOnlyLayer", layer.Id);
    Command(first, "Copy");
    Check(clipboard.GetProperty("Current", Flags).GetValue(null) != null, "Copy command without area copies layer data");
    Command(second, "Paste");
    Check(target.layers.Count == 1 && target.layers[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "Paste command in another window preserves layer type");
    Check(target.layers[0].layerName == "Red", "Layer name preserved by window paste");
    Check((string)type.GetField("selectedLayerId", Flags).GetValue(second) == target.layers[0].Id, "Pasted layer is selected");
    Key(first, KeyCode.C);
    Key(second, KeyCode.V);
    Check(target.layers.Count == 2 && target.layers[0].Id != target.layers[1].Id, "Keyboard copy/paste creates independent layer");

    var text = new UnityEngine.UIElements.TextField();
    Check(!(bool)Call(first, "CanHandleAreaCommand", "Copy", text), "Command leaves text field copy alone");
    var beforeText = clipboard.GetProperty("Current", Flags).GetValue(null);
    Key(first, KeyCode.C, false, text);
    Check(object.ReferenceEquals(beforeText, clipboard.GetProperty("Current", Flags).GetValue(null)), "Key leaves text copy alone");

    object selection = Call(first, "GetAreaSelection");
    Call(selection, "All");
    Key(first, KeyCode.C);
    Check(clipboard.GetProperty("Current", Flags).GetValue(null) == null, "Area copy replaces layer clipboard");
    Check(type.GetField("areaClipboard", Flags).GetValue(null) != null, "Selected area stored as pixels");
    Key(second, KeyCode.V);
    Check(target.layers.Count == 3 && target.layers[0].Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour, "Selected area pastes as Drawing Layer");
    Call(selection, "Clear");
    Key(first, KeyCode.C, true);
    Check(clipboard.GetProperty("Current", Flags).GetValue(null) == null, "Copy Merged without selection stays pixel copy");
    Key(first, KeyCode.C);
    Check(type.GetField("areaClipboard", Flags).GetValue(null) == null, "Layer copy replaces old area pixels");
    Call(Call(second, "GetAreaSelection"), "All");
    Key(second, KeyCode.V);
    Check(target.layers.Count == 4 && target.layers[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "Destination area selection does not clip copied layers");
    UnityEngine.Object.DestroyImmediate(first); first = null;
    Key(second, KeyCode.V);
    Check(target.layers.Count == 5 && target.layers[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "Clipboard still pastes after closing original window");
    return "Layer clipboard window smoke passed: " + checks + " checks";
}
finally
{
    clipboard.GetMethod("Clear", Flags).Invoke(null, null);
    GUIUtility.systemCopyBuffer = savedClipboard;
    type.GetField("areaClipboard", Flags).SetValue(null, savedArea);
    Undo.RevertAllDownToGroup(testGroup);
    if (first != null) { typeof(EditorWindow).GetProperty("hasUnsavedChanges", Flags).SetValue(first, false); UnityEngine.Object.DestroyImmediate(first); }
    if (second != null) { typeof(EditorWindow).GetProperty("hasUnsavedChanges", Flags).SetValue(second, false); UnityEngine.Object.DestroyImmediate(second); }
    Undo.IncrementCurrentGroup();
}
