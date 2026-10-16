using System;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        // The data API uses the same field/default/coverage encoding as document JSON.
        internal static JObject VectorSnapshot(VectorLayerBehaviour vector, WhimTexDocument document)
        {
            var result = new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(vector);
            result.Remove("recoveryId");
            return result;
        }

        internal static void PatchVector(VectorLayerBehaviour vector, JObject patch, WhimTexDocument document)
        {
            CheckKeys(patch, "shapes", "referenceSize", "edgeMode", "brushDraft");
            var data = VectorSnapshot(vector, document);
            foreach (var field in patch.Properties()) data[field.Name] = field.Value.DeepClone();
            // Read and validate a detached replacement before touching the current data.
            var replacement = (VectorLayerBehaviour)new Reader().Value(data, typeof(VectorLayerBehaviour));
            if (replacement.shapes.Count > 1)
                throw new WhimTexDocumentException("A Vector layer contains one figure. Create a separate layer for each figure; use multiple contours for a compound figure.");
            vector.shapes = replacement.shapes;
            vector.referenceSize = replacement.referenceSize;
            vector.edgeMode = replacement.edgeMode;
            vector.brushDraft = replacement.brushDraft;
        }

        internal static JObject BooleanGroupSnapshot(BooleanGroupLayerBehaviour group, WhimTexDocument document)
        {
            var result = new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(group);
            result.Remove("recoveryId");
            return result;
        }

        internal static void PatchBooleanGroup(BooleanGroupLayerBehaviour group, JObject patch, WhimTexDocument document)
        {
            CheckKeys(patch, "style", "referenceSize", "edgeMode", "appearance");
            var data = BooleanGroupSnapshot(group, document);
            foreach (var field in patch.Properties()) data[field.Name] = field.Value.DeepClone();
            // Style is a complete replacement, like Vector.shapes; omitted top-level fields stay intact.
            var replacement = (BooleanGroupLayerBehaviour)new Reader().Value(data, typeof(BooleanGroupLayerBehaviour));
            group.style = replacement.style;
            group.referenceSize = replacement.referenceSize;
            group.edgeMode = replacement.edgeMode;
            group.appearance = replacement.appearance;
        }

        internal static void ValidateVectorStorage(object value)
        {
            try
            {
                if (value is ShapeLayerBehaviour shape)
                {
                    shape.ValidateGradientRepetitions();
                    if (!Enum.IsDefined(typeof(VectorFillMode), shape.fillMode))
                        throw new InvalidOperationException("Shape fillMode must be a defined VectorFillMode.");
                    if (!VectorGeometry.Finite(shape.gradientStart) || !VectorGeometry.Finite(shape.gradientEnd))
                        throw new InvalidOperationException("Shape gradient anchors must be finite.");
                    if (shape.gradient == null && shape.fillMode >= VectorFillMode.LinearGradient)
                        throw new InvalidOperationException("Shape gradient fills require a gradient.");
                    if (!Enum.IsDefined(typeof(VectorFillMode), shape.strokeMode))
                        throw new InvalidOperationException("Shape strokeMode must be a defined VectorFillMode.");
                    if (!VectorGeometry.Finite(shape.strokeGradientStart) || !VectorGeometry.Finite(shape.strokeGradientEnd))
                        throw new InvalidOperationException("Shape stroke gradient anchors must be finite.");
                    if (shape.strokeGradient == null && shape.strokeMode >= VectorFillMode.LinearGradient)
                        throw new InvalidOperationException("Shape gradient strokes require a gradient.");
                }
                if (value is VectorBrushDraft draft) draft.Validate();
                if (value is VectorLayerBehaviour vector) vector.Validate();
                if (value is BooleanGroupLayerBehaviour booleanGroup) booleanGroup.Validate();
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            { throw new WhimTexDocumentException(error.Message, error); }
        }
    }
}
