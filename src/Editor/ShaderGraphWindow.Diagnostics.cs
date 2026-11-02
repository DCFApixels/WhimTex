using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class NodeMessageBadge : VisualElement
        {
            readonly VisualElement icon;
            readonly HelpBox popup;
            ShaderGraphMessageLevel level;
            internal NodeMessageBadge()
            {
                name = "node-message";
                AddToClassList("whimtex-shader-graph-message");
                icon = new VisualElement { name = "message-icon" }; Add(icon);
                icon.generateVisualContent += DrawIcon;
                popup = new HelpBox { name = "message-popup", pickingMode = PickingMode.Ignore };
                popup.AddToClassList("whimtex-shader-graph-message-popup");
                popup.style.display = DisplayStyle.None; Add(popup);
                icon.RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
                icon.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
                RegisterCallback<DetachFromPanelEvent>(_ => SetHovered(false));
            }
            void SetHovered(bool hovered)
            {
                if (hovered) parent?.BringToFront();
                popup.style.display = hovered ? DisplayStyle.Flex : DisplayStyle.None;
            }
            internal void SetMessage(string text, ShaderGraphMessageLevel level)
            {
                if (popup.text == text && this.level == level) return;
                this.level = level;
                popup.text = text;
                icon.MarkDirtyRepaint();
                EnableInClassList("whimtex-shader-graph-message--error", level == ShaderGraphMessageLevel.Error);
                EnableInClassList("whimtex-shader-graph-message--warning", level == ShaderGraphMessageLevel.Warning);
                EnableInClassList("whimtex-shader-graph-message--info", level == ShaderGraphMessageLevel.Info);
            }
            void DrawIcon(MeshGenerationContext context)
            {
                Rect rect = icon.contentRect;
                float scale = Mathf.Min(rect.width, rect.height) / 16f;
                if (scale <= 0) return;
                Vector2 Point(float x, float y) => rect.center + new Vector2(x - 8, y - 8) * scale;
                var painter = context.painter2D;
                painter.fillColor = level switch
                {
                    ShaderGraphMessageLevel.Error => new Color32(229, 81, 81, 255),
                    ShaderGraphMessageLevel.Warning => new Color32(245, 193, 65, 255),
                    _ => new Color32(192, 192, 192, 255)
                };
                painter.strokeColor = new Color(0, 0, 0, .55f);
                painter.lineWidth = scale;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                if (level == ShaderGraphMessageLevel.Warning)
                {
                    painter.MoveTo(Point(8, 1.25f));
                    painter.LineTo(Point(14.75f, 14.25f));
                    painter.LineTo(Point(1.25f, 14.25f));
                    painter.ClosePath();
                }
                else if (level == ShaderGraphMessageLevel.Error)
                {
                    painter.MoveTo(Point(4.2f, 1.2f));
                    painter.LineTo(Point(11.8f, 1.2f));
                    painter.LineTo(Point(14.8f, 4.2f));
                    painter.LineTo(Point(14.8f, 11.8f));
                    painter.LineTo(Point(11.8f, 14.8f));
                    painter.LineTo(Point(4.2f, 14.8f));
                    painter.LineTo(Point(1.2f, 11.8f));
                    painter.LineTo(Point(1.2f, 4.2f));
                    painter.ClosePath();
                }
                else
                {
                    painter.Arc(Point(8, 8), 6.8f * scale, 0, 360);
                    painter.ClosePath();
                }
                painter.Fill(); painter.Stroke();
                painter.fillColor = painter.strokeColor = new Color32(43, 35, 17, 255);
                painter.lineWidth = 1.8f * scale;
                painter.BeginPath();
                float stemTop = level switch
                {
                    ShaderGraphMessageLevel.Error => 4.3f,
                    ShaderGraphMessageLevel.Warning => 5.3f,
                    _ => 7.1f
                };
                bool info = level == ShaderGraphMessageLevel.Info;
                painter.MoveTo(Point(8, stemTop));
                painter.LineTo(Point(8, info ? 11.3f : 8.8f));
                painter.Stroke();
                painter.BeginPath();
                painter.Arc(Point(8, info ? 4.5f : 11.6f), .95f * scale, 0, 360);
                painter.Fill();
            }
        }
        private sealed partial class Canvas
        {
            readonly Dictionary<string, IReadOnlyList<ShaderGraphMessage>> messageSources = new();
            readonly Dictionary<string, NodeMessageBadge> messageBadges = new();
            internal void SetMessages(string source, IReadOnlyList<ShaderGraphMessage> messages)
            {
                if (messageSources.TryGetValue(source, out var previous) && ReferenceEquals(previous, messages)) return;
                if (messages == null || messages.Count == 0) { if (!messageSources.Remove(source)) return; }
                else messageSources[source] = messages;
                RefreshBadges();
            }
            internal void ShowError(string id, string message)
            {
                if (id == null) { window.ShowError(message); return; }
                SetMessages("function:" + id, new[] { new ShaderGraphMessage(id, message, ShaderGraphMessageLevel.Error) });
            }
            void RebuildBadges()
            {
                messageBadges.Clear();
                foreach (var key in new List<string>(messageSources.Keys))
                    if (key != "compile" && !nodeViews.ContainsKey(key.Substring(key.IndexOf(':') + 1))) messageSources.Remove(key);
                RefreshBadges();
            }
            void RefreshBadges()
            {
                foreach (var pair in nodeViews)
                {
                    StringBuilder text = null;
                    var seen = new HashSet<string>();
                    var level = ShaderGraphMessageLevel.Info;
                    foreach (var messages in messageSources.Values) foreach (var message in messages)
                    {
                        if (message.nodeId != pair.Key || string.IsNullOrEmpty(message.text)) continue;
                        if (message.level > level) level = message.level;
                        if (!seen.Add(message.text)) continue;
                        text ??= new StringBuilder();
                        if (text.Length > 0) text.AppendLine().AppendLine();
                        text.Append(message.text);
                    }
                    pair.Value.EnableInClassList("whimtex-shader-graph-node--error", text != null && level == ShaderGraphMessageLevel.Error);
                    if (text == null)
                    {
                        if (messageBadges.Remove(pair.Key, out var previous)) previous.RemoveFromHierarchy();
                        continue;
                    }
                    if (!messageBadges.TryGetValue(pair.Key, out var badge))
                    {
                        badge = new NodeMessageBadge(); messageBadges.Add(pair.Key, badge);
                        pair.Value.hierarchy.Add(badge);
                    }
                    badge.SetMessage(text.ToString(), level);
                }
            }
        }
    }
}
