using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Every sample owns a fresh document: no vector, Boolean or coverage cache survives.
// Shader/JIT startup is reported separately, not confused with cold geometry work.
public static class VectorColdBenchmark
{
    const string Folder = "Temp/WhimTex/vector-cold-jobs-v1";
    static readonly MethodInfo Render = typeof(WhimTexDocument).GetMethod("RenderCanvas", BindingFlags.Instance | BindingFlags.NonPublic);
    [Serializable] public sealed class Result { public string status; public int checks; public string message; public string[] failures; }
    public static string Check()
    {
        var reports = new List<string>();
        try
        {
            if (!Unity.Burst.BurstCompiler.IsEnabled) throw new InvalidOperationException("Enable Burst before measuring Jobs preparation.");
            for (int scenario = 0; scenario < 6; scenario++) reports.Add(Run("after-final", scenario));
            return JsonUtility.ToJson(new Result { status = "passed", checks = 6,
                message = string.Join("\n", reports), failures = Array.Empty<string>() });
        }
        catch (Exception e)
        {
            return JsonUtility.ToJson(new Result { status = "failed", checks = reports.Count,
                message = string.Join("\n", reports), failures = new[] { e.ToString() } });
        }
    }
    public static string Run(string phase, int scenario)
    {
        if (phase != "before" && phase != "after" && phase != "after-final") throw new ArgumentException("Unknown phase");
        if (scenario < 0 || scenario >= 6) throw new ArgumentOutOfRangeException(nameof(scenario));
        Directory.CreateDirectory(Folder);
        string reportPath = Folder + "/" + phase + "-" + scenario + ".txt";
        if (File.Exists(reportPath)) throw new InvalidOperationException("Report exists; do not overwrite measurements");
        int size = scenario < 3 ? 512 : 1024, kind = scenario % 3;
        string label = (kind == 0 ? "vector-curves" : kind == 1 ? "boolean-4" : "boolean-16") + "-" + size;
        string reference = Folder + "/" + label + ".rgba";
        if (phase == "before" && File.Exists(reference)) throw new InvalidOperationException("Reference already exists");
        if (phase != "before" && !File.Exists(reference)) throw new InvalidOperationException("Frozen pre-change reference is required");
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var submissions = new List<double>(); var completions = new List<double>();
        double first = 0, difference = 0, mean = 0;
        try
        {
            for (int sample = -2; sample < 9; sample++)
            {
                var doc = Create(size, kind);
                var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                RenderTexture output = null;
                try
                {
                    var watch = Stopwatch.StartNew();
                    output = (RenderTexture)Render.Invoke(doc, new object[] { size });
                    double submission = watch.Elapsed.TotalMilliseconds;
                    RenderTexture.active = output;
                    read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                    double complete = watch.Elapsed.TotalMilliseconds;
                    if (sample == -2) first = complete;
                    if (sample >= 0) { submissions.Add(submission); completions.Add(complete); }
                    if (sample != 8) continue;
                    var raw = read.GetRawTextureData<float>();
                    if (phase == "before")
                    {
                        using var writer = new BinaryWriter(new FileStream(reference, FileMode.CreateNew));
                        for (int p = 0; p < raw.Length; p++) writer.Write(raw[p]);
                    }
                    else
                    {
                        using var reader = new BinaryReader(File.OpenRead(reference));
                        if (reader.BaseStream.Length != raw.Length * 4L) throw new Exception("Reference size differs");
                        for (int p = 0; p < raw.Length; p++)
                        {
                            double d = Math.Abs(raw[p] - reader.ReadSingle());
                            if (!double.IsFinite(d)) throw new Exception("Nonfinite pixel");
                            difference = Math.Max(difference, d); mean += d;
                        }
                        mean /= raw.Length;
                    }
                    File.WriteAllBytes(Folder + "/" + label + "." + phase + ".png", read.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (output != null) RenderTexture.ReleaseTemporary(output);
                    Object.DestroyImmediate(read); Object.DestroyImmediate(doc);
                }
            }
            submissions.Sort(); completions.Sort();
            string report = label + " coldSamples=9 submitMedianMs=" + submissions[4].ToString("F3") +
                " completedMedianMs=" + completions[4].ToString("F3") + " completedMaxMs=" + completions[8].ToString("F3") +
                " firstIncludingStartupMs=" + first.ToString("F3") + " maxPixelDelta=" + difference.ToString("G9") +
                " meanPixelDelta=" + mean.ToString("G9");
            File.WriteAllText(reportPath, report);
            if (difference > .00001) throw new Exception("Geometry/mask parity failed: " + report);
            return report;
        }
        catch (TargetInvocationException error) { throw new InvalidOperationException(error.GetBaseException().ToString()); }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }
    public static WhimTexDocument Create(int size, int kind)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = size; doc.outputPrecision = WhimTexOutputPrecision.Float32;
        var figure = Figure(0);
        Layer layer;
        if (kind == 0)
            layer = new Layer(new VectorLayerBehaviour { referenceSize = Vector2.one * size, shapes = new List<VectorShape> { figure } });
        else
        {
            var group = new BooleanGroupLayerBehaviour { referenceSize = Vector2.one * size, style = figure.Clone(false) };
            group.style.contours.Clear(); layer = new Layer(group);
            // Large operand set measures Boolean/fill preparation separately from dense offset strokes.
            if (kind == 2) group.style.stroke = false;
            int grid = kind == 1 ? 2 : 4;
            for (int i = 0; i < grid * grid; i++)
            {
                var child = new Layer(new VectorLayerBehaviour { referenceSize = Vector2.one * size, shapes = new List<VectorShape> { Figure(i) } });
                child.transform.scaleF = Vector2.one * (grid == 2 ? .72f : .34f);
                child.transform.positionF = new Vector2((i % grid - (grid - 1) * .5f), (i / grid - (grid - 1) * .5f)) * (size * .65f / grid);
                child.transform.rotation = i * 7;
                child.booleanOperation = i == 0 ? VectorBooleanOperation.Subtract : VectorBooleanOperation.Union;
                layer.children.Add(child);
            }
        }
        layer.colorRange = LayerColorRange.HDR; layer.blendRange = LayerBlendRange.HDR;
        layer.transform.tiling = TransformTilingMode.Unbounded;
        doc.layers.Add(layer); return doc;
    }
    static VectorShape Figure(int seed)
    {
        var shape = new VectorShape { fillMode = VectorFillMode.LinearGradient,
            fillColor = new Color(.1f, .4f, .8f, .7f), stroke = true, strokeWidth = 9,
            strokeColor = new Color(.8f, .2f, .05f, .55f), strokePosition = VectorStrokePosition.Inside,
            join = VectorStrokeJoin.Round, cap = VectorStrokeCap.Round,
            dashPatternRelative = true, dashPattern = new List<float> { 1, 1.5f } };
        shape.gradientStart = new Vector2(.13f, .23f); shape.gradientEnd = new Vector2(.85f, .72f);
        shape.gradient.SetKeys(new[] { new GradientColorKey(Color.cyan, 0), new GradientColorKey(Color.magenta, 1) },
            new[] { new GradientAlphaKey(.6f, 0), new GradientAlphaKey(.9f, 1) });
        var contour = new VectorContour(); shape.contours.Add(contour);
        for (int i = 0; i < 64; i++)
        {
            float a = i * (Mathf.PI * 2 / 64), r = .35f + .085f * Mathf.Cos(a * 8 + seed * .1f);
            var tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * .012f;
            contour.points.Add(new VectorPoint(new Vector2(.5f + Mathf.Cos(a) * r, .5f + Mathf.Sin(a) * r))
                { inTangent = -tangent, outTangent = tangent });
        }
        var hole = new VectorContour(); shape.contours.Add(hole);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            var tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * .028f;
            hole.points.Add(new VectorPoint(new Vector2(.5f + Mathf.Cos(a) * .11f, .5f + Mathf.Sin(a) * .11f))
                { inTangent = -tangent, outTangent = tangent });
        }
        return shape;
    }
}
