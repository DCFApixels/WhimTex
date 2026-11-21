using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexCheckerboard
    {
        private static Texture2D preview, lightSkin, darkSkin, mask, canvas;
        private static Color32 canvasFirst, canvasSecond;

        static WhimTexCheckerboard()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        private static void Release()
        {
            Destroy(ref preview);
            Destroy(ref lightSkin);
            Destroy(ref darkSkin);
            Destroy(ref mask);
            Destroy(ref canvas);
        }

        private static void Destroy(ref Texture2D tile)
        {
            if (tile != null) UnityEngine.Object.DestroyImmediate(tile);
            tile = null;
        }

        internal static Texture2D Preview => Tile(ref preview, Gray(.28f), Gray(.42f));
        internal static Texture2D Thumbnail => EditorGUIUtility.isProSkin
            ? Tile(ref darkSkin, Gray(.26f), Gray(.34f))
            : Tile(ref lightSkin, Gray(.73f), Gray(.83f));

        // White RGB allows vertex tinting. Alpha identifies alternate cells for custom palettes.
        internal static Texture2D Mask => Tile(ref mask, new Color(1, 1, 1, 0), Color.white);
        internal static Color Gray(float v) => new Color(v, v, v, 1);

        internal static Texture2D Canvas
        {
            get
            {
                Color32 first = WhimTexUserSettings.CheckerLight, second = WhimTexUserSettings.CheckerDark;
                if (canvas != null && (!first.Equals(canvasFirst) || !second.Equals(canvasSecond)))
                    SetColors(canvas, first, second);
                canvasFirst = first;
                canvasSecond = second;
                return Tile(ref canvas, first, second);
            }
        }

        private static Texture2D Tile(ref Texture2D tile, Color32 first, Color32 second)
        {
            if (tile != null) return tile;
            tile = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "WhimTex Checkerboard", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat
            };
            SetColors(tile, first, second);
            return tile;
        }

        private static void SetColors(Texture2D tile, Color32 first, Color32 second)
        {
            tile.SetPixels32(new[] { first, second, second, first });
            tile.Apply(false, false);
        }

        // Bulk CPU compositors sample the same tile without a native GetPixel call per output pixel.
        internal static bool Alternate(NativeArray<Color32> pixels, int cellX, int cellY)
            => pixels[(cellX & 1) + (cellY & 1) * 2].a != 0;

        internal static void DrawPreview(MeshGenerationContext context)
            => Draw(context, context.visualElement.contentRect, Preview, 8);

        internal static void Draw(MeshGenerationContext context, Rect rect, Texture2D texture, float cellSize)
        {
            if (!(rect.width > 0 && rect.height > 0 && cellSize > 0)) return;
            float period = 2 * cellSize;
            float u = rect.width / period;
            float v = rect.height / period;
            context.AllocateTempMesh(4, 6, out var vertices, out var indices);
            vertices[0] = new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = Color.white, uv = Vector2.zero };
            vertices[1] = new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = Color.white, uv = new Vector2(u, 0f) };
            vertices[2] = new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = Color.white, uv = new Vector2(u, v) };
            vertices[3] = new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = Color.white, uv = new Vector2(0f, v) };
            indices[0] = 0;
            indices[1] = 1;
            indices[2] = 2;
            indices[3] = 0;
            indices[4] = 2;
            indices[5] = 3;
            Submit(context, vertices, indices, texture);
        }

        internal static void Submit(MeshGenerationContext context, NativeSlice<Vertex> vertices,
            NativeSlice<ushort> indices, Texture2D texture)
        {
#if UNITY_6000_3_OR_NEWER
            context.DrawMesh(vertices, indices, texture, TextureOptions.SkipDynamicAtlas);
#else
            context.DrawMesh(vertices, indices, texture);
#endif
        }
    }
}
