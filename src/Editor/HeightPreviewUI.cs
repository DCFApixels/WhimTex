using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal class HeightPreviewSettings
    {
        public bool heightMode;
        public HeightPreviewRenderer.Channel heightChannel;
        public float heightScale = .25f;
        public bool heightTexture = true;
    }

    internal static class HeightPreviewUI
    {
        internal static ToolbarToggle ModeButton(bool height, string name, Action select)
        {
            var button = new ToolbarToggle { name = name, tooltip = height ? "Height" : "Image" };
            button.AddToClassList("whimtex-layer-preview-mode");
            button.Add(Icon(height));
            button.RegisterValueChangedCallback(_ => select());
            if (height && !HeightPreviewRenderer.Supported)
            {
                button.SetEnabled(false);
                button.tooltip = "Height preview requires compute shaders and floating-point render targets.";
            }
            return button;
        }

        internal static VisualElement Icon(bool height)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("whimtex-layer-preview-mode-icon");
            icon.generateVisualContent += context =>
            {
                var p = context.painter2D; var r = icon.contentRect;
                Vector2 P(float x, float y) => r.position + new Vector2(r.width * x, r.height * y);
                p.strokeColor = icon.resolvedStyle.color; p.lineWidth = 1; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                if (height)
                {
                    p.MoveTo(P(.08f, .72f)); p.LineTo(P(.27f, .47f)); p.LineTo(P(.42f, .56f));
                    p.LineTo(P(.57f, .15f)); p.LineTo(P(.72f, .48f)); p.LineTo(P(.92f, .7f));
                    p.LineTo(P(.5f, .92f)); p.ClosePath();
                    p.MoveTo(P(.57f, .15f)); p.LineTo(P(.5f, .92f));
                }
                else
                {
                    p.MoveTo(P(.08f, .15f)); p.LineTo(P(.92f, .15f)); p.LineTo(P(.92f, .85f));
                    p.LineTo(P(.08f, .85f)); p.ClosePath();
                    p.MoveTo(P(.08f, .7f)); p.LineTo(P(.38f, .42f)); p.LineTo(P(.6f, .66f));
                    p.LineTo(P(.73f, .52f)); p.LineTo(P(.92f, .7f));
                }
                p.Stroke();
            };
            return icon;
        }

        internal sealed class SettingsPopup : PopupWindowContent
        {
            private readonly HeightPreviewSettings state;
            private readonly Action<bool> changed;
            private readonly Action closed;
            internal SettingsPopup(HeightPreviewSettings state, Action<bool> changed, Action closed)
            { this.state = state; this.changed = changed; this.closed = closed; }
            public override Vector2 GetWindowSize() => new Vector2(250,
                (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 3 + 8);
            public override void OnGUI(Rect rect) { }
            public override void OnClose() => closed?.Invoke();
            public override void OnOpen()
            {
                var root = editorWindow.rootVisualElement;
                root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 4;
                var channel = new EnumField("Height Channel", state.heightChannel) { name = "height-channel" };
                var scale = new FloatField("Height Scale") { name = "height-scale", value = state.heightScale };
                var texture = new Toggle("Texture") { name = "height-texture", value = state.heightTexture };
                foreach (var field in new VisualElement[] { channel, scale, texture })
                {
                    field.AddToClassList(BaseField<float>.alignedFieldUssClassName); root.Add(field);
                }
                channel.RegisterValueChangedCallback(e => { state.heightChannel = (HeightPreviewRenderer.Channel)e.newValue; changed(true); });
                scale.RegisterValueChangedCallback(e =>
                {
                    state.heightScale = float.IsFinite(e.newValue) ? Mathf.Clamp(e.newValue, -4, 4) : .25f;
                    scale.SetValueWithoutNotify(state.heightScale); changed(true);
                });
                texture.RegisterValueChangedCallback(e => { state.heightTexture = e.newValue; changed(false); });
            }
        }
    }
}
