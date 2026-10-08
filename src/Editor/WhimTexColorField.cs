using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public enum WhimTexColorRange { Switchable, StandardOnly, HdrOnly }

    public sealed class WhimTexColorField : UnityEditor.UIElements.ColorField
    {
        public WhimTexColorRange Range { get; set; } = WhimTexColorRange.Switchable;
        internal Func<WhimTexDocument> Document;
        internal Func<Color> ReadPickerColor;
        internal Action<bool> HdrChanged;
        internal Action OpenPickerOverride;
        internal object PickerContext;
        public bool UseCanvasChannels { get; set; }
        internal Func<int> ReadCanvasChannels;
        private Func<int> attachedChannelSource;
        private VisualElement channelSwatch;
        private VisualElement nativeColor, nativeGradient, nativeAlphaGradient;
        private int lastChannelMask = -2;
        private bool lastHdr;

        internal Func<int> ResolveChannelSource() => !UseCanvasChannels ? null : ReadCanvasChannels ??
            WhimTexColorChannels.FindSource(this, WhimTexColorPicker.FindDocument(this) ?? Document?.Invoke());

        public WhimTexColorField(string label = null) : base(label)
        {
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (UseCanvasChannels)
                {
                    attachedChannelSource = ResolveChannelSource();
                    BuildChannelSwatch();
                    RefreshChannelSwatch();
                }
            });
            RegisterCallback<DetachFromPanelEvent>(_ => { attachedChannelSource = null; lastChannelMask = -2; });
            schedule.Execute(RefreshChannelSwatch).Every(100);
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !enabledInHierarchy) return;
                var element = e.target as VisualElement;
                if (element == labelElement) return;
                for (var child = element; child != null && child != this; child = child.parent)
                    if (child.ClassListContains(eyeDropperUssClassName)) return;
                e.StopImmediatePropagation();
                OpenPicker();
            }, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                e.StopImmediatePropagation(); OpenPicker();
            }, TrickleDown.TrickleDown);
        }

        internal void OpenPicker()
        {
            if (!enabledInHierarchy) return;
            if (OpenPickerOverride != null) { OpenPickerOverride(); return; }
            WhimTexDocument ReadDocument() => WhimTexColorPicker.FindDocument(this) ?? Document?.Invoke();
            var document = ReadDocument();
            var picker = WhimTexColorPicker.Open(ReadPickerColor != null ? ReadPickerColor() : value, hdr, showAlpha, Range, document,
                ApplyPickerColor, next => { hdr = next; HdrChanged?.Invoke(next); },
                () => panel != null && enabledInHierarchy && ReadDocument() == document);
            picker.SourceContext = PickerContext;
            picker.SetChannelSource(ResolveChannelSource());
        }

        public override void SetValueWithoutNotify(Color next)
        {
            base.SetValueWithoutNotify(next);
            channelSwatch?.MarkDirtyRepaint();
        }

        protected override void UpdateMixedValueContent()
        {
            base.UpdateMixedValueContent();
            channelSwatch?.MarkDirtyRepaint();
        }

        private void BuildChannelSwatch()
        {
            if (channelSwatch != null) return;
            var container = this.Q(className: colorContainerUssClassName);
            var hdrLabel = this.Q(className: hdrLabelUssClassName);
            if (container == null || hdrLabel == null || hdrLabel.parent != container) return;
            nativeColor = this.Q(className: colorUssClassName);
            nativeGradient = this.Q(className: gradientContainerUssClassName);
            foreach (var child in container.Children())
                if (child != nativeGradient && child.ClassListContains(gradientContainerUssClassName)) { nativeAlphaGradient = child; break; }
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexColorField.uss");
            if (sheet != null) styleSheets.Add(sheet);
            channelSwatch = new VisualElement { pickingMode = PickingMode.Ignore };
            channelSwatch.AddToClassList("whimtex-color-channel-swatch");
            channelSwatch.generateVisualContent += context =>
            {
                if (showMixedValue || !UseCanvasChannels || !WhimTexColorChannels.Enabled || lastChannelMask < 0 || lastChannelMask == 15) return;
                var rect = channelSwatch.contentRect;
                if (rect.width <= 0 || rect.height <= 0) return;
                if (hdr)
                {
                    DrawHdrChannelSide(context, rect);
                    return;
                }
                WhimTexColorChannels.DrawSplit(context.painter2D, rect,
                    WhimTexColorInputs.StandardColor(ReadPickerColor != null ? ReadPickerColor() : value), lastChannelMask);
            };
            container.Insert(container.IndexOf(hdrLabel), channelSwatch);
        }

        private void DrawHdrChannelSide(MeshGenerationContext context, Rect rect)
        {
            if (nativeColor == null || nativeGradient == null || nativeGradient.childCount < 3 ||
                nativeAlphaGradient == null || nativeAlphaGradient.childCount < 3) return;
            var left = nativeGradient[0];
            var right = nativeGradient[nativeGradient.childCount - 1];
            float x1 = Mathf.Clamp01((left.worldBound.xMax - channelSwatch.worldBound.xMin) / rect.width);
            float x2 = Mathf.Max(x1, Mathf.Clamp01((right.worldBound.xMin - channelSwatch.worldBound.xMin) / rect.width));
            Color center = (Color32)nativeColor.resolvedStyle.backgroundColor;
            Color start = (Color32)left.resolvedStyle.backgroundColor;
            Color end = (Color32)right.resolvedStyle.backgroundColor;
            float alpha = value.a;
            Color Mask(Color c) { c.a = alpha; c = WhimTexColorChannels.Apply(c, lastChannelMask); c.a = 1; return c; }
            void Segment(float from, float to, Color c, Texture texture = null)
            {
                var mesh = context.Allocate(4, 6, texture);
                float x0 = rect.xMin + rect.width * from, x3 = rect.xMin + rect.width * to;
                float y0 = rect.yMax - rect.height * from, y3 = rect.yMax - rect.height * to;
                c = Mask(c);
                mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = c, uv = new Vector2(0, .5f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x3, y3, Vertex.nearZ), tint = c, uv = new Vector2(1, .5f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x3, rect.yMax, Vertex.nearZ), tint = c, uv = new Vector2(1, .5f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x0, rect.yMax, Vertex.nearZ), tint = c, uv = new Vector2(0, .5f) });
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
            }
            Segment(0, x1, start);
            Segment(x1, x2, center);
            Segment(x2, 1, end);
            if ((lastChannelMask & 7) == 0) return;
            var leftImage = nativeAlphaGradient[0];
            var rightImage = nativeAlphaGradient[nativeAlphaGradient.childCount - 1];
            var leftTexture = leftImage.resolvedStyle.backgroundImage.texture;
            var rightTexture = rightImage.resolvedStyle.backgroundImage.texture;
            if (leftTexture != null) Segment(0, x1, (Color32)leftImage.resolvedStyle.unityBackgroundImageTintColor, leftTexture);
            if (rightTexture != null) Segment(x2, 1, (Color32)rightImage.resolvedStyle.unityBackgroundImageTintColor, rightTexture);
        }

        private void RefreshChannelSwatch()
        {
            if (channelSwatch == null) return;
            int mask = UseCanvasChannels && WhimTexColorChannels.Enabled ? (ReadCanvasChannels ?? attachedChannelSource)?.Invoke() ?? -1 : -1;
            if (mask == lastChannelMask && hdr == lastHdr) return;
            lastChannelMask = mask;
            lastHdr = hdr;
            channelSwatch.MarkDirtyRepaint();
        }

        private void ApplyPickerColor(Color next)
        {
            // Standard display can equal the new color while the source still contains HDR intensity.
            if (value.Equals(next) && ReadPickerColor != null && !ReadPickerColor().Equals(next))
            {
                using (var change = ChangeEvent<Color>.GetPooled(ReadPickerColor(), next))
                { change.target = this; SendEvent(change); }
            }
            else value = next;
        }
    }
}
