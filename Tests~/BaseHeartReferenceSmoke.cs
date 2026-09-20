// run_script entry BaseHeartReferenceSmoke.Run. Validates the package-owned golden TIFF without modifying it.
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using DCFApixels.WhimTex;

public static class BaseHeartReferenceSmoke
{
    private const string FixtureRelativePath = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/BASE_Heart.tiff";
    private const long ExpectedBytes = 23260;
    private const string ExpectedSha256 = "0091ED87FD76729D8F3F7124754852145FF6E629CB28F1E75180C3121ED211A6";

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
    }

    public static string Run()
    {
        string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Check(File.Exists(fullPath), "golden TIFF exists at " + FixtureRelativePath);
        FileInfo file = new FileInfo(fullPath);
        Check(file.Length == ExpectedBytes, "golden TIFF byte length is stable");
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(fullPath))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            Check(string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase), "golden TIFF SHA-256 is stable");
        }

        Check(WhimTexDocumentFile.IsDocument(fullPath), "golden TIFF is recognised as a WhimTex document");
        Check(WhimTexDocumentFile.TryLoad(fullPath, out TextureCompositor document, out string error),
            "golden TIFF opens: " + error);
        try
        {
            Check(document != null && document.width == 512 && document.height == 512, "document canvas is 512x512");
            Check(document.layers != null && document.layers.Count > 0, "document contains the reference layer stack");
            Texture2D composed = document.Compose();
            try
            {
                Check(composed != null && composed.width == 512 && composed.height == 512, "reference document composes at native size");
            }
            finally
            {
                if (composed != null) UnityEngine.Object.DestroyImmediate(composed);
            }
        }
        finally
        {
            if (document != null) UnityEngine.Object.DestroyImmediate(document);
        }

        return "PASS: BASE_Heart.tiff golden reference is intact (512x512, 23,260 bytes, stable hash).";
    }
}
