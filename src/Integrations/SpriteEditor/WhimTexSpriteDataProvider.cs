using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class WhimTexSpriteEditorRegistration
    {
        static WhimTexSpriteEditorRegistration()
        {
            WhimTexSpriteEditorBridge.Open = document =>
            {
                Selection.activeObject = document.OutputTexture;
                if (!EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor"))
                    Debug.LogWarning("Unity Sprite Editor could not be opened.", document);
            };
        }
    }

    public sealed class WhimTexSpriteDataProviderFactory : ISpriteDataProviderFactory<TextureCompositor>,
        ISpriteDataProviderFactory<Texture2D>, ISpriteDataProviderFactory<Sprite>
    {
        public ISpriteEditorDataProvider CreateDataProvider(TextureCompositor document) =>
            document != null && document.SpriteOutputSettings.outputType == TextureCompositor.OutputType.Sprite &&
            AssetDatabase.Contains(document) && document.OutputTexture != null && document.OutputTexture.isReadable ? new WhimTexSpriteDataProvider(document) : null;
        public ISpriteEditorDataProvider CreateDataProvider(Texture2D texture)
        {
            var document = TextureCompositor.FindDocument(texture);
            return document != null && document.OutputTexture == texture ? CreateDataProvider(document) : null;
        }
        public ISpriteEditorDataProvider CreateDataProvider(Sprite sprite)
        {
            var document = TextureCompositor.FindDocument(sprite);
            return document != null && document.IsOutputSprite(sprite) ? CreateDataProvider(document) : null;
        }
    }

    internal sealed class WhimTexSpriteDataProvider : ISpriteEditorDataProvider, ITextureDataProvider, ISpriteNameFileIdDataProvider
#if UNITY_6000_3_OR_NEWER
        , ISpriteFrameEditCapability
#endif
    {
        private readonly TextureCompositor document;
        private SpriteRect[] rects;
        private TextureCompositor.SpriteSlice[] original;
        private TextureCompositor.OutputSpriteMode mode;
        private Vector2 originalPivot;
        private Vector4 originalBorder;
        private int width, height;
#if UNITY_6000_3_OR_NEWER
        private EditCapability editCapability;
#endif

        internal WhimTexSpriteDataProvider(TextureCompositor document) { this.document = document; }
        public UnityEngine.Object targetObject => document;
        public float pixelsPerUnit => document.SpriteOutputSettings.pixelsPerUnit;
        public SpriteImportMode spriteImportMode => document.SpriteOutputSettings.outputType == TextureCompositor.OutputType.Texture ? SpriteImportMode.None :
            document.SpriteOutputSettings.spriteMode == TextureCompositor.OutputSpriteMode.Multiple ? SpriteImportMode.Multiple : SpriteImportMode.Single;
        public Texture2D texture => document.OutputTexture;
        public Texture2D previewTexture => texture;
        public Texture2D GetReadableTexture2D() => texture;
        public void GetTextureActualWidthAndHeight(out int w, out int h) { w = document.width; h = document.height; }
        public T GetDataProvider<T>() where T : class => this as T;
        public bool HasDataProvider(Type type) => type.IsInstanceOfType(this);

        public void InitSpriteEditorDataProvider()
        {
            mode = document.SpriteOutputSettings.spriteMode;
#if UNITY_6000_3_OR_NEWER
            editCapability = AllowedCapabilities();
#endif
            width = document.width; height = document.height;
            originalPivot = document.SpriteOutputSettings.pivot;
            originalBorder = document.SpriteOutputSettings.border;
            original = document.GetSpriteSlices();
            if (mode == TextureCompositor.OutputSpriteMode.Single)
                rects = new[] { new SpriteRect { name = "Output Sprite", spriteID = new GUID(TextureCompositor.SingleSpriteId), rect = new Rect(0, 0, width, height),
                    pivot = originalPivot, border = originalBorder, alignment = SpriteAlignment.Custom } };
            else
            {
                rects = new SpriteRect[original.Length];
                for (int i = 0; i < rects.Length; i++)
                {
                    var slice = original[i];
                    rects[i] = new SpriteRect { name = slice.name, spriteID = new GUID(slice.id), rect = slice.rect,
                        pivot = slice.pivot, border = slice.border, alignment = SpriteAlignment.Custom };
                }
            }
        }

        public SpriteRect[] GetSpriteRects() => Clone(rects);
        public void SetSpriteRects(SpriteRect[] value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
#if UNITY_6000_3_OR_NEWER
            var capability = GetDataProvider<ISpriteFrameEditCapability>();
            if (capability == null) throw new InvalidOperationException("Sprite editing is not supported.");
            void Require(EEditCapability flag)
            {
                if (!capability.GetEditCapability().HasCapability(flag)) throw new InvalidOperationException("Sprite editing capability is disabled: " + flag);
            }
            foreach (var previous in rects)
                if (!Array.Exists(value, x => x.spriteID == previous.spriteID)) Require(EEditCapability.CreateAndDeleteSprite);
            foreach (var next in value)
            {
                var previous = Array.Find(rects, x => x.spriteID == next.spriteID);
                if (previous == null) { Require(EEditCapability.CreateAndDeleteSprite); continue; }
                if (previous.name != next.name) Require(EEditCapability.EditSpriteName);
                if (previous.rect != next.rect) Require(EEditCapability.EditSpriteRect);
                if (previous.pivot != next.pivot) Require(EEditCapability.EditPivot);
                if (previous.border != next.border) Require(EEditCapability.EditBorder);
            }
#endif
            if (mode == TextureCompositor.OutputSpriteMode.Single && value.Length != 1)
                throw new InvalidOperationException("Single mode requires one sprite.");
            rects = Clone(value);
        }

        public void Apply()
        {
            if (document == null || !AssetDatabase.Contains(document)) throw new InvalidOperationException("The WhimTex document is no longer available.");
            if (WhimTexLegacyMigration.IsLegacyAsset(document))
                throw new InvalidOperationException("Legacy WhimTex .asset documents are read-only. Migrate the document to TIFF before editing sprites.");
            var settings = document.SpriteOutputSettings;
            if (settings.outputType != TextureCompositor.OutputType.Sprite || settings.spriteMode != mode || document.width != width || document.height != height ||
                settings.pivot != originalPivot || settings.border != originalBorder || !SameSlices(original, document.GetSpriteSlices()))
                throw new InvalidOperationException("WhimTex sprite settings changed while Sprite Editor was open. Reopen Sprite Editor before applying.");
            var slices = new TextureCompositor.SpriteSlice[rects.Length];
            for (int i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                slices[i] = new TextureCompositor.SpriteSlice { id = r.spriteID.ToString(), name = r.name, rect = r.rect, pivot = r.pivot, border = r.border };
            }
            TextureCompositor.ValidateSpriteSlices(slices, width, height);
            Undo.RegisterCompleteObjectUndo(document, "Edit Sprite Slicing");
            try
            {
                if (mode == TextureCompositor.OutputSpriteMode.Multiple) document.SetSpriteSlices(slices);
                else { settings.pivot = slices[0].pivot; settings.border = slices[0].border; document.MarkChanged(); }
                throw new InvalidOperationException("WhimTex legacy .asset documents are read-only. Save the document as TIFF before applying sprite settings.");
            }
            catch
            {
                settings.pivot = originalPivot; settings.border = originalBorder;
                document.SetSpriteSlices(original);
                throw;
            }
            InitSpriteEditorDataProvider();
        }

        public IEnumerable<SpriteNameFileIdPair> GetNameFileIdPairs()
        {
            foreach (var r in rects) yield return new SpriteNameFileIdPair(r.name, r.spriteID);
        }
        public void SetNameFileIdPairs(IEnumerable<SpriteNameFileIdPair> pairs)
        {
            // The native subassets are identified by the stable IDs in rects, never by their names.
        }
#if UNITY_6000_3_OR_NEWER
        private EditCapability AllowedCapabilities() => mode == TextureCompositor.OutputSpriteMode.Multiple
            ? new EditCapability(EEditCapability.EditSpriteName, EEditCapability.EditSpriteRect, EEditCapability.EditBorder, EEditCapability.EditPivot, EEditCapability.CreateAndDeleteSprite)
            : new EditCapability(EEditCapability.EditBorder, EEditCapability.EditPivot);
        public EditCapability GetEditCapability() => editCapability;
        public void SetEditCapability(EditCapability capability)
        {
            var allowed = AllowedCapabilities();
            editCapability = new EditCapability();
            foreach (EEditCapability flag in new[] { EEditCapability.EditSpriteName, EEditCapability.EditSpriteRect,
                EEditCapability.EditBorder, EEditCapability.EditPivot, EEditCapability.CreateAndDeleteSprite })
                editCapability.SetCapability(flag, allowed.HasCapability(flag) && capability.HasCapability(flag));
        }
#endif
        private static SpriteRect[] Clone(SpriteRect[] source)
        {
            if (source == null) return Array.Empty<SpriteRect>();
            var copy = new SpriteRect[source.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var r = source[i];
                copy[i] = new SpriteRect { name = r.name, spriteID = r.spriteID, rect = r.rect, pivot = r.pivot, border = r.border, alignment = r.alignment };
            }
            return copy;
        }
        private static bool SameSlices(TextureCompositor.SpriteSlice[] a, TextureCompositor.SpriteSlice[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].id != b[i].id || a[i].name != b[i].name || a[i].rect != b[i].rect || a[i].pivot != b[i].pivot || a[i].border != b[i].border) return false;
            return true;
        }
    }
}
