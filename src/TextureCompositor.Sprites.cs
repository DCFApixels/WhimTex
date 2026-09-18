using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        public enum OutputSpriteMode { Single, Multiple }
        internal const string SingleSpriteId = "dcb3c661e4cb4d44a891c6374f5f2c0c";

        [Serializable]
        public sealed class SpriteSlice
        {
            public string id = Guid.NewGuid().ToString("N");
            public string name = "Sprite";
            public Rect rect;
            public Vector2 pivot = new Vector2(.5f, .5f);
            public Vector4 border;
            public SpriteSlice Copy() => (SpriteSlice)MemberwiseClone();
        }

        [Serializable]
        private sealed class SliceOutput
        {
            public string id;
            public Sprite sprite;
        }

        [SerializeField, HideInInspector] private List<SpriteSlice> spriteSlices = new List<SpriteSlice>();
        [SerializeField, HideInInspector] private List<SliceOutput> sliceOutputs = new List<SliceOutput>();
        internal OutputSettings SpriteOutputSettings => outputSettings ??= new OutputSettings();
        internal SpriteSlice[] GetSpriteSlices() => spriteSlices.ConvertAll(x => x.Copy()).ToArray();

        internal static void ValidateSpriteSlices(SpriteSlice[] slices, int width, int height)
        {
            if (slices == null) throw new ArgumentNullException(nameof(slices));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slice in slices)
            {
                if (slice == null || !Guid.TryParseExact(slice.id, "N", out _) || !ids.Add(slice.id))
                    throw new InvalidOperationException("Sprite IDs must be unique valid GUIDs.");
                if (string.IsNullOrWhiteSpace(slice.name) || !names.Add(slice.name))
                    throw new InvalidOperationException("Sprite names must be non-empty and unique.");
                Rect r = slice.rect;
                if (!Finite(r.x) || !Finite(r.y) || !Finite(r.width) || !Finite(r.height) ||
                    r.x < 0 || r.y < 0 || r.width < 1 || r.height < 1 || r.xMax > width || r.yMax > height)
                    throw new InvalidOperationException("Sprite '" + slice.name + "' is outside the canvas or has an invalid size. Adjust its rectangle before saving.");
                if (!Finite(slice.pivot.x) || !Finite(slice.pivot.y) || slice.pivot.x < 0 || slice.pivot.x > 1 || slice.pivot.y < 0 || slice.pivot.y > 1)
                    throw new InvalidOperationException("Sprite pivots must be between 0 and 1.");
                for (int i = 0; i < 4; i++)
                    if (!Finite(slice.border[i]) || slice.border[i] < 0) throw new InvalidOperationException("Sprite borders must be finite and non-negative.");
                if (slice.border.x + slice.border.z > r.width || slice.border.y + slice.border.w > r.height)
                    throw new InvalidOperationException("Sprite borders exceed the sprite rectangle.");
            }
            static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        }

        internal void SetSpriteSlices(SpriteSlice[] slices)
        {
            ValidateSpriteSlices(slices, width, height);
            spriteSlices = new List<SpriteSlice>(slices.Length);
            foreach (var slice in slices) spriteSlices.Add(slice.Copy());
            MarkChanged();
        }

        internal bool IsOutputSprite(Sprite sprite)
        {
            if (sprite == null) return false;
            if (sprite == outputSprite) return true;
            return sliceOutputs.Exists(x => x.sprite == sprite);
        }

        private void RemoveOutputSprites(string path)
        {
            if (outputSprite != null && AssetDatabase.GetAssetPath(outputSprite) == path)
                DestroyImmediate(outputSprite, true);
            outputSprite = null;
            foreach (var entry in sliceOutputs)
                if (entry.sprite != null && AssetDatabase.GetAssetPath(entry.sprite) == path)
                    DestroyImmediate(entry.sprite, true);
            sliceOutputs.Clear();
        }

        private void SaveSliceOutputs(string path)
        {
            bool multiple = SpriteOutputSettings.spriteMode == OutputSpriteMode.Multiple;
            outputSprite.hideFlags = multiple ? HideFlags.HideInHierarchy : HideFlags.None;
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slice in spriteSlices) keep.Add(slice.id);
            for (int i = sliceOutputs.Count - 1; i >= 0; i--)
            {
                var entry = sliceOutputs[i];
                if (entry.sprite != null && AssetDatabase.GetAssetPath(entry.sprite) != path) entry.sprite = null;
                if (!keep.Contains(entry.id))
                {
                    if (entry.sprite != null) DestroyImmediate(entry.sprite, true);
                    sliceOutputs.RemoveAt(i);
                }
            }
            foreach (var slice in spriteSlices)
            {
                var entry = sliceOutputs.Find(x => x.id == slice.id);
                if (!multiple)
                {
                    if (entry?.sprite != null) { entry.sprite.hideFlags = HideFlags.HideInHierarchy; EditorUtility.SetDirty(entry.sprite); }
                    continue;
                }
                var settings = SpriteOutputSettings;
                float sx = (float)outputTexture.width / width, sy = (float)outputTexture.height / height;
                Rect rect = new Rect(slice.rect.x * sx, slice.rect.y * sy, slice.rect.width * sx, slice.rect.height * sy);
                Sprite generated = Sprite.Create(outputTexture, rect, slice.pivot, settings.pixelsPerUnit * sx,
                    (uint)settings.extrude, settings.meshType, ScaleOutputBorder(slice.border), settings.generatePhysicsShape);
                if (generated == null) throw new InvalidOperationException("Could not create sprite '" + slice.name + "'.");
                try
                {
                    generated.name = slice.name;
                    UnityEditor.U2D.SpriteEditorExtension.SetSpriteID(generated, new GUID(slice.id));
                    if (entry == null) { entry = new SliceOutput { id = slice.id }; sliceOutputs.Add(entry); }
                    if (entry.sprite == null)
                    {
                        AssetDatabase.AddObjectToAsset(generated, this);
                        entry.sprite = generated;
                    }
                    else EditorUtility.CopySerialized(generated, entry.sprite);
                    entry.sprite.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(entry.sprite);
                }
                finally { if (!AssetDatabase.Contains(generated)) DestroyImmediate(generated); }
            }
        }
    }
}
