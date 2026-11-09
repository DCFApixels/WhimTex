using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderDiagnosticPassDetails : Foldout
    {
        readonly ScrollView scroll = new ScrollView();
        IReadOnlyList<ShaderDiagnostic> previous;

        internal ShaderDiagnosticPassDetails()
        {
            name = "shader-pass-details";
            text = "Pass details";
            value = false;
            // Limit the expanded section to ten native Editor rows.
            scroll.style.maxHeight = EditorGUIUtility.singleLineHeight * 10;
            Add(scroll);
            style.display = DisplayStyle.None;
        }

        internal void Refresh(IReadOnlyList<ShaderDiagnostic> messages)
        {
            if (ReferenceEquals(previous, messages)) return;
            previous = messages;
            scroll.Clear();
            var passes = new Dictionary<string, List<ShaderDiagnostic>>(StringComparer.Ordinal);
            bool auxiliary = false;
            if (messages != null) foreach (var message in messages)
            {
                if (string.IsNullOrEmpty(message.Pass)) continue;
                auxiliary |= message.Pass != "Main";
                if (!passes.TryGetValue(message.Pass, out var list)) passes.Add(message.Pass, list = new List<ShaderDiagnostic>());
                list.Add(message);
            }
            style.display = auxiliary ? DisplayStyle.Flex : DisplayStyle.None;
            if (!auxiliary) return;
            foreach (var pass in passes)
            {
                scroll.Add(new Label(pass.Key));
                var severity = ShaderDiagnostics.Severity(pass.Value);
                scroll.Add(new HelpBox(ShaderDiagnostics.Format(pass.Value), severity == ShaderDiagnosticSeverity.Error
                    ? HelpBoxMessageType.Error : severity == ShaderDiagnosticSeverity.Warning ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info));
            }
        }
    }
}
