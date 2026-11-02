using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        internal static event Action LiveEditLocksChanged;

        public static string LiveLock(string requestId, string layerId, string sessionId = null, string expectedRevision = null) => Respond(() =>
        {
            var request = new JObject { ["apiVersion"] = ProtocolVersion, ["op"] = "lock", ["requestId"] = requestId, ["layerId"] = layerId };
            if (sessionId != null) request["sessionId"] = sessionId;
            if (expectedRevision != null) request["expectedRevision"] = expectedRevision;
            return Live(request);
        });

        internal static bool IsLayerContentLocked(WhimTexDocument document, Layer layer)
        {
            if (layer == null) return false;
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending" && job.document == document && job.layerId == layer.Id) return true;
            return false;
        }

        private static JObject FxTransformSnapshot(ShaderTransform transform)
        {
            if (transform.storage == TransformStorage.Projective)
            {
                var m = transform.matrix;
                return new JObject { ["matrix"] = new JArray(m.m00,m.m01,m.m02,m.m10,m.m11,m.m12,m.m20,m.m21,m.m22) };
            }
            return new JObject { ["position"] = new JArray(transform.position.x, transform.position.y),
                ["size"] = new JArray(transform.size.x, transform.size.y), ["rotation"] = transform.rotation };
        }

        internal static bool IsShaderFXContentLocked(ShaderFX effect)
        {
            if (effect == null) return false;
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending" && job.document != null &&
                    job.document.FindLayer(job.layerId)?.fx?.Contains(effect) == true) return true;
            return false;
        }

        private static bool IsLiveLockedLayer(Layer layer) => layer != null && liveJobs.Values.Any(j =>
            j.editing && j.state == "pending" && j.document != null && ReferenceEquals(j.document.FindLayer(j.layerId), layer));

        private static void NotifyLiveLockChanged(WhimTexDocument document)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (window.AgentDocument == document) window.RefreshAgentLocks();
            LiveEditLocksChanged?.Invoke();
        }

        private static void CancelLiveEditLocks()
        {
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending")
                { job.state = "cancelled"; NotifyLiveLockChanged(job.document); }
        }

        internal static void CancelLayerEdit(WhimTexDocument document, Layer layer)
        {
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending" && job.document == document && job.layerId == layer?.Id)
                    job.state = "cancelled";
            NotifyLiveLockChanged(document);
        }

        private static JObject LockLiveLayer(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "requestId", "layerId", "expectedRevision", "commentChecks", "workId");
            string requestId = Text(request, "requestId"), canonical = request.ToString(Formatting.None);
            Require(!string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128, "A unique requestId is required.");
            foreach (var existing in liveJobs.Values)
                if (existing.requestId == requestId)
                {
                    Require(existing.request == canonical, "requestId already used for another request.", "request_conflict");
                    RefreshLiveJob(existing); return LiveStatus(existing);
                }
            var window = ResolveLiveBeginWindow(Text(request, "sessionId"));
            var document = window.AgentDocument;
            var replay = RetryLiveWork(document, request); if (replay != null) return replay;
            LiveReady(document); RefreshLiveJobs();
            Layer layer = document.FindLayer(Text(request, "layerId"));
            Require(layer != null && !(layer?.Behaviour is PendingLayerBehaviour), "Specify an existing content layer.", "layer_not_found");
            Require(!IsLayerContentLocked(document, layer) && !liveJobs.Values.Any(j => j.state == "pending" &&
                j.document == document && j.targetId == layer.Id), "Layer already has an active edit job.", "layer_locked");
            Require(liveJobs.Count < 512 && liveJobs.Values.Count(j => j.state == "pending") < 16, "Live job limit reached.", "resource_limit");
            string revision = LiveLayerRevision(layer);
            Require(request["expectedRevision"] == null || Text(request, "expectedRevision") == revision,
                "Layer changed since inspection.", "revision_conflict");
            var job = new LiveJob { id = Guid.NewGuid().ToString("N"), requestId = requestId, request = canonical,
                editing = true, document = document, session = window.AgentSessionId, layerId = layer.Id,
                targetId = layer.Id, targetRevision = revision, width = document.width, height = document.height };
            job.commentChecks = CheckComments(document, request["commentChecks"]);
            job.workId = Text(request, "workId");
            job.recoveryRevision = LiveRecoveryRevision(document);
            job.context = new JObject { ["layer"] = ((JArray)Snapshot(document, DocumentAssetPath(document))["layers"])
                .First(entry => (string)entry["id"] == layer.Id).DeepClone() };
            liveJobs.Add(job.id, job);
            NotifyLiveLockChanged(document);
            return LiveStatus(job);
        }

        private static void ApplyLiveFx(Layer layer, JToken token, WhimTexDocument owner, List<ShaderFX> created)
        {
            if (token == null) return;
            Require(token is JArray array && array.Count <= 16, "fx must be an array of at most 16 operations.");
            layer.fx = layer.fx == null ? new List<ShaderFX>() : new List<ShaderFX>(layer.fx);
            foreach (var item in (JArray)token)
            {
                JObject spec = Obj(item, "fx operation");
                string op = Text(spec, "op", "add");
                Require(op == "add" || op == "replace" || op == "set" || op == "remove", "FX op must be add, replace, set or remove.");
                if (op == "remove") Keys(spec, "op", "index");
                else Keys(spec, "op", "index", "program", "mode", "draftGraph", "draftCode", "workingCode", "draftCodeParameters", "compile",
                    "presetId", "includeBasePath", "parameters", "randomizeSeeds", "enabled");
                Require(op == "add" || spec["index"] != null, "replace/remove requires an explicit FX index.");
                Require(op == "add" || layer.fx.Count > 0, "Cannot replace/remove from an empty FX list.");
                int index = Int(spec, "index", layer.fx.Count, 0, op == "add" ? layer.fx.Count : layer.fx.Count - 1);
                if (op == "remove") { layer.fx.RemoveAt(index); continue; }
                RequireGraphics();
                ShaderFX fx;
                if (op == "set")
                {
                    Require(spec["presetId"] == null && spec["includeBasePath"] == null,
                        "Use replace to load a preset or change includeBasePath.");
                    Require(layer.fx[index] != null, "The Shader effect is missing.");
                    fx = layer.fx[index].CloneForDocument(owner);
                    created.Add(fx);
                    if (HasFxProgramEdit(spec))
                    {
                        fx.ReplaceProgram(ReadFxProgramEdit(fx.Program, spec, owner, null));
                        CompileFxDraft(fx, Bool(spec, "compile", true));
                    }
                    else if (Bool(spec, "compile")) CompileFxDraft(fx, true);
                }
                else
                {
                    fx = CreateFxProgramDraft(owner, spec, true, null);
                    created.Add(fx);
                }
                SetFxValues(fx, spec["parameters"], owner);
                RandomizeFxSeeds(fx, spec["randomizeSeeds"], spec["parameters"], true);
                if (spec["enabled"] != null) fx.Active = Bool(spec, "enabled");
                Require(fx.Parameters.Count <= MaxFxParameters, "At most 128 FX parameters are supported by live authoring.", "resource_limit");
                if (op == "add") layer.fx.Insert(index, fx);
                else layer.fx[index] = fx;
                WhimTexDocumentJson.ValidateShaderProgramInputs(owner, layer, fx.Program, effectContext: true);
                fx.NotifyValuesChanged();
                Require(layer.fx.Count <= 32, "At most 32 FX entries per layer are supported by live authoring.", "resource_limit");
            }
        }

        private static JArray LiveFxSnapshot(Layer layer, WhimTexDocument owner)
        {
            var result = new JArray();
            if (layer.fx == null) return result;
            for (int i = 0; i < layer.fx.Count; i++)
            {
                var fxEntry = layer.fx[i];
                var entry = new JObject { ["index"] = i, ["assetPath"] = fxEntry == null ? null : AssetDatabase.GetAssetPath(fxEntry) };
                if (fxEntry is ShaderFX fx)
                {
                    entry["type"] = "shaderFX"; entry["embedded"] = fx.EmbeddedOwner == owner;
                    entry["enabled"] = fx.Active;
                    entry["draftCode"] = fx.Code;
                    entry["mode"] = fx.Mode.ToString();
                    entry["program"] = WhimTexDocumentJson.ShaderProgramSnapshot(fx.Program, owner, fx.SourcePath);
                    WriteFxDiagnostics(entry, fx);
                    entry["pendingChanges"] = fx.HasPendingChanges;
                    entry["lastCompileFailed"] = fx.LastCompileFailed;
                    var parameters = new JArray();
                    foreach (var p in fx.Parameters)
                    {
                        if (p == null) continue;
                        var seed = ShaderSeed.Control(p);
                        JToken value = p.Components > 0 ? TypedFxValue(p) :
                            p.type == ShaderParameterType.Curve ? new JValue(WhimTexCurveTexture.Format(p.curveValue)) :
                            p.type == ShaderParameterType.Gradient ? GradientSnapshot(p.gradientValue ?? new WhimTexGradient()) :
                            p.type == ShaderParameterType.Transform2D ? FxTransformSnapshot(p.transformValue) :
                            p.type == ShaderParameterType.Color ? (JToken)Json(p.colorValue) :
                            p.type == ShaderParameterType.Vector2 ? new JArray(p.vectorValue.x, p.vectorValue.y) :
                            p.type == ShaderParameterType.Point ? new JArray(p.vectorValue.x, p.vectorValue.y) :
                            p.type == ShaderParameterType.Vector3 || p.type == ShaderParameterType.Normal ? new JArray(p.vectorValue.x, p.vectorValue.y, p.vectorValue.z) :
                            p.type == ShaderParameterType.Vector ? new JArray(p.vectorValue.x, p.vectorValue.y, p.vectorValue.z, p.vectorValue.w) :
                            p.type == ShaderParameterType.Texture2D ? (p.textureSource == ShaderTextureSource.Layer
                                ? (JToken)new JObject { ["layer"] = p.textureLayerId }
                                : p.textureSource == ShaderTextureSource.Self ? new JValue("self")
                                : p.textureSource == ShaderTextureSource.None ? new JValue("none")
                                : new JValue(p.textureValue == null ? "" : AssetDatabase.GetAssetPath(p.textureValue))) : new JValue(p.floatValue);
                        parameters.Add(new JObject { ["name"] = p.name, ["id"] = p.id, ["type"] = p.type.ToString(), ["valueType"] = p.ValueType, ["value"] = value,
                            ["controls"] = new JArray(p.controls.Select(c => new JObject { ["field"] = c.type.ToString().ToLowerInvariant(),
                                ["valueType"] = p.ValueType, ["componentLabels"] = p.Components > 1 ? (JToken)new JArray((c.rgba ? "RGBA" : "XYZW").Substring(0, p.Components).Select(x => x.ToString())) : null })),
                            ["seed"] = seed != null,
                            ["seedMinimum"] = seed != null ? (JToken)new JValue(seed.valueMinimum) : JValue.CreateNull(),
                            ["seedMaximum"] = seed != null ? (JToken)new JValue(seed.valueMaximum) : JValue.CreateNull(),
                            ["softMinimum"] = p.softMinimum, ["softMaximum"] = p.softMaximum,
                            ["cyclicRange"] = p.cyclicRange,
                            ["options"] = p.controls.Any(c => c.type == ShaderParameterType.Enum) ? new JArray(p.controls.SelectMany(c => c.optionNames.Zip(c.optionValues, (n, v) => new JObject { ["name"] = n, ["value"] = v }))) : null,
                            ["minimum"] = p.hasMinimum ? (JToken)new JValue(p.minimum) : JValue.CreateNull(),
                            ["maximum"] = p.hasMaximum ? (JToken)new JValue(p.maximum) : JValue.CreateNull() });
                    }
                    entry["parameters"] = parameters;
                    entry["catalogPath"] = fx.CatalogPath;
                    entry["presetId"] = fx.PresetId;
                    entry["presetVersion"] = fx.PresetVersion;
                    entry["presetFallback"] = fx.UsingPresetFallback;
                    entry["icon"] = FxIconSnapshot(fx.IconMetadata, fx.SourcePath);
                }
                else entry["type"] = "empty";
                result.Add(entry);
            }
            return result;
        }

        private static void ApplyLiveEditSettings(WhimTexDocument document, Layer layer, JObject changes)
        {
            Keys(changes, "settings", "transform", "fx");
            Require(changes.Count > 0, "changes must not be empty.");
            if (changes["settings"] != null)
            {
                JObject settings = Obj(changes["settings"], "settings");
                Require(settings["name"] == null && settings["enabled"] == null, "Name and visibility remain owned by the user.");
                SetLayer(document, layer, settings);
            }
            if (changes["transform"] != null) SetTransform(document, layer, Obj(changes["transform"], "transform"));
            // A geometry edit can invalidate an ancestor even though its own style is unchanged.
            foreach (var candidate in Enumerate(document.layers))
                if (candidate.Behaviour is BooleanGroupLayerBehaviour booleanGroup) booleanGroup.Validate();
        }

        private static JObject ApplyLiveEdit(LiveJob job, JObject request, bool trial)
        {
            if (trial) Keys(request, "apiVersion", "op", "jobId", "changes", "view", "maxSize", "outputPath", "review");
            else Keys(request, "apiVersion", "op", "jobId", "changes");
            Layer target = job.document.FindLayer(job.layerId);
            Require(target != null && IsLayerContentLocked(job.document, target), "Edit lock is no longer active.", "job_closed");
            Require(LiveLayerRevision(target) == job.targetRevision, "Target changed. Unlock and inspect before starting another edit.", "revision_conflict");
            JObject changes = Obj(request["changes"], "changes");
            var created = new List<ShaderFX>();
            WhimTexDocument probe = null;
            Texture2D image = null;
            RenderTexture rt = null;
            bool committed = false;
            try
            {
                probe = CloneLiveDocument(job.document);
                Layer candidate = probe.FindLayer(target.Id);
                ApplyLiveEditSettings(probe, candidate, changes);
                ApplyLiveFx(candidate, changes["fx"], job.document, created);
                if (candidate?.Behaviour is FileLayerBehaviour file && file.sourceTexture != null)
                    Require(!string.Equals(AssetDatabase.GetAssetPath(file.sourceTexture), DocumentAssetPath(job.document), StringComparison.OrdinalIgnoreCase),
                        "A document cannot sample its own output.", "invalid_target");
                if (trial)
                {
                    RequireGraphics();
                    string view = Text(request, "view", "composite");
                    Require(view == "composite" || view == "layer", "view must be composite or layer.");
                    int size = Int(request, "maxSize", 1024, 1, 4096);
                    if (view == "composite") image = probe.ComposeCanvas(size);
                    else
                    {
                        rt = probe.RenderAgentLayerPreview(candidate, size);
                        Require(rt != null, "No preview generated.", "render_failed");
                        image = HdrUtility.ReadLinear(rt);
                    }
                    var result = LiveStatus(job);
                    result["outputPath"] = WriteLivePng(image, Text(request, "outputPath", "Temp/WhimTex/Agent/edit-" + Guid.NewGuid().ToString("N") + ".png"));
                    result["applied"] = false; result["fx"] = LiveFxSnapshot(candidate, job.document);
                    if (Bool(request, "review")) result["review"] = ReviewLiveCandidate(job, probe, size);
                    return result;
                }
                LiveChange(job.document, "Complete Agent Edit", () =>
                {
                    ApplyLiveEditSettings(job.document, target, changes);
                    if (target?.Behaviour is DrawingLayerBehaviour drawing) drawing.SetColorRange(target.colorRange);
                    target.fx = new List<ShaderFX>(candidate.fx);
                    foreach (var fx in created) job.document.AdoptAgentShaderFX(fx, "Complete Agent Edit");
                    RecordLiveWork(job, request);
                });
                committed = true;
                if (job.workId != null) SessionState.SetString(ReceiptMemoryKey(job.document, job.workId, job.requestId), WorkRequestHash(JObject.Parse(job.request)));
                job.state = "completed"; job.completion = request.ToString(Formatting.None); job.error = null;
                NotifyLiveLockChanged(job.document);
                var completed = LiveStatus(job);
                completed["fx"] = LiveFxSnapshot(target, job.document);
                return completed;
            }
            finally
            {
                if (rt != null) ProfiledRenderTexture.ReleaseTemporary(rt);
                if (image != null) Object.DestroyImmediate(image);
                if (probe != null) Object.DestroyImmediate(probe);
                if (!committed) foreach (var fx in created) if (fx != null && !AssetDatabase.Contains(fx)) Object.DestroyImmediate(fx);
            }
        }
    }
}
