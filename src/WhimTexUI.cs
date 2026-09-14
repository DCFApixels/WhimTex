using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexUI
    {
        private static StyleSheet splitViewStyles;

        internal static void ConsumeEvent(EventBase evt)
        {
            var element = evt.currentTarget as VisualElement ?? evt.target as VisualElement;
            element?.panel?.focusController?.IgnoreEvent(evt);
            evt.StopImmediatePropagation();
        }

        internal static bool ApplyWindowStyles(VisualElement root)
        {
            if (splitViewStyles == null)
                splitViewStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    AssetDatabase.GUIDToAssetPath("933b61aeef77442fb4567dbf6f21ce96"));
            if (splitViewStyles == null)
                return false;

            if (!root.styleSheets.Contains(splitViewStyles))
                root.styleSheets.Add(splitViewStyles);
            root.AddToClassList("whimtex-theme");
            return true;
        }

        internal static void StyleSplitView(TwoPaneSplitView split)
        {
            if (!ApplyWindowStyles(split))
                return;

            VisualElement anchor = split.Q<VisualElement>("unity-dragline-anchor");
            VisualElement line = anchor?.Q<VisualElement>("unity-dragline");
            if (line == null)
                return;

            bool columns = split.orientation == TwoPaneSplitViewOrientation.Horizontal;
            anchor.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float thickness = columns ? evt.newRect.width : evt.newRect.height;
                float previousThickness = columns ? evt.oldRect.width : evt.oldRect.height;
                if (Mathf.Approximately(thickness, previousThickness) || split.childCount != 2 ||
                    split[0].resolvedStyle.display == DisplayStyle.None ||
                    split[1].resolvedStyle.display == DisplayStyle.None)
                    return;

                if (columns)
                {
                    split[1].style.left = thickness;
                    split.contentContainer.style.paddingRight = thickness;
                }
                else
                {
                    split[1].style.top = thickness;
                    split.contentContainer.style.paddingBottom = thickness;
                }
            });
            anchor.AddToClassList("whimtex-split-handle");
            anchor.AddToClassList(columns ? "whimtex-split-handle--columns" : "whimtex-split-handle--rows");
            anchor.EnableInClassList("whimtex-split-handle--light", !EditorGUIUtility.isProSkin);
            line.AddToClassList("whimtex-split-line");
            line.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 3; i++)
            {
                VisualElement dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("whimtex-split-grip");
                line.Add(dot);
            }
            VisualElement hitArea = new VisualElement
            {
                name = "splitHitArea",
                pickingMode = PickingMode.Position
            };
            hitArea.AddToClassList("whimtex-split-hit-area");
            anchor.Add(hitArea);
        }

        internal sealed class ValueBindings
        {
            private readonly List<Action<bool>> updates = new List<Action<bool>>();

            public void Clear() => updates.Clear();

            public void Add(Action update) => updates.Add(_ => update());

            public void Track<T>(BaseField<T> field, Func<T> read)
            {
                void Refresh(bool force)
                {
                    T value = read();
                    if (!EqualityComparer<T>.Default.Equals(field.value, value) &&
                        (force || !IsInteracting(field)))
                        field.SetValueWithoutNotify(value);
                }

                bool queued = false;
                void QueueRefresh()
                {
                    if (queued || field.panel == null)
                        return;
                    queued = true;
                    field.schedule.Execute(() =>
                    {
                        queued = false;
                        Refresh(false);
                    });
                }

                field.RegisterCallback<FocusOutEvent>(_ => QueueRefresh(), TrickleDown.TrickleDown);
                field.RegisterCallback<PointerUpEvent>(_ => QueueRefresh(), TrickleDown.TrickleDown);
                field.RegisterCallback<PointerCaptureOutEvent>(_ => QueueRefresh(), TrickleDown.TrickleDown);
                updates.Add(Refresh);
                Refresh(true);
            }

            public void Refresh(bool force = false)
            {
                for (int i = 0; i < updates.Count; i++)
                    updates[i](force);
            }
        }

        internal static bool IsInteracting(VisualElement element)
        {
            VisualElement focused = element.focusController?.focusedElement as VisualElement;
            if (focused != null && (focused == element || element.Contains(focused)))
                return true;

            return HasPointerCaptureWithin(element);
        }

        internal static bool HasPointerCaptureWithin(VisualElement element)
        {
            if (element.panel == null)
                return false;
            for (int pointer = 0; pointer < PointerId.maxPointers; pointer++)
            {
                VisualElement captured = PointerCaptureHelper.GetCapturingElement(element.panel, pointer) as VisualElement;
                if (captured != null && (captured == element || element.Contains(captured)))
                    return true;
            }
            return false;
        }

        public const float StandardLabelWidth = 132f;
        public static readonly Color PanelDark = new Color(0.105f, 0.105f, 0.105f, 1f);
        public static readonly Color PanelLight = new Color(0.65f, 0.65f, 0.65f, 1f);
        public static readonly Color ToolbarDark = new Color(0.16f, 0.16f, 0.16f, 1f);
        public static readonly Color ToolbarLight = new Color(0.78f, 0.78f, 0.78f, 1f);
        public static readonly Color SelectedDark = new Color(0.18f, 0.38f, 0.62f, 1f);
        public static readonly Color SelectedLight = new Color(0.38f, 0.62f, 0.88f, 1f);
        public static readonly Color RowDark = new Color(0.18f, 0.18f, 0.18f, 1f);
        public static readonly Color RowLight = new Color(0.82f, 0.82f, 0.82f, 1f);

        public static Color PanelColor => EditorGUIUtility.isProSkin ? PanelDark : PanelLight;
        public static Color ToolbarColor => EditorGUIUtility.isProSkin ? ToolbarDark : ToolbarLight;
        public static Color SelectedColor => EditorGUIUtility.isProSkin ? SelectedDark : SelectedLight;
        public static Color RowColor => EditorGUIUtility.isProSkin ? RowDark : RowLight;

        public static VisualElement CreateToolbar()
        {
            VisualElement result = CreateRow();
            result.style.minHeight = 24f;
            result.style.paddingLeft = 4f;
            result.style.paddingRight = 4f;
            result.style.backgroundColor = ToolbarColor;
            result.style.borderBottomWidth = 1f;
            result.style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);
            return result;
        }

        public static VisualElement CreateRow()
        {
            return new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };
        }

        public static VisualElement CreateCard(string title = null)
        {
            VisualElement card = new VisualElement();
            card.style.marginTop = 4f;
            card.style.marginBottom = 4f;
            card.style.paddingLeft = 6f;
            card.style.paddingRight = 6f;
            card.style.paddingTop = 5f;
            card.style.paddingBottom = 6f;
            card.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.16f, 0.16f, 1f)
                : new Color(0.86f, 0.86f, 0.86f, 1f);
            card.style.borderTopLeftRadius = 3f;
            card.style.borderTopRightRadius = 3f;
            card.style.borderBottomLeftRadius = 3f;
            card.style.borderBottomRightRadius = 3f;

            if (!string.IsNullOrEmpty(title))
            {
                Label heading = new Label(title);
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                heading.style.marginBottom = 4f;
                card.Add(heading);
            }

            return card;
        }

        public static Label CreateHeading(string text)
        {
            Label result = new Label(text);
            result.style.unityFontStyleAndWeight = FontStyle.Bold;
            result.style.marginTop = 4f;
            result.style.marginBottom = 3f;
            return result;
        }

        public static Button CreateButton(string text, Action clicked, float width = 0f)
        {
            Button result = new Button(clicked) { text = text };
            if (width > 0f)
                result.style.width = width;
            result.style.height = 20f;
            return result;
        }

        public static Button CreateToolbarButton(string text, Action clicked, float width = 0f)
        {
            Button result = CreateButton(text, clicked, width);
            result.style.marginTop = 1f;
            result.style.marginBottom = 1f;
            return result;
        }

        public static T ConfigureField<T>(T field, float labelWidth = StandardLabelWidth)
            where T : VisualElement
        {
            field.style.flexShrink = 0f;
            field.style.marginTop = 1f;
            field.style.marginBottom = 1f;

            Label label = field.Q<Label>(className: BaseField<string>.labelUssClassName);
            if (label != null)
            {
                label.style.minWidth = labelWidth;
                label.style.width = labelWidth;
            }
            return field;
        }

        public static HelpBox AddHelpBox(VisualElement parent, string message, HelpBoxMessageType type)
        {
            HelpBox result = new HelpBox(message, type);
            result.style.marginTop = 4f;
            result.style.marginBottom = 4f;
            parent.Add(result);
            return result;
        }

        internal static Foldout CreateInspectorSection(string title, string name, LayerActionIcon.Kind icon,
            bool expanded, Action<bool> expansionChanged = null, bool available = true)
        {
            var section = new Foldout { text = title, name = name, value = available && expanded };
            section.SetEnabled(available);
            ApplyWindowStyles(section);
            section.AddToClassList("whimtex-inspector-section");
            section.EnableInClassList("whimtex-inspector-section--light", !EditorGUIUtility.isProSkin);
            section.contentContainer.AddToClassList("whimtex-inspector-section-content");
            var headerIcon = new LayerActionIcon(icon);
            headerIcon.AddToClassList("whimtex-inspector-section-icon");
            section.hierarchy.Add(headerIcon);
            if (expansionChanged != null)
                section.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == section) expansionChanged(evt.newValue);
                });
            return section;
        }

        internal static LayerShaderFXView BuildLayerInspectorSections(VisualElement root, Layer layer,
            TextureCompositor owner, Action<string, Action> apply, ValueBindings bindings,
            Action<VisualElement> buildProperties, bool colorExpanded, Action<bool> colorExpansionChanged,
            bool propertiesExpanded, Action<bool> propertiesExpansionChanged,
            bool fxExpanded, Action<bool> fxExpansionChanged)
        {
            bool group = layer?.IsGroup == true;
            if (group)
            {
                var transform = CreateInspectorSection("Transform", "transformSection", LayerActionIcon.Kind.Transform,
                    false, available: false);
                transform.tooltip = "Transform the individual layers inside this group.";
                root.Add(transform);
            }
            else AddTextureTransform(root, layer, owner, apply, bindings);

            LayerColorSettingsView.Build(root, layer, apply, bindings, colorExpanded, colorExpansionChanged, owner);
            var properties = CreateInspectorSection($"Properties ({TextureCompositor.LayerMenuName(layer)})", "propertiesSection", LayerActionIcon.Kind.Properties,
                propertiesExpanded, propertiesExpansionChanged, !group && !(layer?.Behaviour is ShaderProcessorLayerBehaviour));
            root.Add(properties);
            if (group)
                properties.tooltip = "Edit the group's opacity and blend mode in Color & Blending or the layer list.";
            else if (layer?.Behaviour is ShaderProcessorLayerBehaviour)
                properties.tooltip = "Configure this processor in the FX section.";
            else buildProperties(properties.contentContainer);

            var fx = CreateInspectorSection("FX", "fxSection", LayerActionIcon.Kind.Effects,
                fxExpanded, fxExpansionChanged, !group);
            root.Add(fx);
            if (group)
            {
                fx.tooltip = "Add FX to the individual layers, or use an effect layer targeting this group.";
                return null;
            }
            var view = new LayerShaderFXView(layer, owner, apply);
            fx.Add(view);
            return view;
        }

        public static void AddTextureTransform(
            VisualElement parent,
            Layer layer,
            TextureCompositor compositor,
            Action<string, Action> applyChange,
            ValueBindings bindings)
        {
            TextureTransform read() => layer.transform;
            void write(TextureTransform value) => layer.transform = value;
            Foldout card = CreateInspectorSection("Transform", "transformSection", LayerActionIcon.Kind.Transform, false);
            card.AddToClassList("whimtex-transform-card");

            Vector2Field pivot = ConfigureField(new Vector2Field("Pivot"));
            pivot.tooltip = "Normalized pivot inside the output canvas.";
            Vector2Field position = ConfigureField(new Vector2Field("Position (px)"));
            position.tooltip = "Offset in output pixels. Positive X moves right; positive Y moves up.";
            Vector2Field scale = ConfigureField(new Vector2Field("Scale"));
            scale.tooltip = "Visual scale. One means 100 percent; negative values flip the image.";
            FloatField rotation = ConfigureField(new FloatField("Rotation"));
            rotation.tooltip = "Clockwise visual rotation in degrees.";
            EnumField tiling = ConfigureField(new EnumField("Tiling", TransformTilingMode.Clip));
            tiling.tooltip = "Clip = transparent outside the frame; Repeat = tile; Mirror = reflected tiles; " +
                "Source = source texture wrap modes; Clamp = extend edge pixels; " +
                "Unbounded = continue procedural UVs (raster layers use Clip).";
            EnumField filter = ConfigureField(new EnumField("Filter", LayerFilterMode.Source));
            filter.tooltip = "Source = inherit the texture's Filter Mode (default); Point = sharp pixels; " +
                "Bilinear = smooth; Trilinear = smooth mip transitions when the source has mipmaps. Independent of Tiling; does not change texture import settings.";

            bindings.Track(pivot, () => read().pivot);
            bindings.Track(position, () => read().position);
            bindings.Track(scale, () => read().scale);
            bindings.Track(rotation, () => read().rotation);
            bindings.Track(tiling, () => (Enum)read().tiling);
            bindings.Track(filter, () => (Enum)layer.filterMode);

            Button reset = CreateButton("Reset", () =>
            {
                applyChange("Reset Layer Transform", () =>
                {
                    TextureTransform value = read();
                    value.Reset();
                    write(value);
                });
                bindings.Refresh(true);
            }, 54f);
            VisualElement actions = CreateRow();
            actions.Add(reset);
            actions.Add(CreateOriginalAspectButton(() => layer, () => compositor, applyChange, bindings));
            card.Add(actions);

            pivot.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Layer Transform", () =>
                {
                    TextureTransform value = read();
                    value.pivot = evt.newValue;
                    write(value);
                });
            });
            position.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Layer Transform", () =>
                {
                    TextureTransform value = read();
                    value.position = evt.newValue;
                    write(value);
                });
            });
            scale.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Layer Transform", () =>
                {
                    TextureTransform value = read();
                    value.scale = evt.newValue;
                    write(value);
                });
            });
            rotation.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Layer Transform", () =>
                {
                    TextureTransform value = read();
                    value.rotation = evt.newValue;
                    write(value);
                });
            });

            card.Add(pivot);
            card.Add(position);
            card.Add(scale);
            card.Add(rotation);
            tiling.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Transform Tiling", () =>
                {
                    TextureTransform value = read();
                    value.tiling = (TransformTilingMode)evt.newValue;
                    write(value);
                });
            });
            card.Add(tiling);
            filter.RegisterValueChangedCallback(evt =>
                applyChange("Change Layer Filter", () => layer.filterMode = (LayerFilterMode)evt.newValue));
            card.Add(filter);
            parent.Add(card);
        }

        internal static Button CreateOriginalAspectButton(
            Func<Layer> readLayer,
            Func<TextureCompositor> readCompositor,
            Action<string, Action> applyChange,
            ValueBindings bindings,
            bool originalSize = false)
        {
            Button button = CreateButton(originalSize ? "Original Size" : "Original Aspect", () =>
            {
                Layer layer = readLayer();
                if (layer == null || !layer.TryGetOriginalAspectTransform(readCompositor(), out TextureTransform fitted, originalSize) ||
                    fitted.Equals(layer.transform))
                    return;
                applyChange(originalSize ? "Restore Original Size" : "Restore Original Aspect", () => layer.transform = fitted);
            });
            button.tooltip = (originalSize ? "Set one source pixel to one canvas pixel. " :
                "Fit the source aspect ratio inside the current frame by shrinking one axis. ") +
                "Preserve image center, pivot, rotation, and flips. Generated layers use the canvas ratio. " +
                "Requires a source image for File layers and nonzero scale.";
            bindings.Add(() => button.SetEnabled(
                readLayer() is Layer layer && layer.TryGetOriginalAspectTransform(readCompositor(), out _, originalSize)));
            return button;
        }
    }
}
