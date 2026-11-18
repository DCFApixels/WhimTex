using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Explicit developer operation. Catalog browsing never invokes this renderer.
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        const int Size = 64, Padding = 2, Cell = Size + Padding * 2, Columns = 8;
        static readonly List<ShaderGraphNodeKind> kinds = new();
        static readonly List<ShaderGraphCatalogThumbnails.Entry> entries = new();
        static ShaderGraphPreviewVM vm;
        static ShaderProgramRuntime.CompilationRequest request;
        static ShaderProgramRuntime native;
        static ShaderGraph example;
        static WhimTexDocument document;
        static Layer layer;
        static Texture2D atlas, pixels;
        static RenderTexture target;
        static int index;
        static double started;
        static Vector2 displayRange;
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
                if (IsSymbolExample(kind) || IsImageExample(kind) || ShaderGraphNodes.IsNoise(kind) || kind is ShaderGraphNodeKind.CausticsNoise or
                    ShaderGraphNodeKind.Shape or ShaderGraphNodeKind.Checkerboard or ShaderGraphNodeKind.Stripes or
                    ShaderGraphNodeKind.Grid or ShaderGraphNodeKind.Rings or ShaderGraphNodeKind.GradientFactor or ShaderGraphNodeKind.Truchet)
                    kinds.Add(kind);
            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = Size;
            layer = new Layer(new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph });
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
                if (vm == null && request == null && native == null)
                {
                    if (index == kinds.Count) { Save(); Stop(); Status = "Complete: " + index + " thumbnails"; return; }
                    var kind = kinds[index];
                    if (IsSymbolExample(kind))
                    {
                        AddTile(kind, SymbolTile(kind));
                        return;
                    }
                    example = new ShaderGraph();
                    var node = CreateExample(example, kind, out string output, out displayRange);
                    if (kind == ShaderGraphNodeKind.ErosionNoise || IsImageExample(kind))
                    {
                        var result = ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, Vector2.zero);
                        example.nodes.Add(result);
                        example.edges.Add(new ShaderGraphEdge { outputNode = node.id, outputPort = output, inputNode = result.id, inputPort = "RGBA" });
                        request = new ShaderProgramRuntime.CompilationRequest(example, node.id, output);
                    }
                    else if (!ShaderGraphPreviewVM.TryCreate(example, node.id, output, out vm))
                        throw new InvalidOperationException(kind + ": " + ShaderGraphPreviewVM.LastFallbackReason);
                    started = EditorApplication.timeSinceStartup;
                    Status = "Rendering " + (index + 1) + "/" + kinds.Count + ": " + kind;
                }
                if (request != null)
                {
                    if (!request.TryComplete(out native))
                    {
                        if (EditorApplication.timeSinceStartup - started > 120) throw new InvalidOperationException(Status + ": compilation timed out");
                        return;
                    }
                    request.Dispose(); request = null;
                }
                if (vm != null && !vm.UpdatePreparation())
                {
                    if (vm.Failed || EditorApplication.timeSinceStartup - started > 120)
                        throw new InvalidOperationException(Status + ": " + ShaderGraphPreviewVM.LastFallbackReason);
                    return;
                }
                var previous = RenderTexture.active;
                bool srgb = GL.sRGBWrite;
                float[] curveSamples = null;
                try
                {
                    GL.sRGBWrite = false;
                    if (native != null)
                    {
                        curveSamples = RenderNativeExample(kinds[index], native, example, document, layer, target);
                    }
                    else
                    {
                        vm.Material.SetVector("_CanvasSize", new Vector4(Size, Size, 1f / Size, 1f / Size));
                        vm.Material.SetFloat("_PixelScale", 1);
                        if (kinds[index] == ShaderGraphNodeKind.BlueNoise)
                        {
                            vm.Material.SetTexture("_WTNoiseBlue2D", BlueNoiseTextures.TwoD);
                            vm.Material.SetTexture("_WTNoiseBlue1D", BlueNoiseTextures.OneD);
                        }
                        if (!vm.Render(target, 0)) throw new InvalidOperationException("Thumbnail render failed.");
                    }
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); pixels.Apply();
                }
                finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
                var source = pixels.GetPixels();
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (var c in source) { min = Mathf.Min(min, c.r, c.g, c.b); max = Mathf.Max(max, c.r, c.g, c.b); }
                if (max - min < .00001f) throw new InvalidOperationException(kinds[index] + ": empty example");
                var tile = new Color[Cell * Cell];
                float Display(float value) => Mathf.LinearToGammaSpace(Mathf.Clamp01((value - displayRange.x) / (displayRange.y - displayRange.x)));
                for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
                {
                    var c = source[Mathf.Clamp(y - Padding, 0, Size - 1) * Size + Mathf.Clamp(x - Padding, 0, Size - 1)];
                    if (!float.IsFinite(c.r) || !float.IsFinite(c.g) || !float.IsFinite(c.b))
                        throw new InvalidOperationException(kinds[index] + ": non-finite pixels");
                    tile[y * Cell + x] = new Color(Display(c.r), Display(c.g), Display(c.b), 1);
                }
                DrawCurve(kinds[index], tile, curveSamples);
                AddTile(kinds[index], tile);
                vm?.Dispose(); vm = null; native?.Dispose(); native = null; example = null;
            }
            catch (Exception error) { Stop(); Status = "Failed: " + error.Message; Debug.LogException(error); }
        }

        static void AddTile(ShaderGraphNodeKind kind, Color[] tile)
        {
            int px = index % Columns * Cell, py = index / Columns * Cell;
            atlas.SetPixels(px, py, Cell, Cell, tile);
            entries.Add(new ShaderGraphCatalogThumbnails.Entry { kind = kind.ToString(),
                uv = new Rect((float)(px + Padding) / atlas.width, (float)(py + Padding) / atlas.height,
                    (float)Size / atlas.width, (float)Size / atlas.height) });
            index++;
        }

        static ShaderGraphNode CreateExample(ShaderGraph graph, ShaderGraphNodeKind kind, out string output, out Vector2 range)
        {
            var node = ShaderGraphNodes.Create(kind, Vector2.zero);
            graph.nodes.Add(node);
            output = ShaderGraphNodes.Outputs(node)[0];
            range = new Vector2(0, 1);
            if (kind == ShaderGraphNodeKind.ErosionNoise) return ErosionExample(graph, node, out output);
            if (IsImageExample(kind)) return ImageExample(graph, node, out output);
            // Catalog-only framing and fixed display levels; node creation defaults stay intact.
            switch (kind)
            {
                case ShaderGraphNodeKind.Shape:
                    ShaderGraphNodes.ChangeMode(graph, node, 3);
                    Set(node, "Sides", 3); Set(node, "Size", .8f);
                    Set(node, "Center", .5f, .4f); Set(node, "Rotation", 90); break;
                case ShaderGraphNodeKind.PerlinNoise:
                    Set(node, "Scale", 4); range = new Vector2(.15f, .85f); break;
                case ShaderGraphNodeKind.ValueNoise:
                    Set(node, "Scale", 5); break;
                case ShaderGraphNodeKind.ValueCubicNoise:
                    Set(node, "Scale", 5); range = new Vector2(.25f, .75f); break;
                case ShaderGraphNodeKind.OpenSimplex2Noise:
                    Set(node, "Scale", 3); range = new Vector2(.05f, .95f); break;
                case ShaderGraphNodeKind.OpenSimplex2SNoise:
                    Set(node, "Scale", 3); range = new Vector2(.05f, .95f); break;
                case ShaderGraphNodeKind.FlowNoise:
                    Set(node, "Scale", 3); Set(node, "Phase", 1.4f); range = new Vector2(.1f, .9f); break;
                case ShaderGraphNodeKind.CellularNoise:
                    Set(node, "Scale", 4); range = new Vector2(0, .6f); break;
                case ShaderGraphNodeKind.GaborNoise:
                    Set(node, "Scale", 4); Set(node, "Frequency", 1); Set(node, "Direction", .8f, .6f);
                    range = new Vector2(.05f, .95f); break;
                case ShaderGraphNodeKind.SpotsNoise:
                    Set(node, "Scale", 4); break;
                case ShaderGraphNodeKind.ShardNoise:
                    Set(node, "Scale", 4); range = new Vector2(.1f, .9f); break;
                case ShaderGraphNodeKind.TriangleVoronoiNoise:
                    Set(node, "Scale", 4); Set(node, "Evolution", .7f); range = new Vector2(0, .35f); break;
                case ShaderGraphNodeKind.CausticsNoise:
                    Set(node, "Scale", 5); break;
                case ShaderGraphNodeKind.WhiteNoise: case ShaderGraphNodeKind.BlueNoise:
                    Set(node, "GrainSize", 4); break;
            }
            if (ShaderGraphNodes.IsWarpNoise(kind))
            {
                Set(node, "Scale", 3); Set(node, "Strength", .07f);
                var grid = ShaderGraphNodes.Create(ShaderGraphNodeKind.Grid, Vector2.zero);
                Set(grid, "Scale", 5); Set(grid, "Width", .15f);
                graph.nodes.Add(grid);
                graph.edges.Add(new ShaderGraphEdge { outputNode = node.id, outputPort = "Position", inputNode = grid.id, inputPort = "UV" });
                node = grid; output = "Out";
            }
            return node;
        }

        static ShaderGraphNode ErosionExample(ShaderGraph graph, ShaderGraphNode noise, out string output)
        {
            var recipe = new CatalogGraph(graph);
            var transform = recipe.Add(ShaderGraphNodeKind.TransformUV);
            Set(transform, "Offset", 5.09f, .08f);
            Set(noise, "Scale", .2f);
            Set(noise, "Seed", 1337);
            Set(noise, "Strength", .15f);
            Set(noise, "Detail", 4);
            Set(noise, "SlopeStrength", 2);
            Set(noise, "Branching", .1f);
            Set(noise, "Octaves", 5);
            Set(noise, "Lacunarity", 2);
            Set(noise, "Gain", .45f);
            var levels = recipe.Add(ShaderGraphNodeKind.Levels);
            Set(levels, "InputRange", .41f, 1);
            Set(levels, "Gamma", .38f);
            Set(levels, "OutputRange", 0, 11.67f);
            Set(levels, "Clamp", 1);
            recipe.Wire(transform, noise, "Position");
            recipe.Wire(noise, levels, "Color", "Value");
            output = "Out";
            return levels;
        }

        static void Set(ShaderGraphNode node, string port, float x, float? y = null)
        {
            var input = node.FindInput(port) ?? throw new InvalidOperationException(node.kind + ": missing example input " + port);
            input.value = new Vector4(x, y ?? x, 0, 0);
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
            request?.Dispose(); request = null; native?.Dispose(); native = null; example = null;
            layer?.ReleaseTransientResources(); layer = null;
            if (document != null) UnityEngine.Object.DestroyImmediate(document); document = null;
            if (reference != null) UnityEngine.Object.DestroyImmediate(reference); reference = null;
            if (heightReference != null) UnityEngine.Object.DestroyImmediate(heightReference); heightReference = null;
            if (colorReference != null) UnityEngine.Object.DestroyImmediate(colorReference); colorReference = null;
            if (reliefReference != null) UnityEngine.Object.DestroyImmediate(reliefReference); reliefReference = null;
            if (reliefDetailReference != null) UnityEngine.Object.DestroyImmediate(reliefDetailReference); reliefDetailReference = null;
            if (arrowReference != null) UnityEngine.Object.DestroyImmediate(arrowReference); arrowReference = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null; }
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels); pixels = null;
            if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas); atlas = null;
            Running = false;
        }
    }
}
