using UnityEngine;
using Kind = DCFApixels.WhimTex.ShaderGraphNodeKind;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static float[] RenderNativeExample(Kind kind, ShaderProgramRuntime runtime, ShaderGraph graph,
            WhimTexDocument document, Layer layer, RenderTexture target)
        {
            bool coordinates = kind is Kind.UV or Kind.CanvasPosition or Kind.Transform;
            layer.transform = TextureTransform.Default;
            if (coordinates)
            {
                layer.transform.scaleF = new Vector2(.66f, .57f);
                layer.transform.rotationF = 24;
                layer.transform.positionF = new Vector2(-2, 1);
                layer.transform.tiling = TransformTilingMode.Unbounded;
            }
            document.lights.Clear();
            if (kind == Kind.LightAttenuation)
                document.lights.Add(new WhimTexLight { type = WhimTexLightType.Point,
                    position = new Vector3(30, 34, 4), range = 31, falloff = 1.7f });
            var context = new LayerRenderContext(document, null, Size, Size, 1, applyTransform: coordinates, applyFx: false)
                .WithShaderGraphPreviewTime(0);
            runtime.Bind(graph, context);
            var renderContext = runtime.PrepareCoordinates(layer, context);
            runtime.Render(runtime.EmptyInput, target, renderContext);
            return HasCurve(kind) ? ReadCurveSamples(runtime, renderContext) : null;
        }

    }
}
