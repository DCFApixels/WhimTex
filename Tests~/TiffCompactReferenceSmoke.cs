// run_script entries TiffCompactReferenceSmoke.RunGradient and RunDrawing.
// Both tests use only package-owned fixtures and never write project Assets.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using DCFApixels.WhimTex;

public static class TiffCompactReferenceSmoke
{
    private const string FixtureFolder = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/";

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static string FixturePath(string name) => Path.Combine(
        Directory.GetParent(Application.dataPath).FullName,
        (FixtureFolder + name).Replace('/', Path.DirectorySeparatorChar));

    private static string Run(string name, long expectedBytes, string expectedHash, Type expectedLayerType)
    {
        string path = FixturePath(name);
        Check(File.Exists(path), "package fixture exists: " + name);
        FileInfo info = new FileInfo(path);
        Check(info.Length == expectedBytes, name + " byte length is stable");
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            Check(string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase), name + " SHA-256 is stable");
        }
        Check(WhimTexDocumentFile.IsDocument(path), name + " is a WhimTex TIFF");
        Check(WhimTexDocumentFile.TryLoad(path, out TextureCompositor document, out string error),
            name + " opens: " + error);
        try
        {
            Check(document != null && document.width == 128 && document.height == 128, name + " canvas is 128x128");
            Check(document.layers != null && document.layers.Count > 0, name + " contains layers");
            Check(document.layers.Any(layer => layer.Behaviour != null && expectedLayerType.IsInstanceOfType(layer.Behaviour)),
                name + " contains the expected layer kind");
            Texture2D composed = document.Compose();
            try
            {
                Check(composed != null && composed.width == 128 && composed.height == 128,
                    name + " composes at native size");
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
        return name;
    }

    public static string RunGradient()
    {
        Run("BASE_Gradient_128.tiff", 16117,
            "E81A61D2DF86CD35D9FD55685408E71AABFD8C368A939AB27C1FEA6D72FAAB51",
            typeof(GradientLayerBehaviour));
        return "PASS: BASE_Gradient_128.tiff package golden reference is intact.";
    }

    public static string RunDrawing()
    {
        Run("BASE_Drawing_128.tiff", 3730,
            "778D2D7E5A0272733BBB06F3FD14D854AA4391E7CD5511813D17E26D31A7A4F8",
            typeof(DrawingLayerBehaviour));
        return "PASS: BASE_Drawing_128.tiff package golden reference is intact.";
    }
}
