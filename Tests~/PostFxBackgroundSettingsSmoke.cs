var assembly = typeof(DCFApixels.WhimTex.TextureCompositorWindow).Assembly;
var settings = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings", true);
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var mode = settings.GetProperty("PostFxBackgroundMode", flags);
var color = settings.GetProperty("PostFxBackground", flags);
const string modeKey = "DCFApixels.WhimTex.Preview.PostFxBackgroundMode";
const string colorKey = "DCFApixels.WhimTex.Preview.PostFxBackground";
bool hadMode = UnityEditor.EditorPrefs.HasKey(modeKey), hadColor = UnityEditor.EditorPrefs.HasKey(colorKey);
int savedMode = UnityEditor.EditorPrefs.GetInt(modeKey);
string savedColor = UnityEditor.EditorPrefs.GetString(colorKey);
object originalMode = mode.GetValue(null), originalColor = color.GetValue(null);
UnityEditor.EditorWindow preferences = null;
var previousFocus = UnityEditor.EditorWindow.focusedWindow;
var windows = new System.Collections.Generic.List<DCFApixels.WhimTex.TextureCompositorWindow>();
var panels = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
int checks = 0;
void Check(bool valid, string message) { if (!valid) throw new System.Exception(message); checks++; }
UnityEngine.UIElements.EnumField Mode(UnityEngine.UIElements.VisualElement root) =>
    UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.EnumField>(root, "postFxBackgroundMode");
try
{
    preferences = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettingsWindow", true));
    preferences.ShowUtility();
    preferences.GetType().GetMethod("CreateGUI").Invoke(preferences, null);
    for (int i = 0; i < 2; i++)
    {
        var window = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
        windows.Add(window);
        window.ShowUtility();
        var panel = new UnityEngine.UIElements.VisualElement(); panels.Add(panel);
        window.rootVisualElement.Clear();
        window.rootVisualElement.Add(panel);
        window.GetType().GetMethod("BuildPostFxFields", instanceFlags).Invoke(window, new object[] { panel });
    }
    var preferenceMode = Mode(preferences.rootVisualElement);
    Check(preferenceMode != null, "Background Mode exists in User Settings");
    foreach (var value in new[] { DCFApixels.WhimTex.PostFxBackground.SolidColor, DCFApixels.WhimTex.PostFxBackground.Checkerboard })
    {
        preferenceMode.value = value;
        Check((int)mode.GetValue(null) == (int)value, "Settings field writes shared mode");
        Check(UnityEditor.EditorPrefs.GetInt(modeKey) == (int)value, "Mode persists in EditorPrefs");
        foreach (var panel in panels)
        {
            Check((int)(DCFApixels.WhimTex.PostFxBackground)Mode(panel).value == (int)value, "Mode synchronizes across windows");
            var field = UnityEngine.UIElements.UQueryExtensions.Q<UnityEditor.UIElements.ColorField>(panel, "postFxBackground");
            Check(field.ClassListContains("whimtex-post-fx-field--hidden") == (value == DCFApixels.WhimTex.PostFxBackground.Checkerboard), "Solid color visibility follows mode");
        }
    }
    Mode(panels[0]).value = DCFApixels.WhimTex.PostFxBackground.SolidColor;
    Check((DCFApixels.WhimTex.PostFxBackground)preferenceMode.value == DCFApixels.WhimTex.PostFxBackground.SolidColor, "Panel updates User Settings");
    var panelColor = UnityEngine.UIElements.UQueryExtensions.Q<UnityEditor.UIElements.ColorField>(panels[0], "postFxBackground");
    panelColor.value = new UnityEngine.Color(.2f, .4f, .6f, 1f);
    var preferenceColor = (UnityEditor.UIElements.ColorField)preferences.GetType().GetField("postFxBackground", instanceFlags).GetValue(preferences);
    Check(preferenceColor.value == (UnityEngine.Color)color.GetValue(null), "Panel color updates settings");
    preferenceMode.value = DCFApixels.WhimTex.PostFxBackground.Checkerboard;
    Check(!preferenceColor.enabledSelf, "Solid color disabled for checkerboard");
    preferenceMode.value = DCFApixels.WhimTex.PostFxBackground.SolidColor;
    Check(preferenceColor.enabledSelf && preferenceColor.value == panelColor.value, "Solid color preserved when switching back");
    mode.SetValue(null, (DCFApixels.WhimTex.PostFxBackground)999);
    Check((DCFApixels.WhimTex.PostFxBackground)mode.GetValue(null) == DCFApixels.WhimTex.PostFxBackground.SolidColor, "Invalid mode normalizes safely");
    return "PASS: " + checks + " Post FX background settings checks";
}
finally
{
    mode.SetValue(null, originalMode); color.SetValue(null, originalColor);
    if (hadMode) UnityEditor.EditorPrefs.SetInt(modeKey, savedMode); else UnityEditor.EditorPrefs.DeleteKey(modeKey);
    if (hadColor) UnityEditor.EditorPrefs.SetString(colorKey, savedColor); else UnityEditor.EditorPrefs.DeleteKey(colorKey);
    foreach (var window in windows) UnityEngine.Object.DestroyImmediate(window);
    if (preferences != null) UnityEngine.Object.DestroyImmediate(preferences);
    if (previousFocus != null) previousFocus.Focus();
}
