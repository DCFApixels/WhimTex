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
        private static readonly int Coverage = Shader.PropertyToID("_AlphaCoverage");
        private static readonly Vector4[] clearTiles = new Vector4[16];
        internal static bool CoverageSupported => Supported && shader.HasKernel("CommitTracked") &&
            shader.IsSupported(shader.FindKernel("CommitTracked")) && shader.HasKernel("ScanCoverage") &&
            shader.IsSupported(shader.FindKernel("ScanCoverage")) && shader.HasKernel("ClearCoverage") &&
            shader.IsSupported(shader.FindKernel("ClearCoverage"));

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
            Texture stroke, Texture selection, Texture original, PaintAlphaCoverage coverage = null)
            => TryCommit(surface, default, tiles, composite, stroke, selection, original, coverage);

        private static bool TryCommit(RenderTexture surface, RectInt bounds, RectInt[] tiles, Material composite,
            Texture stroke, Texture selection, Texture original, PaintAlphaCoverage coverage = null)
        {
            // Existing surfaces without UAV access remain valid and use the raster fallback.
            if (surface == null || !surface.enableRandomWrite || !surface.IsCreated() ||
                surface.graphicsFormat != GraphicsFormat.R16G16B16A16_SFloat || !Supported)
                return false;
            bool tracked = coverage?.Valid == true && !coverage.Failed && tiles != null && CoverageSupported;
            int kernel = shader.FindKernel(tracked ? "CommitTracked" : "Commit");
            using var commands = new CommandBuffer { name = "WhimTex brush commit" };
            if (tracked) PrepareCoverage(commands, kernel, coverage, tiles);
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
            if (tracked) coverage.Changed();
            else coverage?.Invalidate();
            return true;
        }

        private static void PrepareCoverage(CommandBuffer commands, int kernel, PaintAlphaCoverage coverage, RectInt[] tiles)
        {
            int clear = shader.FindKernel("ClearCoverage");
            commands.SetComputeBufferParam(shader, clear, Coverage, coverage.buffer);
            commands.SetComputeBufferParam(shader, kernel, Coverage, coverage.buffer);
            commands.SetComputeIntParam(shader, "_CoverageColumns", coverage.columns);
            commands.SetComputeIntParam(shader, "_CoverageCount", coverage.buffer.count);
            if (tiles == null)
            {
                commands.SetComputeIntParam(shader, "_CoverageClearCount", -1);
                commands.DispatchCompute(shader, clear, (coverage.buffer.count + 63) / 64, 1, 1);
                return;
            }
            for (int start = 0; start < tiles.Length; start += clearTiles.Length)
            {
                int count = Mathf.Min(clearTiles.Length, tiles.Length - start);
                for (int i = 0; i < count; i++)
                {
                    var tile = tiles[start + i];
                    clearTiles[i] = new Vector4(tile.y / 256 * coverage.columns + tile.x / 256, 0, 0, 0);
                }
                commands.SetComputeIntParam(shader, "_CoverageClearCount", count);
                commands.SetComputeVectorArrayParam(shader, "_CoverageClearTiles", clearTiles);
                commands.DispatchCompute(shader, clear, 1, 1, 1);
            }
        }

        internal static void ScanCoverage(RenderTexture surface, PaintAlphaCoverage coverage, RectInt[] tiles)
        {
            int kernel = shader.FindKernel("ScanCoverage");
            using var commands = new CommandBuffer { name = "WhimTex alpha block coverage" };
            PrepareCoverage(commands, kernel, coverage, tiles);
            commands.SetComputeTextureParam(shader, kernel, Surface, surface);
            for (int i = 0, count = tiles?.Length ?? 1; i < count; i++)
            {
                var tile = tiles == null ? new RectInt(0, 0, surface.width, surface.height) : tiles[i];
                commands.SetComputeVectorParam(shader, Rect, new Vector4(tile.x, tile.y, tile.width, tile.height));
                commands.DispatchCompute(shader, kernel, (tile.width + 7) / 8, (tile.height + 7) / 8, 1);
            }
            Graphics.ExecuteCommandBuffer(commands);
        }
    }
}
