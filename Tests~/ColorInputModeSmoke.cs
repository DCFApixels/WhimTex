// Opt-in live-Editor eval after manual compilation. Detached fields only; no preferences/assets/Undo writes.
var assembly = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly;
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var inputs = assembly.GetType("DCFApixels.WhimTex.WhimTexColorInputs");
var bindingsType = assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings");
var bindings = Activator.CreateInstance(bindingsType, true);
var mode = inputs.GetField("hdr", flags);
bool originalMode = (bool)mode.GetValue(null);
Color primary = new Color(16, 8, 4, .4f), secondary = new Color(2, 4, 1, .7f);
Color originalPrimary = primary;
var colorField = new UnityEditor.UIElements.ColorField();
var gradientField = new UnityEditor.UIElements.GradientField();
var sourceGradient = new Gradient { mode = GradientMode.Fixed, colorSpace = ColorSpace.Linear };
sourceGradient.SetKeys(new[] { new GradientColorKey(primary, 0), new GradientColorKey(secondary, 1) },
    new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.8f, 1) });
int checks = 0, events = 0;
colorField.RegisterValueChangedCallback(_ => events++);
gradientField.RegisterValueChangedCallback(_ => events++);
void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
void Near(Color a, Color b, string label) => Check(
    Mathf.Abs(a.r - b.r) < .0001f && Mathf.Abs(a.g - b.g) < .0001f &&
    Mathf.Abs(a.b - b.b) < .0001f && Mathf.Abs(a.a - b.a) < .0001f, label);
Color Display(Color value) => (Color)inputs.GetMethod("DisplayColor", flags).Invoke(null, new object[] { value });
void Refresh(bool hdr)
{
    // Avoid the preference setter and global events in this isolated test.
    mode.SetValue(null, hdr);
    bindingsType.GetMethod("Refresh").Invoke(bindings, new object[] { true });
}
try
{
    mode.SetValue(null, true);
    inputs.GetMethod("Bind", flags, null,
        new[] { typeof(UnityEditor.UIElements.ColorField), bindingsType, typeof(Func<Color>) }, null)
        .Invoke(null, new object[] { colorField, bindings, (Func<Color>)(() => primary) });
    inputs.GetMethod("Bind", flags, null,
        new[] { typeof(UnityEditor.UIElements.GradientField), bindingsType, typeof(Func<Gradient>) }, null)
        .Invoke(null, new object[] { gradientField, bindings, (Func<Gradient>)(() => sourceGradient) });
    Near(colorField.value, primary, "HDR field retains intensity");
    Refresh(false);
    Near(colorField.value, new Color(1, .5f, .25f, .4f), "Standard field removes excessive intensity");
    Near(Display(primary), colorField.value, "Tool color matches displayed color");
    Near(primary, originalPrimary, "Display refresh must not rewrite source");
    Near(Display(new Color(.3f, .5f, .1f, .6f)), new Color(.3f, .5f, .1f, .6f), "Ordinary color is unchanged");
    Near(Display(new Color(float.MaxValue, float.MaxValue / 2, 0, .5f)), new Color(1, .5f, 0, .5f), "Extreme finite intensity stays colored");
    Near(gradientField.value.colorKeys[0].color,
        new Color(1, .5f, .25f, sourceGradient.colorKeys[0].color.a), "Gradient key is bounded");
    Check(sourceGradient.colorKeys[0].color.r == 16, "Gradient source is not mutated");
    Check(gradientField.value.mode == sourceGradient.mode && gradientField.value.colorSpace == sourceGradient.colorSpace,
        "Gradient metadata is preserved");
    Check(gradientField.value.alphaKeys[0].alpha == .2f, "Gradient alpha is preserved");
    var swap = primary; primary = secondary; secondary = swap;
    Refresh(false);
    Near(colorField.value, new Color(.5f, 1, .25f, .7f), "Color swap uses Standard display");
    Refresh(true);
    Near(colorField.value, primary, "HDR intensity returns after switching");
    Check(gradientField.value.Equals(sourceGradient), "HDR gradient returns unchanged");
    Refresh(false);
    primary = new Color(.2f, .4f, .8f, 1);
    Refresh(true);
    Near(colorField.value, primary, "Explicit source edit replaces the color, not a stale cached HDR value");
    Check(events == 0, "Mode/source refresh must not emit edit events");
    return $"Color input mode: {checks} checks passed.";
}
finally { mode.SetValue(null, originalMode); }
