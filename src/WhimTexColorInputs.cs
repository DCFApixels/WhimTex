using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexColorInputs
    {
        private const string PreferenceKey = "DCFApixels.WhimTex.HdrColorInputs";
        private static bool hdr = EditorPrefs.GetBool(PreferenceKey, false);
        private static event Action Changed;

        internal static bool Hdr
        {
            get => hdr;
            set
            {
                if (hdr == value)
                    return;
                hdr = value;
                EditorPrefs.SetBool(PreferenceKey, value);
                Changed?.Invoke();
            }
        }

        internal static void Reset()
        {
            EditorPrefs.DeleteKey(PreferenceKey);
            hdr = false;
            Changed?.Invoke();
        }

        internal static Color DisplayColor(Color color) => Hdr ? color : StandardColor(color);

        internal static Color StandardColor(Color color)
        {
            float r = PositiveFinite(color.r), g = PositiveFinite(color.g), b = PositiveFinite(color.b);
            float peak = Mathf.Max(1f, Mathf.Max(r, Mathf.Max(g, b)));
            return new Color(r / peak, g / peak, b / peak, Mathf.Clamp01(PositiveFinite(color.a)));
        }

        private static float PositiveFinite(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

        internal static ColorField Bind(ColorField field, WhimTexUI.ValueBindings bindings, Func<Color> read)
        {
            field.UseCanvasChannels = true;
            field.ReadPickerColor = read;
            field.HdrChanged = value => Hdr = value;
            void Refresh()
            {
                field.hdr = Hdr;
                field.SetValueWithoutNotify(DisplayColor(read()));
            }
            Observe(field, Refresh);
            bindings.Track(field, () => DisplayColor(read()));
            return field;
        }

        internal static ColorField Bind(ColorField field, Func<Color> read, Action<Color> write)
        {
            field.UseCanvasChannels = true;
            field.ReadPickerColor = read;
            field.HdrChanged = value => Hdr = value;
            Observe(field, () =>
            {
                field.hdr = Hdr;
                field.SetValueWithoutNotify(DisplayColor(read()));
            });
            field.RegisterValueChangedCallback(evt => write(evt.newValue));
            return field;
        }

        internal static ColorField Bind(ColorField field, SerializedProperty property, Action edited)
        {
            field.UseCanvasChannels = true;
            SerializedProperty source = property.Copy();
            field.ReadPickerColor = () => source.colorValue;
            field.Document = () => WhimTexColorPicker.DocumentFor(source.serializedObject.targetObject);
            field.HdrChanged = value => Hdr = value;
            void Refresh()
            {
                field.hdr = Hdr;
                field.SetValueWithoutNotify(DisplayColor(source.colorValue));
            }
            Observe(field, Refresh);
            field.TrackPropertyValue(source, _ => Refresh());
            field.RegisterValueChangedCallback(evt =>
            {
                source.serializedObject.UpdateIfRequiredOrScript();
                source.colorValue = evt.newValue;
                if (source.serializedObject.ApplyModifiedProperties()) edited?.Invoke();
            });
            return field;
        }

        internal static WhimTexGradientValueField Bind(WhimTexGradientValueField field, WhimTexUI.ValueBindings bindings, Func<WhimTexGradient> read)
        {
            bindings.Track(field, read);
            return field;
        }

        internal static Button CreateToggleControl()
        {
            Button button = new Button(() => Hdr = !Hdr) { text = "HDR" };
            button.name = "colorInputMode";
            button.AddToClassList("whimtex-channel-button");
            button.AddToClassList("whimtex-hdr-button");
            button.tooltip = "HDR color input: on uses HDR colors; off uses Standard colors. " +
                "Standard displays and paints colors without HDR intensity. Switching preserves stored colors; " +
                "editing replaces the selected color. Existing layers, blending and export are unchanged.";
            Observe(button, () => button.EnableInClassList("whimtex-channel-button--enabled", Hdr));
            return button;
        }

        private static void Observe(VisualElement element, Action refresh)
        {
            refresh();
            element.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != element)
                    return;
                Changed -= refresh;
                Changed += refresh;
                refresh();
            });
            element.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target == element)
                    Changed -= refresh;
            });
        }
    }
}
