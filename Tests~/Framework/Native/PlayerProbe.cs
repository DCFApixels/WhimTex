// Runtime-only fixture. Parent installs as WhimTexMigrationPlayerProbe.cs in its owned GUID folder.
// No WhimTex/editor/test-framework dependency; output allowlist is serialized into the owned scene.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public sealed class WhimTexMigrationPlayerProbe : MonoBehaviour
{
    public string resourcePrefix;
    public string runId;
    public string outputDirectory;
    [Serializable] public sealed class Sample
    {
        public string name, format;
        public int width, height, mips;
        public bool readable;
        public double loadMs;
        public Color pixel;
    }
    [Serializable] public sealed class Result
    {
        public int version = 1;
        public string runId;
        public string status = "running";
        public int checks;
        public string[] failures = new string[0];
        public string unity, graphics;
        public string message = "Player texture loading and editor-only assembly boundary";
        public List<Sample> samples = new List<Sample>();
        public string[] assemblies = new string[0];
    }
    static void Check(Result result, bool value, string message)
    {
        result.checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    static void NoLinks(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected Player output is forbidden: " + current);
    }
    string OutputPath(Result result)
    {
        string[] args = Environment.GetCommandLineArgs();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--whimtex-probe-output" || args[i] == "--whimtex-probe-run-id" || args[i] == "--whimtex-probe-attempt")
            {
                string key = args[i];
                if (i + 1 == args.Length || values.ContainsKey(key)) throw new ArgumentException("Missing/duplicate probe argument: " + key);
                values.Add(key, args[++i]);
            }
        Check(result, Guid.TryParseExact(runId, "N", out var guid) && guid.ToString("N") == runId &&
            values.TryGetValue("--whimtex-probe-run-id", out string suppliedId) && suppliedId == runId,
            "Command and serialized scene must share the owned GUID.");
        int attempt = 0;
        Check(result, values.TryGetValue("--whimtex-probe-attempt", out string suppliedAttempt) &&
            int.TryParse(suppliedAttempt, out attempt) && attempt > 0, "Positive build attempt is required.");
        // Unity's external temporaryCachePath is intentionally irrelevant. Parent authorizes this exact project Temp directory.
        string root = Path.GetFullPath(outputDirectory);
        Check(result, Path.IsPathRooted(outputDirectory) && Path.GetFileName(root) == runId &&
            Path.GetFileName(Path.GetDirectoryName(root)) == "player-release" &&
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(root))) == "WhimTex" &&
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(root)))) == "Temp",
            "Serialized allowlist must be the explicit owned project Temp/WhimTex/player-release/GUID directory.");
        string expected = Path.Combine(root, "attempt-" + attempt.ToString("D3"), "player-result.json");
        Check(result, values.TryGetValue("--whimtex-probe-output", out string suppliedOutput) &&
            Path.GetFullPath(suppliedOutput) == expected, "Report must be the exact owned attempt's player-result.json.");
        NoLinks(expected);
        Check(result, Directory.Exists(Path.GetDirectoryName(expected)) && !File.Exists(expected), "Parent created a fresh owned output directory.");
        return expected;
    }
    static void AssemblyBoundary(Result result)
    {
        var names = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = assembly.GetName().Name; names.Add(name);
            Check(result, !name.StartsWith("DCFApixels.WhimTex", StringComparison.Ordinal), "Editor-only WhimTex assembly entered Player: " + name);
        }
        result.assemblies = names.ToArray();
    }
    IEnumerator Start()
    {
        var result = new Result { runId = runId, unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        string output = null;
        yield return null;
        try
        {
            output = OutputPath(result);
            AssemblyBoundary(result);
            foreach (string name in new[] { "Document", "Plain", "Compressed" })
            {
                var clock = Stopwatch.StartNew();
                var texture = Resources.Load<Texture2D>(resourcePrefix + "/" + name);
                clock.Stop();
                Check(result, texture != null, "Missing texture: " + name);
                try
                {
                var sample = new Sample { name = name, format = texture.format.ToString(), width = texture.width,
                    height = texture.height, mips = texture.mipmapCount, readable = texture.isReadable, loadMs = clock.Elapsed.TotalMilliseconds };
                var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
                var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                try
                {
                    GL.sRGBWrite = false; Graphics.Blit(texture, rt); RenderTexture.active = rt;
                    cpu.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); cpu.Apply(); sample.pixel = cpu.GetPixel(0, 0);
                }
                finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(rt); Destroy(cpu); }
                result.samples.Add(sample);
                Check(result, !sample.readable && !float.IsNaN(sample.pixel.g) && sample.pixel.g >= .9f &&
                    !float.IsNaN(sample.pixel.r) && sample.pixel.r <= .05f && !float.IsNaN(sample.pixel.b) && sample.pixel.b <= .05f,
                    "Expected saved green, non-readable texture: " + name);
                if (name == "Compressed") Check(result, sample.width == 128 && sample.height == 128 && sample.mips == 8 && texture.format == TextureFormat.DXT5,
                    "Standalone compression/MaxSize/mip override was not applied.");
                else Check(result, sample.width == 256 && sample.height == 256 && sample.mips == 1, "Expected 256px fixture without mipmaps.");
                }
                finally { Resources.UnloadAsset(texture); }
            }
            AssemblyBoundary(result);
            result.status = "passed";
        }
        catch (Exception error) { result.status = "failed"; result.failures = new[] { error.ToString() }; }
        try
        {
            if (output != null)
                using (var writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read)))
                    writer.Write(JsonUtility.ToJson(result, true));
        }
        catch (Exception error) { result.status = "failed"; result.failures = new[] { error.ToString() }; }
        UnityEngine.Debug.Log("WhimTex Player probe: " + JsonUtility.ToJson(result));
        Application.Quit(result.status == "passed" ? 0 : 1);
    }
}
