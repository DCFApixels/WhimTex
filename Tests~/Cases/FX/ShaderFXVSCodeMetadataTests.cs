// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;

// Read-only parity check. The same package-owned cases are run against the JS validator.
public static class ShaderFXVSCodeMetadataTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXVSCodeMetadataTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    [Serializable] public class Case { public string name; public string source; public bool valid; }
    private static void ExecuteMain()
    {
        var assembly = typeof(ShaderFX).Assembly;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(assembly);
        string root = package.resolvedPath;
        var parse = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true)
            .GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
        // Some projects also expose another Newtonsoft copy through Localization.
        // Bind the package assembly explicitly instead of creating a compile-time ambiguity.
        var json = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
        var deserialize = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        var cases = (Case[])deserialize.Invoke(null, new object[] {
            File.ReadAllText(Path.Combine(root, "Tests~", "Fixtures", "ShaderFXVSCodeMetadata.cases.json")), typeof(Case[]) });
        context.True(cases != null && cases.Length > 0, "Shared metadata inputs exist");
        int checkedCount = 0;
        foreach (var item in cases)
        {
            bool accepted = true;
            string error = null;
            try { parse.Invoke(null, new object[] { item.source, false, null }); }
            catch (TargetInvocationException exception) when (exception.InnerException is FormatException)
            { accepted = false; error = exception.InnerException.Message; }
            context.True(!(accepted != item.valid), "Metadata parity: " + item.name + ": " + (error ?? "unexpectedly accepted"));
            checkedCount++;
        }
        int presets = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(root, "src", "FXPresets"), "*.hlsl"))
        {
            parse.Invoke(null, new object[] { File.ReadAllText(file), true, null });
            context.True(true, "Bundled metadata parses: " + Path.GetFileName(file)); presets++;
        }
        return;
    }
}
