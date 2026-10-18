using System;
using System.IO;
using UnityEditor;
using WhimTex.Tests;

// Explicit opt-in only. Canonical executable test sources stay under Cases/Framework.
public static class CodecFixture
{
    const string Folder = "Assets/WhimTexCodecDiagnostics_Temporary";
    const string Marker = "owned-test-marker.txt";
    static readonly string[] Sources = { "Cases/Painting/PaintCodecKernels.cs", "Cases/Painting/PaintCodecPreflight.cs", "Framework/TestApi.cs" };
    public static string Stage() => TestContext.Run("Stage authorized native codec diagnostic fixture", t =>
    {
        t.True(!EditorApplication.isCompiling, "Wait for current compilation");
        t.True(!Directory.Exists(Folder), "Never overwrite an existing directory");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, Marker), "Owned codec comparison fixture; explicit user approval required. Canonical sources in WhimTex Tests~/Cases/Painting and Framework.");
        foreach (string source in Sources) File.Copy("Packages/com.dcfapixels.whimtex/Tests~/" + source, Path.Combine(Folder, Path.GetFileName(source)), false);
        File.WriteAllText(Path.Combine(Folder, "WhimTex.CodecDiagnostics.asmdef"),
            "{\"name\":\"WhimTex.CodecDiagnostics\",\"references\":[\"DCFApixels.WhimTex\",\"Unity.Burst\",\"Unity.Collections\",\"Unity.Mathematics\"],\"includePlatforms\":[\"Editor\"],\"allowUnsafeCode\":true,\"autoReferenced\":false}");
        // Refresh is deliberately separate: the invoking command must return before domain reload.
        t.True(File.Exists(Path.Combine(Folder, "PaintCodecKernels.cs")), "Canonical kernels staged");
    });
    public static string Remove() => TestContext.Run("Remove only owned diagnostic fixture", t =>
    {
        t.True(!EditorApplication.isCompiling, "Wait for current compilation");
        t.True(AppDomain.CurrentDomain.GetData("WhimTex.Tests.CodecBenchmark") == null &&
            AppDomain.CurrentDomain.GetData("WhimTex.Tests.SpillPolicy") == null && AppDomain.CurrentDomain.GetData("WhimTex.Tests.CodecCorpus") == null, "No active fixture workers");
        string root = Path.GetFullPath(Folder), assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
        t.True(root.StartsWith(assets, StringComparison.OrdinalIgnoreCase), "Owned folder is inside project Assets");
        t.True(File.Exists(Path.Combine(root, Marker)) && File.ReadAllText(Path.Combine(root, Marker)).StartsWith("Owned codec comparison fixture;"), "Ownership marker");
        foreach (string source in Sources)
        {
            string canonical = "Packages/com.dcfapixels.whimtex/Tests~/" + source, staged = Path.Combine(root, Path.GetFileName(source));
            t.True(File.ReadAllText(canonical) == File.ReadAllText(staged), "Do not remove changed staged source");
        }
        foreach (string file in Directory.GetFiles(root))
        {
            string name = Path.GetFileName(file), stem = name.EndsWith(".meta", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;
            t.True(stem == Marker || stem == "WhimTex.CodecDiagnostics.asmdef" || stem == "PaintCodecKernels.cs" || stem == "PaintCodecPreflight.cs" || stem == "TestApi.cs", "No unrelated files");
        }
        t.True(Directory.GetDirectories(root).Length == 0, "No unrelated subdirectories");
        foreach (string file in Directory.GetFiles(root)) File.Delete(file);
        Directory.Delete(root, false); if (File.Exists(root + ".meta")) File.Delete(root + ".meta");
        t.True(!Directory.Exists(root), "Owned fixture removed; refresh separately");
    });
}
