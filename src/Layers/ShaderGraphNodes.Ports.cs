using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
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
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Expression:
                    foreach (var input in node.inputs)
                        if (input != null && ShaderGraphExpression.IsInputName(input.port))
                            IncludePort(new ShaderGraphPort(input.port, node.valueType));
                    break;
                case ShaderGraphNodeKind.Shape:
                    Include(ports[node.kind]); Include(polygonPorts); Include(starPorts); Include(capsulePorts); Include(segmentPorts);
                    break;
                case ShaderGraphNodeKind.RotateVector: Include(rotate3D); break;
                case ShaderGraphNodeKind.BilateralBlur: Include(bilateralCustom); break;
                case ShaderGraphNodeKind.NormalLighting:
                    Include(node.normalEncoding == ShaderGraphNormalEncoding.SignedXYZ ? signedExtendedLighting : ports[node.kind]);
                    Include(node.normalEncoding == ShaderGraphNormalEncoding.SignedXYZ ? globalSignedLighting : globalPackedLighting);
                    break;
            }
            return combined ?? current;
        }

        internal static bool InputVisible(ShaderGraphNode node, string name)
        {
            if (HasNoisePeriod(node.kind) && (name is "Period" or "Offset") && !node.noisePeriodic) return false;
            if (node.kind == ShaderGraphNodeKind.GaborNoise && name == "Width" && node.option != 2) return false;
            if (node.kind == ShaderGraphNodeKind.Fractal && !FractalInputVisible(node.fractal, name)) return false;
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
