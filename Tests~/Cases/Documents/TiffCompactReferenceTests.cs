// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// run_script entries TiffCompactReferenceTests.RunGradient and RunDrawing.
// Both tests use only package-owned fixtures and never write project Assets.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using DCFApixels.WhimTex;

public static class TiffCompactReferenceTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string RunGradient() => TestContext.Run("TiffCompactReferenceTests.RunGradient", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRunGradient();
    });

    public static string RunDrawing() => TestContext.Run("TiffCompactReferenceTests.RunDrawing", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRunDrawing();
    });

    private const string FixtureFolder = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/";

    private static void Check(bool value, string message)
    { context.True(value, message); }

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
        Check(WhimTexDocumentFile.TryLoad(path, out WhimTexDocument document, out string error),
            name + " opens: " + error);
        try
        {
            Check(document != null && document.width == 128 && document.height == 128, name + " canvas is 128x128");
            Check(document.layers != null && document.layers.Count > 0, name + " contains layers");
            Check(document.layers.Any(layer => layer.Behaviour != null && expectedLayerType.IsInstanceOfType(layer.Behaviour)),
                name + " contains the expected layer kind");
            Texture2D composed = document.ComposeCanvas();
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

    private static void ExecuteRunGradient()
    {
        Run("BASE_Gradient_128.tiff", 16117,
            "E81A61D2DF86CD35D9FD55685408E71AABFD8C368A939AB27C1FEA6D72FAAB51",
            typeof(GradientLayerBehaviour));
        return;
    }

    private static void ExecuteRunDrawing()
    {
        Run("BASE_Drawing_128.tiff", 3730,
            "778D2D7E5A0272733BBB06F3FD14D854AA4391E7CD5511813D17E26D31A7A4F8",
            typeof(DrawingLayerBehaviour));
        return;
    }
}

