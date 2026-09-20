using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Compatibility-only writer for the retired ScriptableObject compositor format.
    ///
    /// Normal editor and agent save paths use <see cref="WhimTexDocumentFile"/> and write TIFF.
    /// This method remains solely for migration/regression fixtures that need to manufacture an
    /// old asset; it must not be used to create new production documents.
    /// </summary>
    public sealed partial class TextureCompositor
    {
        [Obsolete("Legacy .asset writer; use WhimTexDocumentFile.Save for TIFF documents.")]
        internal void SaveLegacyAssetForCompatibility(string newAssetPath)
        {
            bool createAsset = !string.IsNullOrEmpty(newAssetPath);
            string path = createAsset ? newAssetPath : AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the document as a Unity .asset file first.");
            if (createAsset && (AssetDatabase.Contains(this) || File.Exists(path) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null))
                throw new InvalidOperationException("Save As requires a new document and an unused asset path.");

            string linkedPath = GetLinkedTexturePath(SpriteOutputSettings.linkedTextureGuid);

            StopLiveOutput();
            Undo.FlushUndoRecordObjects();
            RemoveUnusedEmbeddedShaderFX();
            SyncDrawingLayerTextures();
            Texture2D rendered = null;
            Sprite generatedSprite = null;
            string previousSettings = savedOutputSettings;
            RenderTexture previous = RenderTexture.active;
            try
            {
                rendered = CreateSavedOutputWithLinkedTexture(linkedPath, out byte[] linkedBytes);
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
                if (settings.outputType == OutputType.Sprite)
                {
                    generatedSprite = Sprite.Create(outputTexture,
                        new Rect(0f, 0f, outputTexture.width, outputTexture.height),
                        settings.pivot, settings.pixelsPerUnit * outputTexture.width / width, (uint)settings.extrude, settings.meshType,
                        ScaleOutputBorder(settings.border), settings.generatePhysicsShape);
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

                    SaveSliceOutputs(path);
                    EditorUtility.SetDirty(outputSprite);
                }
                else RemoveOutputSprites(path);

                if (!settings.readable) outputTexture.Apply(false, true);
                EditorUtility.SetDirty(outputTexture);
                savedOutputSettings = CaptureOutputSettings();
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssetIfDirty(this);
                AssetDatabase.SetMainObject(outputTexture, path);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (linkedBytes != null) WriteLinkedTexture(linkedPath, linkedBytes);
                TextureCompositorProjectPreview.ClearCache();
                NotifyOutputTextureChanged();
                Changed?.Invoke(this);
            }
            catch
            {
                savedOutputSettings = previousSettings;
                EditorUtility.SetDirty(this);
                throw;
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
    }
}
