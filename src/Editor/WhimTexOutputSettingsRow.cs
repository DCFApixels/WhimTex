using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexOutputSettingsRow
    {
        internal static VisualElement Add(VisualElement parent, string label, VisualElement control, string tooltip = null, string propertyName = null)
        {
            var row = new VisualElement { name = "output-row-" + label, tooltip = tooltip, userData = propertyName };
            row.AddToClassList("whimtex-output-row");
            var caption = new Label(label) { tooltip = tooltip };
            caption.AddToClassList("whimtex-output-row-label");
            control.AddToClassList("whimtex-output-row-control");
            control.tooltip = tooltip;
            row.Add(caption);
            row.Add(control);
            parent.Add(row);
            caption.RegisterCallback<ClickEvent>(_ => control.Focus());
            return row;
        }

        internal static VisualElement AddProperty(VisualElement parent, SerializedProperty property, string label, string tooltip)
        {
            BindableElement control;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean: control = new Toggle(); break;
                case SerializedPropertyType.Enum: control = new PopupField<string>(new System.Collections.Generic.List<string>(property.enumDisplayNames), property.enumValueIndex); break;
                case SerializedPropertyType.Vector2: control = new Vector2Field(); break;
                case SerializedPropertyType.Vector4: control = new Vector4Field(); break;
                case SerializedPropertyType.Integer:
                    control = property.name == "anisoLevel" || property.name == "extrude"
                        ? new SliderInt(0, property.name == "anisoLevel" ? 16 : 32) { showInputField = true }
                        : new IntegerField();
                    break;
                case SerializedPropertyType.Float:
                    control = property.name == "alphaCutoff" ? new Slider(0, 1) { showInputField = true } : new FloatField();
                    break;
                default: throw new System.ArgumentException("Unsupported output setting: " + property.propertyPath);
            }
            control.bindingPath = property.propertyPath;
            // Enum popups use indices explicitly; serialized enum values need not be consecutive.
            if (control is PopupField<string> popup)
            {
                control.bindingPath = null;
                popup.RegisterValueChangedCallback(_ =>
                {
                    property.enumValueIndex = popup.index;
                    property.serializedObject.ApplyModifiedProperties();
                    ((TextureCompositor)property.serializedObject.targetObject).MarkChanged();
                });
                control.TrackPropertyValue(property, value => popup.SetValueWithoutNotify(popup.choices[value.enumValueIndex]));
            }
            else control.BindProperty(property);
            Add(parent, label, control, tooltip, property.name);
            return control;
        }

        internal static void Validate(VisualElement root, TextureCompositor document)
        {
            string error = document.ValidateOutputSettings(out string[] fields);
            foreach (var row in root.Query<VisualElement>(className: "whimtex-output-row").ToList())
            {
                bool invalid = row.userData is string key && System.Array.IndexOf(fields, key) >= 0;
                row.EnableInClassList("whimtex-output-row--invalid", invalid);
                var message = row.Q<Label>(className: "whimtex-output-field-error");
                if (invalid && message == null)
                {
                    message = new Label();
                    message.AddToClassList("whimtex-output-field-error");
                    row.Add(message);
                }
                if (message != null)
                {
                    message.text = invalid ? error : "";
                    message.EnableInClassList("whimtex-output-settings-hidden", !invalid);
                }
            }
        }
    }
}
