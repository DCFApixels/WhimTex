using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetBrush(TextureCompositor document, DrawingLayerBehaviour layer, JObject brush)
        {
            Keys(brush, "color", "size", "hardness", "spacing", "mirrorX", "mirrorY", "mirrorAngle", "center", "repeat", "repeatCount", "repeatSecondaryCount", "radialStartAngle", "elements", "boundary",
                "opacity", "flow", "scatter", "scatterBias", "sizeJitter", "angleJitter", "angleOffset", "flipX", "flipY", "rotationMode", "randomAlgorithm", "tintGradient", "tip", "tipChannel", "tipSdf", "proceduralMode", "tipGradient", "blend", "blendApplication", "seed");
            layer.NormalizeSettings();
            BrushDynamics dynamics = layer.brushDynamics ??= new BrushDynamics();
            dynamics.Normalize();
            dynamics.opacity = Number(brush, "opacity", dynamics.opacity, 0f, 1f);
            dynamics.flow = Number(brush, "flow", dynamics.flow, 0f, 1f);
            dynamics.scatter = Number(brush, "scatter", dynamics.scatter, 0f, 4f);
            dynamics.scatterBias = Number(brush, "scatterBias", dynamics.scatterBias, -1f, 1f);
            dynamics.sizeJitter = Number(brush, "sizeJitter", dynamics.sizeJitter, 0f, 1f);
            dynamics.angleJitter = Number(brush, "angleJitter", dynamics.angleJitter, 0f, 180f);
            dynamics.angleOffset = Number(brush, "angleOffset", dynamics.angleOffset, -180f, 180f);
            dynamics.flipX = Number(brush, "flipX", dynamics.flipX, 0f, 1f);
            dynamics.flipY = Number(brush, "flipY", dynamics.flipY, 0f, 1f);
            dynamics.rotationMode = Enum(brush, "rotationMode", dynamics.rotationMode);
            dynamics.randomAlgorithm = Enum(brush, "randomAlgorithm", dynamics.randomAlgorithm);
            if (brush["tintGradient"] != null) dynamics.tintGradient = ReadGradient(brush["tintGradient"]);
            dynamics.tipChannel = Enum(brush, "tipChannel", dynamics.tipChannel);
            dynamics.tipSdf = Bool(brush, "tipSdf", dynamics.tipSdf);
            dynamics.proceduralMode = Enum(brush, "proceduralMode", dynamics.proceduralMode);
            if (brush["tipGradient"] != null) dynamics.tipGradient = ReadGradient(brush["tipGradient"]);
            dynamics.blend = Enum(brush, "blend", dynamics.blend);
            dynamics.blendApplication = Enum(brush, "blendApplication", dynamics.blendApplication);
            Require(dynamics.blend != BlendMode.Overwrite && dynamics.blend != BlendMode.None, "Brush blend must be a color blend mode, not Overwrite or None.");
            dynamics.seed = Int(brush, "seed", dynamics.seed, 1, int.MaxValue);
            if (brush["tip"] != null)
            {
                if (brush["tip"].Type == JTokenType.Null) dynamics.tip = null;
                else
                {
                    string path = ReadAssetPath(Text(brush, "tip"));
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    Require(texture != null, "No imported Texture2D at " + path + ".", "texture_not_found");
                    Require(!WhimTexDocumentService.IsOwnOutput(document, texture), "A document cannot use its own output as a brush tip.");
                    dynamics.tip = texture;
                }
            }
            if (brush["color"] != null) layer.brushColor = Color(brush["color"]);
            layer.brushSize = Number(brush, "size", layer.brushSize, 1f, 4096f);
            layer.brushHardness = Number(brush, "hardness", layer.brushHardness, 0f, 1f);
            layer.brushSpacing = Number(brush, "spacing", layer.brushSpacing, DrawingLayerBehaviour.MinimumBrushSpacing, DrawingLayerBehaviour.MaximumBrushSpacing);
            layer.mirrorAcrossVerticalAxis = Bool(brush, "mirrorX", layer.mirrorAcrossVerticalAxis);
            layer.mirrorAcrossHorizontalAxis = Bool(brush, "mirrorY", layer.mirrorAcrossHorizontalAxis);
            layer.mirrorAngle = Number(brush, "mirrorAngle", layer.mirrorAngle, 0f, 360f);
            if (brush["center"] != null)
            {
                Vector2 center = Vector(brush["center"], "center");
                Require(center.x >= 0f && center.x <= 1f && center.y >= 0f && center.y <= 1f, "Pattern center must be inside 0..1.");
                layer.patternCenter = center;
            }
            layer.repeatMode = Enum(brush, "repeat", layer.repeatMode);
            layer.repeatCount = Int(brush, "repeatCount", layer.repeatCount, 2, 64);
            layer.repeatSecondaryCount = Int(brush, "repeatSecondaryCount", layer.repeatSecondaryCount, 2, 64);
            layer.radialStartAngle = Number(brush, "radialStartAngle", layer.radialStartAngle, 0f, 360f);
            layer.repeatElementMode = Enum(brush, "elements", layer.repeatElementMode);
            layer.repeatBoundaryMode = Enum(brush, "boundary", layer.repeatBoundaryMode);
        }

        private static void Paint(TextureCompositor document, DrawingLayerBehaviour layer, JObject operation, bool execute)
        {
            if (operation["brush"] != null) SetBrush(document, layer, Obj(operation["brush"], "brush"));
            Require(operation["points"] is JArray points && points.Count >= 1 && points.Count <= 4096, "A stroke needs 1..4096 [x,y] points.");
            JArray values = (JArray)operation["points"];
            string space = Text(operation, "space", "canvasPixels");
            Require(space == "canvasPixels" || space == "layerUv", "space must be canvasPixels or layerUv.");
            Require(space != "canvasPixels" || document.GetCanvasTransform(layer).tiling == TransformTilingMode.Clip,
                "canvasPixels painting requires Clip tiling. For repeating transforms, use layerUv to edit the source tile explicitly.");
            bool erase = Bool(operation, "erase");
            PaintStrokeParameters parameters = layer.GetStrokeParameters(erase);
            if (operation["pencil"] != null)
                parameters = new PaintStrokeParameters(parameters.Color, parameters.Size, 1f, 0f, erase,
                    true, Enum(operation, "pencil", PencilShape.Circle));
            var uv = new Vector2[values.Count];
            double stamps = 1d;
            float spacing = parameters.SpacingPixels;
            for (int i = 0; i < values.Count; i++)
            {
                Vector2 point = Vector(values[i], "point");
                uv[i] = space == "layerUv" ? point : CanvasToLayerUv(point, document, document.GetCanvasTransform(layer));
                Require(uv[i].x >= -4f && uv[i].x <= 5f && uv[i].y >= -4f && uv[i].y <= 5f, "Stroke points are too far outside the source canvas.", "resource_limit");
                if (i > 0)
                    {
                        var dimensions=new Vector2(document.width,document.height);
                        Vector2 a=document.GetCanvasTransform(layer).Map(uv[i-1],dimensions),b=document.GetCanvasTransform(layer).Map(uv[i],dimensions);
                        Require(ProjectiveMatrix.Finite(a.x) && ProjectiveMatrix.Finite(a.y) && ProjectiveMatrix.Finite(b.x) && ProjectiveMatrix.Finite(b.y),
                            "Stroke crosses an invalid transform point.");
                        stamps += System.Math.Ceiling(Vector2.Scale(b-a,dimensions).magnitude / spacing);
                    }
            }
            long copies = layer.UsesRepeatedPattern ? layer.repeatCount : 1;
            if (layer.repeatMode == PaintRepeatMode.Grid) copies *= layer.repeatSecondaryCount;
            if (layer.UsesMirrorPattern && layer.mirrorAcrossVerticalAxis) copies *= 2;
            if (layer.UsesMirrorPattern && layer.mirrorAcrossHorizontalAxis) copies *= 2;
            Require(stamps * copies <= 100000d, "Stroke exceeds the 100,000 stamp budget. Increase spacing or split/simplify the stroke.", "resource_limit");
            double maxSize = parameters.Size * (1d + (parameters.Dynamics?.sizeJitter ?? 0f));
            if (parameters.Dynamics?.CanRotateTip == true) maxSize *= System.Math.Sqrt(2d);
            double area = System.Math.Min(maxSize, document.width) * System.Math.Min(maxSize, document.height);
            Require(stamps * copies * area <= 250000000d, "Stroke exceeds the brush coverage budget. Reduce repetitions, size or point count.", "resource_limit");
            if (!execute || parameters.Color.a <= 0f) return;
            layer.PrepareStroke(document.width, document.height, UndoName);
            layer.BeginStroke(uv[0]);
            try
            {
                layer.PaintPoint(uv[0], document.width, document.height, parameters);
                for (int i = 1; i < uv.Length; i++)
                {
                    Vector2 end = uv[i];
                    bool inside = layer.IsStrokePointInsideRepeatShape(end, document.width, document.height);
                    if (!inside && !layer.TryClipStrokeSegmentToRepeatShape(uv[i - 1], end, document.width, document.height, out end)) break;
                    layer.PaintSegment(uv[i - 1], end, document.width, document.height, false, parameters);
                    if (!inside) break;
                }
                layer.SyncSurfaceToTexture();
            }
            finally { layer.EndStroke(); }
        }

        private static Vector2 CanvasToLayerUv(Vector2 point, TextureCompositor document, TextureTransform transform)
        {
            Require(TiledCanvasUtility.IsInvertible(transform), "Cannot paint through a singular transform.");
            Vector2 result = transform.Unmap(new Vector2(point.x / document.width, 1f - point.y / document.height),
                new Vector2(document.width, document.height));
            Require(!float.IsNaN(result.x) && !float.IsInfinity(result.x) && !float.IsNaN(result.y) && !float.IsInfinity(result.y),
                "Paint point is on the perspective horizon.");
            return result;
        }
    }
}
