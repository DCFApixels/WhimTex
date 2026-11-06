using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class NoteView : StickyNote, IGraphItemView
        {
            readonly Canvas canvas;
            readonly VisualElement[] colorSurfaces;
            readonly NoteColors[] nativeColors;
            bool colorsReady;
            float hueShift;
            public string Id { get; }
            internal NoteView(Canvas canvas, ShaderGraphNote data)
            {
                this.canvas = canvas;
                Id = data.id; viewDataKey = Id;
                AddToClassList("whimtex-shader-graph-note");
                capabilities |= Capabilities.Copiable | Capabilities.Groupable;
                // Keep selection/resize affordances native. Rotate the note's complete palette,
                // including text and outlines, using the resolved colors of its current theme.
                var surfaces = new List<VisualElement> { this };
                void Collect(VisualElement element)
                { if (element == null) return; surfaces.Add(element); foreach (var child in element.Children()) Collect(child); }
                Collect(this.Q("node-border"));
                colorSurfaces = surfaces.ToArray(); nativeColors = new NoteColors[colorSurfaces.Length];
                MatchEditor(this.Q<Label>("title"), this.Q<TextField>("title-field"));
                MatchEditor(this.Q<Label>("contents"), this.Q<TextField>("contents-field"));
                Refresh(data);
                RegisterCallback<StickyNoteChangeEvent>(_ => canvas.NoteChanged(this));
                RegisterCallback<GeometryChangedEvent>(_ => { if (!colorsReady) { ReadNativeColors(); ApplyHue(); } });
                generateVisualContent += _ =>
                {
                    // Theme changes may affect only colors, without a GeometryChangedEvent.
                    // Read after styles resolve and apply outside mesh generation.
                    if (!colorsReady) { ReadNativeColors(); schedule.Execute(ApplyHue); }
                };
            }
            static void MatchEditor(Label label, TextField field)
            {
                if (label == null || field == null) return;
                var input = field.Q("unity-text-input");
                var text = field.Query<TextElement>().ToList();
                void CopyTypography(VisualElement element, IResolvedStyle source)
                {
                    var target = element.style;
                    target.fontSize = source.fontSize;
                    target.unityFontDefinition = source.unityFontDefinition;
                    target.unityFontStyleAndWeight = source.unityFontStyleAndWeight;
                    target.unityTextAlign = source.unityTextAlign;
                    target.whiteSpace = source.whiteSpace;
                    target.letterSpacing = source.letterSpacing; target.wordSpacing = source.wordSpacing;
                }
                void Sync()
                {
                    // Native StickyNote swaps the label for a TextField. Its nested text input
                    // otherwise keeps the ordinary Editor field font and changes the row layout.
                    // Measure the actual visible label before that swap, including its theme/font size.
                    if (label.resolvedStyle.display == DisplayStyle.None || label.layout.width <= 0 || label.layout.height <= 0) return;
                    var source = label.resolvedStyle; var target = field.style;
                    target.width = label.layout.width; target.height = label.layout.height;
                    target.minWidth = 0; target.minHeight = 0;
                    target.flexGrow = 0; target.flexShrink = 0;
                    target.marginLeft = source.marginLeft; target.marginRight = source.marginRight;
                    target.marginTop = source.marginTop; target.marginBottom = source.marginBottom;
                    target.paddingLeft = source.paddingLeft; target.paddingRight = source.paddingRight;
                    target.paddingTop = source.paddingTop; target.paddingBottom = source.paddingBottom;
                    target.borderLeftWidth = source.borderLeftWidth; target.borderRightWidth = source.borderRightWidth;
                    target.borderTopWidth = source.borderTopWidth; target.borderBottomWidth = source.borderBottomWidth;
                    CopyTypography(field, source);
                    if (input != null) CopyTypography(input, source);
                    foreach (var element in text) CopyTypography(element, source);
                }
                label.RegisterCallback<GeometryChangedEvent>(_ => Sync());
                label.RegisterCallback<MouseDownEvent>(_ => Sync(), TrickleDown.TrickleDown);
                field.RegisterCallback<FocusInEvent>(_ => Sync());
            }
            internal void Refresh(ShaderGraphNote data)
            {
                if (title != data.title) title = data.title;
                if (contents != data.contents) contents = data.contents;
                bool themeChanged = theme != (StickyNoteTheme)data.theme;
                if (themeChanged) ResetHueStyles();
                theme = (StickyNoteTheme)data.theme; fontSize = (StickyNoteFontSize)data.fontSize;
                hueShift = data.hueShift;
                ApplyHue();
                var rect = new Rect(data.position, data.size);
                if (GetPosition() != rect) SetPosition(rect);
            }
            internal void ResetHueStyles()
            {
                colorsReady = false;
                foreach (var surface in colorSurfaces) NoteColors.Clear(surface);
                MarkDirtyRepaint();
            }
            void ReadNativeColors()
            {
                for (int i = 0; i < colorSurfaces.Length; i++) nativeColors[i] = new NoteColors(colorSurfaces[i].resolvedStyle);
                colorsReady = true;
            }
            internal float NativeHue
            {
                get { Color.RGBToHSV(colorsReady ? nativeColors[0].background : resolvedStyle.backgroundColor, out float h, out _, out _); return h; }
            }
            void ApplyHue()
            {
                if (hueShift == 0)
                { foreach (var surface in colorSurfaces) NoteColors.Clear(surface); return; }
                if (!colorsReady || panel == null) return;
                for (int i = 0; i < colorSurfaces.Length; i++) nativeColors[i].Apply(colorSurfaces[i], hueShift);
            }
            readonly struct NoteColors
            {
                internal readonly Color background;
                readonly Color foreground, left, right, top, bottom;
                internal NoteColors(IResolvedStyle style)
                {
                    background = style.backgroundColor; foreground = style.color;
                    left = style.borderLeftColor; right = style.borderRightColor;
                    top = style.borderTopColor; bottom = style.borderBottomColor;
                }
                static Color Shift(Color color, float degrees)
                {
                    Color.RGBToHSV(color, out float h, out float s, out float v);
                    if (s == 0) return color;
                    var result = Color.HSVToRGB(Mathf.Repeat(h + degrees / 360f, 1), s, v); result.a = color.a; return result;
                }
                internal void Apply(VisualElement element, float degrees)
                {
                    var style = element.style;
                    style.backgroundColor = Shift(background, degrees); style.color = Shift(foreground, degrees);
                    style.borderLeftColor = Shift(left, degrees); style.borderRightColor = Shift(right, degrees);
                    style.borderTopColor = Shift(top, degrees); style.borderBottomColor = Shift(bottom, degrees);
                }
                internal static void Clear(VisualElement element)
                {
                    var style = element.style;
                    style.backgroundColor = StyleKeyword.Null; style.color = StyleKeyword.Null;
                    style.borderLeftColor = StyleKeyword.Null; style.borderRightColor = StyleKeyword.Null;
                    style.borderTopColor = StyleKeyword.Null; style.borderBottomColor = StyleKeyword.Null;
                }
            }
            public override void BuildContextualMenu(ContextualMenuPopulateEvent e)
            {
                base.BuildContextualMenu(e);
                e.menu.AppendAction("Hue…", _ => UnityEditor.PopupWindow.Show(worldBound, new NoteHuePopup(canvas, Id)),
                    _ => canvas.window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
        private sealed class NoteHuePopup : PopupWindowContent
        {
            readonly Canvas canvas;
            readonly string id;
            internal NoteHuePopup(Canvas canvas, string id) { this.canvas = canvas; this.id = id; }
            public override Vector2 GetWindowSize() => new Vector2(300, 2 * EditorGUIUtility.singleLineHeight + 4 * EditorGUIUtility.standardVerticalSpacing);
            public override void OnGUI(Rect rect) { }
            public override void OnOpen()
            {
                var root = editorWindow.rootVisualElement; WhimTexUI.ApplyWindowStyles(root);
                var slider = WhimTexUI.ConfigureField(new Slider(-180, 180) { showInputField = true, name = "note-hue" });
                slider.SetValueWithoutNotify(canvas.window.Graph?.notes.Find(n => n.id == id)?.hueShift ?? 0);
                slider.RegisterValueChangedCallback(e => canvas.SetNoteHue(id, e.newValue)); root.Add(slider);
                slider.AddToClassList("whimtex-fx-preview-slider");
                float baseHue = canvas.NoteNativeHue(id);
                var ramp = new WhimTexColorPicker.ChannelRamp(t => Color.HSVToRGB(Mathf.Repeat(baseHue + t - .5f, 1), 1, 1))
                    { name = "note-hue-ramp", pickingMode = PickingMode.Ignore };
                ramp.AddToClassList("whimtex-fx-preview--slider");
                slider.Q(className: Slider.trackerUssClassName).Add(ramp);
                var thumb = slider.Q(className: Slider.draggerUssClassName);
                thumb.style.position = Position.Absolute; thumb.style.top = Length.Percent(50); thumb.style.marginTop = 0;
                thumb.Add(new ShaderPreviewThumb());
                root.Add(new Button(() => slider.value = 0) { text = "Reset" });
            }
        }
        private sealed class GroupView : Group, IGraphItemView
        {
            public string Id { get; }
            internal GroupView(ShaderGraphGroup data)
            {
                Id = data.id; viewDataKey = Id; title = data.title;
                capabilities = (capabilities | Capabilities.Copiable) & ~Capabilities.Groupable;
                SetPosition(new Rect(data.position, Vector2.zero));
            }
            public override bool AcceptsElement(GraphElement element, ref string reasonWhyNotAccepted)
            {
                if (element is INodeView || element is NoteView) return base.AcceptsElement(element, ref reasonWhyNotAccepted);
                reasonWhyNotAccepted = "Groups contain nodes and notes."; return false;
            }
        }
        private sealed partial class Canvas
        {
            readonly Dictionary<string, NoteView> noteViews = new Dictionary<string, NoteView>();
            readonly Dictionary<string, GroupView> groupViews = new Dictionary<string, GroupView>();

            void SetupAnnotations()
            {
                groupTitleChanged = (view, title) =>
                {
                    if (rebuilding || !window.Editable || view is not GroupView group) return;
                    window.Edit("Rename Shader Group", graph => graph.groups.Find(g => g.id == group.Id).title = title,
                        structural: false, rebuild: false, layoutOnly: true);
                };
                elementsAddedToGroup = (group, elements) => MembershipChanged(group, elements, true);
                elementsRemovedFromGroup = (group, elements) => MembershipChanged(group, elements, false);
                elementResized = element => { if (element is NoteView note) NoteChanged(note); };
            }
            internal void NoteChanged(NoteView view)
            {
                if (rebuilding || !window.Editable) return;
                var data = window.Graph.notes.Find(n => n.id == view.Id);
                if (data == null) return;
                var rect = view.GetPosition();
                if (data.title == view.title && data.contents == view.contents && data.theme == (int)view.theme &&
                    data.fontSize == (int)view.fontSize && data.position == rect.position && data.size == rect.size) return;
                window.Edit("Edit Shader Note", graph =>
                {
                    var note = graph.notes.Find(n => n.id == view.Id);
                    note.title = view.title; note.contents = view.contents;
                    note.position = rect.position; note.size = rect.size;
                    if (note.theme != (int)view.theme) view.ResetHueStyles();
                    note.theme = (int)view.theme; note.fontSize = (int)view.fontSize;
                }, structural: false, rebuild: false, layoutOnly: true);
            }
            internal float NoteNativeHue(string id) => noteViews.TryGetValue(id, out var note) ? note.NativeHue : 0;
            internal void SetNoteHue(string id, float value)
            {
                if (!window.Editable || !ProjectiveMatrix.Finite(value)) return;
                var note = window.Graph?.notes.Find(n => n.id == id);
                if (note == null) return;
                value = Mathf.Clamp(value, -180, 180);
                if (note.hueShift == value) return;
                window.Edit("Change Shader Note Hue", graph => graph.notes.Find(n => n.id == id).hueShift = value,
                    structural: false, rebuild: false, layoutOnly: true);
            }
            void MembershipChanged(Group view, IEnumerable<GraphElement> elements, bool added)
            {
                if (rebuilding || !window.Editable || view is not GroupView group || !window.Graph.groups.Exists(g => g.id == group.Id)) return;
                var ids = elements.OfType<IGraphItemView>().Select(e => e.Id).ToArray();
                if (ids.Length == 0) return;
                window.Edit(added ? "Group Shader Nodes" : "Ungroup Shader Nodes", graph =>
                {
                    var target = graph.groups.Find(g => g.id == group.Id);
                    foreach (string id in ids)
                    {
                        foreach (var other in graph.groups) if (added || other == target) other.elements.Remove(id);
                        if (added) target.elements.Add(id);
                    }
                }, structural: false, rebuild: false, layoutOnly: true);
            }
            void ClearAnnotations()
            {
                foreach (var group in groupViews.Values)
                { group.RemoveElementsWithoutNotification(group.containedElements.ToArray()); selection.Remove(group); RemoveElement(group); }
                foreach (var note in noteViews.Values) { selection.Remove(note); RemoveElement(note); }
                groupViews.Clear(); noteViews.Clear();
            }
            GraphElement ItemView(string id) => nodeViews.TryGetValue(id, out var node) ? node :
                noteViews.TryGetValue(id, out var note) ? note : groupViews.TryGetValue(id, out var group) ? group : null;
            void RestoreSelection(GraphElement view, HashSet<string> selected)
            {
                if (view is not IGraphItemView item || !selected.Contains(item.Id) || selection.Contains(view)) return;
                selection.Add(view); view.selected = true; view.OnSelected();
                view.RegisterCallback<DetachFromPanelEvent>(_ => selection.Remove(view));
            }
            void BuildAnnotations(HashSet<string> selected)
            {
                foreach (var data in window.Graph.notes)
                {
                    var note = new NoteView(this, data); noteViews.Add(data.id, note); AddElement(note); RestoreSelection(note, selected);
                }
                foreach (var data in window.Graph.groups)
                {
                    var group = new GroupView(data); groupViews.Add(data.id, group); AddElement(group);
                    foreach (string id in data.elements) if (ItemView(id) is GraphElement item) group.AddElement(item);
                    RestoreSelection(group, selected);
                }
            }
            void RefreshAnnotations()
            {
                bool previous = rebuilding; rebuilding = true;
                try
                {
                    foreach (var data in window.Graph.notes) if (noteViews.TryGetValue(data.id, out var note)) note.Refresh(data);
                    foreach (var data in window.Graph.groups) if (groupViews.TryGetValue(data.id, out var group))
                    {
                        if (group.title != data.title) group.title = data.title;
                        if (data.elements.Count == 0 && group.GetPosition().position != data.position) group.SetPosition(new Rect(data.position, Vector2.zero));
                    }
                }
                finally { rebuilding = previous; }
            }
            void StorePositions(ShaderGraph graph)
            {
                foreach (var data in graph.nodes) if (nodeViews.TryGetValue(data.id, out var view)) data.position = view.GetPosition().position;
                foreach (var data in graph.notes) if (noteViews.TryGetValue(data.id, out var note))
                { var rect = note.GetPosition(); data.position = rect.position; data.size = rect.size; }
                foreach (var data in graph.groups) if (groupViews.TryGetValue(data.id, out var group)) data.position = group.GetPosition().position;
            }
            void AddNote(Vector2 position)
            {
                if (!window.Editable || window.Graph.notes.Count >= 512) return;
                var note = new ShaderGraphNote { position = position, size = StickyNote.defaultSize };
                selectAfterRebuild = note.id;
                window.Edit("Add Shader Note", graph => graph.notes.Add(note), structural: false, layoutOnly: true);
            }
            void CreateGroup(Vector2 position)
            {
                if (!window.Editable || window.Graph.groups.Count >= 512) return;
                var ids = selection.Where(e => e is INodeView || e is NoteView).Cast<IGraphItemView>().Select(e => e.Id).ToList();
                var group = new ShaderGraphGroup { position = position, elements = ids };
                selectAfterRebuild = group.id;
                window.Edit("Create Shader Group", graph =>
                {
                    foreach (var existing in graph.groups) existing.elements.RemoveAll(ids.Contains);
                    graph.groups.Add(group);
                }, structural: false, layoutOnly: true);
            }
            void UngroupSelection()
            {
                var ids = selection.OfType<IGraphItemView>().Select(e => e.Id).ToHashSet();
                window.Edit("Ungroup Shader Nodes", graph =>
                {
                    graph.groups.RemoveAll(g => ids.Contains(g.id));
                    foreach (var group in graph.groups) group.elements.RemoveAll(ids.Contains);
                }, structural: false, layoutOnly: true);
            }
            void AnnotationMenu(ContextualMenuPopulateEvent e)
            {
                Vector2 point = contentViewContainer.WorldToLocal(e.mousePosition);
                e.menu.AppendAction("Add Note", _ => AddNote(point), _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Group Selection", _ => CreateGroup(point), _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Ungroup Selection", _ => UngroupSelection(), _ => window.Editable && selection.OfType<IGraphItemView>().Any()
                    ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
    }
}
