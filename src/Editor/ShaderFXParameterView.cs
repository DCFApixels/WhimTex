using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXParameterView : VisualElement
    {
        private sealed class ParameterGroupView
        {
            internal int id;
            internal string headerParameterId;
            internal VisualElement content;
            internal VisualElement root;
            internal readonly List<ParameterGroupView> pages = new List<ParameterGroupView>();
            internal Button tab;
            internal string stateKey;
        }

        private readonly ShaderParameterViewSource source;
        private readonly WhimTexDocument previewDocument;
        private readonly Layer previewLayer;
        private readonly int previewIndex;
        private string previewSource;
        private List<ShaderFXParameterControl> standalonePreviews = new List<ShaderFXParameterControl>();
        private readonly List<ShaderFXParameter> parameterLayout = new List<ShaderFXParameter>();
        private readonly Dictionary<string, ShaderFXParameter> parametersById = new Dictionary<string, ShaderFXParameter>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShaderFXParameter> parametersByName = new Dictionary<string, ShaderFXParameter>(StringComparer.Ordinal);
        private bool layoutBuilt;
        private readonly bool effectControl;
        private string controlSource, controlName;
        private readonly List<Action> refresh = new List<Action>();
        private readonly List<Action> postRefresh = new List<Action>();
        private readonly Dictionary<string, int> selectedTabs = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> expandedFoldouts = new Dictionary<string, bool>(StringComparer.Ordinal);

        internal ShaderFXParameterView(ShaderFX effect) : this(effect, false) { }
        internal ShaderFXParameterView(ShaderFX effect, bool effectControl, WhimTexDocument document = null, Layer layer = null, int fxIndex = -1)
            : this(new ShaderFXParameterViewSource(effect), effectControl, document, layer, fxIndex) { }

        internal ShaderFXParameterView(ShaderParameterViewSource source, bool effectControl = false,
            WhimTexDocument document = null, Layer layer = null, int fxIndex = -1)
        {
            this.source = source; this.effectControl = effectControl;
            previewDocument = document; previewLayer = layer; previewIndex = fxIndex;
            Refresh();
        }

        internal void Refresh()
        {
            if (source == null) return;
            parametersById.Clear();
            parametersByName.Clear();
            bool changed = !layoutBuilt || parameterLayout.Count != source.Parameters.Count;
            if (!effectControl && previewSource != source.Code)
            {
                previewSource = source.Code;
                try { ShaderFXMetadata.ParseWithPreviews(previewSource, false, out _, out standalonePreviews); }
                catch (FormatException) { standalonePreviews = new List<ShaderFXParameterControl>(); }
                changed = true;
            }
            if (effectControl && controlSource != source.Code)
            {
                controlSource = source.Code;
                string next = ShaderFXMetadata.ReadControl(controlSource, out _);
                changed |= next != controlName;
                controlName = next;
            }
            for (int i = 0; i < source.Parameters.Count; i++)
            {
                var p = source.Parameters[i];
                if (p != null)
                {
                    if (p.id != null && !parametersById.ContainsKey(p.id)) parametersById.Add(p.id, p);
                    if (p.name != null && !parametersByName.ContainsKey(p.name)) parametersByName.Add(p.name, p);
                }
                if (!changed && !SameLayout(parameterLayout[i], p)) changed = true;
            }
            if (changed)
            {
                layoutBuilt = true;
                parameterLayout.Clear();
                foreach (var p in source.Parameters) parameterLayout.Add(CopyLayout(p));
                Clear(); refresh.Clear(); postRefresh.Clear();
                if (effectControl)
                {
                    EnableInClassList("whimtex-fx-effect-control--seed", false);
                    if (controlName != null && parametersByName.TryGetValue(controlName, out var parameter) && parameter.controls.Count == 1)
                    {
                        var control = parameter.controls[0];
                        if (control.type == ShaderFXParameterType.Float)
                        {
                            bool alpha = parameter.name.IndexOf("Opacity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                parameter.name.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0;
                            VisualElement handle = alpha
                                ? new LayerActionIcon(LayerActionIcon.Kind.Alpha) { pickingMode = PickingMode.Position }
                                : new Label("↔");
                            handle.AddToClassList("whimtex-fx-effect-control-handle");
                            MakeGroupTitleDraggable(handle, parameter.id, control);
                            Add(handle);
                        }
                        AddGroupHeaderParameter(this, parameter, control);
                        EnableInClassList("whimtex-fx-effect-control--seed", control.seed);
                        EnableInClassList("whimtex-fx-effect-control--bool", control.type == ShaderFXParameterType.Bool);
                        EnableInClassList("whimtex-fx-effect-control--vector", control.type == ShaderFXParameterType.Vector2 ||
                            control.type == ShaderFXParameterType.Vector3 || control.type == ShaderFXParameterType.Vector);
                        tooltip = string.IsNullOrEmpty(control.tooltip) ? control.label ?? ObjectNames.NicifyVariableName(parameter.name) : control.tooltip;
                    }
                }
                else
                {
                    var rows = new List<(ShaderFXParameter parameter, ShaderFXParameterControl control)>();
                    foreach (var p in source.Parameters)
                        if (p != null)
                        {
                            foreach (var control in p.controls) rows.Add((p, control));
                        }
                    if (source is ShaderFXParameterViewSource)
                        foreach (var preview in standalonePreviews) rows.Add((null, preview));
                    rows.Sort((a, b) => a.control.order.CompareTo(b.control.order));
                    var containers = new List<(ShaderFXLayoutContainer definition, ParameterGroupView view)>();
                    foreach (var row in rows)
                    {
                        var path = row.control.containers ?? Array.Empty<ShaderFXLayoutContainer>();
                        int shared = 0;
                        while (shared < containers.Count && shared < path.Length && containers[shared].definition.Equals(path[shared])) shared++;
                        if (shared < containers.Count) containers.RemoveRange(shared, containers.Count - shared);
                        for (int i = shared; i < path.Length; i++)
                        {
                            var parent = containers.Count == 0 ? null : containers[containers.Count - 1].view;
                            var definition = path[i];
                            ParameterGroupView view = definition.kind == ShaderFXContainerKind.Tabs
                                ? AddTabs(parent?.content ?? this, definition)
                                : definition.kind == ShaderFXContainerKind.Tab
                                    ? AddTab(parent, definition)
                                    : AddGroup(parent?.content ?? this, definition);
                            containers.Add((definition, view));
                        }
                        bool header = false;
                        foreach (var container in containers)
                        {
                            if (row.parameter != null && row.parameter.id == container.view.headerParameterId) { header = true; break; }
                        }
                        if (!row.control.hidden && !header)
                            AddParameter(containers.Count == 0 ? this : containers[containers.Count - 1].view.content, row.parameter, row.control);
                    }
                }
            }
            if (effectControl)
            {
                EnableInClassList("whimtex-hidden", childCount == 0);
            }
            bool enabled = !source.IsReadOnly;
            if (effectControl && controlName != null && parametersByName.TryGetValue(controlName, out var mainControl) && mainControl.controls.Count == 1)
                enabled &= IsAvailable(mainControl.controls[0]);
            SetEnabled(enabled);
            foreach (var update in refresh) update();
            foreach (var update in postRefresh) update();
        }

        private ShaderFXParameter Find(string id)
        {
            return id != null && parametersById.TryGetValue(id, out var parameter) ? parameter : null;
        }

        private static bool SameArray<T>(T[] a, T[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            return true;
        }

        private static bool SameLayout(ShaderFXParameter a, ShaderFXParameter b)
        {
            if (a == null || b == null) return a == b;
            if (a.id != b.id || a.name != b.name || a.type != b.type ||
                a.hasMinimum != b.hasMinimum || a.hasMaximum != b.hasMaximum ||
                a.softMinimum != b.softMinimum || a.softMaximum != b.softMaximum || a.cyclicRange != b.cyclicRange ||
                !a.minimum.Equals(b.minimum) || !a.maximum.Equals(b.maximum) || a.controls.Count != b.controls.Count)
                return false;
            for (int i = 0; i < a.controls.Count; i++)
            {
                var x = a.controls[i]; var y = b.controls[i];
                if (x.type != y.type || x.order != y.order || x.tooltip != y.tooltip || x.label != y.label || x.seed != y.seed || x.previewFunction != y.previewFunction ||
                    !SameArray(x.previewParameters, y.previewParameters) || x.hasMinimum != y.hasMinimum || x.hasMaximum != y.hasMaximum ||
                    x.softMinimum != y.softMinimum || x.softMaximum != y.softMaximum || x.cyclicRange != y.cyclicRange ||
                    !x.minimum.Equals(y.minimum) || !x.maximum.Equals(y.maximum) ||
                    x.visibleIfParameter != y.visibleIfParameter || x.visibleIfNotEqual != y.visibleIfNotEqual ||
                    !SameArray(x.enableIf, y.enableIf) ||
                    !x.visibleIfValue.Equals(y.visibleIfValue) || !SameArray(x.headers, y.headers) || !SameArray(x.helpBoxes, y.helpBoxes) ||
                    x.hidden != y.hidden || x.inGroup != y.inGroup || x.groupId != y.groupId ||
                    x.groupTitle != y.groupTitle || x.groupHeaderParameter != y.groupHeaderParameter ||
                    !SameArray(x.containers, y.containers) ||
                    !SameArray(x.optionNames, y.optionNames) || !SameArray(x.optionValues, y.optionValues))
                    return false;
            }
            return true;
        }

        private static ShaderFXParameter CopyLayout(ShaderFXParameter p)
        {
            if (p == null) return null;
            var copy = new ShaderFXParameter { id = p.id, name = p.name, type = p.type,
                hasMinimum = p.hasMinimum, hasMaximum = p.hasMaximum,
                softMinimum = p.softMinimum, softMaximum = p.softMaximum, cyclicRange = p.cyclicRange, minimum = p.minimum, maximum = p.maximum };
            foreach (var c in p.controls)
                copy.controls.Add(new ShaderFXParameterControl { type = c.type, order = c.order, tooltip = c.tooltip, label = c.label, previewFunction = c.previewFunction,
                    previewParameters = c.previewParameters == null ? Array.Empty<string>() : (string[])c.previewParameters.Clone(),
                    headers = c.headers == null ? null : (string[])c.headers.Clone(),
                    helpBoxes = c.helpBoxes == null ? null : (string[])c.helpBoxes.Clone(),
                    hidden = c.hidden, seed = c.seed, inGroup = c.inGroup, groupId = c.groupId,
                    groupTitle = c.groupTitle, groupHeaderParameter = c.groupHeaderParameter,
                    containers = c.containers == null ? Array.Empty<ShaderFXLayoutContainer>() : (ShaderFXLayoutContainer[])c.containers.Clone(),
                    hasMinimum = c.hasMinimum, hasMaximum = c.hasMaximum,
                    softMinimum = c.softMinimum, softMaximum = c.softMaximum, cyclicRange = c.cyclicRange, minimum = c.minimum, maximum = c.maximum,
                    optionNames = c.optionNames == null ? null : (string[])c.optionNames.Clone(),
                    optionValues = c.optionValues == null ? null : (float[])c.optionValues.Clone(),
                    visibleIfParameter = c.visibleIfParameter, visibleIfNotEqual = c.visibleIfNotEqual, visibleIfValue = c.visibleIfValue,
                    enableIf = c.enableIf == null ? Array.Empty<ShaderFXEnableCondition>() : (ShaderFXEnableCondition[])c.enableIf.Clone() });
            return copy;
        }

        private bool IsVisible(ShaderFXParameterControl condition)
            => MatchesCondition(condition.visibleIfParameter, condition.visibleIfNotEqual, condition.visibleIfValue);

        private bool IsAvailable(ShaderFXParameterControl control)
        {
            if (control.enableIf != null)
                foreach (var condition in control.enableIf)
                    if (!MatchesCondition(condition.parameter, condition.notEqual, condition.value)) return false;
            return true;
        }

        private bool MatchesCondition(string parameter, bool notEqual, float value)
        {
            parametersByName.TryGetValue(parameter, out var driver);
            if (driver == null) return false;
            bool floatDriver = false;
            foreach (var control in driver.controls)
                if (control.type == ShaderFXParameterType.Float) { floatDriver = true; break; }
            bool equal = floatDriver ? Mathf.Approximately(driver.floatValue, value) : driver.floatValue == value;
            return notEqual ? !equal : equal;
        }

        private void Change(string id, Action<ShaderFXParameter> update)
        {
            if (source.IsReadOnly) return;
            source.Change(id, update);
            Refresh();
        }

        private ParameterGroupView AddGroup(VisualElement parent, ShaderFXLayoutContainer definition)
        {
            var root = new VisualElement();
            root.AddToClassList("whimtex-fx-parameter-group");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("whimtex-fx-parameter-group--light");
            parent.Add(root);

            string headerParameterId = null;
            VisualElement content;
            Foldout foldout = null;
            if (definition.kind == ShaderFXContainerKind.Foldout)
            {
                string key = ContainerStateKey(definition);
                bool expanded = expandedFoldouts.TryGetValue(key, out bool saved) ? saved : SessionState.GetBool(key, true);
                foldout = new Foldout { value = expanded };
                foldout.AddToClassList("whimtex-fx-parameter-foldout");
                foldout.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != foldout) return;
                    expandedFoldouts[key] = evt.newValue;
                    SessionState.SetBool(key, evt.newValue);
                    evt.StopPropagation();
                });
                root.Add(foldout);
            }
            if (!string.IsNullOrEmpty(definition.title) || !string.IsNullOrEmpty(definition.headerParameter))
            {
                var header = new VisualElement();
                header.AddToClassList("whimtex-fx-parameter-group-header");
                if (foldout != null)
                {
                    var toggle = foldout.Q<Toggle>();
                    toggle.AddToClassList("whimtex-fx-parameter-foldout-toggle");
                    toggle.Add(header);
                    header.AddToClassList("whimtex-fx-parameter-foldout-header");
                }
                else root.Add(header);
                ShaderFXParameter linkedParameter = null;
                ShaderFXParameterControl linkedControl = null;
                if (!string.IsNullOrEmpty(definition.headerParameter) &&
                    parametersByName.TryGetValue(definition.headerParameter, out linkedParameter) &&
                    linkedParameter.controls.Count == 1)
                    linkedControl = linkedParameter.controls[0];

                if (linkedParameter != null && linkedControl != null &&
                    linkedControl.type == ShaderFXParameterType.Bool)
                {
                    var toggle = new Toggle();
                    toggle.AddToClassList("whimtex-fx-parameter-group-toggle");
                    toggle.tooltip = linkedControl.tooltip;
                    string id = linkedParameter.id;
                    headerParameterId = id;
                    toggle.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue ? 1f : 0f));
                    header.Add(WithPreview(toggle, linkedParameter, linkedControl));
                    refresh.Add(() => toggle.SetValueWithoutNotify(Find(id)?.BoolValue ?? false));
                }
                if (!string.IsNullOrEmpty(definition.title))
                {
                    var title = new Label(definition.title);
                    title.AddToClassList("whimtex-fx-parameter-group-title");
                    if (linkedParameter != null && linkedControl?.type == ShaderFXParameterType.Float)
                        MakeGroupTitleDraggable(title, linkedParameter.id, linkedControl);
                    header.Add(title);
                }
                if (linkedParameter != null && linkedControl != null &&
                    linkedControl.type != ShaderFXParameterType.Bool &&
                    AddGroupHeaderParameter(header, linkedParameter, linkedControl))
                    headerParameterId = linkedParameter.id;
                if (foldout != null)
                {
                    header.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (evt.target is VisualElement target && target != header && !target.ClassListContains("whimtex-fx-parameter-group-title")) evt.StopPropagation();
                    });
                    header.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopPropagation());
                }
                if (!string.IsNullOrEmpty(definition.title)) header.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    BuildContainerMenu(evt.menu, definition.id);
                    evt.StopPropagation();
                }));
                content = new VisualElement();
                content.AddToClassList("whimtex-fx-parameter-group-content");
                if (foldout != null) foldout.Add(content); else root.Add(content);
            }
            else
            {
                content = new VisualElement();
                content.AddToClassList("whimtex-fx-parameter-group-content");
                root.Add(content);
            }

            postRefresh.Add(() =>
            {
                bool hasVisibleContent = false;
                foreach (var child in content.Children())
                    if (!child.ClassListContains("whimtex-fx-conditional-parameter--hidden"))
                    {
                        hasVisibleContent = true;
                        break;
                    }
                root.EnableInClassList("whimtex-fx-parameter-group--empty", !hasVisibleContent);
            });
            return new ParameterGroupView { id = definition.id, headerParameterId = headerParameterId, content = content, root = root };
        }

        private string ContainerStateKey(ShaderFXLayoutContainer definition)
        {
            string owner = source is ShaderFXParameterViewSource fxSource ? fxSource.Effect.ShaderKey :
                source.Parameters.Count > 0 ? source.Parameters[0].id : source.GetType().Name;
            return "WhimTex.FX.Layout." + owner + "." + definition.kind + "." + definition.id + "." + definition.title;
        }

        private ParameterGroupView AddTabs(VisualElement parent, ShaderFXLayoutContainer definition)
        {
            var root = new VisualElement { name = "whimtex-fx-tabs-" + definition.id };
            root.AddToClassList("whimtex-fx-tabs");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("whimtex-fx-tabs--light");
            parent.Add(root);
            if (!string.IsNullOrEmpty(definition.title))
            {
                var title = new Label(definition.title);
                title.AddToClassList("whimtex-fx-tabs-title");
                root.Add(title);
            }
            var strip = new VisualElement();
            strip.AddToClassList("whimtex-fx-tabs-strip");
            root.Add(strip);
            string key = ContainerStateKey(definition);
            var view = new ParameterGroupView { id = definition.id, root = root, content = strip, stateKey = key };
            void Select(int index)
            {
                for (int i = 0; i < view.pages.Count; i++)
                {
                    view.pages[i].root.EnableInClassList("whimtex-fx-tab-page--inactive", i != index);
                    view.pages[i].tab.EnableInClassList("whimtex-fx-tab--selected", i == index);
                }
            }
            strip.RegisterCallback<KeyDownEvent>(evt =>
            {
                int index = view.pages.FindIndex(p => p.tab == evt.target);
                if (index < 0 || view.pages.Count == 0) return;
                if (evt.keyCode == KeyCode.LeftArrow) index = (index + view.pages.Count - 1) % view.pages.Count;
                else if (evt.keyCode == KeyCode.RightArrow) index = (index + 1) % view.pages.Count;
                else if (evt.keyCode == KeyCode.Home) index = 0;
                else if (evt.keyCode == KeyCode.End) index = view.pages.Count - 1;
                else return;
                selectedTabs[key] = index; SessionState.SetInt(key, index); Select(index);
                view.pages[index].tab.Focus(); evt.StopPropagation();
            });
            postRefresh.Add(() =>
            {
                int selected = selectedTabs.TryGetValue(key, out int saved) ? saved : SessionState.GetInt(key, 0);
                selected = Mathf.Clamp(selected, 0, view.pages.Count - 1);
                Select(selected);
            });
            return view;
        }

        private ParameterGroupView AddTab(ParameterGroupView parent, ShaderFXLayoutContainer definition)
        {
            var page = new VisualElement { name = "whimtex-fx-tab-page-" + definition.id };
            page.AddToClassList("whimtex-fx-tab-page");
            parent.root.Add(page);
            var tab = new Button { text = definition.title, name = "whimtex-fx-tab-" + definition.id };
            tab.AddToClassList("whimtex-fx-tab");
            VisualElement header = tab;
            string headerParameterId = null;
            if (!string.IsNullOrEmpty(definition.headerParameter) &&
                parametersByName.TryGetValue(definition.headerParameter, out var linkedParameter) &&
                linkedParameter.controls.Count == 1 && linkedParameter.controls[0].type == ShaderFXParameterType.Bool)
            {
                string id = linkedParameter.id;
                headerParameterId = id;
                var toggle = new Toggle { tooltip = linkedParameter.controls[0].tooltip };
                toggle.AddToClassList("whimtex-fx-parameter-group-toggle");
                toggle.AddToClassList("whimtex-fx-tab-toggle");
                toggle.RegisterValueChangedCallback(evt => Change(id, p => p.floatValue = evt.newValue ? 1f : 0f));
                refresh.Add(() => toggle.SetValueWithoutNotify(Find(id)?.BoolValue ?? false));
                tab.AddToClassList("whimtex-fx-tab--toggle");
                header = new VisualElement();
                header.AddToClassList("whimtex-fx-tab-header");
                header.Add(tab);
                header.Add(toggle);
            }
            header.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                BuildContainerMenu(evt.menu, definition.id);
                evt.StopPropagation();
            }));
            parent.content.Add(header);
            var view = new ParameterGroupView { id = definition.id, headerParameterId = headerParameterId, root = page, content = page, tab = tab };
            int index = parent.pages.Count;
            parent.pages.Add(view);
            tab.clicked += () =>
            {
                string key = parent.stateKey;
                selectedTabs[key] = index; SessionState.SetInt(key, index);
                foreach (var update in postRefresh) update();
            };
            return view;
        }

        private bool HasContainerHeader(int groupId)
        {
            foreach (var parameter in source.Parameters)
                if (parameter != null && ShaderParameterGroupClipboard.ContainerControl(parameter, groupId) is ShaderFXParameterControl control)
                    foreach (var scope in control.containers)
                        if (scope.id == groupId && !string.IsNullOrEmpty(scope.title)) return true;
            return false;
        }

        private void BuildContainerMenu(DropdownMenu menu, int groupId)
        {
            if (!HasContainerHeader(groupId)) return;
            menu.AppendAction("Copy", _ => CopyContainer(groupId));
            menu.AppendAction("Paste", _ => PasteContainer(groupId),
                !source.IsReadOnly && ShaderParameterGroupClipboard.TryPaste(source, groupId, out _) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendSeparator();
            menu.AppendAction("Reset", _ => ResetContainer(groupId),
                !source.IsReadOnly && TryContainerDefaults(groupId, out _) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        private void CopyContainer(int groupId)
        {
            if (HasContainerHeader(groupId)) ShaderParameterGroupClipboard.Copy(source, groupId);
        }

        private void PasteContainer(int groupId)
        {
            if (source.IsReadOnly || !HasContainerHeader(groupId) || !ShaderParameterGroupClipboard.TryPaste(source, groupId, out var values)) return;
            source.ChangeParameters("Paste FX Parameters", values);
            Refresh();
        }

        private bool TryContainerDefaults(int groupId, out List<ShaderFXParameter> values)
        {
            values = null;
            List<ShaderFXParameter> defaults;
            try { defaults = ShaderParameterGroupClipboard.Parameters(ShaderFXMetadata.Parse(source.Code, false, out _), groupId); }
            catch (FormatException) { return false; }
            var targets = ShaderParameterGroupClipboard.Parameters(source.Parameters, groupId);
            if (targets.Count == 0 || defaults.Count != targets.Count) return false;
            var prepared = new List<ShaderFXParameter>(targets.Count);
            foreach (var target in targets)
            {
                var value = defaults.Find(p => p.name == target.name && p.type == target.type);
                if (value == null) return false;
                var next = target.Copy();
                ShaderFXMetadata.CopyValue(value, next);
                prepared.Add(next);
            }
            values = prepared;
            return true;
        }

        private void ResetContainer(int groupId)
        {
            if (source.IsReadOnly || !HasContainerHeader(groupId) || !TryContainerDefaults(groupId, out var values)) return;
            source.ChangeParameters("Reset FX Parameters", values);
            Refresh();
        }

        private void MakeGroupTitleDraggable(VisualElement title, string id, ShaderFXParameterControl control)
        {
            title.AddToClassList("whimtex-fx-parameter-group-title--draggable");
            title.tooltip = string.IsNullOrEmpty(control.tooltip)
                ? "Drag horizontally to adjust the value. Hold Shift for fine control or Ctrl for faster changes."
                : control.tooltip + "\nDrag horizontally to adjust the value. Hold Shift for fine control or Ctrl for faster changes.";

            bool dragging = false;
            bool changed = false;
            int activePointer = -1;
            float startX = 0f;
            float startValue = 0f;
            float unitsPerPixel = 0.01f;

            void EndDrag(int pointerId)
            {
                if (!dragging) return;
                dragging = false;
                source.EndDrag(changed);
                if (title.HasPointerCapture(pointerId)) title.ReleasePointer(pointerId);
                activePointer = -1;
                changed = false;
            }

            title.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || dragging || source.IsReadOnly) return;
                ShaderFXParameter parameter = Find(id);
                if (parameter == null) return;

                dragging = true;
                changed = false;
                activePointer = evt.pointerId;
                startX = evt.position.x;
                startValue = parameter.floatValue;
                float range = control.hasMinimum && control.hasMaximum
                    ? control.maximum - control.minimum
                    : Mathf.Max(1f, Mathf.Abs(startValue));
                unitsPerPixel = Mathf.Max(range / 200f, 0.0001f);
                source.BeginDrag();
                title.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            title.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || evt.pointerId != activePointer || !title.HasPointerCapture(evt.pointerId)) return;
                ShaderFXParameter parameter = Find(id);
                if (parameter == null) { EndDrag(evt.pointerId); return; }
                float sensitivity = evt.shiftKey ? 0.1f : evt.ctrlKey ? 10f : 1f;
                float value = parameter.Clamp(startValue + (evt.position.x - startX) * unitsPerPixel * sensitivity);
                if (!Mathf.Approximately(value, parameter.floatValue))
                {
                    source.Change(id, p => p.floatValue = value, false);
                    changed = true;
                    Refresh();
                }
                evt.StopPropagation();
            });

            title.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != activePointer) return;
                EndDrag(evt.pointerId);
                evt.StopPropagation();
            });
            title.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (dragging) EndDrag(activePointer);
            });
        }

        private bool AddGroupHeaderParameter(VisualElement header, ShaderFXParameter parameter, ShaderFXParameterControl control)
        {
            string id = parameter.id;
            VisualElement field;
            switch (control.type)
            {
                case ShaderFXParameterType.Bool:
                    var toggle = new Toggle();
                    toggle.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue ? 1 : 0));
                    refresh.Add(() => toggle.SetValueWithoutNotify(Find(id).BoolValue));
                    field = toggle;
                    break;
                case ShaderFXParameterType.Enum:
                    var choices = new List<string>();
                    foreach (string option in control.optionNames) choices.Add(ObjectNames.NicifyVariableName(option));
                    for (int i = 0; i < choices.Count; i++)
                        for (int j = i + 1; j < choices.Count; j++)
                            if (choices[i] == choices[j])
                            {
                                choices[i] += " (" + control.optionNames[i] + ")";
                                choices[j] += " (" + control.optionNames[j] + ")";
                            }
                    var dropdown = new DropdownField(string.Empty, choices, 0);
                    TwoChoiceDropdown.Attach(dropdown);
                    dropdown.RegisterValueChangedCallback(e =>
                    {
                        int index = choices.IndexOf(e.newValue);
                        if (index >= 0) Change(id, p => p.floatValue = control.optionValues[index]);
                    });
                    refresh.Add(() =>
                    {
                        int index = Array.IndexOf(control.optionValues, Find(id).floatValue);
                        dropdown.SetValueWithoutNotify(index >= 0 ? choices[index] : "Custom (" + Find(id).floatValue.ToString("G9") + ")");
                    });
                    field = dropdown;
                    break;
                case ShaderFXParameterType.Float:
                    var number = new FloatField(string.Empty);
                    number.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = parameter.Clamp(e.newValue)));
                    refresh.Add(() => number.SetValueWithoutNotify(Find(id).floatValue));
                    field = number;
                    break;
                case ShaderFXParameterType.Color:
                    field = source.CreateColorField(string.Empty, id, refresh);
                    break;
                case ShaderFXParameterType.Vector2:
                    if (control.hasMinimum && control.hasMaximum)
                    {
                        BaseField<Vector2> interval = control.cyclicRange
                            ? new WhimTexCyclicRangeField(string.Empty, control.minimum, control.maximum)
                            : new WhimTexMinMaxRangeField(string.Empty, control.minimum, control.maximum);
                        interval.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                        refresh.Add(() => interval.SetValueWithoutNotify(Find(id).vectorValue));
                        field = interval;
                        break;
                    }
                    var vector2 = new Vector2Field(string.Empty);
                    vector2.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    refresh.Add(() => vector2.SetValueWithoutNotify(Find(id).vectorValue));
                    field = vector2;
                    break;
                case ShaderFXParameterType.Vector3:
                    var vector3 = new Vector3Field(string.Empty);
                    vector3.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    refresh.Add(() => vector3.SetValueWithoutNotify(Find(id).vectorValue));
                    field = vector3;
                    break;
                case ShaderFXParameterType.Vector:
                    var vector4 = new Vector4Field(string.Empty);
                    vector4.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    refresh.Add(() => vector4.SetValueWithoutNotify(Find(id).vectorValue));
                    field = vector4;
                    break;
                default:
                    return false;
            }

            if (control.seed) field = SeedField(field, id, control);
            field.AddToClassList("whimtex-fx-group-header-field");
            if (!string.IsNullOrEmpty(control.tooltip)) field.tooltip = control.tooltip;
            field = WithPreview(field, parameter, control);
            header.Add(field);
            return true;
        }

        private static Button CreateCanvasEditButton(string name, Action edit, string tooltip)
        {
            var button = new Button(edit) { name = name, tooltip = "Edit on Canvas. " + tooltip };
            button.AddToClassList("whimtex-fx-parameter-action");
            button.Add(WhimTexWindow.CreateTransformToolIcon());
            return button;
        }

        private VisualElement SeedField(VisualElement field, string id, ShaderFXParameterControl control)
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-fx-seed-row");
            field.AddToClassList("whimtex-fx-seed-input");
            row.Add(field);
            var button = new Button(() => RandomizeSeed(id, control))
            {
                name = "randomizeSeed", text = "Random", tooltip = "Choose an integer within the declared range."
            };
            button.AddToClassList("whimtex-fx-seed-random");
            row.Add(button);
            return row;
        }

        private void RandomizeSeed(string id, ShaderFXParameterControl control) =>
            Change(id, p => p.floatValue = ShaderFXSeed.NextValue(control));

        private static void AddCanvasField(VisualElement parent, VisualElement field, Button edit)
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-fx-canvas-row");
            field.AddToClassList("whimtex-fx-canvas-field");
            row.Add(field);
            row.Add(edit);
            parent.Add(row);
        }

        private void AddParameter(VisualElement parent, ShaderFXParameter declaration, ShaderFXParameterControl control)
        {
            VisualElement rowRoot = parent;
            if (!string.IsNullOrEmpty(control.visibleIfParameter) || control.enableIf?.Length > 0)
            {
                rowRoot = new VisualElement();
                rowRoot.AddToClassList("whimtex-fx-conditional-parameter");
                parent.Add(rowRoot);
                ShaderFXParameterControl condition = control;
                refresh.Add(() =>
                {
                    bool visible = string.IsNullOrEmpty(condition.visibleIfParameter) || IsVisible(condition);
                    var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                    if (rowRoot.style.display.value != display) rowRoot.style.display = display;
                    rowRoot.EnableInClassList("whimtex-fx-conditional-parameter--hidden", !visible);
                    rowRoot.SetEnabled(IsAvailable(condition));
                });
            }

            if (control.headers != null)
                foreach (string title in control.headers)
                {
                    var heading = new Label(title);
                    heading.AddToClassList("whimtex-fx-parameter-header");
                    rowRoot.Add(heading);
                }
            if (control.helpBoxes != null)
                foreach (string message in control.helpBoxes)
                    rowRoot.Add(new HelpBox(message, HelpBoxMessageType.Info));
            if (declaration == null)
            {
                if (source is ShaderFXParameterViewSource fxSource)
                    rowRoot.Add(new ShaderFXPreviewElement(fxSource.Effect, null, control, false, previewDocument, previewLayer, previewIndex));
                return;
            }
            int firstChild = rowRoot.childCount;
            declaration = declaration.Copy();
            declaration.type = control.type;
            declaration.hasMinimum = control.hasMinimum; declaration.hasMaximum = control.hasMaximum;
            declaration.softMinimum = control.softMinimum; declaration.softMaximum = control.softMaximum;
            declaration.cyclicRange = control.cyclicRange;
            declaration.minimum = control.minimum; declaration.maximum = control.maximum;
            string id = declaration.id;
            string label = !string.IsNullOrWhiteSpace(control.label)
                ? control.label
                : ObjectNames.NicifyVariableName(declaration.name.TrimStart('_'));
            switch (declaration.type)
            {
                case ShaderFXParameterType.Curve:
                    var curve = new CurveField(label);
                    curve.RegisterValueChangedCallback(e => Change(id, p => p.curveValue = WhimTexCurveTexture.Copy(e.newValue)));
                    rowRoot.Add(curve);
                    refresh.Add(() => curve.SetValueWithoutNotify(Find(id).curveValue ?? WhimTexCurveTexture.Default()));
                    break;
                case ShaderFXParameterType.Gradient:
                    var gradient = new WhimTexGradientValueField(label);
                    gradient.RegisterValueChangedCallback(e => Change(id, p => p.gradientValue = e.newValue?.Clone() ?? new WhimTexGradient()));
                    rowRoot.Add(gradient);
                    refresh.Add(() => gradient.SetValueWithoutNotify(Find(id).gradientValue ??= new WhimTexGradient()));
                    break;
                case ShaderFXParameterType.Enum:
                    var choices = new List<string>();
                    foreach (var option in control.optionNames) choices.Add(ObjectNames.NicifyVariableName(option));
                    for (int i = 0; i < choices.Count; i++)
                        for (int j = i + 1; j < choices.Count; j++)
                            if (choices[i] == choices[j])
                            {
                                choices[i] += " (" + control.optionNames[i] + ")";
                                choices[j] += " (" + control.optionNames[j] + ")";
                            }
                    var dropdown = new DropdownField(label, choices, 0);
                    TwoChoiceDropdown.Attach(dropdown);
                    dropdown.RegisterValueChangedCallback(e =>
                    {
                        int index = choices.IndexOf(e.newValue);
                        if (index >= 0) Change(id, p => p.floatValue = control.optionValues[index]);
                    });
                    rowRoot.Add(dropdown);
                    refresh.Add(() =>
                    {
                        int index = Array.IndexOf(control.optionValues, Find(id).floatValue);
                        dropdown.SetValueWithoutNotify(index >= 0 ? choices[index] : "Custom (" + Find(id).floatValue.ToString("G9") + ")");
                    });
                    break;
                case ShaderFXParameterType.Bool:
                    var toggle = new Toggle(label);
                    toggle.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue ? 1f : 0f));
                    rowRoot.Add(toggle);
                    refresh.Add(() => toggle.SetValueWithoutNotify(Find(id).BoolValue));
                    break;
                case ShaderFXParameterType.Float:
                    if (control.seed)
                    {
                        var field = new FloatField(label);
                        field.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        rowRoot.Add(SeedField(field, id, control));
                        refresh.Add(() => field.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    else if (declaration.HasSoftRange || declaration.cyclicRange)
                    {
                        var soft = new WhimTexSoftRangeField(label, declaration.minimum, declaration.maximum, declaration.softMinimum, declaration.softMaximum, declaration.cyclicRange);
                        soft.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue));
                        rowRoot.Add(soft);
                        refresh.Add(() => soft.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    else if (declaration.hasMinimum && declaration.hasMaximum && declaration.minimum < declaration.maximum)
                    {
                        var slider = new Slider(label, declaration.minimum, declaration.maximum) { showInputField = true };
                        slider.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        rowRoot.Add(slider);
                        var sliderInput = slider.Q<TextField>();
                        float displayedValue = float.NaN;
                        string displayedText = null;
                        refresh.Add(() =>
                        {
                            float value = Find(id).floatValue;
                            if (displayedText == null || !value.Equals(displayedValue))
                            {
                                displayedValue = value;
                                displayedText = value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
                            }
                            slider.SetValueWithoutNotify(value);
                            sliderInput?.SetValueWithoutNotify(displayedText);
                        });
                    }
                    else
                    {
                        var field = new FloatField(label);
                        field.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        rowRoot.Add(field);
                        refresh.Add(() => field.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    break;
                case ShaderFXParameterType.Vector2:
                case ShaderFXParameterType.Point:
                    if (declaration.IsInterval)
                    {
                        BaseField<Vector2> interval = declaration.cyclicRange
                            ? new WhimTexCyclicRangeField(label, declaration.minimum, declaration.maximum)
                            : new WhimTexMinMaxRangeField(label, declaration.minimum, declaration.maximum);
                        interval.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                        refresh.Add(() => interval.SetValueWithoutNotify(Find(id).vectorValue));
                        rowRoot.Add(interval);
                        break;
                    }
                    var vector2 = new Vector2Field(label);
                    if (declaration.type == ShaderFXParameterType.Point)
                        vector2.tooltip = source.CanEditOnCanvas
                            ? "Canvas UV: bottom-left (0, 0), top-right (1, 1). Values outside the canvas are allowed."
                            : "Tip UV: bottom-left (0, 0), top-right (1, 1). Values outside the tip are allowed.";
                    vector2.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    refresh.Add(() => vector2.SetValueWithoutNotify(Find(id).vectorValue));
                    if (declaration.type == ShaderFXParameterType.Point && source.CanEditOnCanvas)
                        AddCanvasField(rowRoot, vector2, CreateCanvasEditButton("editFXPoint", () => source.EditOnCanvas(ShaderFXParameterType.Point, id),
                            "Drag the point handle, including outside the canvas."));
                    else rowRoot.Add(vector2);
                    break;
                case ShaderFXParameterType.Vector3:
                case ShaderFXParameterType.Normal:
                    var vector3 = new Vector3Field(label);
                    bool normal = declaration.type == ShaderFXParameterType.Normal;
                    vector3.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = normal ? ShaderFXParameter.NormalizeNormal(e.newValue) : e.newValue));
                    refresh.Add(() => vector3.SetValueWithoutNotify(Find(id).vectorValue));
                    if (normal && source.CanEditOnCanvas)
                        AddCanvasField(rowRoot, vector3, CreateCanvasEditButton("editFXNormal", () => source.EditOnCanvas(ShaderFXParameterType.Normal, id),
                            "Drag the direction handle. Click its endpoint to switch hemisphere."));
                    else rowRoot.Add(vector3);
                    break;
                case ShaderFXParameterType.Vector:
                    var vector = new Vector4Field(label);
                    vector.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    rowRoot.Add(vector); refresh.Add(() => vector.SetValueWithoutNotify(Find(id).vectorValue));
                    break;
                case ShaderFXParameterType.Color:
                    rowRoot.Add(source.CreateColorField(label, id, refresh));
                    break;
                case ShaderFXParameterType.Texture2D:
                    rowRoot.Add(source.CreateTextureField(label, id, refresh));
                    break;
                case ShaderFXParameterType.Transform2D:
                    var foldout = new Foldout { text = label, value = false };
                    foldout.AddToClassList("whimtex-fx-transform");
                    var transformHeader = new VisualElement();
                    transformHeader.AddToClassList("whimtex-fx-transform-header");
                    var transformToggle = foldout.Q<Toggle>();
                    foldout.hierarchy.Insert(0, transformHeader);
                    transformHeader.Add(transformToggle);
                    var editTransform = CreateCanvasEditButton("editFXTransform", () => source.EditOnCanvas(ShaderFXParameterType.Transform2D, id),
                        "Toggle the green FX frame on the selected layer.");
                    var resetTransform = new Button(() => Change(id, p => p.transformValue = ShaderFXTransform.Default))
                    {
                        name = "resetFXTransform", text = "↺", tooltip = "Reset Transform"
                    };
                    resetTransform.AddToClassList("whimtex-fx-parameter-action");
                    if (source.CanEditOnCanvas) transformHeader.Add(editTransform);
                    transformHeader.Add(resetTransform);
                    rowRoot.Add(foldout);
                    var position = new Vector2Field("Position");
                    var size = new Vector2Field("Size");
                    var rotation = new DoubleField("Rotation");
                    Vector2 Dimensions() => source.Dimensions;
                    position.tooltip = "Normalized input coordinates. (0.5, 0.5) is the image center.";
                    size.tooltip = "Relative to the input image. (1, 1) covers the whole image.";
                    position.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.EditPosition(e.newValue, Dimensions())));
                    size.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.EditSize(new Vector2(ShaderFXTransform.SafeSize(e.newValue.x), ShaderFXTransform.SafeSize(e.newValue.y)), Dimensions())));
                    rotation.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.EditRotation(e.newValue, Dimensions())));
                    foldout.Add(position); foldout.Add(size); foldout.Add(rotation);
                    refresh.Add(() => { var p = Find(id); p.transformValue.GetDisplay(Dimensions(), out var location, out var scale, out var angle); position.SetValueWithoutNotify(location); size.SetValueWithoutNotify(scale); rotation.SetValueWithoutNotify(angle); });
                    break;
            }
            if (!string.IsNullOrEmpty(control.tooltip))
                for (int i = firstChild; i < rowRoot.childCount; i++) rowRoot[i].tooltip = control.tooltip;
            if (!string.IsNullOrEmpty(control.previewFunction) && rowRoot.childCount > firstChild)
            {
                var field = rowRoot[firstChild];
                var decorated = WithPreview(field, declaration, control);
                if (decorated != field) rowRoot.Insert(firstChild, decorated);
            }
        }

        private VisualElement WithPreview(VisualElement field, ShaderFXParameter parameter, ShaderFXParameterControl control)
        {
            if (string.IsNullOrEmpty(control.previewFunction) || source is not ShaderFXParameterViewSource fxSource) return field;
            Slider slider = field as Slider ?? field.Q<Slider>();
            MinMaxSlider interval = field as MinMaxSlider ?? field.Q<MinMaxSlider>();
            var cyclic = field as WhimTexCyclicRangeField;
            var track = cyclic?.Track ?? slider?.Q(className: Slider.trackerUssClassName) ?? interval?.Q(className: MinMaxSlider.trackerUssClassName);
            bool embedded = track != null;
            var preview = new ShaderFXPreviewElement(fxSource.Effect, parameter, control, embedded, previewDocument, previewLayer, previewIndex);
            if (embedded)
            {
                field.AddToClassList("whimtex-fx-preview-range");
                track.Add(preview);
                if (cyclic != null) return field;
                if (interval != null)
                {
                    interval.AddToClassList("whimtex-fx-preview-minmax");
                    interval.Q(className: MinMaxSlider.minThumbUssClassName)?.Add(new ShaderFXPreviewThumb(-1));
                    interval.Q(className: MinMaxSlider.maxThumbUssClassName)?.Add(new ShaderFXPreviewThumb(1));
                    return field;
                }
                slider.AddToClassList("whimtex-fx-preview-slider");
                var thumb = slider.Q(className: Slider.draggerUssClassName);
                if (thumb != null)
                {
                    // The native theme also positions the dragger. Anchor its vertical geometry
                    // explicitly, leaving Unity's horizontal translate/input handling intact.
                    thumb.style.position = Position.Absolute;
                    thumb.style.top = new Length(50, LengthUnit.Percent);
                    thumb.style.marginTop = 0;
                    thumb.Add(new ShaderFXPreviewThumb());
                }
                return field;
            }
            var row = new VisualElement();
            row.AddToClassList("whimtex-fx-preview-row");
            field.AddToClassList("whimtex-fx-preview-input");
            row.Add(field); row.Add(preview);
            return row;
        }
    }
}
