using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    // Only final assembly uses compute. Accumulation and live preview share the raster path.
    internal static class BrushStrokeCompute
    {
        private static ComputeShader shader;
        private static readonly int Surface = Shader.PropertyToID("_BrushSurface");
        private static readonly int Size = Shader.PropertyToID("_BrushSurfaceSize");
        private static readonly int Rect = Shader.PropertyToID("_BrushCommitRect");
        private static readonly int[] Floats = IDs("_StrokeOpacity", "_StrokeMode", "_HdrBlend",
            "_BrushErase", "_BrushStandard", "_BrushStampAccumulation", "_BrushPressureOpacity",
            "_BrushUseSelection", "_BrushSelectionWrap", "_BrushStraightFallback", "_BrushGamma");
        private static readonly int[] Vectors = IDs("_BrushWriteChannels",
            "_BrushCanvasRow0", "_BrushCanvasRow1", "_BrushCanvasRow2", "_BrushDirtyRect");
        private static readonly int Stroke = Shader.PropertyToID("_BrushStroke");
        private static readonly int Selection = Shader.PropertyToID("_BrushSelection");
        private static readonly int Original = Shader.PropertyToID("_BrushOriginalStraight");

        private static int[] IDs(params string[] names)
        {
            var ids = new int[names.Length];
            for (int i = 0; i < names.Length; i++) ids[i] = Shader.PropertyToID(names[i]);
            return ids;
        }

        internal static bool Supported
        {
            get
            {
                if (!SystemInfo.supportsComputeShaders ||
                    !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.LoadStore))
                    return false;
                if (shader == null)
                    shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/BrushCommit.compute");
                return shader != null && shader.HasKernel("Commit") && shader.IsSupported(shader.FindKernel("Commit"));
            }
        }

        internal static bool TryCommit(RenderTexture surface, RectInt bounds, Material composite,
            Texture stroke, Texture selection, Texture original)
            => TryCommit(surface, bounds, null, composite, stroke, selection, original);

        internal static bool TryCommitTiles(RenderTexture surface, RectInt[] tiles, Material composite,
            Texture stroke, Texture selection, Texture original)
            => TryCommit(surface, default, tiles, composite, stroke, selection, original);

        private static bool TryCommit(RenderTexture surface, RectInt bounds, RectInt[] tiles, Material composite,
            Texture stroke, Texture selection, Texture original)
        {
            // Existing surfaces without UAV access remain valid and use the raster fallback.
            if (surface == null || !surface.enableRandomWrite || !surface.IsCreated() ||
                surface.graphicsFormat != GraphicsFormat.R16G16B16A16_SFloat || !Supported)
                return false;
            int kernel = shader.FindKernel("Commit");
            using var commands = new CommandBuffer { name = "WhimTex brush commit" };
            foreach (int id in Floats) commands.SetComputeFloatParam(shader, id, composite.GetFloat(id));
            foreach (int id in Vectors) commands.SetComputeVectorParam(shader, id, composite.GetVector(id));
            // Bind even inactive resources for backends that validate all kernel declarations.
            commands.SetComputeTextureParam(shader, kernel, Stroke, stroke);
            commands.SetComputeTextureParam(shader, kernel, Selection, selection != null ? selection : Texture2D.blackTexture);
            commands.SetComputeTextureParam(shader, kernel, Original, original != null ? original : Texture2D.blackTexture);
            commands.SetComputeTextureParam(shader, kernel, Surface, surface);
            commands.SetComputeVectorParam(shader, Size, new Vector4(surface.width, surface.height, 1f / surface.width, 1f / surface.height));
            for (int i = 0, count = tiles?.Length ?? 1; i < count; i++)
            {
                var tile = tiles == null ? bounds : tiles[i];
                commands.SetComputeVectorParam(shader, Rect, new Vector4(tile.x, tile.y, tile.width, tile.height));
                commands.DispatchCompute(shader, kernel, (tile.width + 7) / 8, (tile.height + 7) / 8, 1);
            }
            // Keep this on the graphics queue: preview, commit and release are ordered together.
            // Never retry after dispatch: a partially executed commit must not blend twice.
            Graphics.ExecuteCommandBuffer(commands);
            return true;
        }
    }
}
