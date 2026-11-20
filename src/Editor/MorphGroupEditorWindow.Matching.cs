using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        [Serializable] private sealed class MarcoResult { public List<MorphAnchor> anchors = new(); }

        private EnumField matchingField;
        private Button analyzePair;
        private Label marcoStatus;
        private VisualElement marcoControls;
        private Process marcoJob;
        private string jobDirectory, marcoMessage;
        private double analysisTime;
        private bool checkMarcoKeys;

        private void AddMatchingControls(VisualElement root)
        {
            matchingField = WhimTexUI.ConfigureField(new EnumField("Matching", MorphMatching.Liao) { name = "morphMatching" });
            matchingField.RegisterValueChangedCallback(evt =>
            {
                if (!Writable || !IsRaster || morph.classicLiao)
                    matchingField.SetValueWithoutNotify(morph?.matching ?? MorphMatching.Liao);
                else if ((MorphMatching)evt.newValue != morph.matching)
                {
                    marcoMessage = null;
                    Edit("Change Morph Matching", () => morph.matching = (MorphMatching)evt.newValue);
                }
            });
            root.Add(matchingField);
            marcoControls = new VisualElement();
            analyzePair = new Button(ToggleMarcoAnalysis) { name = "analyzeMorphPair" };
            marcoStatus = new Label { name = "marcoStatus" };
            marcoStatus.style.whiteSpace = WhiteSpace.Normal;
            marcoControls.Add(analyzePair);
            marcoControls.Add(marcoStatus);
            root.Add(marcoControls);
        }

        private void RefreshMarcoAction()
        {
            bool visible = IsRaster && !morph.classicLiao && morph.matching == MorphMatching.MARCO;
            SetVisible(marcoControls, visible);
            if (!visible) return;
            int count = transition?.marcoAnchors.Count ?? 0;
            analyzePair.text = marcoJob != null ? "Cancel Analysis" : "Analyze Pair";
            analyzePair.SetEnabled(marcoJob != null || Writable && !previewsDirty && a?.Texture != null && b?.Texture != null);
            marcoStatus.text = marcoJob != null ? "Finding correspondences…" : marcoMessage ??
                (count > 0 ? $"{count} saved matches" : "Analyze this pair to generate matches.");
        }

        private void ToggleMarcoAnalysis()
        {
            if (marcoJob != null)
            {
                StopMarcoJob();
                RefreshMarcoAction();
                return;
            }
            if (!Writable || !IsRaster || morph.classicLiao || morph.matching != MorphMatching.MARCO ||
                previewsDirty || a.Texture == null || b.Texture == null) return;
            document.SetPlayback(false);
            var runtime = Path.GetFullPath("Library/WhimTex/MARCO");
            try
            {
                string python = Path.Combine(runtime, "env", "bin", "python");
                if (!File.Exists(python)) python = Path.Combine(runtime, "env", "Scripts", "python.exe");
                if (!File.Exists(python) || !File.Exists(Path.Combine(runtime, "verified.safetensors")))
                    throw new InvalidOperationException("Install the local matcher using ExternalTools~/MorphMatch/setup.py.");
                const string package = "Packages/com.dcfapixels.whimtex";
                string worker = Path.Combine(PackageInfo.FindForAssetPath(package + "/package.json").resolvedPath,
                    "ExternalTools~/MorphMatch/match.py");
                jobDirectory = Path.GetFullPath(Path.Combine("Temp/WhimTex/MorphMatches", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(jobDirectory);
                File.WriteAllBytes(Path.Combine(jobDirectory, "a.png"), EncodeKey(a.Texture));
                File.WriteAllBytes(Path.Combine(jobDirectory, "b.png"), EncodeKey(b.Texture));
                File.WriteAllText(Path.Combine(jobDirectory, "pending"), "");
                checkMarcoKeys = false;
                analysisTime = document.PreviewTime;
                var info = new ProcessStartInfo(python, $"-B \"{worker}\" \"{runtime}\" \"{jobDirectory}\"")
                {
                    WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true
                };
                marcoMessage = null;
                marcoJob = Process.Start(info);
            }
            catch (Exception ex)
            {
                StopMarcoJob();
                marcoMessage = ex.Message;
            }
            RefreshMarcoAction();
        }

        private static byte[] EncodeKey(RenderTexture rt)
        {
            var pixels = HdrUtility.ReadLinear(rt);
            try { return WhimTexRasterEncoder.Encode(pixels, RasterImageFormat.Png); }
            finally { DestroyImmediate(pixels); }
        }

        private void PollMarcoJob()
        {
            if (marcoJob == null) return;
            if (!Writable || !IsRaster || morph.matching != MorphMatching.MARCO || morph.classicLiao ||
                document.PreviewTime != analysisTime && (document.LayerPreviewUsesTime(from) || document.LayerPreviewUsesTime(to)))
            {
                StopMarcoJob();
                RefreshMarcoAction();
                return;
            }
            if (!marcoJob.HasExited || previewsDirty) return;
            try
            {
                if (marcoJob.ExitCode != 0)
                {
                    string error = Path.Combine(jobDirectory, "error.txt");
                    throw new InvalidOperationException(File.Exists(error) ? File.ReadAllText(error) : $"Matcher exited with code {marcoJob.ExitCode}.");
                }
                if (checkMarcoKeys && (!SameKey(a.Texture, "a.png") || !SameKey(b.Texture, "b.png")))
                    throw new InvalidOperationException("The source images changed during analysis. Analyze the pair again.");
                var data = JsonUtility.FromJson<MarcoResult>(File.ReadAllText(Path.Combine(jobDirectory, "matches.json")));
                var points = data?.anchors;
                if (points == null || points.Count > 40 || points.Exists(p => p == null || !VectorGeometry.Finite(p.from) ||
                    !VectorGeometry.Finite(p.to) || !MorphTransition.ValidRadius(p.radius)))
                    throw new InvalidDataException("Invalid MARCO matches.");
                StopMarcoJob();
                marcoMessage = points.Count == 0 ? "No reliable matches found for this pair." : null;
                Edit("Analyze Morph Pair", () => morph.GetOrCreate(fromId, toId).marcoAnchors = points);
            }
            catch (Exception ex)
            {
                StopMarcoJob();
                marcoMessage = ex.Message;
            }
            RefreshMarcoAction();
        }

        private bool SameKey(RenderTexture texture, string name) => texture != null &&
            EncodeKey(texture).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(jobDirectory, name)));

        private void StopMarcoJob()
        {
            marcoJob?.Dispose();
            marcoJob = null;
            if (jobDirectory == null) return;
            try
            {
                if (Directory.Exists(jobDirectory))
                {
                    File.Delete(Path.Combine(jobDirectory, "pending"));
                    Directory.Delete(jobDirectory, true);
                }
            }
            catch (IOException) { /* The cancelled worker removes any files it still has open. */ }
            finally { jobDirectory = null; }
        }
    }
}
