using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class TransformInputPopup : PopupWindowContent
        {
            private readonly TransformInputSource source;
            internal TransformInputPopup(ShaderGraphWindow window, string nodeId, Func<ShaderParameter> read, Action<Action<ShaderParameter>> write)
                => source = new TransformInputSource(window, nodeId, read, write);
            public override Vector2 GetWindowSize() => new Vector2(320, 160);
            public override void OnGUI(Rect rect) { }
            public override void OnOpen()
            {
                var view = new ShaderParameterView(source);
                editorWindow.rootVisualElement.Add(view);
                view.Query<Foldout>().ForEach(f => f.value = true);
                view.schedule.Execute(() => { if (!source.Available) editorWindow.Close(); else view.Refresh(); }).Every(100);
            }
        }
        private sealed class TransformInputSource : ShaderParameterViewSource
        {
            private readonly ShaderGraphWindow window;
            private readonly string nodeId, scopeId;
            private readonly ShaderProgram program;
            private readonly Func<ShaderParameter> read;
            private readonly Action<Action<ShaderParameter>> write;
            private readonly List<ShaderParameter> parameters = new List<ShaderParameter> { new ShaderParameter() };
            internal TransformInputSource(ShaderGraphWindow window, string nodeId, Func<ShaderParameter> read, Action<Action<ShaderParameter>> write)
            { this.window = window; this.nodeId = nodeId; scopeId = window.editingSubGraphId; program = window.Target.Program; this.read = read; this.write = write; RefreshParameters(); }
            internal bool Available => window != null && window.Editable && ReferenceEquals(program, window.Target.Program) &&
                scopeId == window.editingSubGraphId && window.Graph?.FindNode(nodeId) != null;
            internal override IReadOnlyList<ShaderParameter> Parameters => parameters;
            internal override string Code => string.Empty;
            internal override bool IsReadOnly => !Available;
            internal override Vector2 Dimensions => window.document != null ? new Vector2(window.document.width, window.document.height) : Vector2.one;
            internal override void RefreshParameters()
            {
                if (!Available) return;
                var p = read(); var view = parameters[0]; view.id = nodeId + ":Transform"; view.type = p.type; view.name = "Transform"; view.transformValue = p.transformValue;
                if (view.controls.Count == 0) view.controls.Add(ShaderGraphParameters.DefaultControl(view));
            }
            internal override void Change(string id, Action<ShaderParameter> edit, bool recordUndo = true) { if (!IsReadOnly) { write(edit); RefreshParameters(); } }
            internal override void ChangeParameters(string undoName, IReadOnlyList<ShaderParameter> values)
            { if (!IsReadOnly && values.Count == 1) write(p => p.transformValue = values[0].transformValue); }
        }
    }
}
