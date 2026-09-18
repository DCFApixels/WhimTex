using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        private Texture2D ProcessOutputTexture(Texture2D source, OutputSettings settings)
        {
            var generation = new TextureGenerationSettings(TextureImporterType.Default);
            generation.enablePostProcessor = false;
            generation.sourceTextureInformation = new SourceTextureInformation
            {
                width = source.width, height = source.height, containsAlpha = true,
                hdr = source.format != TextureFormat.RGBA32
            };
            var importer = generation.textureImporterSettings;
            importer.sRGBTexture = source.isDataSRGB;
            importer.readable = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = settings.alphaIsTransparency;
            importer.mipmapEnabled = settings.mipMaps;
            importer.mipmapFilter = settings.mipFilter;
            importer.mipMapsPreserveCoverage = settings.preserveCoverage;
            importer.alphaTestReferenceValue = settings.alphaCutoff;
            importer.wrapModeU = settings.wrapU;
            importer.wrapModeV = settings.wrapV;
            importer.filterMode = outputFilter;
            importer.aniso = settings.anisoLevel;
            generation.platformSettings.maxTextureSize = settings.maxSize;
            generation.platformSettings.resizeAlgorithm = settings.resizeAlgorithm;
            generation.platformSettings.format = source.format == TextureFormat.RGBAHalf ? TextureImporterFormat.RGBAHalf :
                source.format == TextureFormat.RGBAFloat ? TextureImporterFormat.RGBAFloat : TextureImporterFormat.RGBA32;
            generation.platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
            TextureGenerationOutput result;
            if (source.format == TextureFormat.RGBA32)
                result = TextureGenerator.GenerateTexture(generation, source.GetPixelData<Color32>(0));
            else
            {
                using var pixels = new NativeArray<Color>(source.GetPixels(0), Allocator.Temp);
                result = TextureGenerator.GenerateTexture(generation, pixels);
            }
            if (result.thumbNail != null && result.thumbNail != result.texture) DestroyImmediate(result.thumbNail);
            if (result.sprites != null)
                foreach (var sprite in result.sprites) if (sprite != null) DestroyImmediate(sprite);
            if (result.texture == null) throw new System.InvalidOperationException("Unity could not generate the output texture.");
            result.texture.hideFlags = HideFlags.HideAndDontSave;
            return result.texture;
        }

        private Vector4 ScaleOutputBorder(Vector4 border) => Vector4.Scale(border,
            new Vector4((float)outputTexture.width / width, (float)outputTexture.height / height,
                (float)outputTexture.width / width, (float)outputTexture.height / height));
    }
}
