using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class PaintTileHistoryTests
{
    const string CorpusKey = "WhimTex.Tests.CodecCorpus";
    static object[] CorpusState => AppDomain.CurrentDomain.GetData(CorpusKey) as object[];
    public static string StartCorpus(string runId, int side, string mode)
    {
        if (CorpusState != null) return TestContext.Result("failed", 0, "Another owned capture exists").ToJson();
        var state = new object[] { runId, TestContext.Result("running", 0, "Capturing actual brush history blocks").ToJson(), null };
        AppDomain.CurrentDomain.SetData(CorpusKey, state);
        var run = new CorpusRun(state, side, mode); state[2] = (Action)(() => run.Finish(new OperationCanceledException()));
        try { run.Prepare(); } catch (Exception error) { run.Finish(error); }
        return (string)state[1];
    }
    public static string PollCorpus(string runId) => CorpusState != null && (string)CorpusState[0] == runId ? (string)CorpusState[1] : TestContext.Result("failed", 0, "Missing capture").ToJson();
    public static string CleanupCorpus(string runId) => TestContext.Run("Owned corpus fixture cleanup", t =>
    {
        t.True(CorpusState == null || (string)CorpusState[0] == runId, "Matching owner");
        if (CorpusState != null) { ((Action)CorpusState[2])(); AppDomain.CurrentDomain.SetData(CorpusKey, null); }
    });
    sealed class CorpusRun
    {
        readonly object[] state; readonly int side; readonly string mode;
        readonly TestContext test = new(); readonly List<object> pages = new(); readonly List<string> stepIds = new();
        Fixture fixture; DiskScope disk; Texture2D mask; Vector2[] points; object parameters;
        string root; int stroke, point, phase, group, copyPage; Array capturePages; bool finished; long started;
        internal CorpusRun(object[] state, int side, string mode) { this.state = state; this.side = side; this.mode = mode; }
        internal void Prepare()
        {
            test.True(side == 512 || side == 8192, "Bounded size");
            test.True(mode == "thin" || mode == "wide" || mode == "detail" || mode == "hdr", "Known corpus");
            test.True(side < 8192 || SystemInfo.graphicsMemorySize >= 7000, "8K needs 7 GB VRAM");
            test.Equal(0, Transfer<int>("PendingCount"), "No pending user transfer");
            test.Equal(0L, (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null), "No pending user writes");
            test.True(!(bool)Type("WhimTexProfiler").GetProperty("Recording", S).GetValue(null), "No user recording");
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>()) test.True(Get(window, "paintingLayer") == null, "No active user stroke");
            root = Path.GetFullPath("Temp/WhimTex/codec-comparison/corpus/" + side + "-" + mode + "-" + Guid.NewGuid().ToString("N"));
            test.True(new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace >= 8L * 1024 * 1024 * 1024, "At least 8 GiB free");
            Directory.CreateDirectory(root);
            disk = new DiskScope(); Call(disk.disk, "SetLimits", (int?)16, (int?)50);
            fixture = new Fixture(side, mode == "hdr", mode == "detail");
            Set(fixture.settings, "brushSize", mode == "thin" ? 32f : side / 4f);
            mask = new Texture2D(32, 32, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[1024];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) pixels[y * 32 + x] = new Color32((byte)(x < 3 ? 0 : y < 8 ? 64 : y < 20 ? 160 : 255), 0, 0, 255);
            mask.SetPixels32(pixels); mask.Apply(false, false);
            if (mode == "thin")
            {
                points = new Vector2[129];
                for (int i = 0; i < points.Length; i++) { float a = i * 2 * Mathf.PI / 128; points[i] = new Vector2(.5f + .45f * Mathf.Sin(3 * a + .2f), .5f + .45f * Mathf.Sin(2 * a)); }
            }
            else
            {
                points = new Vector2[16];
                for (int i = 0; i < 8; i++) { points[2 * i] = new Vector2(i % 2 == 0 ? .04f : .96f, .06f + .125f * i); points[2 * i + 1] = new Vector2(i % 2 == 0 ? .96f : .04f, .06f + .125f * i); }
            }
            started = Stopwatch.GetTimestamp(); EditorApplication.update += Tick;
        }
        void Tick()
        {
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { Advance(); } catch (Exception error) { Finish(error); }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        void Advance()
        {
            if (Stopwatch.GetTimestamp() - started > 220 * Stopwatch.Frequency) throw new TimeoutException("Corpus exceeded 220 seconds");
            if (phase == 0)
            {
                if (point == 0)
                {
                    Set(fixture.dynamics, "flow", mode == "hdr" && stroke == 1 ? .0001f : .13f);
                    if (stroke == 1) Set(fixture.settings, "brushColor", mode == "hdr" ? new Color(3f, -.1f, .8f, 1f) : new Color(.3f, .6f, .8f, 1f));
                    parameters = fixture.Parameters(mask); Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                    Call(fixture.layer, "PrepareBufferedStroke", side, side, "Owned codec corpus"); Call(fixture.layer, "BeginStroke", points[0]);
                }
                int end = Math.Min(points.Length, point + (mode == "thin" ? 8 : 1));
                for (; point < end; point++)
                    if (point == 0) Call(fixture.layer, "PaintPoint", points[0], side, side, parameters);
                    else Call(fixture.layer, "PaintSegment", points[point - 1], points[point], side, side, false, parameters);
                Probe(fixture.surface, new RectInt(0, 0, 1, 1)); // Bounded fence; no full-canvas ReadPixels.
                if (point < points.Length) return;
                Call(fixture.layer, "EndStroke"); Call(fixture.document, "MarkChanged");
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); phase = 1;
            }
            else if (phase == 1)
            {
                if (Transfer<int>("PendingCount") != 0 || (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null) != 0) return;
                Static("PaintHistoryStore", "FlushWrites");
                string revision = (string)Get(Get(fixture.layer, "PixelUndoObject"), "Revision"); stepIds.Add(revision);
                var step = Static("PaintHistoryStore", "Get", revision);
                capturePages = (Array)Get(step, "payload"); copyPage = 0; phase = 2;
                test.True(capturePages.Length > 0, "Real changed history blocks captured");
            }
            else
            {
                // Keep hashing/copying bounded per Editor update; noisy pages can be nearly raw.
                if (copyPage < capturePages.Length)
                {
                    var page = capturePages.GetValue(copyPage);
                    string from = (string)Get(page, "path"), name = "stroke-" + stroke + "-page-" + copyPage++ + ".bin", path = Path.Combine(root, name);
                    File.Copy(from, path, false); using var stream = File.OpenRead(path); using var sha = SHA256.Create();
                    pages.Add(new { file = name, stroke, blocks = Get(page, "blockCount"), rawBytes = Get(page, "bytes"), fileBytes = stream.Length,
                        sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() });
                }
                if (copyPage < capturePages.Length) return;
                stroke++; point = 0; phase = 0; if (stroke == 2) Finish(null);
            }
        }
        internal void Finish(Exception error)
        {
            if (finished) return; finished = true; EditorApplication.update -= Tick;
            try { fixture?.Dispose(); fixture = null; if (mask != null) Object.DestroyImmediate(mask); foreach (string id in stepIds) Static("PaintHistoryStore", "DeleteStep", Static("PaintHistoryStore", "Get", id)); }
            catch (Exception e) { error ??= e; }
            finally { try { disk?.Dispose(); } catch (Exception e) { error ??= e; } }
            try
            {
                if (root != null)
                {
                    File.WriteAllText(Path.Combine(root, "manifest.json"), Json(new { version = 1, side, mode, bpp = mode == "hdr" ? 8 : 4,
                        pageFormat = "57545035", tile = 256, selection = "zero/fractional/full bands", strokes = stroke, pages,
                        unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, createdUtc = DateTime.UtcNow, error = error?.ToString() }));
                    if (error == null) File.WriteAllText(Path.GetFullPath("Temp/WhimTex/codec-comparison/latest-" + side + "-" + mode + ".txt"), root);
                }
            }
            catch (Exception e) { error ??= e; }
            state[1] = TestContext.Result(error == null ? "passed" : "failed", test.Checks, "Captured brush corpus: " + root,
                error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
        }
    }
}
