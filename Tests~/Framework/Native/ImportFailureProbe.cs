// Optional fixture-only source. Installation imports assets and must be separately selected.
using System;
using System.IO;
using UnityEditor;

public sealed class WhimTexMigrationImportFailureProbe : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith("Assets/WhimTexTestMigration/", StringComparison.Ordinal)
            && File.Exists(assetPath + ".failimport"))
            context.LogImportError("WHIMTEX_EXPECTED_IMPORT_FAILURE: owned migration fixture.");
    }
}
