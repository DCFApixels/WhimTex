using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        internal static bool CanHaveNodePreview(ShaderGraphNodeKind kind) =>
            !IsSubGraphInterface(kind) && kind is not (ShaderGraphNodeKind.Output or ShaderGraphNodeKind.Parameter or
                ShaderGraphNodeKind.Reroute or ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.Value or ShaderGraphNodeKind.InstanceIndex or
                ShaderGraphNodeKind.Time or ShaderGraphNodeKind.ForBegin or ShaderGraphNodeKind.ScatterInstance or ShaderGraphNodeKind.CanvasData or
                ShaderGraphNodeKind.TextureSize or ShaderGraphNodeKind.Split or ShaderGraphNodeKind.Compare or ShaderGraphNodeKind.MatrixDeterminant);

        // Storage keeps ports from every mode; evaluation uses only the current layout.
        internal static IReadOnlyList<ShaderGraphPort> StoredInputs(ShaderGraphNode node)
        {
            var current = Inputs(node);
            List<ShaderGraphPort> combined = null;
            void IncludePort(ShaderGraphPort port)
            {
                foreach (var existing in (IReadOnlyList<ShaderGraphPort>)combined ?? current)
                    if (existing.name == port.name) return;
                combined ??= new List<ShaderGraphPort>(current);
                combined.Add(port);
            }
            void Include(IReadOnlyList<ShaderGraphPort> alternative)
            {
                foreach (var port in alternative)
                    IncludePort(port);
            }
            if (UsesHeightTexture(node.kind)) IncludePort(InactiveHeightInput(node));
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Expression:
                    foreach (var input in node.inputs)
                        if (input != null && ShaderGraphExpression.IsInputName(input.port))
                            IncludePort(new ShaderGraphPort(input.port, node.valueType));
                    break;
                case ShaderGraphNodeKind.Shape:
                    Include(ports[node.kind]); Include(polygonPorts); Include(starPorts); Include(capsulePorts); Include(segmentPorts); Include(roundedPolygonPorts);
                    break;
                case ShaderGraphNodeKind.BilateralBlur: Include(bilateralCustom); break;
                case ShaderGraphNodeKind.NormalLighting:
                    Include(ports[node.kind]);
                    Include(globalLighting);
                    break;
            }
            return combined ?? current;
        }

        internal static bool InputVisible(ShaderGraphNode node, string name)
        {
            if (node.kind == ShaderGraphNodeKind.Kuwahara && node.option == 0 && (name is "Sharpness" or "Uniformity" or "Eccentricity")) return false;
            if (node.kind == ShaderGraphNodeKind.PaletteQuantize && name == "Colors" && node.paletteKeys) return false;
            if (node.kind == ShaderGraphNodeKind.Scatter && name == "Period" && !node.scatterPeriodic) return false;
            if (HasNoisePeriod(node.kind) && (name is "Period" or "Offset") && !node.noisePeriodic) return false;
            if (node.kind == ShaderGraphNodeKind.GaborNoise && name == "Width" && node.option != 2) return false;
            if (node.kind == ShaderGraphNodeKind.Fractal && !FractalInputVisible(node.fractal, name)) return false;
            if (node.kind == ShaderGraphNodeKind.RGBShift)
            {
                if (name == "Angle" && node.option == 0) return false;
                if ((name is "Center" or "Falloff") && node.option != 0) return false;
            }
            if (node.kind == ShaderGraphNodeKind.Combine && "XYZW".IndexOf(name, StringComparison.Ordinal) >= Components(node.valueType)) return false;
            foreach (var port in Inputs(node)) if (port.name == name) return true;
            return false;
        }

        internal static string[] StoredOutputs(ShaderGraphNode node) =>
            node.kind == ShaderGraphNodeKind.GaborNoise ? noiseGabor :
            HasNoiseDerivatives(node.kind) ? noiseField : Outputs(node);

        internal static bool OutputVisible(ShaderGraphNode node, string name) => node != null && Array.IndexOf(Outputs(node), name) >= 0;
    }
}
