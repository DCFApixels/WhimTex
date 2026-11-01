using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        static void AddFunctionSearchEntries(List<SearchTreeEntry> tree, ShaderGraphValueType? source)
        {
            var choices = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var entry in ShaderGraphFunctionCatalog.Entries)
            {
                if (source.HasValue)
                {
                    foreach (var p in entry.definition.InputPorts())
                        if (Compatible(source.Value, p.type ?? ShaderGraphValueType.Float, !p.type.HasValue))
                            choices[entry.menuPath + "(" + p.name + ")"] = new ConnectedNodeChoice(ShaderGraphNodeKind.CustomFunction, p.name, entry);
                }
                else choices[entry.menuPath] = entry;
            }
            if (choices.Count == 0) return;
            tree.Add(new SearchTreeGroupEntry(new GUIContent("HLSL"), 1));
            string previous = "";
            foreach (var choice in choices)
            {
                var parts = choice.Key.Split('/'); var before = previous.Split('/');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    bool same = i < before.Length;
                    for (int k = 0; same && k <= i; k++) same &= k < before.Length && parts[k] == before[k];
                    if (!same) tree.Add(new SearchTreeGroupEntry(new GUIContent(parts[i]), i + 2));
                }
                tree.Add(new SearchTreeEntry(new GUIContent(parts[parts.Length - 1])) { level = parts.Length + 1, userData = choice.Value });
                previous = choice.Key;
            }
        }
        readonly Dictionary<string, object> functionCaretKeys = new Dictionary<string, object>();
        object FunctionCaretKey(string id)
        {
            if (!functionCaretKeys.TryGetValue(id, out var key)) functionCaretKeys.Add(id, key = new object());
            return key;
        }
        void DirtyFunctionDraft()
        {
            EditorUtility.SetDirty(document);
            if (document.documentBinding != null) document.documentBinding.dirty = true;
        }
        void RefreshFunctionCatalog()
        {
            ShaderGraphFunctionCatalog.Refresh();
            if (ShaderGraphFunctionCatalog.Errors.Count > 0) ShowError(string.Join("\n", ShaderGraphFunctionCatalog.Errors));
            else RefreshDiagnostics();
        }
        static T CopyFunctionData<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

        // Compile a standalone invocation as well: disconnected functions must be validated too.
        static void CheckFunction(ShaderGraphFunction function, List<ShaderGraphSource> sources)
        {
            function.Validate();
            var graph = new ShaderGraph { sources = sources };
            graph.functions.Add(function);
            var node = ShaderGraphNodes.Create(ShaderGraphNodeKind.CustomFunction, default);
            ShaderGraphNodes.SetFunction(node, function); graph.nodes.Add(node);
            var output = ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, default); graph.nodes.Add(output);
            foreach (var p in function.ports)
                if (p.type == ShaderGraphValueType.Texture)
                {
                    var parameter = new ShaderFXParameter { name = "_" + p.name, type = ShaderFXParameterType.Texture2D, textureSource = ShaderFXTextureSource.Texture };
                    graph.parameters.Add(parameter);
                    var input = ShaderGraphNodes.Create(ShaderGraphNodeKind.Parameter, default); input.parameterId = parameter.id; graph.nodes.Add(input);
                    graph.edges.Add(new ShaderGraphEdge { outputNode = input.id, inputNode = node.id, inputPort = p.name });
                }
            // Boolean is a valid function output, although it cannot wire directly to Output's color input.
            using var program = ShaderGraphProgram.Create(graph, node.id, function.OutputPorts()[0]);
        }
        void ApplyFunction(string nodeId, ShaderGraphFunction candidate, List<ShaderGraphSource> sources)
        {
            try
            {
                if (candidate.inline)
                {
                    string path = Path.Combine(Application.dataPath, "WhimTexInline.hlsl");
                    candidate.sourceId = !ShaderGraphCompiler.HasIncludes(candidate.body) ? null : candidate.fullSource
                        ? ShaderGraphSources.CaptureRetainingIncludes(candidate.body, path, sources, candidate.sourceId)
                        : ShaderGraphSources.Capture(candidate.body, path, sources);
                }
                CheckFunction(candidate, sources);
                canvas.SetMessages("function:" + nodeId, null);
                Edit("Compile Shader Function", graph =>
                {
                    var node = graph.FindNode(nodeId);
                    string previous = node.functionId;
                    // A function shared by imported nodes becomes independent when explicitly edited.
                    if (graph.nodes.Exists(n => n.id != nodeId && n.functionId == previous)) candidate.id = Guid.NewGuid().ToString("N");
                    else { candidate.id = previous; graph.functions.RemoveAll(f => f.id == previous); }
                    graph.functions.Add(candidate); graph.sources = sources;
                    ShaderGraphNodes.SetFunction(node, candidate);
                    graph.edges.RemoveAll(e => e.inputNode == nodeId && !candidate.ports.Exists(p => p.name == e.inputPort && p.direction != ShaderGraphFunctionDirection.Output) ||
                        e.outputNode == nodeId && Array.IndexOf(candidate.OutputPorts(), e.outputPort) < 0);
                });
            }
            catch (Exception error) { canvas.ShowError(nodeId, error.Message); }
        }
        internal void AddFunctionNode(ShaderGraphFunctionCatalog.Entry entry, Vector2 position)
        {
            if (!Editable || Graph == null || Graph.nodes.Count >= 512) return;
            try
            {
                var staged = new ShaderGraph { sources = new List<ShaderGraphSource>(Graph.sources) };
                var function = entry.Import(staged);
                CheckFunction(function, staged.sources);
                Edit("Add HLSL Node", graph =>
                {
                    graph.sources = staged.sources; graph.functions.Add(function);
                    var node = ShaderGraphNodes.Create(ShaderGraphNodeKind.CustomFunction, position);
                    ShaderGraphNodes.SetFunction(node, function); graph.nodes.Add(node);
                });
            }
            catch (Exception error) { ShowError(error.Message); }
        }

        void ConvertToCustomFunction(string nodeId)
        {
            var node = Graph?.FindNode(nodeId);
            var function = Graph?.FindFunction(node?.functionId);
            if (!Editable || node?.kind != ShaderGraphNodeKind.CustomFunction || function?.inline != false) return;
            try
            {
                var source = Graph.sources.Find(s => s.id == function.sourceId);
                if (source == null) throw new InvalidOperationException("The function source backup is missing.");
                var candidate = CopyFunctionData(function);
                candidate.inline = true; candidate.fullSource = true;
                candidate.body = candidate.draft = source.code; candidate.draftPorts = null; candidate.hasDraftPorts = false;
                candidate.sourcePath = candidate.presetId = candidate.sourceFingerprint = null;
                candidate.presetVersion = 0; candidate.reflected = false;
                CheckFunction(candidate, Graph.sources);
                canvas.SetMessages("function:" + nodeId, null);
                Edit("Convert to Custom Function", graph =>
                {
                    var target = graph.FindNode(nodeId);
                    if (graph.nodes.Exists(n => n.id != nodeId && n.functionId == target.functionId)) candidate.id = Guid.NewGuid().ToString("N");
                    else graph.functions.RemoveAll(f => f.id == target.functionId);
                    graph.functions.Add(candidate);
                    target.functionId = candidate.id; target.function = candidate; target.codeExpanded = true;
                });
            }
            catch (Exception error) { canvas.ShowError(nodeId, error.Message); }
        }

        private sealed partial class NodeView
        {
            internal ShaderFXCodeField functionCode;
            ShaderGraphFunction Function => window.Graph?.FindFunction(Data?.functionId);

            void EditDraftPorts(Action<List<ShaderGraphFunctionPort>> change)
            {
                window.Edit("Edit Function Ports", graph =>
                {
                    var f = graph.FindFunction(graph.FindNode(Id).functionId);
                    if (!f.HasDraftPorts) f.draftPorts = CopyFunctionData(f).ports;
                    f.draftPorts ??= new List<ShaderGraphFunctionPort>();
                    f.hasDraftPorts = true;
                    change(f.draftPorts);
                }, structural: false, rebuild: false, layoutOnly: true);
            }
            void BuildFunctionEditor()
            {
                var f = Function;
                if (f == null) return;
                title = f.name;
                if (!f.inline)
                {
                    this.AddManipulator(new ContextualMenuManipulator(e =>
                    {
                        e.menu.AppendAction("Convert to Custom Function", _ => window.ConvertToCustomFunction(Id),
                            _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                        e.menu.AppendAction("Refresh HLSL", _ => RefreshFunctionFile(),
                            _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                        e.menu.AppendAction("Choose HLSL File…", _ => ChooseFunctionFile(),
                            _ => window.Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    }));
                    return;
                }
                AddToClassList("whimtex-shader-graph-function");
                var actions = new VisualElement(); actions.AddToClassList("whimtex-shader-graph-function-actions");
                actions.Add(new Button(ChooseFunctionFile) { text = "File…" });
                extensionContainer.Add(actions);
                if (!f.fullSource)
                {
                    var configuration = new Foldout { text = "Ports", value = false };
                    var portRows = new VisualElement(); configuration.Add(portRows);
                    void RebuildPorts()
                    {
                        portRows.Clear();
                        var ports = Function.EditingPorts;
                        for (int i = 0; i < ports.Count; i++)
                        {
                            int index = i; var p = ports[i];
                            var row = new VisualElement(); row.AddToClassList("whimtex-shader-graph-function-port");
                            var name = new TextField { value = p.name, isDelayed = true };
                            var header = new VisualElement(); header.AddToClassList("whimtex-shader-graph-function-actions");
                            name.style.flexGrow = 1; name.style.minWidth = 0;
                            name.RegisterValueChangedCallback(e => EditDraftPorts(list => list[index].name = e.newValue)); header.Add(name);
                            header.Add(new Button(() => { EditDraftPorts(list => list.RemoveAt(index)); RebuildPorts(); }) { text = "−" }); row.Add(header);
                            var types = new List<string> { "Float", "Float2", "Float3", "Float4", "Color", "Boolean", "Texture" };
                            var type = new DropdownField(types, Math.Max(0, types.IndexOf(p.type.ToString())));
                            type.RegisterValueChangedCallback(e => EditDraftPorts(list => list[index].type = Enum.Parse<ShaderGraphValueType>(e.newValue))); row.Add(type);
                            var direction = new EnumField(p.direction);
                            direction.RegisterValueChangedCallback(e => EditDraftPorts(list => list[index].direction = (ShaderGraphFunctionDirection)e.newValue)); row.Add(direction);
                            portRows.Add(row);
                        }
                    }
                    RebuildPorts();
                    var shownPorts = Function.EditingPorts;
                    refresh.Add(() => { var current = Function.EditingPorts; if (!ReferenceEquals(shownPorts, current)) { shownPorts = current; RebuildPorts(); } });
                    configuration.Add(new Button(() =>
                    {
                        if (Function.EditingPorts.Count >= 64) return;
                        EditDraftPorts(list => { string name = "Value" + list.Count; while (list.Exists(p => p.name == name)) name += "_"; list.Add(new ShaderGraphFunctionPort { name = name }); });
                        RebuildPorts();
                    }) { text = "Add Port" });
                    extensionContainer.Add(configuration);
                }
                actions.Add(new Button(CompileFunctionDraft) { text = "Compile" });
                var codeFoldout = new Foldout { text = "Code", value = Data.codeExpanded };
                codeFoldout.AddToClassList("whimtex-shader-graph-function-code-foldout");
                codeFoldout.RegisterValueChangedCallback(e =>
                {
                    if (e.target != codeFoldout) return;
                    window.Edit("Toggle Function Code", g => g.FindNode(Id).codeExpanded = e.newValue, false, false, true);
                    RefreshCodeLayout();
                });
                var signature = new Label(f.Signature()); signature.AddToClassList("whimtex-shader-graph-function-signature"); codeFoldout.Add(signature);
                functionCode = new ShaderFXCodeField(window.document, window.FunctionCaretKey(Id), () => Function?.draft ?? "",
                    code => { if (window.Editable && Function != null) { Function.draft = code; window.DirtyFunctionDraft(); } }, () => window.Editable);
                functionCode.AddToClassList("whimtex-shader-graph-function-code");
                var codeArea = new VisualElement();
                codeArea.Add(functionCode);
                var resize = new Label("◢"); resize.AddToClassList("whimtex-shader-graph-function-resize");
                resize.AddManipulator(new FunctionResizer(this)); codeArea.Add(resize);
                codeFoldout.Add(codeArea);
                extensionContainer.Add(codeFoldout);
                void RefreshCodeLayout()
                {
                    var data = Data;
                    style.width = data.codeExpanded && data.codeSize.x > 0 ? new StyleLength(data.codeSize.x) : StyleKeyword.Null;
                    functionCode.style.height = data.codeExpanded && data.codeSize.y > 0 ? new StyleLength(data.codeSize.y) : StyleKeyword.Null;
                    codeFoldout.SetValueWithoutNotify(data.codeExpanded);
                }
                refresh.Add(() =>
                {
                    functionCode.SyncFromModel();
                    RefreshCodeLayout();
                });
            }
            void CompileFunctionDraft()
            {
                if (!window.Editable || Function == null) return;
                try
                {
                    var candidate = CopyFunctionData(Function);
                    candidate.body = candidate.draft;
                    if (candidate.fullSource)
                    {
                        var definitions = ShaderGraphFunctionParser.Parse(candidate.body, "Custom Function", true);
                        var matches = definitions.FindAll(d => d.entryPoint == candidate.entryPoint);
                        var definition = matches.Find(d => d.Signature() == candidate.Signature());
                        if (definition == null && matches.Count == 1) definition = matches[0];
                        if (definition == null) throw new FormatException("The entry function '" + candidate.entryPoint + "' is missing or ambiguous.");
                        // Keep imported Color/default metadata when only the implementation changed.
                        if (definition.Signature() != candidate.Signature())
                        {
                            candidate.ports = definition.ports; candidate.returnType = definition.returnType;
                            candidate.returnName = definition.returnName; candidate.returnValueType = definition.returnValueType;
                        }
                    }
                    else if (candidate.HasDraftPorts) candidate.ports = candidate.draftPorts ?? new List<ShaderGraphFunctionPort>();
                    candidate.draftPorts = null; candidate.hasDraftPorts = false;
                    window.ApplyFunction(Id, candidate, new List<ShaderGraphSource>(window.Graph.sources));
                }
                catch (Exception error) { window.canvas.ShowError(Id, error.Message); }
            }
            void ChooseFunctionFile()
            {
                string path = EditorUtility.OpenFilePanel("HLSL Function", "", "hlsl");
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    var entries = ShaderGraphFunctionCatalog.LoadFile(path);
                    if (entries.Count == 0) throw new FormatException("No supported HLSL functions in this file.");
                    void Select(ShaderGraphFunctionCatalog.Entry entry)
                    {
                        try { var staged = new ShaderGraph { sources = new List<ShaderGraphSource>(window.Graph.sources) }; var candidate = entry.Import(staged); window.ApplyFunction(Id, candidate, staged.sources); }
                        catch (Exception error) { window.canvas.ShowError(Id, error.Message); }
                    }
                    if (entries.Count == 1) Select(entries[0]);
                    else { var menu = new GenericMenu(); foreach (var entry in entries) { var chosen = entry; menu.AddItem(new GUIContent(entry.menuPath), false, () => Select(chosen)); } menu.ShowAsContext(); }
                }
                catch (Exception error) { window.canvas.ShowError(Id, error.Message); }
            }
            void RefreshFunctionFile()
            {
                try
                {
                    var f = Function;
                    ShaderGraphFunctionCatalog.Refresh();
                    var entry = ShaderGraphFunctionCatalog.FindForRefresh(f);
                    if (entry == null) throw new InvalidOperationException("The original function is unavailable or ambiguous. The embedded backup is retained; use File to choose a replacement.");
                    var staged = new ShaderGraph { sources = new List<ShaderGraphSource>(window.Graph.sources) };
                    var candidate = entry.Import(staged); window.ApplyFunction(Id, candidate, staged.sources);
                }
                catch (Exception error) { window.canvas.ShowError(Id, error.Message); }
            }
        }
        private sealed class FunctionResizer : PointerManipulator
        {
            readonly NodeView node;
            Vector2 origin, size;
            int pointer = -1;
            internal FunctionResizer(NodeView node) { this.node = node; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            void Down(PointerDownEvent e)
            {
                if (e.button != 0 || !node.window.Editable) return;
                origin = e.position; size = new Vector2(node.resolvedStyle.width, node.functionCode.resolvedStyle.height);
                pointer = e.pointerId; target.CapturePointer(pointer);
                Undo.IncrementCurrentGroup(); Undo.RecordObject(node.window.document, "Resize Function Code"); e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                float scale = node.worldTransform.lossyScale.x;
                var next = size + ((Vector2)e.position - origin) / Mathf.Max(scale, .001f);
                // Inherit the graph's existing node minimum; code uses its own shared editor minimum.
                next.x = Mathf.Max(node.resolvedStyle.minWidth.value, next.x);
                next.y = Mathf.Max(node.functionCode.resolvedStyle.minHeight.value, next.y);
                node.Data.codeSize = next; node.style.width = next.x; node.functionCode.style.height = next.y;
                node.window.DirtyFunctionDraft(); e.StopPropagation();
            }
            void Up(PointerUpEvent e) { if (e.pointerId != pointer) return; target.ReleasePointer(pointer); e.StopPropagation(); }
            void Lost(PointerCaptureOutEvent e) { pointer = -1; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); }
        }
    }
}
