using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

// run_script entry: TwoChoiceDropdownSmoke.Main. Only an owned, empty utility window.
public class TwoChoiceDropdownSmokeWindow : EditorWindow { }

public static class TwoChoiceDropdownSmoke
{
    public enum LabeledChoice { [InspectorName("First Choice")] First = 4, Second = 12 }
    [Flags] public enum FlagChoice { First = 1, Second = 2 }

    public static async Task<string> Main()
    {
        var previousFocus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<TwoChoiceDropdownSmokeWindow>();
        ShaderFX fx = null;
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        try
        {
            window.ShowUtility();
            window.position = new Rect(100, 100, 420, 200);
            window.Focus();
            var field = new DropdownField("Dynamic choices", new List<string> { "A", "B" }, 0);
            window.rootVisualElement.Add(field);
            var input = field.Q<VisualElement>(className: DropdownField.inputUssClassName);
            Check(input != null, "Public input USS selector finds the dropdown input");
            var assembly = typeof(TextureCompositorWindow).Assembly;
            var type = assembly.GetType("DCFApixels.WhimTex.TwoChoiceDropdownManipulator`1", true).MakeGenericType(typeof(string));
            var manipulator = (IManipulator)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { input, (Func<IReadOnlyList<string>>)(() => field.choices),
                    (Func<string>)(() => field.value), (Action<string>)(v => field.value = v), (Func<string, string>)(v => v) }, null);
            field.AddManipulator(manipulator);
            await Task.Delay(100);
            int changes = 0;
            field.RegisterValueChangedCallback(_ => changes++);
            int Pointer() => (int)type.GetField("pointer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manipulator);
            void Down(int button = 0)
            {
                using var evt = PointerDownEvent.GetPooled(new Event
                    { type = EventType.MouseDown, button = button, mousePosition = input.worldBound.center });
                evt.target = input;
                input.SendEvent(evt);
            }
            void Up(bool inside = true)
            {
                using var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0,
                    mousePosition = inside ? input.worldBound.center : new Vector2(-20, -20) });
                evt.target = input;
                input.SendEvent(evt);
            }
            void Escape()
            {
                using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                field.SendEvent(evt);
            }
            Down(); Check(Pointer() >= 0, "Two choices arm short-click handling"); Up();
            Check(field.value == "B" && changes == 1, "Short click changes the value exactly once");
            Check(Pointer() == -1 && !field.HasPointerCapture(PointerId.mousePointerId), "Release clears capture");
            Down(); Up(); Check(field.value == "A" && changes == 2, "Second click toggles back");
            Down();
            var drag = type.GetMethod("IsDownwardDrag", BindingFlags.Instance | BindingFlags.NonPublic);
            bool Opens(float x, float y) => (bool)drag.Invoke(manipulator, new object[] { input.worldBound.center + new Vector2(x, y) });
            Check(!Opens(0, 0) && !Opens(0, 3), "Small pointer jitter does not request a menu");
            Check(Opens(0, 4), "Downward drag requests menu at four UI pixels without waiting");
            Check(Opens(0, input.worldBound.height + 10), "Downward drag remains valid below the input");
            Check(Opens(5, 5), "Diagonal downward movement also requests menu");
            Check(!Opens(0, -10) && !Opens(10, 0), "Upward and sideways movement do not request menu");
            Escape();
            Down(); Escape(); Up(); Check(field.value == "A" && Pointer() == -1, "Escape cancels without applying");
            Down(); Up(false); Check(field.value == "A", "Outside release cancels");
            Down(); field.choices.Add("C"); Up(); Check(field.value == "A", "Mid-gesture choice count change cancels");
            // Call the package handler directly for native-fallback cases, so this automated test opens no OS menu.
            void ProbeDown()
            {
                using var evt = PointerDownEvent.GetPooled(new Event
                    { type = EventType.MouseDown, button = 0, mousePosition = input.worldBound.center });
                type.GetMethod("OnDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manipulator, new object[] { evt });
            }
            ProbeDown(); Check(Pointer() == -1, "Three choices leave native behavior untouched");
            field.choices.RemoveAt(2);
            Down(); Up(); Check(field.value == "B", "Returning to two choices activates automatically");
            Down(); field.choices = new List<string> { "X", "Y" }; Up();
            Check(field.value != "A", "Replacing two options does not apply an old option");
            field.choices = new List<string> { "A", "B" }; field.SetValueWithoutNotify("A");
            Down(); field.SetValueWithoutNotify("B"); Up(); Check(field.value == "B", "External value update is not overwritten");
            field.SetValueWithoutNotify(""); ProbeDown(); Check(Pointer() == -1, "Mixed/unknown value falls back to menu");
            field.SetValueWithoutNotify("A"); field.SetEnabled(false); ProbeDown();
            Check(Pointer() == -1, "Disabled control does not arm"); field.SetEnabled(true);
            Down(); field.SetEnabled(false); Up(); Check(field.value == "A", "Disabled during gesture cancels"); field.SetEnabled(true);
            Down(); field.RemoveFromHierarchy(); Check(Pointer() == -1, "Detach cancels the scheduled hold");
            window.rootVisualElement.Add(field);
            await Task.Delay(350);
            Check(Pointer() == -1 && field.value == "A", "Cancelled hold stays cancelled after reattach");
            Down(); field.RemoveManipulator(manipulator); Check(Pointer() == -1, "Removing manipulator releases capture");

            var enumField = new EnumField("Color Range", LayerColorRange.Standard);
            window.rootVisualElement.Add(enumField);
            var attach = assembly.GetType("DCFApixels.WhimTex.TwoChoiceDropdown", true).GetMethod("Attach",
                BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(EnumField) }, null);
            attach.Invoke(null, new object[] { enumField });
            await Task.Delay(100);
            var enumInput = enumField.Q<VisualElement>(className: EnumField.inputUssClassName);
            Check(enumInput != null, "Public input USS selector finds the enum input");
            using (var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = enumInput.worldBound.center }))
            { evt.target = enumInput; enumInput.SendEvent(evt); }
            using (var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = enumInput.worldBound.center }))
            { evt.target = enumInput; enumInput.SendEvent(evt); }
            Check(Equals(enumField.value, LayerColorRange.HDR), "Enum adapter toggles Standard to HDR");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var adapter = assembly.GetType("DCFApixels.WhimTex.TwoChoiceDropdown", true);
            var attachAny = adapter.GetMethod("Attach", flags, null, new[] { typeof(VisualElement) }, null);
            var attached = (System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, IManipulator>)adapter.GetField("attached", flags).GetValue(null);
            IManipulator Attached(VisualElement control) => attached.TryGetValue(control, out var result) ? result : null;
            object Choices(VisualElement control)
            {
                var handler = Attached(control);
                return ((Delegate)handler.GetType().GetField("getChoices", flags).GetValue(handler)).DynamicInvoke();
            }
            void Click(VisualElement control)
            {
                var visualInput = control.Q<VisualElement>(className: BaseField<string>.inputUssClassName);
                Check(visualInput != null && visualInput.worldBound.width > 0, "Control has a laid-out input");
                using (var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = visualInput.worldBound.center }))
                { evt.target = visualInput; visualInput.SendEvent(evt); }
                using (var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = visualInput.worldBound.center }))
                { evt.target = visualInput; visualInput.SendEvent(evt); }
            }
            var originalHandler = Attached(enumField);
            attachAny.Invoke(null, new object[] { enumField });
            Check(ReferenceEquals(originalHandler, Attached(enumField)), "Repeated configuration does not install another handler");
            enumField.showMixedValue = true;
            Check(Choices(enumField) == null, "Mixed enum does not toggle an arbitrary selection");
            enumField.showMixedValue = false;
            enumField.Init(LabeledChoice.First);
            var enumChoices = (IReadOnlyList<Enum>)Choices(enumField);
            Check(enumChoices.Count == 2 && Equals(enumChoices[1], LabeledChoice.Second), "Enum Init refreshes cached type and nonconsecutive values");
            var label = (Delegate)originalHandler.GetType().GetField("getLabel", flags).GetValue(originalHandler);
            Check((string)label.DynamicInvoke(LabeledChoice.First) == "First Choice", "InspectorName survives the held menu");
            enumField.Init(FlagChoice.First);
            Check(Choices(enumField) == null, "Flags retain native behavior");

            window.rootVisualElement.Clear();
            window.position = new Rect(100, 100, 420, 500);
            var popup = new PopupField<int>("Mip", new List<int> { 0, 1 }, 0, v => "Mip " + v, v => "Mip " + v);
            window.rootVisualElement.Add(popup);
            attachAny.Invoke(null, new object[] { popup });
            var nullChoice = new NoiseLayerBehaviour().Owner;
            var layerPopup = new PopupField<Layer>("Layer", new List<Layer> { null, nullChoice }, 0, v => v == null ? "None" : "Layer", v => v == null ? "None" : "Layer");
            window.rootVisualElement.Add(layerPopup);
            attachAny.Invoke(null, new object[] { layerPopup });
            await Task.Delay(100);
            Click(popup); Check(popup.value == 1, "Typed popup toggles and preserves values");
            var popupHandler = Attached(popup);
            var popupLabel = (Delegate)popupHandler.GetType().GetField("getLabel", flags).GetValue(popupHandler);
            Check((string)popupLabel.DynamicInvoke(1) == "Mip 1", "Typed popup preserves its label formatter");
            popup.showMixedValue = true; Check(Choices(popup) == null, "Mixed typed popup retains native behavior");
            popup.showMixedValue = false;
            popup.choices.Add(2); Check(((IReadOnlyList<int>)Choices(popup)).Count == 3, "Adapter observes live option count");
            Click(layerPopup); Check(ReferenceEquals(layerPopup.value, nullChoice), "Layer popup accepts a null first choice");

            var configure = assembly.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ConfigureField", flags).MakeGenericMethod(typeof(EnumField));
            var standardField = new EnumField("Color", LayerColorRange.Standard);
            configure.Invoke(null, new object[] { standardField, 120f });
            Check(Attached(standardField) != null, "Shared field configuration installs the behavior");

            var parse = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", flags);
            var parameters = (List<ShaderFXParameter>)parse.Invoke(null, new object[] {
                "// @param enum _Body = A { A: 2, B: 7 }\n" +
                "// @group(Mode; _Mode)\n// @param hidden enum _Mode = A { A: 3, B: 9 }\n" +
                "// @if _Mode == 9\n// @param float _Amount = 1\n// @endif\n// @endgroup\n", false, null });
            fx = ScriptableObject.CreateInstance<ShaderFX>();
            fx.hideFlags = HideFlags.HideAndDontSave;
            typeof(ShaderFX).GetField("parameters", flags).SetValue(fx, parameters);
            float ParameterValue(int index) => ((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx))[index].floatValue;
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            var view = (VisualElement)Activator.CreateInstance(viewType, flags, null, new object[] { fx }, null);
            window.rootVisualElement.Add(view);
            var dropdowns = view.Query<DropdownField>().ToList();
            Check(dropdowns.Count == 2 && Attached(dropdowns[0]) != null && Attached(dropdowns[1]) != null,
                "FX body and group-header dropdowns are attached");
            await Task.Delay(100);
            Undo.IncrementCurrentGroup();
            Click(dropdowns[0]); Undo.FlushUndoRecordObjects();
            Check(ParameterValue(0) == 7, "FX body toggles to the declared numeric value");
            Undo.PerformUndo(); viewType.GetMethod("Refresh", flags).Invoke(view, null);
            dropdowns = view.Query<DropdownField>().ToList();
            Check(ParameterValue(0) == 2 && dropdowns[0].value == "A", "FX click Undo restores model and control: " + ParameterValue(0) + ", " + dropdowns[0].value);
            Undo.PerformRedo(); viewType.GetMethod("Refresh", flags).Invoke(view, null);
            dropdowns = view.Query<DropdownField>().ToList();
            Check(ParameterValue(0) == 7 && dropdowns[0].value == "B", "FX click Redo restores model and control");
            await Task.Delay(100);
            Click(dropdowns[1]);
            Check(ParameterValue(1) == 9, "FX group-header toggle updates the parameter");
            var content = view.Q<VisualElement>(className: "whimtex-fx-parameter-group-content");
            Check(content != null && content.style.display.value != DisplayStyle.None, "FX condition refresh keeps group content visible");
            return "Two-choice dropdown: " + checks + " checks passed, including typed fields, FX body/header and Undo/Redo. Native menu interaction requires manual verification.";
        }
        finally
        {
            window.Close();
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            if (previousFocus != null) previousFocus.Focus();
        }
    }
}
