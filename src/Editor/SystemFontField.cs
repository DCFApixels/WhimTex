using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class SystemFontPreview
    {
        private static readonly Dictionary<string, (string family, string style)> faces =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        private static int facesRevision = -1;

        internal static FontAsset Create(string name, int pointSize)
        {
            if (!SystemFontCatalog.Contains(name)) return null;
            try
            {
                if (!TryGetFace(name, out var face) ||
                    FontEngine.LoadFontFace(face.family, face.style, pointSize) != FontEngineError.Success) return null;
                // UI Toolkit's conversion from legacy Font assumes a Regular face of font.name.
                // Use the OS catalog's actual family and style instead of treating a face name as a family.
                var font = FontAsset.CreateFontAsset(face.family, face.style, pointSize, 4, GlyphRenderMode.SDFAA);
                if (font != null)
                {
                    font.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var atlas in font.atlasTextures)
                        if (atlas != null) atlas.hideFlags = HideFlags.HideAndDontSave;
                    if (font.material != null) font.material.hideFlags = HideFlags.HideAndDontSave;
                }
                return font;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is UnityException) { return null; }
        }

        private static bool TryGetFace(string name, out (string family, string style) face)
        {
            if (facesRevision != SystemFontCatalog.Revision)
            {
                faces.Clear();
                foreach (string entry in FontEngine.GetSystemFontNames() ?? Array.Empty<string>())
                {
                    int separator = entry.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (separator < 1) continue;
                    string family = entry.Substring(0, separator), style = entry.Substring(separator + 3);
                    if (style.Length == 0) continue;
                    faces[family + " " + style] = (family, style);
                }
                // Preserve family names which themselves contain words such as Bold or Italic.
                foreach (var faceName in new List<string>(faces.Keys))
                {
                    var regular = faces[faceName];
                    if (regular.style.Equals("Regular", StringComparison.OrdinalIgnoreCase)) faces[regular.family] = regular;
                }
                facesRevision = SystemFontCatalog.Revision;
            }
            return faces.TryGetValue(name, out face);
        }

        internal static void Destroy(FontAsset font)
        {
            if (font == null) return;
            foreach (var atlas in font.atlasTextures)
                if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
            if (font.material != null) UnityEngine.Object.DestroyImmediate(font.material);
            UnityEngine.Object.DestroyImmediate(font);
        }
    }

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
            private FontAsset font;
            private readonly bool allowFallback;

            internal PreviewLabel(bool allowFallback = false)
            {
                this.allowFallback = allowFallback;
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
                    font = SystemFontPreview.Create(family, 32);
                    if (font == null) return;
                    font.hideFlags = HideFlags.HideAndDontSave;
                    bool complete = font.TryAddCharacters(text);
                    foreach (var atlas in font.atlasTextures)
                        if (atlas != null) atlas.hideFlags = HideFlags.HideAndDontSave;
                    if (font.material != null) font.material.hideFlags = HideFlags.HideAndDontSave;
                    if (!complete && !allowFallback) { ReleaseFont(); attempted = true; return; }
                    style.unityFontDefinition = FontDefinition.FromSDFFont(font);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is UnityException)
                {
                    ReleaseFont(); attempted = true;
                }
            }

            private void ReleaseFont()
            {
                style.unityFontDefinition = StyleKeyword.Null;
                SystemFontPreview.Destroy(font);
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
            private PreviewLabel preview;

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
                preview = new PreviewLabel(allowFallback: true) { name = "fontPreview" };
                preview.AddToClassList("whimtex-text-font-preview"); root.Add(preview);
                var actions = new VisualElement(); actions.AddToClassList("whimtex-text-font-actions"); root.Add(actions);
                actions.Add(new Button(() => { SystemFontCatalog.Refresh(); Filter(search.value); })
                    { text = "Refresh", tooltip = "Rescan installed fonts" });
                actions.Add(new Button(() => { if (list.selectedItem is string name) { apply(name); editorWindow.Close(); } }) { text = "Select" });
                Filter(""); Preview(selected); search.Focus();
            }

            private void Filter(string query)
            {
                string highlighted = list.selectedItem as string ?? selected;
                list.ClearSelection(); Preview(null);
                filtered.Clear();
                foreach (string name in SystemFontCatalog.Names)
                    if (name.IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(name);
                list.Rebuild();
                int index = filtered.IndexOf(highlighted);
                if (index < 0) index = filtered.IndexOf(selected);
                if (index >= 0) list.SetSelection(index);
            }

            private void Preview(string name)
            {
                preview?.SetPreview(name, "Aa Бб 0123");
            }

            public override void OnClose()
            {
                preview?.ClearPreview();
                if (list != null)
                    foreach (var row in list.Query<PreviewLabel>().ToList()) row.ClearPreview();
            }
        }
    }
}
