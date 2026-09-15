using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [ScriptedImporter(1, BrushPresetLibrary.Extension)]
    public sealed class BrushPresetImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            Texture2D tip = null;
            BrushPresetAsset asset = null;
            try
            {
                var preset = BrushPresetLibrary.Load(PresetLibraryPaths.PhysicalPath(context.assetPath), out tip);
                asset = ScriptableObject.CreateInstance<BrushPresetAsset>();
                asset.name = Path.GetFileNameWithoutExtension(context.assetPath);
                asset.preset = preset;
                if (tip != null)
                {
                    tip.hideFlags = HideFlags.HideInHierarchy;
                    preset.dynamics.tip = tip;
                    context.AddObjectToAsset("tip", tip);
                }
                context.AddObjectToAsset("preset", asset);
                context.SetMainObject(asset);
            }
            catch (Exception error)
            {
                if (tip != null) DestroyImmediate(tip);
                if (asset != null) DestroyImmediate(asset);
                context.LogImportError("Cannot import WhimTex brush preset: " + error.Message);
            }
        }
    }
}
