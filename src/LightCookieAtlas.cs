using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Only instantiated for active Spot cookies. Source assets remain untouched.
    internal sealed class LightCookieAtlas : IDisposable
    {
        const int MaximumSize = 4096, MaximumCookieSize = 1024, Border = 2;
        readonly List<Texture2D> textures = new();
        readonly List<RectInt> slots = new();
        readonly Vector4[] views = new Vector4[WhimTexDocument.MaximumLights];
        RenderTexture atlas;
        Material copy;
        ulong stamp;
        int atlasWidth, atlasHeight;

        internal static ulong TextureStamp(ulong hash, Texture2D texture)
        {
            if (texture == null) return RenderFingerprint.Mix(hash, 0);
            foreach (char c in UnityObjectID.FromObject(texture).ToString()) hash = RenderFingerprint.Mix(hash, c);
            hash = RenderFingerprint.Mix(hash, texture.updateCount);
            hash = RenderFingerprint.Mix(hash, (ulong)EditorUtility.GetDirtyCount(texture));
            hash = RenderFingerprint.Mix(hash, (ulong)texture.width);
            return RenderFingerprint.Mix(hash, (ulong)texture.height);
        }

        internal void Bind(Material material, List<WhimTexLight> lights)
        {
            ulong next = 14695981039346656037UL;
            for (int i = 0; i < lights.Count; i++)
                next = TextureStamp(RenderFingerprint.Mix(next, (ulong)i), lights[i].ActiveCookie ? lights[i].cookie : null);
            if (atlas == null || !atlas.IsCreated() || next != stamp)
            {
                Rebuild(lights);
                stamp = next;
            }
            material.SetTexture("_WhimTex_LightCookieAtlas", atlas);
            material.SetVectorArray("_WhimTex_LightCookieViews", views);
        }

        void Rebuild(List<WhimTexLight> lights)
        {
            textures.Clear();
            foreach (var light in lights)
                if (light.ActiveCookie && !textures.Contains(light.cookie)) textures.Add(light.cookie);
            int limit = Mathf.Min(MaximumSize, SystemInfo.maxTextureSize);
            int cap = Mathf.Min(MaximumCookieSize, limit - Border * 2);
            while (!Pack(cap, limit)) cap = Mathf.Max(1, cap / 2);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture replacement = null;
            try
            {
                replacement = new RenderTexture(atlasWidth, atlasHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { name = "WhimTex light cookies", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!replacement.Create()) throw new InvalidOperationException("Cannot allocate the light Cookie atlas.");
                WhimTexProfiler.TrackTexture(replacement, "light cookies");
                if (copy == null) copy = new Material(Shader.Find("Hidden/WhimTex/LightCookieCopy")) { hideFlags = HideFlags.HideAndDontSave };
                GL.sRGBWrite = false;
                RenderTexture.active = replacement; GL.Clear(false, true, Color.clear);
                for (int i = 0; i < textures.Count; i++)
                {
                    var slot = slots[i];
                    var tile = RenderTexture.GetTemporary(slot.width, slot.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    try
                    {
                        copy.SetVector("_CookieInterior", new Vector4(Border, Border, slot.width - Border * 2, slot.height - Border * 2));
                        copy.SetVector("_CookieTileSize", new Vector4(slot.width, slot.height, 0, 0));
                        Graphics.Blit(textures[i], tile, copy);
                        Graphics.CopyTexture(tile, 0, 0, 0, 0, slot.width, slot.height, replacement, 0, 0, slot.x, slot.y);
                    }
                    finally { RenderTexture.ReleaseTemporary(tile); }
                }
                for (int i = 0; i < lights.Count; i++)
                {
                    int index = lights[i].ActiveCookie ? textures.IndexOf(lights[i].cookie) : -1;
                    if (index < 0) { views[i] = Vector4.zero; continue; }
                    var slot = slots[index];
                    views[i] = new Vector4((slot.x + Border) / (float)atlasWidth, (slot.y + Border) / (float)atlasHeight,
                        (slot.width - Border * 2) / (float)atlasWidth, (slot.height - Border * 2) / (float)atlasHeight);
                }
                WhimTexProfiler.ReleaseTexture(atlas); UnityEngine.Object.DestroyImmediate(atlas);
                atlas = replacement; replacement = null;
            }
            finally
            {
                if (replacement != null) { WhimTexProfiler.ReleaseTexture(replacement); UnityEngine.Object.DestroyImmediate(replacement); }
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }

        bool Pack(int cap, int limit)
        {
            slots.Clear();
            int area = 0, widest = 1;
            foreach (var texture in textures)
            {
                float scale = Mathf.Min(1, cap / (float)Mathf.Max(texture.width, texture.height));
                int width = Mathf.Max(1, Mathf.CeilToInt(texture.width * scale)) + Border * 2;
                int height = Mathf.Max(1, Mathf.CeilToInt(texture.height * scale)) + Border * 2;
                slots.Add(new RectInt(0, 0, width, height)); area += width * height; widest = Mathf.Max(widest, width);
            }
            int start = Mathf.NextPowerOfTwo(Mathf.Max(widest, Mathf.CeilToInt(Mathf.Sqrt(area))));
            for (int width = Mathf.Min(start, limit); width <= limit; width *= 2)
            {
                int x = 0, y = 0, row = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    var r = slots[i];
                    if (x + r.width > width) { x = 0; y += row; row = 0; }
                    r.position = new Vector2Int(x, y); slots[i] = r;
                    x += r.width; row = Mathf.Max(row, r.height);
                }
                int height = Mathf.NextPowerOfTwo(Mathf.Max(1, y + row));
                if (width >= widest && height <= limit) { atlasWidth = width; atlasHeight = height; return true; }
                if (width == limit) break;
            }
            return false;
        }

        public void Dispose()
        {
            WhimTexProfiler.ReleaseTexture(atlas);
            UnityEngine.Object.DestroyImmediate(atlas); UnityEngine.Object.DestroyImmediate(copy);
            atlas = null; copy = null;
        }
    }
}
