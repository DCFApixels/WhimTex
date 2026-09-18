using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        private const string OutputSpriteName = "Output Sprite";

        public enum OutputStorage { HdrHalf, HdrFloat, LinearRgba32, SrgbRgba32 }

        [Serializable]
        public sealed class OutputSettings
        {
            public OutputStorage storage = OutputStorage.HdrHalf;
            public TextureWrapMode wrapU = TextureWrapMode.Clamp;
            public TextureWrapMode wrapV = TextureWrapMode.Clamp;
            [Range(0, 16)] public int anisoLevel = 1;
            public bool mipMaps;
            public OutputSpriteMode spriteMode;
            public float pixelsPerUnit = 100f;
            public Vector2 pivot = new Vector2(.5f, .5f);
            public Vector4 border;
            public SpriteMeshType meshType = SpriteMeshType.FullRect;
            [Range(0, 32)] public int extrude;
            public bool generatePhysicsShape;

            internal void Validate(int width, int height)
            {
                if (!Enum.IsDefined(typeof(OutputSpriteMode), spriteMode) || !Enum.IsDefined(typeof(OutputStorage), storage) ||
                    !Enum.IsDefined(typeof(TextureWrapMode), wrapU) || !Enum.IsDefined(typeof(TextureWrapMode), wrapV) ||
                    !Enum.IsDefined(typeof(SpriteMeshType), meshType))
                    throw new InvalidOperationException("Invalid output texture or sprite mode.");
                if (!Finite(pixelsPerUnit) || pixelsPerUnit <= 0 || !Finite(pivot.x) || !Finite(pivot.y) ||
                    pivot.x < 0 || pivot.x > 1 || pivot.y < 0 || pivot.y > 1)
                    throw new InvalidOperationException("Pixels Per Unit must be positive; Pivot must be between 0 and 1.");
                for (int i = 0; i < 4; i++)
                    if (!Finite(border[i]) || border[i] < 0) throw new InvalidOperationException("Sprite borders must be finite and non-negative.");
                if (border.x + border.z > width || border.y + border.w > height)
                    throw new InvalidOperationException("Sprite borders exceed the canvas dimensions.");
                if (anisoLevel < 0 || anisoLevel > 16 || extrude < 0 || extrude > 32)
                    throw new InvalidOperationException("Aniso Level must be 0–16; Extrude must be 0–32.");
            }

            private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        }

        [SerializeField, HideInInspector] private OutputSettings outputSettings = new OutputSettings();

        internal Texture2D CreateSavedOutput()
        {
            var settings = outputSettings ?? new OutputSettings();
            NormalizeModel();
            settings.Validate(width, height);
            if (settings.spriteMode == OutputSpriteMode.Multiple)
                ValidateSpriteSlices(GetSpriteSlices(), width, height);
            TextureFormat format = settings.storage == OutputStorage.HdrHalf ? TextureFormat.RGBAHalf :
                settings.storage == OutputStorage.HdrFloat ? TextureFormat.RGBAFloat : TextureFormat.RGBA32;
            if (!SystemInfo.SupportsTextureFormat(format))
                throw new InvalidOperationException("This device does not support the selected output texture format.");
            RenderTexture composite = RenderComposite(width, height, 1f);
            RenderTexture converted = null;
            Texture2D texture = null;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                bool srgb = settings.storage == OutputStorage.SrgbRgba32;
                var rtFormat = format == TextureFormat.RGBA32 ? RenderTextureFormat.ARGB32 :
                    format == TextureFormat.RGBAFloat ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf;
                converted = RenderTexture.GetTemporary(width, height, 0, rtFormat, RenderTextureReadWrite.Linear);
                var conversion = WhimTexMaterials.Hdr;
                conversion.SetFloat("_Saturate", format == TextureFormat.RGBA32 ? 1f : 0f);
                conversion.SetFloat("_Encode", srgb ? 1f : 0f);
                conversion.SetFloat("_UseSwizzle", 0f);
                GL.sRGBWrite = false;
                Graphics.Blit(composite, converted, conversion, 0);
                texture = new Texture2D(width, height, format, settings.mipMaps, !srgb)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = outputFilter, wrapModeU = settings.wrapU, wrapModeV = settings.wrapV,
                    anisoLevel = settings.anisoLevel
                };
                RenderTexture.active = converted;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(settings.mipMaps, false);
                return texture;
            }
            catch { if (texture != null) DestroyImmediate(texture); throw; }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                if (converted != null) RenderTexture.ReleaseTemporary(converted);
                RenderTexture.ReleaseTemporary(composite);
            }
        }

        [SerializeField, HideInInspector] private Texture2D outputTexture;
        [SerializeField, HideInInspector] private Sprite outputSprite;

        public Texture2D OutputTexture => outputTexture;
        public Sprite OutputSprite => outputSprite;

        [NonSerialized] private LiveOutputSession liveOutput;
        internal static event Action<CompositorOutputChange> OutputTextureChanged;

        private void NotifyOutputTextureChanged()
        {
            if (outputTexture != null && OutputTextureChanged != null)
                OutputTextureChanged.Invoke(new CompositorOutputChange(this));
        }

        internal void PublishLiveOutput(RenderTexture source)
        {
            if (source == null || outputTexture == null || !AssetDatabase.Contains(this)) return;
            if (liveOutput != null && !liveOutput.Matches(outputTexture)) StopLiveOutput();
            liveOutput ??= new LiveOutputSession(outputTexture);
            liveOutput.SetFilter(outputFilter);
            liveOutput.Publish(source);
            NotifyOutputTextureChanged();
        }

        internal void StopLiveOutput()
        {
            LiveOutputSession previous = liveOutput;
            liveOutput = null;
            previous?.Dispose();
            if (previous != null) NotifyOutputTextureChanged();
        }

        internal bool HasUnsavedAssetChanges()
        {
            if (EditorUtility.IsDirty(this) || outputTexture == null || outputSprite == null ||
                EditorUtility.IsDirty(outputTexture) || EditorUtility.IsDirty(outputSprite)) return true;
            foreach (ShaderFX effect in embeddedShaderFX)
                if (effect != null && EditorUtility.IsDirty(effect)) return true;
            return HasDirtyPixels(layers);

            bool HasDirtyPixels(System.Collections.Generic.List<Layer> source)
            {
                foreach (Layer layer in source)
                {
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null &&
                        EditorUtility.IsDirty(drawing.StoredTexture)) return true;
                    if (layer?.AsGroup() is Layer group && HasDirtyPixels(group.layers)) return true;
                }
                return false;
            }
        }

        internal static TextureCompositor FindDocument(UnityEngine.Object asset)
        {
            if (asset is TextureCompositor document)
                return document;
            if (!(asset is Texture2D) && !(asset is Sprite))
                return null;
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                return null;
            foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                if (child is TextureCompositor compositor)
                    return compositor;
            return null;
        }

        internal bool TrySaveWithOutput(string newAssetPath = null)
        {
            try
            {
                SaveWithOutput(newAssetPath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                EditorUtility.DisplayDialog("WhimTex save failed", exception.Message, "OK");
                return false;
            }
        }

        internal void SaveWithOutput(string newAssetPath)
        {
            bool createAsset = !string.IsNullOrEmpty(newAssetPath);
            string path = createAsset ? newAssetPath : AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the document as a Unity .asset file first.");
            if (createAsset && (AssetDatabase.Contains(this) || File.Exists(path) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null))
                throw new InvalidOperationException("Save As requires a new document and an unused asset path.");

            StopLiveOutput();
            Undo.FlushUndoRecordObjects();
            RemoveUnusedEmbeddedShaderFX();
            SyncDrawingLayerTextures();
            Texture2D rendered = null;
            Sprite generatedSprite = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                rendered = CreateSavedOutput();
                if (rendered == null)
                    throw new InvalidOperationException("The compositor returned no output texture.");
                if (createAsset)
                {
                    outputTexture = null;
                    outputSprite = null;
                    AssetDatabase.CreateAsset(this, path);
                    if (AssetDatabase.GetAssetPath(this) != path)
                        throw new InvalidOperationException("Unity could not create the document asset at " + path);
                }
                PersistDrawingLayerTextures(reimport: false);
                PersistEmbeddedShaderFX();
                RecoverOutputReferences(path);

                rendered.name = Path.GetFileNameWithoutExtension(path);
                rendered.hideFlags = HideFlags.None;
                if (outputTexture == null)
                {
                    AssetDatabase.AddObjectToAsset(rendered, this);
                    if (AssetDatabase.GetAssetPath(rendered) != path)
                        throw new InvalidOperationException("Unity could not store the output texture in " + path);
                    outputTexture = rendered;
                    rendered = null;
                }
                else
                {
                    EditorUtility.CopySerialized(rendered, outputTexture);
                }

                var settings = outputSettings ?? new OutputSettings();
                generatedSprite = Sprite.Create(outputTexture,
                    new Rect(0f, 0f, outputTexture.width, outputTexture.height),
                    settings.pivot, settings.pixelsPerUnit, (uint)settings.extrude, settings.meshType,
                    settings.border, settings.generatePhysicsShape);
                if (generatedSprite == null)
                    throw new InvalidOperationException("Unity could not create the output sprite.");
                generatedSprite.name = OutputSpriteName;
                UnityEditor.U2D.SpriteEditorExtension.SetSpriteID(generatedSprite, new GUID(SingleSpriteId));
                generatedSprite.hideFlags = HideFlags.None;
                if (outputSprite == null)
                {
                    AssetDatabase.AddObjectToAsset(generatedSprite, this);
                    if (AssetDatabase.GetAssetPath(generatedSprite) != path)
                        throw new InvalidOperationException("Unity could not store the output sprite in " + path);
                    outputSprite = generatedSprite;
                    generatedSprite = null;
                }
                else
                {
                    EditorUtility.CopySerialized(generatedSprite, outputSprite);
                }

                EditorUtility.SetDirty(outputTexture);
                SaveSliceOutputs(path);
                EditorUtility.SetDirty(outputSprite);
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssetIfDirty(this);
                AssetDatabase.SetMainObject(outputTexture, path);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureCompositorProjectPreview.ClearCache();
                NotifyOutputTextureChanged();
                Changed?.Invoke(this);
            }
            finally
            {
                RenderTexture.active = previous;
                if (generatedSprite != null && !AssetDatabase.Contains(generatedSprite))
                    DestroyImmediate(generatedSprite);
                if (rendered != null && !AssetDatabase.Contains(rendered))
                    DestroyImmediate(rendered);
            }
        }

        private void RecoverOutputReferences(string path)
        {
            if (outputTexture != null && AssetDatabase.GetAssetPath(outputTexture) != path)
                outputTexture = null;
            if (outputSprite != null && AssetDatabase.GetAssetPath(outputSprite) != path)
                outputSprite = null;
            if (outputTexture == null)
                outputTexture = AssetDatabase.LoadMainAssetAtPath(path) as Texture2D;
            if (outputSprite == null && outputTexture != null)
            {
                foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (child is Sprite sprite && sprite.name == OutputSpriteName && sprite.texture == outputTexture)
                    {
                        outputSprite = sprite;
                        break;
                    }
            }
        }
    }
}
