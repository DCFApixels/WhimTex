using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed partial class NodeView
        {
            void BuildSwitchEditor(ShaderGraphNode node)
            {
                var floor = new Toggle("Floor Predicate") { value = node.floorPredicate };
                floor.RegisterValueChangedCallback(e => Change(n => n.floorPredicate = e.newValue));
                AddOption(floor);
                var cases = ShaderGraphNodes.SwitchCases(node);
                for (int i = 0; i < cases.Count; i++)
                {
                    int index = i;
                    var row = new VisualElement();
                    row.AddToClassList("whimtex-shader-switch-case");
                    var operators = new List<string> { "<", "<=", ">", ">=", "==", "!=" };
                    var condition = new DropdownField(((char)('A' + i)).ToString(), operators, (int)cases[i].comparison);
                    condition.AddToClassList("whimtex-shader-switch-comparison");
                    condition.RegisterValueChangedCallback(e => Change(n => ShaderGraphNodes.EditableSwitchCases(n)[index].comparison = (ShaderGraphComparison)operators.IndexOf(e.newValue)));
                    AddOption(condition, row);
                    var threshold = new FloatField { value = cases[i].threshold, isDelayed = true };
                    threshold.RegisterValueChangedCallback(e =>
                    {
                        if (float.IsFinite(e.newValue)) Change(n => ShaderGraphNodes.EditableSwitchCases(n)[index].threshold = e.newValue);
                        else threshold.SetValueWithoutNotify(ShaderGraphNodes.SwitchCases(Data)[index].threshold);
                    });
                    AddOption(threshold, row);
                    var remove = new Button(() => window.Edit("Remove Switch Case", graph => ShaderGraphNodes.RemoveSwitchCase(graph, graph.FindNode(Id), index))) { text = "−" };
                    row.Add(remove); extensionContainer.Add(row);
                }
                var add = new Button(() => Change(n =>
                {
                    var entries = ShaderGraphNodes.EditableSwitchCases(n);
                    entries.Add(new ShaderGraphSwitchCase { threshold = entries.Count });
                })) { text = "Add Case" };
                add.SetEnabled(cases.Count < 26); AddOption(add);
            }
        }
    }
}
