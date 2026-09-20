// run_script entry TiffAgentDiagnosticsSmoke.Run. Creates and removes only its own temporary assets.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class TiffAgentDiagnosticsSmoke
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }

    public static string Run()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string folder = "Assets/WhimTexTiffDiagnostics_" + Guid.NewGuid().ToString("N");
        string source = folder + "/Source.tiff";
        string recovered = folder + "/Recovered.tiff";
        string staged = source + "." + Guid.NewGuid().ToString("N") + ".whimtex-tmp";
        string png = "Temp/WhimTex/diagnostics-" + Guid.NewGuid().ToString("N") + ".png";
        string exr = "Temp/WhimTex/diagnostics-" + Guid.NewGuid().ToString("N") + ".exr";
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        try
        {
            string created = WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"" + source + "\",\"create\":true,\"width\":16,\"height\":16,\"operations\":[{\"op\":\"add\",\"type\":\"color\",\"settings\":{\"name\":\"Base\",\"color\":[0.2,0.4,0.8,1]}}]}");
            Check(created.Contains("\"success\":true"), "create source TIFF");
            string compare = WhimTexApi.Compare(source, source, true, 16);
            Check(compare.Contains("\"success\":true") && compare.Contains("\"modelEqual\":true") && compare.Contains("\"renderEqual\":true"), "compare model and render");
            string exportPng = WhimTexApi.Export(source, png, 16, false);
            string exportExr = WhimTexApi.Export(source, exr, 16, false);
            Check(exportPng.Contains("\"success\":true") && exportExr.Contains("\"success\":true"), "export PNG and EXR");
            File.Copy(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(root, staged.Replace('/', Path.DirectorySeparatorChar)), false);
            string recover = WhimTexApi.Recover(Path.Combine(root, staged.Replace('/', Path.DirectorySeparatorChar)), recovered);
            Check(recover.Contains("\"success\":true") && File.Exists(Path.Combine(root, recovered.Replace('/', Path.DirectorySeparatorChar))), "recover staged TIFF");
            string invalid = WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"unknown\"}");
            Check(invalid.Contains("\"success\":false") && invalid.Contains("\"phase\":\"unknown\""), "TIFF live error includes phase");
            return "PASS: TIFF compare, staged recovery, image export, and phased errors.";
        }
        finally
        {
            string stagedFull = Path.Combine(root, staged.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(stagedFull)) File.Delete(stagedFull);
            string pngFull = Path.Combine(root, png.Replace('/', Path.DirectorySeparatorChar));
            string exrFull = Path.Combine(root, exr.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(pngFull)) File.Delete(pngFull);
            if (File.Exists(exrFull)) File.Delete(exrFull);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
