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
        public enum OutputType { Texture = 1, Sprite = 0 }
        // Kept for deserializing old ScriptableObject documents and compatibility tests. New TIFF
        // documents use Unity's TextureImporter for compression/platform overrides; the editor no
        // longer exposes these legacy embedded-output controls.
        public enum OutputCompression { None, BC1, BC3, BC7, BC6H, Automatic }
        public enum OutputCompressionLevel { None, LowQuality, NormalQuality, HighQuality }

        [Serializable]
        public sealed class OutputSettings
        {
            public string linkedTextureGuid;
            public OutputType outputType = OutputType.Sprite;
            public OutputStorage storage = OutputStorage.HdrHalf;
            public bool alphaIsTransparency;
            public bool readable = true;
            public int maxSize = 16384;
            public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            public TextureImporterMipFilter mipFilter = TextureImporterMipFilter.BoxFilter;
            public bool preserveCoverage;
            [Range(0, 1)] public float alphaCutoff = 0.5f;
            public OutputCompression compression;
            public TextureCompressionQuality compressionQuality = TextureCompressionQuality.Normal;
            public OutputCompressionLevel compressionLevel = OutputCompressionLevel.NormalQuality;
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
                if (!Enum.IsDefined(typeof(OutputType), outputType)) throw new OutputSettingsError("Choose a valid output type.", "outputType");
                if (!Enum.IsDefined(typeof(OutputStorage), storage)) throw new OutputSettingsError("Choose a valid storage format.", "storage");
                if (!Enum.IsDefined(typeof(TextureWrapMode), wrapU)) throw new OutputSettingsError("Choose a valid horizontal wrap mode.", "wrapU");
                if (!Enum.IsDefined(typeof(TextureWrapMode), wrapV)) throw new OutputSettingsError("Choose a valid vertical wrap mode.", "wrapV");
                if (anisoLevel < 0 || anisoLevel > 16)
                    throw new OutputSettingsError("Aniso Level must be 0–16.", "anisoLevel");
                if (maxSize < 4 || maxSize > 16384 || !Mathf.IsPowerOfTwo(maxSize))
                    throw new OutputSettingsError("Max Size must be a power of two between 4 and 16384.", "maxSize");
                if (!Enum.IsDefined(typeof(TextureResizeAlgorithm), resizeAlgorithm))
                    throw new OutputSettingsError("Choose a valid resize algorithm.", "resizeAlgorithm");
                if (!Enum.IsDefined(typeof(TextureImporterMipFilter), mipFilter))
                    throw new OutputSettingsError("Choose a valid mipmap filter.", "mipFilter");
                if (!Finite(alphaCutoff) || alphaCutoff < 0 || alphaCutoff > 1)
                    throw new OutputSettingsError("Alpha Cutoff must be between 0 and 1.", "alphaCutoff");
                var size = GetSize(width, height);
                ValidateCompression(size.x, size.y);
                if (outputType == OutputType.Texture) return;
                if (!Enum.IsDefined(typeof(OutputSpriteMode), spriteMode)) throw new OutputSettingsError("Choose a valid sprite mode.", "spriteMode");
                if (!Enum.IsDefined(typeof(SpriteMeshType), meshType)) throw new OutputSettingsError("Choose a valid mesh type.", "meshType");
                if (!Finite(pixelsPerUnit) || pixelsPerUnit <= 0)
                    throw new OutputSettingsError("Pixels Per Unit must be finite and greater than zero.", "pixelsPerUnit");
                if (!Finite(pivot.x) || !Finite(pivot.y) ||
                    pivot.x < 0 || pivot.x > 1 || pivot.y < 0 || pivot.y > 1)
                    throw new OutputSettingsError("Pivot coordinates must be between 0 and 1.", "pivot");
                for (int i = 0; i < 4; i++)
                    if (!Finite(border[i]) || border[i] < 0) throw new OutputSettingsError("Sprite borders must be finite and non-negative.", "border");
                if (border.x + border.z > width || border.y + border.w > height)
                    throw new OutputSettingsError("Sprite borders exceed the canvas dimensions.", "border");
                if (anisoLevel < 0 || anisoLevel > 16 || extrude < 0 || extrude > 32)
                    throw new OutputSettingsError("Aniso Level must be 0–16; Extrude must be 0–32.", "extrude");
            }

            private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

            internal Vector2Int GetSize(int width, int height)
            {
                float scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(width, height));
                return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * scale)), Mathf.Max(1, Mathf.RoundToInt(height * scale)));
            }

            internal bool CompressionEnabled => compression != OutputCompression.None &&
                (compression != OutputCompression.Automatic || compressionLevel != OutputCompressionLevel.None);

            internal TextureFormat ResolveCompression(Texture2D texture)
            {
                if (compression != OutputCompression.Automatic) return CompressedFormat;
                bool hdr = storage == OutputStorage.HdrHalf || storage == OutputStorage.HdrFloat;
                bool alpha = false;
                if (hdr)
                {
                    foreach (Color pixel in texture.GetPixels())
                        if (pixel.a != 1f || pixel.r < 0f || pixel.g < 0f || pixel.b < 0f)
                            return texture.format;
                    return TextureFormat.BC6H;
                }
                var pixels = texture.GetPixelData<Color32>(0);
                for (int i = 0; i < pixels.Length; i++)
                    if (pixels[i].a != 255) { alpha = true; break; }
                return compressionLevel == OutputCompressionLevel.HighQuality ? TextureFormat.BC7 :
                    alpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
            }

            internal TextureCompressionQuality ResolvedCompressionQuality => compression != OutputCompression.Automatic
                ? compressionQuality : compressionLevel == OutputCompressionLevel.LowQuality ? TextureCompressionQuality.Fast
                : compressionLevel == OutputCompressionLevel.HighQuality ? TextureCompressionQuality.Best : TextureCompressionQuality.Normal;

            internal TextureFormat CompressedFormat => compression switch
            {
                OutputCompression.BC1 => TextureFormat.DXT1,
                OutputCompression.BC3 => TextureFormat.DXT5,
                OutputCompression.BC7 => TextureFormat.BC7,
                OutputCompression.BC6H => TextureFormat.BC6H,
                _ => throw new InvalidOperationException("No compression format selected.")
            };

            private void ValidateCompression(int width, int height)
            {
                if (!Enum.IsDefined(typeof(OutputCompression), compression) || !Enum.IsDefined(typeof(TextureCompressionQuality), compressionQuality) ||
                    !Enum.IsDefined(typeof(OutputCompressionLevel), compressionLevel))
                    throw new OutputSettingsError("Invalid texture compression settings.", "compression", "compressionQuality", "compressionLevel");
                if (!CompressionEnabled) return;
                bool hdr = storage == OutputStorage.HdrHalf || storage == OutputStorage.HdrFloat;
                if (compression != OutputCompression.Automatic && hdr != (compression == OutputCompression.BC6H))
                    throw new OutputSettingsError("BC6H requires HDR storage. BC1, BC3 and BC7 require Linear RGBA32 or sRGB RGBA32 storage.", "storage", "compression");
                if (width % 4 != 0 || height % 4 != 0)
                    throw new OutputSettingsError("BC compression requires output dimensions divisible by 4. Adjust Max Size or the canvas dimensions.", "maxSize", "compression");
                if (compression != OutputCompression.Automatic && !SystemInfo.SupportsTextureFormat(CompressedFormat))
                    throw new OutputSettingsError("This graphics device does not support the selected compressed format.", "compression");
            }
        }

        [SerializeField, HideInInspector] private OutputSettings outputSettings = new OutputSettings();

        internal Texture2D CreateSavedOutput() => CreateSavedOutputWithLinkedTexture(null, out _);

        private Texture2D CreateSavedOutputWithLinkedTexture(string linkedPath, out byte[] linkedBytes)
        {
            linkedBytes = null;
            var settings = outputSettings ?? new OutputSettings();
            NormalizeModel();
            settings.Validate(width, height);
            if (settings.outputType == OutputType.Sprite && settings.spriteMode == OutputSpriteMode.Multiple)
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
                if (!string.IsNullOrEmpty(linkedPath)) linkedBytes = EncodeLinkedTexture(composite, linkedPath);
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
                Texture2D processed = ProcessOutputTexture(texture, settings);
                DestroyImmediate(texture);
                texture = processed;
                if (settings.CompressionEnabled)
                {
                    TextureFormat compressedFormat = settings.ResolveCompression(texture);
                    if (!SystemInfo.SupportsTextureFormat(compressedFormat))
                        throw new InvalidOperationException("This graphics device does not support the automatically selected compression format.");
                    if (compressedFormat != texture.format)
                        EditorUtility.CompressTexture(texture, compressedFormat, settings.ResolvedCompressionQuality);
                    if (texture.format != compressedFormat)
                        throw new InvalidOperationException("Unity could not compress the output into the requested format.");
                    texture.Apply(false, false);
                }
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

        internal void NotifyOutputTextureChanged()
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
            if (EditorUtility.IsDirty(this) || outputTexture == null || EditorUtility.IsDirty(outputTexture)) return true;
            if (SpriteOutputSettings.outputType == OutputType.Sprite &&
                (outputSprite == null || EditorUtility.IsDirty(outputSprite))) return true;
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
