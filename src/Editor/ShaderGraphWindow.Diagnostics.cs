using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class NodeMessageBadge : VisualElement
        {
            readonly Image icon;
            readonly HelpBox popup;
            internal NodeMessageBadge()
            {
                name = "node-message";
                AddToClassList("whimtex-shader-graph-message");
                icon = new Image { name = "message-icon" }; Add(icon);
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
                if (popup.text == text && Equals(userData, level)) return;
                userData = level;
                popup.text = text;
                // The small console icons match Unity's native diagnostic severity colors.
                icon.image = EditorGUIUtility.IconContent(level switch
                {
                    ShaderGraphMessageLevel.Error => "console.erroricon.sml",
                    ShaderGraphMessageLevel.Warning => "console.warnicon.sml",
                    _ => "console.infoicon.sml"
                }).image;
                EnableInClassList("whimtex-shader-graph-message--error", level == ShaderGraphMessageLevel.Error);
                EnableInClassList("whimtex-shader-graph-message--warning", level == ShaderGraphMessageLevel.Warning);
                EnableInClassList("whimtex-shader-graph-message--info", level == ShaderGraphMessageLevel.Info);
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
