using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Editor-owned paths and labels are captured before a worker starts. The scope never crosses awaits.
    internal sealed class ShaderPreparationEnvironment
    {
        [ThreadStatic] internal static ShaderPreparationEnvironment Current;
        internal readonly string projectRoot, presetFolder;
        internal readonly bool supportsLightingTargets;
        readonly Dictionary<string, string> packages = new(StringComparer.Ordinal);
        readonly Dictionary<ShaderGraphNodeKind, string> labels = new();
        internal ShaderPreparationEnvironment()
        {
            supportsLightingTargets = SystemInfo.supportedRenderTargetCount >= 4 && (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;
            projectRoot = Path.GetDirectoryName(Application.dataPath);
            presetFolder = ShaderPresetCatalog.Folder;
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                packages[package.assetPath] = package.resolvedPath;
            foreach (ShaderGraphNodeKind kind in Enum.GetValues(typeof(ShaderGraphNodeKind)))
                labels[kind] = ShaderGraphNodes.Name(kind);
        }
        internal string Name(ShaderGraphNodeKind kind) => labels.TryGetValue(kind, out var label) ? label : kind.ToString();
        internal string PhysicalPath(string path)
        {
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                int slash = path.IndexOf('/', "Packages/".Length);
                if (slash >= 0 && packages.TryGetValue(path.Substring(0, slash), out var root))
                    return Path.GetFullPath(Path.Combine(root, path.Substring(slash + 1)));
            }
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }
    }
}
