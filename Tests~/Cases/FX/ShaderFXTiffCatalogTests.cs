// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ShaderFXTiffCatalogTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXTiffCatalogTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

    private static object CallStatic(Type type, string name, params object[] args) =>
        type.GetMethod(name, Hidden).Invoke(null, args);

    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Hidden).Invoke(target, args);

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Hidden).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Hidden).SetValue(target, value);

    private static void Check(bool value, string message)
    { context.True(value, message); }

    private static void ExecuteRun()
    {
        var owned = new List<UnityEngine.Object>();
        var files = new List<string>();
        string path = null;
        string folder = fixture.AssetFolder();
        try
        {
            var assembly = typeof(DCFApixels.WhimTex.ShaderFX).Assembly;
            Type catalogType = assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            IEnumerable entries = (IEnumerable)CallStatic(catalogType, "GetEntries");
            object selected = null;
            foreach (object entry in entries)
            {
                string entryPath = (string)Field(entry, "path");
                bool user = (bool)Field(entry, "user");
                bool assetPreset = (bool)Field(entry, "assetPreset");
                if (!user && !assetPreset && entryPath != null && entryPath.EndsWith("/Levels.hlsl", StringComparison.OrdinalIgnoreCase))
                {
                    selected = entry;
                    break;
                }
            }
            Check(selected != null, "Could not find a project Shader FX preset.");

            var document = ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>();
            owned.Add(document);
            document.width = document.height = 16;
            var layer = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.white });
            document.layers.Add(layer);
            var shaderType = typeof(DCFApixels.WhimTex.ShaderFX);
            var effect = (DCFApixels.WhimTex.ShaderFX)CallStatic(shaderType, "FromCatalog", document, selected);
            owned.Add(effect);
            layer.fx.Add(effect);
            Call(document, "AdoptAgentShaderFX", effect, "Catalog smoke");
            string guid = (string)Field(effect, "catalogGuid");
            string source = (string)Field(effect, "catalogSourcePath");
            string sourceCode = (string)Field(effect, "code");
            Check(!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(source), "Catalog preset was not linked before save.");

            path = folder + "/Catalog.tiff";
            path = DCFApixels.WhimTex.WhimTexDocumentFile.Save(document, path);
            files.Add(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Check(File.Exists(path), "Catalog smoke TIFF was not written.");
            Check(DCFApixels.WhimTex.WhimTexDocumentFile.TryLoad(path, out var loaded, out string error), "Reload failed: " + error);
            owned.Add(loaded);
            var loadedEffect = (DCFApixels.WhimTex.ShaderFX)loaded.layers[0].fx[0];
            Check(string.Equals((string)Field(loadedEffect, "catalogGuid"), guid, StringComparison.Ordinal), "Catalog GUID was lost after TIFF round trip.");
            Check(string.Equals((string)Field(loadedEffect, "catalogSourcePath"), source, StringComparison.Ordinal), "Catalog source path was lost after TIFF round trip.");
            Check((bool)shaderType.GetProperty("IsCatalogLinked", Hidden).GetValue(loadedEffect), "Reloaded preset became an inline Shader FX.");
            Check(string.Equals((string)Field(loadedEffect, "code"), sourceCode, StringComparison.Ordinal), "Reloaded preset kept the TIFF fallback instead of catalog source.");
            Check((Shader)Field(loadedEffect, "compiledShader") != null, "Reloaded catalog effect was not compiled.");

            // A missing preset must degrade to the source snapshot embedded in the TIFF.
            SetField(loadedEffect, "catalogGuid", "missing-shaderfx-preset-guid");
            SetField(loadedEffect, "catalogSourcePath", "Assets/MissingShaderFXPreset.hlsl");
            string fallbackPath = folder + "/Fallback.tiff";
            fallbackPath = DCFApixels.WhimTex.WhimTexDocumentFile.Save(loaded, fallbackPath);
            files.Add(fallbackPath);
            AssetDatabase.ImportAsset(fallbackPath, ImportAssetOptions.ForceUpdate);
            Check(DCFApixels.WhimTex.WhimTexDocumentFile.TryLoad(fallbackPath, out var fallback, out string fallbackError), "Fallback reload failed: " + fallbackError);
            owned.Add(fallback);
            var fallbackEffect = (DCFApixels.WhimTex.ShaderFX)fallback.layers[0].fx[0];
            Check(!(bool)shaderType.GetProperty("IsCatalogLinked", Hidden).GetValue(fallbackEffect), "Missing preset stayed linked instead of detaching to fallback.");
            Check(string.Equals((string)Field(fallbackEffect, "code"), sourceCode, StringComparison.Ordinal), "Missing preset did not retain fallback code.");
            Check((Shader)Field(fallbackEffect, "compiledShader") != null, "Fallback Shader FX was not compiled.");
            return;
        }
        finally
        {
            foreach (var item in owned)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            foreach (string file in files)
                if (!string.IsNullOrEmpty(file) && File.Exists(file)) MigrationD.DeleteAsset(file);
        }
    }
}
