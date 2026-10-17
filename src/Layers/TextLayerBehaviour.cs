using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum TextLayoutMode { Point, Frame }
    public enum TextWrapping { Manual, Words, Characters }
    public enum TextOverflowMode { None, Clip, Ellipsis }
    public enum TextCasing { Normal, Lowercase, Uppercase, SmallCaps }
    public enum TextEdgeMode { [InspectorName("Hinted AA")] HintedAA = 0, Antialiased = 1, Pixel = 2 }
    [Serializable]
    public struct TextSpacing : IEquatable<TextSpacing>
    {
        public float character, word, line, paragraph;
        internal const float Minimum = -1, Maximum = 10;
        internal static float Clamp(float value) => Mathf.Clamp(float.IsFinite(value) ? value : 0, Minimum, Maximum);
        internal bool IsValid => Valid(character) && Valid(word) && Valid(line) && Valid(paragraph);
        private static bool Valid(float value) => float.IsFinite(value) && value >= Minimum && value <= Maximum;
        public bool Equals(TextSpacing other) => character == other.character && word == other.word && line == other.line && paragraph == other.paragraph;
        public override bool Equals(object other) => other is TextSpacing spacing && Equals(spacing);
        public override int GetHashCode() => HashCode.Combine(character, word, line, paragraph);
    }
    [Serializable]
    public sealed class TextLayerBehaviour : LayerBehaviour
    {
        public const int MaxCharacters = 8192;
        public string text = "Text";
        public string fontFamily = "";
        public FontStyle fontStyle;
        public TextCasing casing;
        public TextEdgeMode edgeMode = TextEdgeMode.HintedAA;
        public float fontSize = 64;
        public float maxFontSize = 256;
        public TextSpacing spacing;
        public float characterHorizontalScale = 1;
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        public TextLayoutMode layoutMode;
        public Vector2 layoutOffset;
        public Vector2 frameSize = new Vector2(256, 128);
        public TextWrapping wrapping = TextWrapping.Words;
        public TextOverflowMode overflow;
        public bool justify;
        public bool autoSize;
        public Color color = Color.white;

        [SerializeField, HideInInspector] private string fallbackPng;
        [SerializeField, HideInInspector] private string fallbackKey;
        [SerializeField, HideInInspector] private string fallbackFont;
        [SerializeField, HideInInspector] private int fallbackWidth;
        [SerializeField, HideInInspector] private int fallbackHeight;
        [SerializeField, HideInInspector] private Rect fallbackRect;
        [SerializeField, HideInInspector] private float fallbackPixelScale = 1f;
        [NonSerialized] private TextLayerRenderer renderer;
        [NonSerialized] private Texture2D decodedFallback;
        [NonSerialized] private string decodedPng;
        [NonSerialized] private string loggedNotice;
        [NonSerialized] private VectorLayerBehaviour vectorGeometry;
        [NonSerialized] private string vectorKey;
        [NonSerialized] private int vectorFontRevision;
        [NonSerialized] private float vectorPixelScale;
        [NonSerialized] private Vector2Int vectorCanvasSize;

        internal VectorLayerBehaviour GetVectorGeometry(WhimTexDocument document)
        {
            string key = LayoutKey(document.width, document.height);
            float pixelScale = Owner.GetPixelScale();
            var canvasSize = Owner.GetSourceCanvasSize(document.width, document.height);
            if (vectorGeometry == null || vectorKey != key || vectorFontRevision != SystemFontCatalog.Revision ||
                vectorPixelScale != pixelScale || vectorCanvasSize != canvasSize)
            {
                var next = TextToVector.Build(this, document);
                vectorGeometry?.ReleaseTransientResources();
                vectorGeometry = next; vectorKey = key; vectorFontRevision = SystemFontCatalog.Revision;
                vectorPixelScale = pixelScale; vectorCanvasSize = canvasSize;
            }
            vectorGeometry.shapes[0].fillColor = color;
            return vectorGeometry;
        }

        internal override void InitializeLayer(Layer layer)
        {
            if (string.IsNullOrEmpty(fontFamily)) fontFamily = SystemFontCatalog.DefaultName ?? "";
        }
        internal string ResolvedFont => !string.IsNullOrEmpty(fontFamily) ? fontFamily :
            !string.IsNullOrEmpty(fallbackFont) ? fallbackFont : SystemFontCatalog.DefaultName;
        internal bool FontAvailable => SystemFontCatalog.Contains(ResolvedFont);
        internal bool UsesAutoSize => autoSize && layoutMode == TextLayoutMode.Frame;
        internal bool CanJustify => layoutMode == TextLayoutMode.Frame && wrapping != TextWrapping.Manual;
        internal TextAnchor LayoutAlignment => justify ? (TextAnchor)((int)alignment / 3 * 3) : alignment;
        internal void SetAlignment(TextAnchor value, bool justified)
        {
            alignment = justified ? (TextAnchor)((int)value / 3 * 3) : value;
            justify = justified && CanJustify;
        }
        internal void SetLayoutMode(TextLayoutMode value)
        {
            layoutMode = value;
            SetAutoSize(autoSize);
            SetAlignment(alignment, justify);
        }
        internal void SetWrapping(TextWrapping value)
        {
            wrapping = value;
            SetAlignment(alignment, justify);
        }
        internal void SetFontSize(float value)
        {
            fontSize = Mathf.Clamp(float.IsFinite(value) ? value : 64, 1, 2048);
            if (UsesAutoSize) maxFontSize = Mathf.Max(maxFontSize, Mathf.Ceil(fontSize));
        }
        internal void SetMaxFontSize(float value)
        {
            maxFontSize = Mathf.Clamp(float.IsFinite(value) ? value : 256, 1, 2048);
            if (UsesAutoSize) fontSize = Mathf.Min(fontSize, Mathf.Floor(maxFontSize));
        }
        internal void SetAutoSize(bool enabled)
        {
            autoSize = enabled;
            if (UsesAutoSize) maxFontSize = Mathf.Max(maxFontSize, Mathf.Ceil(fontSize));
        }
        internal void SetCharacterHorizontalScale(float value) =>
            characterHorizontalScale = Mathf.Clamp(float.IsFinite(value) ? value : 1, .01f, 10);
        internal void SetLayoutOffset(Vector2 value) => layoutOffset = new Vector2(
            Mathf.Clamp(float.IsFinite(value.x) ? value.x : 0, -1000000, 1000000),
            Mathf.Clamp(float.IsFinite(value.y) ? value.y : 0, -1000000, 1000000));
        internal bool HasSavedAppearance => !string.IsNullOrEmpty(fallbackPng) && fallbackKey == LayoutKey(fallbackWidth, fallbackHeight);
        internal string Notice => FontAvailable ? null : HasSavedAppearance
            ? "System font '" + ResolvedFont + "' is unavailable. The saved text appearance is retained; choose an installed font to edit its layout."
            : "System font '" + ResolvedFont + "' is unavailable and no matching saved appearance exists. Choose an installed font to display the text.";

        internal void Validate()
        {
            WhimTexDocument.ValidatePixelScale(fallbackPixelScale);
            if ((text?.Length ?? 0) > MaxCharacters) throw new ArgumentException("Text exceeds the 8192-character limit.");
            if (!float.IsFinite(fontSize) || fontSize < 1 || fontSize > 2048) throw new ArgumentException("Font Size must be 1..2048 pixels.");
            if (!float.IsFinite(maxFontSize) || maxFontSize < 1 || maxFontSize > 2048) throw new ArgumentException("Max Font Size must be 1..2048 pixels.");
            if (UsesAutoSize && Mathf.Ceil(fontSize) > Mathf.Floor(maxFontSize))
                throw new ArgumentException("Auto Size requires Min Size <= Max Size and at least one whole-pixel font size in the range.");
            if (!spacing.IsValid) throw new ArgumentException("Text spacing must be -1..10 em per field.");
            if (!float.IsFinite(layoutOffset.x) || !float.IsFinite(layoutOffset.y) || Mathf.Abs(layoutOffset.x) > 1000000 || Mathf.Abs(layoutOffset.y) > 1000000)
                throw new ArgumentException("Text Layout Offset must be finite and within +/-1000000 pixels per axis.");
            if (!float.IsFinite(characterHorizontalScale) || characterHorizontalScale < .01f || characterHorizontalScale > 10)
                throw new ArgumentException("Character Horizontal Scale must be 0.01..10.");
            if (!Enum.IsDefined(typeof(FontStyle), fontStyle) || !Enum.IsDefined(typeof(TextAnchor), alignment) || !Enum.IsDefined(typeof(TextCasing), casing) || !Enum.IsDefined(typeof(TextEdgeMode), edgeMode))
                throw new ArgumentException("Unknown text style or alignment.");
            if (!Enum.IsDefined(typeof(TextLayoutMode), layoutMode) || !Enum.IsDefined(typeof(TextWrapping), wrapping) || !Enum.IsDefined(typeof(TextOverflowMode), overflow))
                throw new ArgumentException("Unknown text layout, wrapping or overflow rule.");
            if (!float.IsFinite(frameSize.x) || !float.IsFinite(frameSize.y) || frameSize.x < 1 || frameSize.y < 1 || frameSize.x > 32768 || frameSize.y > 32768)
                throw new ArgumentException("Text Frame Size must be 1..32768 pixels per axis.");
        }

        internal string LayoutKey(int width, int height)
        {
            string data = (text ?? "") + "\0" + (fontFamily ?? "") + "\0" + (int)fontStyle + "\0" + (int)casing + "\0" +
                (int)edgeMode + "\0" + fontSize.ToString("R", CultureInfo.InvariantCulture) + "\0" + maxFontSize.ToString("R", CultureInfo.InvariantCulture) +
                "\0" + spacing.character.ToString("R", CultureInfo.InvariantCulture) +
                "\0" + spacing.word.ToString("R", CultureInfo.InvariantCulture) + "\0" + spacing.line.ToString("R", CultureInfo.InvariantCulture) +
                "\0" + spacing.paragraph.ToString("R", CultureInfo.InvariantCulture) +
                "\0" + characterHorizontalScale.ToString("R", CultureInfo.InvariantCulture) +
                "\0" + (int)alignment + "\0" + (int)layoutMode + "\0" + (int)wrapping + "\0" + (int)overflow + "\0" + justify + "\0" + autoSize +
                "\0" + frameSize.x.ToString("R", CultureInfo.InvariantCulture) + "\0" + frameSize.y.ToString("R", CultureInfo.InvariantCulture) +
                "\0" + width + "\0" + height;
            if (layoutOffset != Vector2.zero) data += "\0offset\0" + layoutOffset.x.ToString("R", CultureInfo.InvariantCulture) + "\0" + layoutOffset.y.ToString("R", CultureInfo.InvariantCulture);
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }

        private Texture2D Mask(int width, int height, float scale)
        {
            Validate();
            string notice = Notice;
            if (notice != loggedNotice)
            {
                loggedNotice = notice;
                if (notice != null) Debug.LogWarning("WhimTex Text: " + notice);
            }
            if (FontAvailable)
                return (renderer ??= new TextLayerRenderer()).GetMask(this, width, height, scale);
            if (!HasSavedAppearance) return null;
            if (decodedFallback != null && decodedPng == fallbackPng) return decodedFallback;
            if (decodedFallback != null) UnityEngine.Object.DestroyImmediate(decodedFallback);
            decodedFallback = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            { name = "Saved Text Appearance", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            try
            {
                if (fallbackPng.Length > 1024 * 1024) throw new InvalidOperationException("Saved text appearance exceeds its budget.");
                byte[] bytes = Convert.FromBase64String(fallbackPng);
                int PngInt(int start) => (bytes[start] << 24) | (bytes[start + 1] << 16) | (bytes[start + 2] << 8) | bytes[start + 3];
                if (bytes.Length < 24 || bytes.Length > 768 * 1024 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                    PngInt(16) < 1 || PngInt(16) > 2048 || PngInt(20) < 1 || PngInt(20) > 2048 ||
                    !ImageConversion.LoadImage(decodedFallback, bytes, false) ||
                    decodedFallback.width > 2048 || decodedFallback.height > 2048)
                    throw new InvalidOperationException("Invalid saved text appearance.");
                decodedPng = fallbackPng;
                return decodedFallback;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(decodedFallback); decodedFallback = null; decodedPng = null;
                throw;
            }
        }

        internal Rect GetLayoutBounds(int width, int height)
        {
            Validate();
            if (layoutMode == TextLayoutMode.Frame) return new Rect((layoutOffset - frameSize * .5f) * Owner.GetPixelScale(), frameSize * Owner.GetPixelScale());
            if (!FontAvailable) return HasSavedAppearance ? ScaledFallbackRect() : new Rect(-width * .5f, -height * .5f, width, height);
            return (renderer ??= new TextLayerRenderer()).GetBounds(this, width, height);
        }

        internal float GetFontSize(int width, int height)
        {
            if (!UsesAutoSize || !FontAvailable) return fontSize;
            Validate();
            (renderer ??= new TextLayerRenderer()).GetBounds(this, width, height);
            return renderer.FontSize;
        }

        internal IReadOnlyList<Vector3> GetPointLines(int width, int height)
        {
            if (layoutMode != TextLayoutMode.Point || !FontAvailable) return Array.Empty<Vector3>();
            GetLayoutBounds(width, height);
            return renderer.PointLines;
        }

        internal Vector2 GetPointAnchor(int width, int height)
        {
            var lines = GetPointLines(width, height);
            if (lines.Count == 0) return Vector2.zero;
            var first = lines[0];
            return new Vector2(Mathf.Lerp(first.x, first.y, (int)LayoutAlignment % 3 * .5f), first.z);
        }

        internal void PrepareSavedAppearance(int width, int height)
        {
            Validate();
            if (!FontAvailable) return;
            string key = LayoutKey(width, height);
            if (key == fallbackKey && fallbackFont == ResolvedFont && fallbackPixelScale == Owner.GetPixelScale() && !string.IsNullOrEmpty(fallbackPng)) return;
            Mask(width, height, 1);
            float longest = Mathf.Max(renderer.SourceRect.width, renderer.SourceRect.height);
            float scale = Mathf.Min(1, 2048f / longest);
            byte[] png;
            do
            {
                var mask = Mask(width, height, scale);
                png = mask.EncodeToPNG();
                scale *= .5f;
            } while (png.Length > 768 * 1024 && scale * longest >= 128);
            if (png.Length > 768 * 1024) throw new InvalidOperationException("Saved text appearance exceeds the 768 KiB budget.");
            fallbackPng = Convert.ToBase64String(png);
            fallbackKey = key; fallbackWidth = width; fallbackHeight = height; fallbackFont = ResolvedFont;
            fallbackRect = renderer.SourceRect;
            fallbackPixelScale = Owner.GetPixelScale();
        }

        private Rect ScaledFallbackRect()
        {
            double scale = (double)Owner.GetPixelScale() / fallbackPixelScale;
            return new Rect((float)(fallbackRect.x * scale), (float)(fallbackRect.y * scale),
                (float)(fallbackRect.width * scale), (float)(fallbackRect.height * scale));
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Texture2D mask = Mask(context.activeDocument.width, context.activeDocument.height, 1f / context.scaleMultiplier);
            if (mask == null) return null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var source = ProfiledRenderTexture.GetTemporary(mask.width, mask.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            source.filterMode = edgeMode == TextEdgeMode.Pixel ? FilterMode.Point : FilterMode.Bilinear; source.wrapMode = TextureWrapMode.Clamp;
            try
            {
                Material material = WhimTexMaterials.Text;
                material.SetColor("_TextColor", HdrUtility.Decode(color));
                GL.sRGBWrite = false;
                Graphics.Blit(mask, source, material, 1);
                Rect rect = FontAvailable ? renderer.SourceRect : ScaledFallbackRect();
                var sourceToLocal = ProjectiveMatrix.Translate(.5 + rect.x / context.activeDocument.width, .5 + rect.y / context.activeDocument.height) *
                    ProjectiveMatrix.Scale(rect.width / context.activeDocument.width, rect.height / context.activeDocument.height);
                return Owner.ApplyTransformAndFx(source, context, sourceToLocal);
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb; ProfiledRenderTexture.ReleaseTemporary(source);
            }
        }

        internal override void ReleaseTransientResources()
        {
            vectorGeometry?.ReleaseTransientResources(); vectorGeometry = null; vectorKey = null;
            renderer?.Dispose(); renderer = null;
            if (decodedFallback != null) UnityEngine.Object.DestroyImmediate(decodedFallback);
            decodedFallback = null; decodedPng = null;
        }
    }
}
