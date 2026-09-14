using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

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
            void Refresh()
            {
                field.hdr = Hdr;
                field.SetValueWithoutNotify(DisplayColor(read()));
            }
            Observe(field, Refresh);
            bindings.Track(field, () => DisplayColor(read()));
            return field;
        }

        internal static ColorField Bind(ColorField field, SerializedProperty property, Action edited)
        {
            SerializedProperty source = property.Copy();
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

        internal static GradientField Bind(GradientField field, WhimTexUI.ValueBindings bindings, Func<Gradient> read)
        {
            Gradient snapshot = null, display = null;
            bool lastHdr = Hdr;
            Gradient ReadDisplay()
            {
                Gradient source = read();
                if (source == null) return null;
                if (snapshot == null || !snapshot.Equals(source) || lastHdr != Hdr)
                {
                    snapshot = CopyGradient(source, false);
                    display = CopyGradient(source, !Hdr);
                    lastHdr = Hdr;
                }
                return display;
            }
            Observe(field, () =>
            {
                field.hdr = Hdr;
                field.SetValueWithoutNotify(ReadDisplay());
            });
            bindings.Track(field, ReadDisplay);
            return field;
        }

        private static Gradient CopyGradient(Gradient source, bool standard)
        {
            GradientColorKey[] colors = source.colorKeys;
            if (standard)
                for (int i = 0; i < colors.Length; i++) colors[i].color = StandardColor(colors[i].color);
            var result = new Gradient { mode = source.mode, colorSpace = source.colorSpace };
            result.SetKeys(colors, source.alphaKeys);
            return result;
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
