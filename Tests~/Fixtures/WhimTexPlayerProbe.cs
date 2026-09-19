// Copied into a unique temporary Assets folder by DocumentReleaseValidation.Install.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public sealed class WhimTexPlayerProbe : MonoBehaviour
{
    public string resourcePrefix;
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
        public bool success;
        public string unity, graphics, error;
        public List<Sample> samples = new List<Sample>();
    }
    IEnumerator Start()
    {
        var result = new Result { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        yield return null;
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name.StartsWith("DCFApixels.WhimTex", StringComparison.Ordinal))
                    throw new Exception("Editor-only WhimTex assembly entered the Player.");
            foreach (string name in new[] { "Document", "Plain", "Compressed" })
            {
                var clock = Stopwatch.StartNew();
                var texture = Resources.Load<Texture2D>(resourcePrefix + "/" + name);
                clock.Stop();
                if (texture == null) throw new Exception("Missing texture: " + name);
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
                if (sample.readable || sample.pixel.g < .9f || sample.pixel.r > .05f || sample.pixel.b > .05f)
                    throw new Exception("Expected saved green, non-readable texture: " + name);
                if (name == "Compressed" && (sample.width != 128 || sample.mips != 8 || texture.format != TextureFormat.DXT5))
                    throw new Exception("Standalone compression/MaxSize/mip override was not applied.");
                Resources.UnloadAsset(texture);
            }
            result.success = true;
        }
        catch (Exception error) { result.error = error.ToString(); }
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--whimtex-probe-output") File.WriteAllText(args[i + 1], JsonUtility.ToJson(result, true));
        UnityEngine.Debug.Log("WhimTex Player probe: " + JsonUtility.ToJson(result));
        Application.Quit(result.success ? 0 : 1);
    }
}
