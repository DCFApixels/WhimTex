using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class SystemFontField : BaseField<string>
    {
        private readonly PreviewLabel fontName;
        private readonly Button choose;

        internal SystemFontField(string label = null) : base(label, new VisualElement())
        {
            AddToClassList("whimtex-system-font-field");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            choose = new Button(() => UnityEditor.PopupWindow.Show(choose.worldBound, new FontPicker(value, next => value = next)))
            {
                name = "chooseFont",
                tooltip = "Choose an installed system font"
            };
            choose.AddToClassList("whimtex-system-font-button");
            fontName = new PreviewLabel { name = "fontName", pickingMode = PickingMode.Ignore };
            fontName.AddToClassList("whimtex-system-font-name");
            var arrow = new Label("▾") { pickingMode = PickingMode.Ignore, enableRichText = false };
            arrow.AddToClassList("whimtex-system-font-arrow");
            choose.Add(fontName); choose.Add(arrow); input.Add(choose);
            SetValueWithoutNotify("");
        }

        public override void SetValueWithoutNotify(string next)
        {
            base.SetValueWithoutNotify(next ?? "");
            RefreshPreview();
        }

        internal void RefreshPreview() => fontName?.SetPreview(value, string.IsNullOrEmpty(value) ? "No system fonts" : value);

        internal sealed class PreviewLabel : Label
        {
            private string family;
            private int revision = -1;
            private bool attempted;
            private Font font;

            internal PreviewLabel()
            {
                enableRichText = false;
                RegisterCallback<AttachToPanelEvent>(_ => RefreshFont());
                RegisterCallback<DetachFromPanelEvent>(_ => ReleaseFont());
            }

            internal void SetPreview(string nextFamily, string previewText)
            {
                if (family != nextFamily || text != previewText || revision != SystemFontCatalog.Revision)
                {
                    ReleaseFont(); family = nextFamily;
                }
                text = previewText; tooltip = previewText;
                RefreshFont();
            }

            internal void ClearPreview()
            {
                ReleaseFont(); family = null; text = ""; tooltip = "";
            }

            private void RefreshFont()
            {
                if (panel == null || attempted) return;
                attempted = true; revision = SystemFontCatalog.Revision;
                if (!SystemFontCatalog.Contains(family)) return;
                try
                {
                    font = Font.CreateDynamicFontFromOSFont(family, 16);
                    if (font == null) return;
                    font.hideFlags = HideFlags.HideAndDontSave;
                    foreach (char character in text)
                        if (!char.IsWhiteSpace(character) && !font.HasCharacter(character))
                        {
                            ReleaseFont(); attempted = true;
                            return;
                        }
                    style.unityFontDefinition = FontDefinition.FromFont(font);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is UnityException)
                {
                    ReleaseFont(); attempted = true;
                }
            }

            private void ReleaseFont()
            {
                style.unityFontDefinition = StyleKeyword.Null;
                if (font != null) UnityEngine.Object.DestroyImmediate(font);
                font = null; attempted = false;
            }
        }

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
                list = new ListView(filtered, 26, () =>
                {
                    var row = new PreviewLabel(); row.AddToClassList("whimtex-system-font-option"); return row;
                }, (element, index) => ((PreviewLabel)element).SetPreview(filtered[index], filtered[index]));
                list.unbindItem = (element, _) => ((PreviewLabel)element).ClearPreview();
                list.destroyItem = element => ((PreviewLabel)element).ClearPreview();
                list.AddToClassList("whimtex-text-font-list"); root.Add(list);
                list.selectionChanged += values => { foreach (string name in values) { Preview(name); break; } };
                list.itemsChosen += values => { foreach (string name in values) { apply(name); editorWindow.Close(); break; } };
                preview = new Label("Aa Бб 0123") { enableRichText = false };
                preview.AddToClassList("whimtex-text-font-preview"); root.Add(preview);
                var actions = new VisualElement(); actions.AddToClassList("whimtex-text-font-actions"); root.Add(actions);
                actions.Add(new Button(() => { SystemFontCatalog.Refresh(); Filter(search.value); })
                    { text = "Refresh", tooltip = "Rescan installed fonts" });
                actions.Add(new Button(() => { if (list.selectedItem is string name) { apply(name); editorWindow.Close(); } }) { text = "Select" });
                Filter(""); Preview(selected); search.Focus();
            }

            private void Filter(string query)
            {
                filtered.Clear();
                foreach (string name in SystemFontCatalog.Names)
                    if (name.IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(name);
                list.Rebuild();
                int index = filtered.IndexOf(selected); if (index >= 0) list.SetSelection(index);
            }

            private void Preview(string name)
            {
                if (preview == null || !SystemFontCatalog.Contains(name)) return;
                preview.style.unityFontDefinition = StyleKeyword.Null;
                if (previewFont != null) UnityEngine.Object.DestroyImmediate(previewFont);
                previewFont = Font.CreateDynamicFontFromOSFont(name, 24);
                if (previewFont == null) return;
                previewFont.hideFlags = HideFlags.HideAndDontSave;
                preview.style.unityFontDefinition = FontDefinition.FromFont(previewFont);
            }

            public override void OnClose()
            {
                if (preview != null) preview.style.unityFontDefinition = StyleKeyword.Null;
                if (previewFont != null) UnityEngine.Object.DestroyImmediate(previewFont);
                previewFont = null;
                if (list != null)
                    foreach (var row in list.Query<PreviewLabel>().ToList()) row.ClearPreview();
            }
        }
    }
}
