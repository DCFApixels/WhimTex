using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {

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
            void RefreshDiagnosticSources()
            {
                // Operation failures have no compiler to replace them after a later edit.
                foreach (var node in window.Graph.nodes)
                    if (previewState.IsChanged(node.id)) SetMessages("function:" + node.id, null);
                SetMessages("compile", window.Target.Messages);
            }
            void RebuildBadges()
            {
                foreach (var pair in new List<KeyValuePair<string, NodeMessageBadge>>(messageBadges))
                {
                    if (!nodeViews.TryGetValue(pair.Key, out var view))
                    { pair.Value.RemoveFromHierarchy(); messageBadges.Remove(pair.Key); }
                    else if (pair.Value.parent != view) view.hierarchy.Add(pair.Value);
                }
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
