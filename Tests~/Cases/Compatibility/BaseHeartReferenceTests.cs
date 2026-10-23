// run_script entry BaseHeartReferenceTests.Run. Authenticates and rejects the archived package-owned TIFF without modifying it.
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using DCFApixels.WhimTex;

public static class BaseHeartReferenceTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    private const string FixtureRelativePath = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/BASE_Heart.tiff";
    private const long ExpectedBytes = 23260;
    private const string ExpectedSha256 = "0091ED87FD76729D8F3F7124754852145FF6E629CB28F1E75180C3121ED211A6";

    private static void Check(bool value, string message)
    { T.True(value, message); }

    private static string BodyRun()
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
        bool loaded = WhimTexDocumentFile.TryLoad(fullPath, out WhimTexDocument document, out string error);
        try
        {
            // The authenticated fixture retains container v1. Current readers deliberately
            // reject it; the historical bytes must not be regenerated as a current document.
            Check(!loaded && document == null &&
                string.Equals(error, "Unsupported document container.", StringComparison.Ordinal),
                "archived golden TIFF is explicitly rejected without a partial document: " + error);
        }
        finally
        {
            if (document != null) UnityEngine.Object.DestroyImmediate(document);
        }

        return null;
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
