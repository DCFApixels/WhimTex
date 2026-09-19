// Test-only fault, installed into the temporary fixture's Editor folder.
using System.IO;
using UnityEditor;

public sealed class WhimTexImportFailureProbe : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith("Assets/WhimTexValidation_") && File.Exists(assetPath + ".failimport"))
            context.LogImportError("WHIMTEX_EXPECTED_IMPORT_FAILURE: validation fixture.");
    }
}
