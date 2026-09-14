using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static Layer LiveCandidate(LiveJob job, JObject request, out Texture2D owned)
        {
            owned = null;
            Require((request["imagePath"] != null) != (request["layer"] != null), "Provide exactly one of imagePath or layer.");
            Require(request["imagePath"] != null || request["fit"] == null, "fit is only used with imagePath.");
            var reservation = job.document.FindLayer(job.layerId)?.Behaviour as PendingLayerBehaviour;
            Require(reservation != null && reservation.jobId == job.id, "Reservation was removed.", "job_closed");
            if (request["imagePath"] != null)
            {
                DrawingLayerBehaviour target = null;
                if (job.targetId != null)
                {
                    target = job.document.FindLayer(job.targetId)?.Behaviour as DrawingLayerBehaviour;
                    Require(target != null && LiveLayerRevision(target) == job.targetRevision,
                        "The target pixels or settings changed. Preserve the user's work: start a newLayer job or capture again.", "revision_conflict");
                }
                RequireGraphics();
                Texture2D image = ReadLiveImage(Text(request, "imagePath"));
                try
                {
                    if (target == null)
                    {
                        owned = CreateLiveDrawingImage(job, image,
                            Text(request, "fit", job.mask == null ? "contain" : "stretch"), out var transform);
                        owned.name = reservation.layerName;
                        var generated = DrawingLayerBehaviour.FromMergedTexture(owned);
                        generated.colorRange = LayerColorRange.Standard;
                        generated.transform = transform;
                        generated.AdoptReservation(reservation);
                        return generated;
                    }
                    using var pixels = LiveImagePixels(job, image, Text(request, "fit", job.mask == null ? "contain" : "stretch"), target);
                    int width = target?.StoredTexture != null ? target.StoredTexture.width : job.width;
                    int height = target?.StoredTexture != null ? target.StoredTexture.height : job.height;
                    bool hdr = target?.StoredTexture != null && HdrUtility.IsHdr(target.StoredTexture);
                    owned = new Texture2D(width, height, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr)
                    { name = reservation.layerName, hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    HdrUtility.WritePixels(owned, pixels);
                    return DrawingLayerBehaviour.FromRasterizedLayer(target, owned, false);
                }
                catch { if (owned != null) Object.DestroyImmediate(owned); owned = null; throw; }
                finally { Object.DestroyImmediate(image); }
            }
            Require(job.targetId == null && job.mask == null,
                "Selection edits and replacePixels jobs require imagePath. Parameter layers are created with area=canvas and destination=newLayer.");
            JObject spec = Obj(request["layer"], "layer");
            Keys(spec, "type", "settings", "transform", "input", "target", "fx");
            if (spec["settings"] is JObject settings)
                Require(settings["name"] == null && settings["enabled"] == null,
                    "The reservation owns name and enabled. Completion must not overwrite them.");
            var builder = ScriptableObject.CreateInstance<TextureCompositor>();
            Layer candidate = null;
            try
            {
                builder.width = job.width; builder.height = job.height;
                var add = new JObject { ["op"] = "add", ["type"] = Text(spec, "type") };
                if (spec["settings"] != null) add["settings"] = spec["settings"].DeepClone();
                if (spec["transform"] != null) add["transform"] = spec["transform"].DeepClone();
                candidate = ApplyOperation(builder, add, new Dictionary<string, Layer>(), false);
                candidate.AdoptReservation(reservation);
                if (candidate?.Behaviour is TargetedLayerBehaviour effect)
                {
                    effect.inputMode = Enum(spec, "input", EffectInputMode.Specific);
                    effect.TargetLayerId = Text(spec, "target");
                    Require(effect.inputMode != EffectInputMode.Previous || effect.TargetLayerId == null, "Previous input does not take a target.");
                }
                else Require(spec["input"] == null && spec["target"] == null, "input/target require an effect layer.");
                if (candidate?.Behaviour is FileLayerBehaviour file && file.sourceTexture != null)
                {
                    string path = AssetDatabase.GetAssetPath(job.document);
                    Require(string.IsNullOrEmpty(path) || !string.Equals(path, AssetDatabase.GetAssetPath(file.sourceTexture), StringComparison.OrdinalIgnoreCase),
                        "A document cannot sample its own saved output.", "invalid_target");
                }
                return candidate;
            }
            finally { builder.layers.Clear(); Object.DestroyImmediate(builder); }
        }

        private static TextureCompositor CloneLiveDocument(TextureCompositor source)
        {
            var clone = Object.Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            // Unsaved Drawing textures must not be shared with a disposable preview document.
            var copied = new List<DrawingLayerBehaviour>();
            try
            {
                foreach (var layer in Enumerate(clone.layers))
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing) { drawing.CloneStoredTexture(); copied.Add(drawing); }
                return clone;
            }
            catch
            {
                foreach (var drawing in copied) drawing.ReleaseTransientResources();
                clone.layers.Clear(); Object.DestroyImmediate(clone); throw;
            }
        }

        private static void PutLiveCandidate(TextureCompositor document, LiveJob job, Layer candidate)
        {
            var pending = document.FindLayer(job.layerId);
            Require(pending?.Behaviour is PendingLayerBehaviour && document.TryFindLayer(pending, out _, out _), "Reservation is missing.", "job_closed");
            document.TryFindLayer(pending, out var container, out int index);
            if (job.targetId == null) container[index] = candidate;
            else
            {
                var target = document.FindLayer(job.targetId);
                Require(target != null && document.TryFindLayer(target, out _, out _), "Target is missing.", "revision_conflict");
                document.TryFindLayer(target, out var targets, out int targetIndex);
                targets[targetIndex] = candidate;
                target.ReleaseTransientResources();
                container.Remove(pending);
            }
            if (candidate?.Behaviour is TargetedLayerBehaviour effect)
            {
                document.TryFindLayer(candidate, out var siblings, out int effectIndex);
                Require(document.HasUsableEffectInput(effect, siblings, effectIndex), "Effect target is missing or cyclic.", "invalid_target");
            }
        }

        private static JObject PreviewLiveCandidate(LiveJob job, JObject request)
        {
            Keys(request, "apiVersion", "op", "jobId", "layer", "imagePath", "fit", "fx", "view", "maxSize", "outputPath");
            string view = Text(request, "view", "layer");
            Require(view == "layer" || view == "composite", "view must be layer or composite.");
            int size = Int(request, "maxSize", 1024, 1, 4096);
            RequireGraphics();
            Texture2D owned = null, image = null;
            TextureCompositor preview = null;
            Layer candidate = null;
            bool installed = false;
            RenderTexture rt = null;
            var effects = new List<ShaderFX>();
            try
            {
                candidate = LiveCandidate(job, request, out owned);
                ApplyLiveCandidateFx(job, request, candidate, effects);
                preview = CloneLiveDocument(job.document);
                PutLiveCandidate(preview, job, candidate); installed = true;
                if (view == "composite") image = preview.ComposePreview(size);
                else
                {
                    rt = preview.RenderAgentLayerPreview(candidate, size);
                    Require(rt != null, "No candidate preview was generated.", "render_failed");
                    image = HdrUtility.ReadLinear(rt);
                }
                var result = LiveStatus(job);
                result["outputPath"] = WriteLivePng(image, Text(request, "outputPath",
                    "Temp/WhimTex/Agent/" + job.id + "/candidate-" + Guid.NewGuid().ToString("N") + ".png"));
                result["width"] = image.width; result["height"] = image.height;
                result["applied"] = false;
                result["fx"] = LiveFxSnapshot(candidate, job.document);
                return result;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (image != null) Object.DestroyImmediate(image);
                if (preview != null) Object.DestroyImmediate(preview);
                if (!installed) candidate?.ReleaseTransientResources();
                if (owned != null) Object.DestroyImmediate(owned);
                foreach (var fx in effects) if (fx != null) Object.DestroyImmediate(fx);
            }
        }

        private static JObject CompleteLiveJob(LiveJob job, JObject request)
        {
            Keys(request, "apiVersion", "op", "jobId", "layer", "imagePath", "fit", "fx");
            RequireGraphics();
            Texture2D owned = null;
            Layer candidate = null;
            bool committed = false;
            var effects = new List<ShaderFX>();
            try
            {
                candidate = LiveCandidate(job, request, out owned);
                ApplyLiveCandidateFx(job, request, candidate, effects);
                long pixels = Enumerate(job.document.layers).Select(layer => layer.Behaviour).OfType<DrawingLayerBehaviour>().Sum(d =>
                    d.StoredTexture != null ? (long)d.StoredTexture.width * d.StoredTexture.height : (long)job.width * job.height);
                if (job.targetId == null && candidate?.Behaviour is DrawingLayerBehaviour generated)
                    pixels += generated.StoredTexture != null
                        ? (long)generated.StoredTexture.width * generated.StoredTexture.height
                        : (long)job.width * job.height;
                Require(pixels <= 67108864, "Owned Drawing pixel limit reached.", "resource_limit");
                // Validate topology on a detached model before touching the live layer tree.
                var validation = Object.Instantiate(job.document);
                try
                {
                    if (job.targetId == null) PutLiveCandidate(validation, job, candidate);
                }
                finally { validation.layers.Clear(); Object.DestroyImmediate(validation); }
                LiveChange(job.document, "Complete Agent Layer", () =>
                {
                    if (job.targetId != null)
                    {
                        var target = (DrawingLayerBehaviour)job.document.FindLayer(job.targetId);
                        using var values = HdrUtility.ReadPixels(owned, Allocator.Temp);
                        target.ApplyFillPixels(values, owned.width, owned.height, "Complete Agent Layer");
                        var pending = job.document.FindLayer(job.layerId);
                        job.document.TryFindLayer(pending, out var container, out _);
                        container.Remove(pending);
                    }
                    else
                    {
                        var pending = job.document.FindLayer(job.layerId);
                        job.document.TryFindLayer(pending, out var container, out int index);
                        if (candidate?.Behaviour is DrawingLayerBehaviour drawing)
                        {
                            drawing.InitializeCanvas(job.width, job.height);
                            drawing.MakeTexturePersistent(job.document);
                            Undo.RegisterCreatedObjectUndo(drawing.StoredTexture, "Complete Agent Layer");
                        }
                        // Native object creation flushes the initial LiveChange record.
                        Undo.RegisterCompleteObjectUndo(job.document, "Complete Agent Layer");
                        pending.AdoptContent(candidate);
                        candidate = pending;
                        foreach (var fx in effects) job.document.AdoptAgentShaderFX(fx, "Complete Agent Layer");
                    }
                });
                committed = true;
                job.state = "completed"; job.completion = request.ToString(Formatting.None);
                job.mask = null; job.error = null;
                var result = LiveStatus(job);
                result["fx"] = LiveFxSnapshot(candidate, job.document);
                return result;
            }
            finally
            {
                if (!committed || job.targetId != null)
                {
                    candidate?.ReleaseTransientResources();
                    if (owned != null && !AssetDatabase.Contains(owned)) Object.DestroyImmediate(owned);
                }
                if (!committed) foreach (var fx in effects) if (fx != null && !AssetDatabase.Contains(fx)) Object.DestroyImmediate(fx);
            }
        }

        private static void ApplyLiveCandidateFx(LiveJob job, JObject request, Layer candidate, List<ShaderFX> effects)
        {
            JToken nested = (request["layer"] as JObject)?["fx"];
            Require(nested == null || request["fx"] == null, "Supply fx either in layer or at the request root, not both.");
            JToken fx = nested ?? request["fx"];
            Require(fx == null || job.targetId == null, "Use a lock/edit job to modify FX on an existing layer.");
            ApplyLiveFx(candidate, fx, job.document, effects);
        }
    }
}
