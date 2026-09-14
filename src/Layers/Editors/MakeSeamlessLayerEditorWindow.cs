using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class MakeSeamlessLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(MakeSeamlessLayerBehaviour);
        protected override string PreviewTitle => "Preview (Make Seamless)";
        public static void Open(MakeSeamlessLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<MakeSeamlessLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (MakeSeamlessLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);

        internal static void BuildFields(VisualElement root, MakeSeamlessLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            addEffectTarget(root, layer);
            root.Add(BuildEdgeSelector(layer, applyChange, bindings));
            var horizontal = WhimTexUI.ConfigureField(new EnumField("Horizontal", layer.horizontal)
                { tooltip = "Mirror the chosen source edge onto the opposite edge, or leave this axis unchanged." });
            bindings.Track(horizontal, () => (Enum)layer.horizontal);
            horizontal.RegisterValueChangedCallback(evt => applyChange("Change Seamless Direction",
                () => layer.horizontal = (MakeSeamlessLayerBehaviour.HorizontalDirection)evt.newValue));
            root.Add(horizontal);
            var vertical = WhimTexUI.ConfigureField(new EnumField("Vertical", layer.vertical));
            bindings.Track(vertical, () => (Enum)layer.vertical);
            vertical.RegisterValueChangedCallback(evt => applyChange("Change Seamless Direction",
                () => layer.vertical = (MakeSeamlessLayerBehaviour.VerticalDirection)evt.newValue));
            root.Add(vertical);
            var width = WhimTexUI.ConfigureField(new Slider("Fade Width (%)", .1f, 50f)
                { showInputField = true, tooltip = "Transition width as a percentage of each canvas dimension. Wider transitions hide the join more gradually." });
            width.SetValueWithoutNotify(layer.blendWidth * 100f);
            bindings.Track(width, () => layer.blendWidth * 100f);
            width.RegisterValueChangedCallback(evt => applyChange("Change Seamless Fade Width", () =>
                layer.blendWidth = Safe(evt.newValue / 100f, .001f, .5f, layer.blendWidth)));
            root.Add(width);
            var falloff = WhimTexUI.ConfigureField(new Slider("Falloff", .25f, 4f)
                { showInputField = true, tooltip = "Higher values concentrate the reflected image closer to the destination edge." });
            falloff.SetValueWithoutNotify(layer.falloff);
            bindings.Track(falloff, () => layer.falloff);
            falloff.RegisterValueChangedCallback(evt => applyChange("Change Seamless Falloff", () =>
                layer.falloff = Safe(evt.newValue, .25f, 4f, layer.falloff)));
            root.Add(falloff);
        }

        private static VisualElement BuildEdgeSelector(MakeSeamlessLayerBehaviour layer,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {
            var selector = new VisualElement { name = "seamlessEdges" };
            selector.AddToClassList("whimtex-seamless-edges");
            selector.Add(new SeamlessImageIcon());

            AddEdge("left", "Copy the right edge onto the left. Click again to turn horizontal blending off.",
                () => layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft,
                () => layer.horizontal = layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft
                    ? MakeSeamlessLayerBehaviour.HorizontalDirection.Off : MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft);
            AddEdge("right", "Copy the left edge onto the right. Click again to turn horizontal blending off.",
                () => layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight,
                () => layer.horizontal = layer.horizontal == MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight
                    ? MakeSeamlessLayerBehaviour.HorizontalDirection.Off : MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight);
            AddEdge("top", "Copy the bottom edge onto the top. Click again to turn vertical blending off.",
                () => layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop,
                () => layer.vertical = layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop
                    ? MakeSeamlessLayerBehaviour.VerticalDirection.Off : MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop);
            AddEdge("bottom", "Copy the top edge onto the bottom. Click again to turn vertical blending off.",
                () => layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom,
                () => layer.vertical = layer.vertical == MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom
                    ? MakeSeamlessLayerBehaviour.VerticalDirection.Off : MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom);
            return selector;

            void AddEdge(string edge, string tooltip, Func<bool> selected, Action toggle)
            {
                var button = new Button(() => applyChange("Change Seamless Direction", toggle))
                    { name = "seamlessEdge-" + edge, tooltip = tooltip };
                button.AddToClassList("whimtex-seamless-edge");
                button.AddToClassList("whimtex-seamless-edge--" + edge);
                void Refresh() => button.EnableInClassList("whimtex-seamless-edge--selected", selected());
                Refresh();
                bindings.Add(Refresh);
                selector.Add(button);
            }
        }

        private sealed class SeamlessImageIcon : VisualElement
        {
            public SeamlessImageIcon()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-seamless-image");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                if (r.width < 1f || r.height < 1f) return;
                var painter = context.painter2D;
                painter.fillColor = resolvedStyle.color;
                Vector2 Point(float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
                painter.BeginPath();
                painter.Arc(Point(.7f, .29f), r.width * .075f, 0f, 360f);
                painter.Fill();
                painter.BeginPath();
                painter.MoveTo(Point(.16f, .77f));
                painter.LineTo(Point(.4f, .37f));
                painter.LineTo(Point(.57f, .62f));
                painter.LineTo(Point(.69f, .49f));
                painter.LineTo(Point(.85f, .77f));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private static float Safe(float value, float min, float max, float previous) =>
            float.IsNaN(value) || float.IsInfinity(value) ? previous : Mathf.Clamp(value, min, max);
    }
}
