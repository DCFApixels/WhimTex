using System;
using System.Collections.Generic;
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
            var font = WhimTexUI.ConfigureField(new TextField("Font") { name = "textFont", isReadOnly = true });
            bindings.Track(font, () => layer.ResolvedFont ?? "No system fonts");
            var choose = new Button(() => UnityEditor.PopupWindow.Show(font.worldBound, new FontPicker(layer.ResolvedFont,
                name => apply("Change Text Font", () => layer.fontFamily = name)))) { text = "…", tooltip = "Choose an installed system font" };
            choose.AddToClassList("whimtex-text-font-button");
            font.Add(choose); root.Add(font);
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

        internal static void ChooseFont(Rect rect, string selected, Action<string> apply) =>
            UnityEditor.PopupWindow.Show(rect, new FontPicker(selected, apply));

        private sealed class FontPicker : PopupWindowContent
        {
            private readonly string selected;
            private readonly Action<string> apply;
            private readonly List<string> filtered = new List<string>();
            private ListView list;
            private TextField search;
            private Font previewFont;
            private Label preview;
            internal FontPicker(string selected, Action<string> apply) { this.selected = selected; this.apply = apply; }
            public override Vector2 GetWindowSize() => new Vector2(340, 360);
            public override void OnGUI(Rect rect) { }
            public override void OnOpen()
            {
                var root = editorWindow.rootVisualElement; WhimTexUI.ApplyWindowStyles(root);
                search = WhimTexUI.ConfigureField(new TextField("Search") { name = "fontSearch" }); root.Add(search);
                search.RegisterValueChangedCallback(e => Filter(e.newValue));
                list = new ListView(filtered, 22, () => new Label { enableRichText = false }, (element, index) => ((Label)element).text = filtered[index]);
                list.AddToClassList("whimtex-text-font-list"); root.Add(list);
                list.selectionChanged += values => { foreach (string name in values) { Preview(name); break; } };
                list.itemsChosen += values => { foreach (string name in values) { apply(name); editorWindow.Close(); break; } };
                preview = new Label("Aa Бб 0123") { enableRichText = false }; preview.AddToClassList("whimtex-text-font-preview"); root.Add(preview);
                var actions = new VisualElement(); actions.AddToClassList("whimtex-text-font-actions"); root.Add(actions);
                actions.Add(new Button(() => { SystemFontCatalog.Refresh(); Filter(search.value); }) { text = "Refresh", tooltip = "Rescan installed fonts" });
                actions.Add(new Button(() => { if (list.selectedItem is string name) { apply(name); editorWindow.Close(); } }) { text = "Select" });
                Filter(""); Preview(selected); search.Focus();
            }
            private void Filter(string query)
            {
                filtered.Clear();
                foreach (string name in SystemFontCatalog.Names)
                    if (name.IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(name);
                list.Rebuild(); int index = filtered.IndexOf(selected); if (index >= 0) list.SetSelection(index);
            }
            private void Preview(string name)
            {
                if (preview == null || !SystemFontCatalog.Contains(name)) return;
                if (previewFont != null) UnityEngine.Object.DestroyImmediate(previewFont);
                previewFont = Font.CreateDynamicFontFromOSFont(name, 24); previewFont.hideFlags = HideFlags.HideAndDontSave;
                preview.style.unityFontDefinition = FontDefinition.FromFont(previewFont);
            }
            public override void OnClose() { if (previewFont != null) UnityEngine.Object.DestroyImmediate(previewFont); }
        }
    }
}
