using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexProfiler
    {
        struct Cause { internal long id; internal string layerId, layerName, property, kind; }
        static readonly Dictionary<(string document, string layer), JObject> settingsSnapshots = new();
        static readonly Dictionary<(string document, string layer), int> snapshotSizes = new();
        static int snapshotCharacters;
        static readonly Dictionary<(string document, string layer), Cause> layerCauses = new();
        static readonly Dictionary<(string document, string layer), List<long>> layerCauseLinks = new();
        static readonly HashSet<(string document, string layer)> observedLayers = new();
        static void ResetCauses() { settingsSnapshots.Clear(); snapshotSizes.Clear(); snapshotCharacters = 0; layerCauses.Clear(); layerCauseLinks.Clear(); observedLayers.Clear(); }

        internal static void BeginCauseFrame()
        {
            if (!Recording) return;
            layerCauses.Clear(); layerCauseLinks.Clear(); observedLayers.Clear();
        }

        internal static void ObserveSettings(WhimTexDocument document, Layer layer, string serializedLayer)
        {
            if (!Recording || suppressed != 0 || layer == null) return;
            try { ObserveSettingsCore(document, layer, serializedLayer); }
            catch (Exception) { capture.causeTrackingLimited = true; }
        }

        static void ObserveSettingsCore(WhimTexDocument document, Layer layer, string serializedLayer)
        {
            string doc = DocumentId(document);
            if (capture.documentId != null && capture.documentId != doc) return;
            var key = (doc, layer.Id);
            if (!observedLayers.Add(key)) return;
            if (settingsSnapshots.Count >= 1024 && !settingsSnapshots.ContainsKey(key)) { capture.causeTrackingLimited = true; return; }
            snapshotSizes.TryGetValue(key, out int previousSize);
            int size = serializedLayer.Length;
            bool Fits() => size <= 262144 && snapshotCharacters - previousSize + size <= 2097152;
            void Skip()
            {
                capture.causeTrackingLimited = true;
                settingsSnapshots.Remove(key); snapshotSizes.Remove(key); snapshotCharacters -= previousSize;
            }
            if (!Fits()) { Skip(); return; }
            // Read the same settings used by the render cache; diagnostics never change its hash.
            var state = JObject.Parse(serializedLayer);
            state.Remove("children"); state.Remove("layerName"); state.Remove("id");
            if (layer.IsGroup)
            {
                var children = new JArray();
                if (layer.layers != null) foreach (var child in layer.layers) children.Add(child?.Id);
                state["childrenOrder"] = children;
            }
            var effects = new JArray();
            if (layer.fx != null)
                foreach (var fx in layer.fx)
                {
                    string json = fx == null ? null : UnityEditor.EditorJsonUtility.ToJson(fx);
                    size += json?.Length ?? 0;
                    if (!Fits()) { Skip(); return; }
                    effects.Add(json == null ? JValue.CreateNull() : JToken.Parse(json));
                }
            state["fx"] = effects;
            var source = layer.SamplingSource;
            state["sourcePixels"] = source == null ? null : UnityObjectID.FromObject(source) + ":" + source.updateCount;
            if (layer.Behaviour is DrawingLayerBehaviour drawing) state["paintRevision"] = drawing.PaintSurfaceRevision;
            if (settingsSnapshots.TryGetValue(key, out var previous))
            {
                var paths = new List<string>();
                ChangedPaths(previous, state, "", paths);
                foreach (string path in paths)
                {
                    int slot = Reserve(); if (slot < 0) break;
                    var sample = CreateSample(slot, "edit.observed_change", "change", "Observed settings difference between cache preparations", doc, layer.Id, layerName: layer.layerName);
                    sample.causeKind = "observed_property"; sample.causeProperty = path;
                    sample.causeLayerId = layer.Id; sample.causeLayerName = layer.layerName;
                    sample.completed = true; sample.end = sample.start;
                    capture.events[slot] = sample;
                    AddCauseLink(key, sample.id);
                    layerCauses[key] = new Cause { id = sample.id, layerId = layer.Id, layerName = layer.layerName,
                        property = path, kind = "observed_property" };
                }
            }
            if (!Recording) return; // Reserve can stop at the event limit and clear the trackers.
            settingsSnapshots[key] = state;
            snapshotSizes[key] = size; snapshotCharacters += size - previousSize;
        }

        static void ChangedPaths(JToken before, JToken after, string path, List<string> paths)
        {
            if (JToken.DeepEquals(before, after)) return;
            if (paths.Count >= 32) { capture.causeTrackingLimited = true; return; }
            if (before is JObject a && after is JObject b)
            {
                foreach (var p in b.Properties()) ChangedPaths(a[p.Name], p.Value, path.Length == 0 ? p.Name : path + "." + p.Name, paths);
                foreach (var p in a.Properties())
                    if (b[p.Name] == null)
                    {
                        if (paths.Count < 32) paths.Add(path.Length == 0 ? p.Name : path + "." + p.Name);
                        else capture.causeTrackingLimited = true;
                    }
            }
            else if (before is JArray aa && after is JArray ab && aa.Count == ab.Count)
            {
                for (int i = 0; i < aa.Count; i++) ChangedPaths(aa[i], ab[i], path + "[" + i + "]", paths);
            }
            else paths.Add(path);
        }

        static void AddCauseLink((string document, string layer) key, long id)
        {
            if (!layerCauseLinks.TryGetValue(key, out var links)) layerCauseLinks[key] = links = new List<long>();
            if (links.Count < 32) links.Add(id);
            else capture.causeTrackingLimited = true;
        }

        internal static void DependencyCause(WhimTexDocument document, Layer consumer, Layer input, string relation)
        {
            if (!Recording || suppressed != 0 || consumer == null || input == null) return;
            string doc = DocumentId(document);
            if (!layerCauses.TryGetValue((doc, input.Id), out var cause)) return;
            int slot = Reserve(); if (slot < 0) return;
            var sample = CreateSample(slot, "cache.dependency_changed", "reason", relation, doc, consumer.Id, layerName: consumer.layerName);
            ApplyCause(ref sample, cause);
            if (layerCauseLinks.TryGetValue((doc, input.Id), out var links)) sample.causeEventIds = links.ToArray();
            sample.causeKind = "dependency"; sample.value = 1; sample.completed = true; sample.end = sample.start;
            capture.events[slot] = sample;
            AddCauseLink((doc, consumer.Id), sample.id);
            layerCauses[(doc, consumer.Id)] = new Cause { id = sample.id, layerId = cause.layerId,
                layerName = cause.layerName, property = cause.property, kind = "dependency" };
        }

        static void ApplyCause(ref Sample sample, Cause cause)
        {
            sample.causeEventId = cause.id; sample.causeLayerId = cause.layerId; sample.causeLayerName = cause.layerName;
            sample.causeProperty = cause.property; sample.causeKind = cause.kind;
        }

        static void AttachCause(ref Sample sample)
        {
            if (sample.phase == "cache.miss" && sample.detail == "miss:content_or_settings" &&
                layerCauses.TryGetValue((sample.documentId, sample.layerId), out var cause))
            {
                ApplyCause(ref sample, cause);
                if (layerCauseLinks.TryGetValue((sample.documentId, sample.layerId), out var links)) sample.causeEventIds = links.ToArray();
            }
        }
    }
}
