using UnityEditor;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexIcon : VisualElement
    {
        private static StyleSheet sheet;
        private WhimTexIcons.Definition definition;

        internal WhimTexIcon(WhimTexIcons.Definition definition = null)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-icon");
            if (sheet == null) sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/Icons/Drawn/WhimTexIcon.uss");
            if (sheet != null) styleSheets.Add(sheet);
            if (definition != null) Bind(definition);
        }

        internal void Bind(WhimTexIcons.Definition value)
        {
            if (ReferenceEquals(definition, value)) return;
            definition = value;
            Clear();
            if (value == null) return;
            VisualElement visual;
            if (value.action.HasValue) visual = new LayerActionIcon(value.action.Value);
            else if (value.symbol != null)
                visual = new Label(value.symbol) { displayTooltipWhenElided = false };
            else
            {
                visual = new VisualElement();
                visual.generateVisualContent += Draw;
            }
            visual.pickingMode = PickingMode.Ignore;
            visual.AddToClassList("whimtex-icon-mark");
            if (value.symbol?.Length > 2) visual.AddToClassList("whimtex-icon-small-symbol");
            Add(visual);
        }

        private void Draw(MeshGenerationContext context) => WhimTexIcons.Draw(definition.kind, context, resolvedStyle.color);
    }
}
