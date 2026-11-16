using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Explicit developer operation. Catalog browsing never invokes this renderer.
    internal static class ShaderGraphCatalogThumbnailBaker
    {
        const int Size = 64, Padding = 2, Cell = Size + Padding * 2, Columns = 8;
        static readonly List<ShaderGraphNodeKind> kinds = new();
        static readonly List<ShaderGraphCatalogThumbnails.Entry> entries = new();
        static ShaderGraphPreviewVM vm;
        static Texture2D atlas, pixels;
        static RenderTexture target;
        static int index;
        static double started;
        internal static string Status { get; private set; } = "Idle";
        internal static bool Running { get; private set; }

#if WHIMTEX_DEEP_DEBUG
        [MenuItem("Window/WhimTex/Debug/Rebuild Node Thumbnails")]
#endif
        internal static void Start()
        {
            if (Running) throw new InvalidOperationException("Thumbnail generation is already running.");
            kinds.Clear(); entries.Clear(); index = 0;
            foreach (ShaderGraphNodeKind kind in Enum.GetValues(typeof(ShaderGraphNodeKind)))
                if (ShaderGraphNodes.IsNoise(kind) || kind is ShaderGraphNodeKind.CausticsNoise or
                    ShaderGraphNodeKind.Shape or ShaderGraphNodeKind.Checkerboard or ShaderGraphNodeKind.Stripes or
                    ShaderGraphNodeKind.Grid or ShaderGraphNodeKind.Rings or ShaderGraphNodeKind.GradientFactor or ShaderGraphNodeKind.Truchet)
                    kinds.Add(kind);
            atlas = new Texture2D(Columns * Cell, ((kinds.Count + Columns - 1) / Columns) * Cell, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
            pixels = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create(); Running = true; Status = "Starting";
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        }

        static void Tick()
        {
            try
            {
                if (vm == null)
                {
                    if (index == kinds.Count) { Save(); Stop(); Status = "Complete: " + index + " thumbnails"; return; }
                    var kind = kinds[index];
                    var graph = new ShaderGraph();
                    var node = ShaderGraphNodes.Create(kind, Vector2.zero);
                    graph.nodes.Add(node);
                    string output = ShaderGraphNodes.Outputs(node)[0];
                    // Warp is illustrated on a grid, matching its coordinate-output use.
                    if (ShaderGraphNodes.IsWarpNoise(kind))
                    {
                        var grid = ShaderGraphNodes.Create(ShaderGraphNodeKind.Checkerboard, Vector2.zero);
                        graph.nodes.Add(grid);
                        graph.edges.Add(new ShaderGraphEdge { outputNode = node.id, outputPort = "Position", inputNode = grid.id, inputPort = "UV" });
                        node = grid; output = "Out";
                    }
                    if (!ShaderGraphPreviewVM.TryCreate(graph, node.id, output, out vm))
                        throw new InvalidOperationException(kind + ": " + ShaderGraphPreviewVM.LastFallbackReason);
                    started = EditorApplication.timeSinceStartup;
                    Status = "Rendering " + (index + 1) + "/" + kinds.Count + ": " + kind;
                }
                if (!vm.UpdatePreparation())
                {
                    if (vm.Failed || EditorApplication.timeSinceStartup - started > 120)
                        throw new InvalidOperationException(Status + ": " + ShaderGraphPreviewVM.LastFallbackReason);
                    return;
                }
                vm.Material.SetVector("_CanvasSize", new Vector4(Size, Size, 1f / Size, 1f / Size));
                vm.Material.SetFloat("_PixelScale", 1);
                if (kinds[index] == ShaderGraphNodeKind.BlueNoise)
                {
                    vm.Material.SetTexture("_WTNoiseBlue2D", BlueNoiseTextures.TwoD);
                    vm.Material.SetTexture("_WTNoiseBlue1D", BlueNoiseTextures.OneD);
                }
                if (!vm.Render(target, 0)) throw new InvalidOperationException("Thumbnail render failed.");
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); pixels.Apply(); }
                finally { RenderTexture.active = previous; }
                var source = pixels.GetPixels();
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (var c in source) { min = Mathf.Min(min, c.r, c.g, c.b); max = Mathf.Max(max, c.r, c.g, c.b); }
                if (max - min < .00001f) throw new InvalidOperationException(kinds[index] + ": empty example");
                var tile = new Color[Cell * Cell];
                for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
                {
                    var c = source[Mathf.Clamp(y - Padding, 0, Size - 1) * Size + Mathf.Clamp(x - Padding, 0, Size - 1)];
                    if (!float.IsFinite(c.r) || !float.IsFinite(c.g) || !float.IsFinite(c.b))
                        throw new InvalidOperationException(kinds[index] + ": non-finite pixels");
                    tile[y * Cell + x] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)),
                        Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1);
                }
                int px = index % Columns * Cell, py = index / Columns * Cell;
                atlas.SetPixels(px, py, Cell, Cell, tile);
                entries.Add(new ShaderGraphCatalogThumbnails.Entry { kind = kinds[index].ToString(),
                    uv = new Rect((float)(px + Padding) / atlas.width, (float)(py + Padding) / atlas.height,
                        (float)Size / atlas.width, (float)Size / atlas.height) });
                vm.Dispose(); vm = null; index++;
            }
            catch (Exception error) { Stop(); Status = "Failed: " + error.Message; Debug.LogException(error); }
        }

        static void Save()
        {
            atlas.Apply();
            string folder = ShaderGraphCatalogThumbnails.Folder, path = folder + "Catalog.png";
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            File.WriteAllText(folder + "Catalog.json", JsonUtility.ToJson(new ShaderGraphCatalogThumbnails.Manifest { entries = entries.ToArray() }, true));
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
            importer.mipmapEnabled = false; importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(folder + "Catalog.json");
            ShaderGraphCatalogThumbnails.Reset();
        }
        static void Cancel() { Stop(); Status = "Cancelled by assembly reload"; }
        static void Stop()
        {
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            vm?.Dispose(); vm = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null; }
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels); pixels = null;
            if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas); atlas = null;
            Running = false;
        }
    }
}
