using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class TextLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(TextLayerBehaviour);
        public static void Open(TextLayerBehaviour layer, WhimTexDocument document) => OpenPropertiesWindow<TextLayerEditorWindow>(layer, document);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (TextLayerBehaviour)source, ApplyLayerChange, SettingsBindings);

        internal static void BuildFields(VisualElement root, TextLayerBehaviour layer,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            var font = WhimTexUI.ConfigureField(new SystemFontField("Font") { name = "textFont" });
            bindings.Track(font, () => layer.ResolvedFont ?? "");
            bindings.Add(font.RefreshPreview);
            font.RegisterValueChangedCallback(e => apply("Change Text Font", () => layer.fontFamily = e.newValue));
            root.Add(font);
            var notice = new HelpBox("", HelpBoxMessageType.Warning) { name = "textFontNotice" };
            root.Add(notice);
            var layout = new VisualElement(); root.Add(layout);
            var text = WhimTexUI.ConfigureField(new TextField("Text") { name = "textContent", multiline = true, maxLength = TextLayerBehaviour.MaxCharacters });
            text.AddToClassList("whimtex-text-content");
            bindings.Track(text, () => layer.text ?? "");
            text.RegisterValueChangedCallback(e => apply("Change Text", () => layer.text = e.newValue)); layout.Add(text);
            var mode = WhimTexUI.ConfigureField(new EnumField("Layout", layer.layoutMode) { name = "textLayoutMode" });
            bindings.Track(mode, () => (Enum)layer.layoutMode);
            mode.RegisterValueChangedCallback(e => apply("Change Text Layout", () => layer.SetLayoutMode((TextLayoutMode)e.newValue))); layout.Add(mode);
            var frame = WhimTexUI.ConfigureField(new Vector2Field("Frame Size (px)") { name = "textFrameSize" });
            bindings.Track(frame, () => layer.frameSize);
            frame.RegisterValueChangedCallback(e => apply("Resize Text Frame", () => layer.frameSize = new Vector2(
                Mathf.Clamp(float.IsFinite(e.newValue.x) ? e.newValue.x : 256, 1, 32768),
                Mathf.Clamp(float.IsFinite(e.newValue.y) ? e.newValue.y : 128, 1, 32768)))); layout.Add(frame);
            var style = new TextStyleField { name = "textStyle", tooltip = "Style" };
            bindings.Track(style, () => layer.fontStyle);
            style.RegisterValueChangedCallback(e => apply("Change Text Style", () => layer.fontStyle = e.newValue));
            var casing = new TextCasingField { name = "textCasing", tooltip = "Casing" };
            bindings.Track(casing, () => layer.casing);
            casing.RegisterValueChangedCallback(e => apply("Change Text Casing", () => layer.casing = e.newValue));
            var sizeRange = WhimTexUI.ConfigureField(new Vector2Field("Size") { name = "textSizeRange",
                tooltip = "Font size in canvas pixels. Auto Size fits text between Min and Max." });
            sizeRange.AddToClassList("whimtex-text-size");
            var sizeFields = sizeRange.Query<FloatField>().ToList();
            var size = sizeFields[0]; size.name = "textSize";
            var maxSize = sizeFields[1]; maxSize.name = "textMaxSize"; maxSize.label = "Max";
            bindings.Track(sizeRange, () => new Vector2(layer.fontSize, layer.maxFontSize));
            sizeRange.RegisterValueChangedCallback(e => apply("Change Text Size", () =>
            {
                if (e.newValue.x != e.previousValue.x) layer.SetFontSize(e.newValue.x);
                if (e.newValue.y != e.previousValue.y) layer.SetMaxFontSize(e.newValue.y);
            })); layout.Add(sizeRange);
            var auto = WhimTexUI.ConfigureField(new Toggle("Auto Size") { name = "textAutoSize",
                tooltip = "Fit text inside the frame between Min Size and Max Size." });
            bindings.Track(auto, () => layer.autoSize);
            auto.RegisterValueChangedCallback(e => apply("Change Text Auto Size", () => layer.SetAutoSize(e.newValue))); layout.Add(auto);
            AddFormatRow(layout, "textStyleRow", "Style", style, casing);
            var alignment = new TextAlignmentField { name = "textAlignment", tooltip = "Horizontal alignment" };
            bindings.Track(alignment, () => new TextAlignmentField.Settings(layer.alignment, layer.justify));
            alignment.RegisterValueChangedCallback(e => apply("Change Text Alignment", () =>
                layer.SetAlignment(e.newValue.alignment, e.newValue.justify)));
            var vertical = new TextAlignmentField(null, true) { name = "textVerticalAlignment", tooltip = "Vertical alignment" };
            bindings.Track(vertical, () => new TextAlignmentField.Settings(layer.alignment, layer.justify));
            vertical.RegisterValueChangedCallback(e => apply("Change Vertical Text Alignment", () => layer.SetAlignment(e.newValue.alignment, layer.justify)));
            AddFormatRow(layout, "textAlignmentRow", "Alignment", alignment, vertical);
            var spacing = WhimTexUI.ConfigureField(new TextSpacingField { name = "textSpacing" });
            bindings.Track(spacing, () => layer.spacing);
            spacing.RegisterValueChangedCallback(e => apply("Change Text Spacing", () => layer.spacing = e.newValue)); layout.Add(spacing);
            var horizontalScale = WhimTexUI.ConfigureField(new FloatField("Horizontal Scale") { name = "textCharacterHorizontalScale",
                tooltip = "Character Horizontal Scale: 1 keeps the font's width; 0.5 halves it; 2 doubles it. Height and added em spacing are unchanged." });
            bindings.Track(horizontalScale, () => layer.characterHorizontalScale);
            horizontalScale.RegisterValueChangedCallback(e => apply("Change Character Horizontal Scale", () => layer.SetCharacterHorizontalScale(e.newValue)));
            layout.Add(horizontalScale);
            var wrap = WhimTexUI.ConfigureField(new EnumField("Wrapping", layer.wrapping) { name = "textWrapping" });
            bindings.Track(wrap, () => (Enum)layer.wrapping);
            wrap.RegisterValueChangedCallback(e => apply("Change Text Wrapping", () => layer.SetWrapping((TextWrapping)e.newValue))); layout.Add(wrap);
            var overflow = WhimTexUI.ConfigureField(new EnumField("Overflow", layer.overflow) { name = "textOverflow" });
            bindings.Track(overflow, () => (Enum)layer.overflow);
            overflow.RegisterValueChangedCallback(e => apply("Change Text Overflow", () => layer.overflow = (TextOverflowMode)e.newValue)); layout.Add(overflow);
            var color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexColorField("Color") { name = "textColor" }, bindings, () => layer.color));
            color.RegisterValueChangedCallback(e => apply("Change Text Color", () => layer.color = e.newValue)); root.Add(color);
            bindings.Add(() =>
            {
                string message = layer.Notice;
                notice.text = message ?? ""; notice.EnableInClassList("whimtex-hidden", message == null);
                layout.SetEnabled(layer.FontAvailable);
                bool framed = layer.layoutMode == TextLayoutMode.Frame;
                frame.EnableInClassList("whimtex-hidden", !framed); wrap.EnableInClassList("whimtex-hidden", !framed);
                overflow.EnableInClassList("whimtex-hidden", !framed);
                auto.EnableInClassList("whimtex-hidden", !framed);
                size.label = layer.UsesAutoSize ? "Min" : "";
                maxSize.EnableInClassList("whimtex-hidden", !layer.UsesAutoSize);
                alignment.SetCanJustify(layer.CanJustify);
            });
        }

        private static void AddFormatRow(VisualElement root, string name, string title, VisualElement first, VisualElement second)
        {
            var row = new VisualElement { name = name };
            row.AddToClassList("unity-base-field");
            row.AddToClassList("whimtex-text-format-row");
            var label = new Label(title);
            label.AddToClassList(BaseField<string>.labelUssClassName);
            label.style.width = WhimTexUI.StandardLabelWidth;
            row.Add(label); row.Add(first);
            var separator = new VisualElement { pickingMode = PickingMode.Ignore };
            separator.AddToClassList("whimtex-text-format-separator");
            row.Add(separator); row.Add(second); root.Add(row);
        }

    }
}
