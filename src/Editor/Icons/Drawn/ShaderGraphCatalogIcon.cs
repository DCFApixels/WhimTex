using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphCatalogIcon : VisualElement
    {
        internal readonly struct Appearance
        {
            internal readonly Color color;
            internal readonly WhimTexIcons.Definition icon;
            internal readonly bool group;
            internal Appearance(string category, WhimTexIcons.Definition icon, bool group = false)
            { color = ShaderGraphNodeColors.Category(category); this.icon = icon; this.group = group; }
            internal VisualElement CreateIcon()
            {
                var visual = new ShaderGraphCatalogIcon();
                visual.Bind(this);
                return visual;
            }
        }

        internal static Appearance ForCategory(string path)
        {
            string category = path == "Parameters" ? "Input" : path;
            return new Appearance(category, WhimTexIcons.Get(CategoryIcon(path)), true);
        }

        internal static Appearance ForNode(ShaderGraphNodeKind kind)
        {
            string category = ShaderGraphNodes.Category(kind);
            WhimTexIcons.Kind? symbol = kind switch
            {
                ShaderGraphNodeKind.Add => WhimTexIcons.Kind.Add, ShaderGraphNodeKind.Subtract or ShaderGraphNodeKind.Negate => WhimTexIcons.Kind.Subtract,
                ShaderGraphNodeKind.Multiply => WhimTexIcons.Kind.Multiply, ShaderGraphNodeKind.Divide => WhimTexIcons.Kind.Divide,
                ShaderGraphNodeKind.Power => WhimTexIcons.Kind.Power, ShaderGraphNodeKind.SquareRoot => WhimTexIcons.Kind.SquareRoot,
                ShaderGraphNodeKind.Absolute => WhimTexIcons.Kind.Absolute, ShaderGraphNodeKind.OneMinus => WhimTexIcons.Kind.OneMinus,
                ShaderGraphNodeKind.Minimum => WhimTexIcons.Kind.Minimum, ShaderGraphNodeKind.Maximum => WhimTexIcons.Kind.Maximum,
                ShaderGraphNodeKind.Exponential => WhimTexIcons.Kind.Exponential, ShaderGraphNodeKind.Log => WhimTexIcons.Kind.Logarithm,
                ShaderGraphNodeKind.And => WhimTexIcons.Kind.And, ShaderGraphNodeKind.Or => WhimTexIcons.Kind.Or, ShaderGraphNodeKind.Not => WhimTexIcons.Kind.Not,
                ShaderGraphNodeKind.Compare => WhimTexIcons.Kind.Compare, ShaderGraphNodeKind.Reciprocal => WhimTexIcons.Kind.Reciprocal,
                _ => null
            };
            LayerActionIcon.Kind? action = kind switch
            {
                ShaderGraphNodeKind.Mirror => LayerActionIcon.Kind.FlipHorizontal,
                ShaderGraphNodeKind.RotateAboutAxis => LayerActionIcon.Kind.RotateRight,
                ShaderGraphNodeKind.UV or ShaderGraphNodeKind.CanvasPosition or ShaderGraphNodeKind.TransformUV or
                ShaderGraphNodeKind.ApplyTransform2D => LayerActionIcon.Kind.Transform,
                _ => null
            };
            WhimTexIcons.Kind glyph = kind switch
            {
                ShaderGraphNodeKind.Buffer => WhimTexIcons.Kind.Buffer,
                ShaderGraphNodeKind.Time => WhimTexIcons.Kind.Time,
                ShaderGraphNodeKind.Split or ShaderGraphNodeKind.MatrixSplit => WhimTexIcons.Kind.Split,
                ShaderGraphNodeKind.Combine or ShaderGraphNodeKind.Append => WhimTexIcons.Kind.Combine,
                ShaderGraphNodeKind.Expression or ShaderGraphNodeKind.CustomFunction => WhimTexIcons.Kind.Code,
                ShaderGraphNodeKind.SubGraph => WhimTexIcons.Kind.Graph,
                ShaderGraphNodeKind.Step or ShaderGraphNodeKind.Floor or ShaderGraphNodeKind.Ceiling or
                ShaderGraphNodeKind.Round or ShaderGraphNodeKind.Truncate => WhimTexIcons.Kind.Step,
                ShaderGraphNodeKind.Smoothstep or ShaderGraphNodeKind.SampleCurve or ShaderGraphNodeKind.ColorCurves => WhimTexIcons.Kind.Curve,
                ShaderGraphNodeKind.SampleGradient or ShaderGraphNodeKind.GradientMap => WhimTexIcons.Kind.Color,
                ShaderGraphNodeKind.Fractal => WhimTexIcons.Kind.Loop,
                _ => CategoryIcon(category)
            };
            var icon = action.HasValue ? WhimTexIcons.Action(action.Value) : WhimTexIcons.Get(symbol ?? glyph);
            return new Appearance(category, icon);
        }

        private static WhimTexIcons.Kind CategoryIcon(string path)
        {
            if (path.StartsWith("HLSL", StringComparison.Ordinal)) return WhimTexIcons.Kind.Code;
            if (path.StartsWith("Sub Graph", StringComparison.Ordinal)) return WhimTexIcons.Kind.Graph;
            int separator = path.LastIndexOf('/');
            string name = separator < 0 ? path : path.Substring(separator + 1);
            return name switch
            {
                "Math" or "Basic" or "Advanced" or "Derivative" => WhimTexIcons.Kind.Function,
                "Trigonometry" or "Wave" => WhimTexIcons.Kind.Wave, "Round" => WhimTexIcons.Kind.Step,
                "Range" => WhimTexIcons.Kind.Range, "Interpolation" => WhimTexIcons.Kind.Curve,
                "Matrix" => WhimTexIcons.Kind.Matrix, "Vector" or "UV" or "Distortion" => WhimTexIcons.Kind.Vector,
                "Noise" or "Grain" or "Warp" or "Random" => WhimTexIcons.Kind.Noise,
                "Procedural" or "Pattern" or "Scatter" => WhimTexIcons.Kind.Pattern, "Shape" => WhimTexIcons.Kind.Shape,
                "Channel" => WhimTexIcons.Kind.Split, "Logic" => WhimTexIcons.Kind.Logic,
                "Color" or "Adjustment" or "Adjustments" or "Conversion" or "Mapping" or "Selection" or "Mixing" or "Alpha" or "Generation" => WhimTexIcons.Kind.Color,
                "Filter" => WhimTexIcons.Kind.Filter, "Normal" => WhimTexIcons.Kind.Normal, "Lighting" => WhimTexIcons.Kind.Light,
                "Input" or "Parameters" => WhimTexIcons.Kind.Input, "Sampling" => WhimTexIcons.Kind.Texture,
                "Loop" => WhimTexIcons.Kind.Loop, _ => WhimTexIcons.Kind.Graph
            };
        }

        private static StyleSheet sheet;
        private readonly WhimTexIcon icon;
        private Appearance appearance;
        private bool bound;

        internal ShaderGraphCatalogIcon()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-graph-catalog-icon");
            if (sheet == null) sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/Icons/Drawn/ShaderGraphCatalogIcon.uss");
            if (sheet != null) styleSheets.Add(sheet);
            icon = new WhimTexIcon(); Add(icon);
        }

        internal void Bind(Appearance value)
        {
            if (bound && appearance.color == value.color && appearance.icon == value.icon && appearance.group == value.group) return;
            appearance = value; bound = true;
            icon.style.color = value.group ? new StyleColor(value.color) : new StyleColor(StyleKeyword.Null);
            icon.Bind(value.icon);
        }
    }
}
