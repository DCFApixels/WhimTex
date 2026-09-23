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

        internal static bool IsLayerContentLocked(TextureCompositor document, Layer layer)
        {
            if (layer == null) return false;
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending" && job.document == document && job.layerId == layer.Id) return true;
            return false;
        }

        private static JObject FxTransformSnapshot(ShaderFXTransform transform)
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
                    job.document.FindLayer(job.layerId)?.modifiers?.Contains(effect) == true) return true;
            return false;
        }

        private static bool IsLiveLockedLayer(Layer layer) => layer != null && liveJobs.Values.Any(j =>
            j.editing && j.state == "pending" && j.document != null && ReferenceEquals(j.document.FindLayer(j.layerId), layer));

        private static void NotifyLiveLockChanged(TextureCompositor document)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window.AgentDocument == document) window.RefreshAgentLocks();
            LiveEditLocksChanged?.Invoke();
        }

        private static void CancelLiveEditLocks()
        {
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending")
                { job.state = "cancelled"; NotifyLiveLockChanged(job.document); }
        }

        internal static void CancelLayerEdit(TextureCompositor document, Layer layer)
        {
            foreach (var job in liveJobs.Values)
                if (job.editing && job.state == "pending" && job.document == document && job.layerId == layer?.Id)
                    job.state = "cancelled";
            NotifyLiveLockChanged(document);
        }

        private static JObject LockLiveLayer(JObject request)
        {
            Keys(request, "apiVersion", "op", "sessionId", "requestId", "layerId", "expectedRevision");
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
            job.context = new JObject { ["layer"] = ((JArray)Snapshot(document, AssetDatabase.GetAssetPath(document))["layers"])
                .First(entry => (string)entry["id"] == layer.Id).DeepClone() };
            liveJobs.Add(job.id, job);
            NotifyLiveLockChanged(document);
            return LiveStatus(job);
        }

        private static void ApplyLiveFx(Layer layer, JToken token, TextureCompositor owner, List<ShaderFX> created)
        {
            if (token == null) return;
            Require(token is JArray array && array.Count <= 16, "fx must be an array of at most 16 operations.");
            layer.modifiers = layer.modifiers == null ? new List<Object>() : new List<Object>(layer.modifiers);
            foreach (var item in (JArray)token)
            {
                JObject spec = Obj(item, "fx operation");
                string op = Text(spec, "op", "add");
                Require(op == "add" || op == "replace" || op == "remove", "FX op must be add, replace or remove.");
                if (op == "remove") Keys(spec, "op", "index");
                else Keys(spec, "op", "index", "code", "parameters");
                Require(op == "add" || spec["index"] != null, "replace/remove requires an explicit modifier index.");
                Require(op == "add" || layer.modifiers.Count > 0, "Cannot replace/remove from an empty FX list.");
                int index = Int(spec, "index", layer.modifiers.Count, 0, op == "add" ? layer.modifiers.Count : layer.modifiers.Count - 1);
                if (op == "remove") { layer.modifiers.RemoveAt(index); continue; }
                Require(spec["code"]?.Type == JTokenType.String && ((string)spec["code"]).Length > 0 &&
                    ((string)spec["code"]).Length <= 65536, "code must contain 1..65536 characters of inline HLSL.");
                var parameters = ReadLiveFxParameters(spec["parameters"], owner);
                RequireGraphics();
                var fx = ShaderFX.CreateAgentDraft(owner, (string)spec["code"], parameters);
                created.Add(fx);
                try { fx.ApplyAgentDraft(); }
                catch (Exception error) { throw new WhimTexApiException("shader_compile_failed", error.Message); }
                Require(fx.Parameters.Count <= 32, "At most 32 FX parameters are supported by live authoring.", "resource_limit");
                if (op == "add") layer.modifiers.Insert(index, fx);
                else layer.modifiers[index] = fx;
                foreach (var parameter in fx.TextureLayerParameters())
                    Require(owner.IsUsableShaderTexture(layer, parameter.textureLayerId), "Texture layer would create a cyclic dependency.", "invalid_target");
                Require(layer.modifiers.Count <= 32, "At most 32 FX entries per layer are supported by live authoring.", "resource_limit");
            }
        }

        private static List<ShaderFXParameter> ReadLiveFxParameters(JToken token, TextureCompositor owner)
        {
            var result = new List<ShaderFXParameter>();
            if (token == null) return result;
            Require(token is JArray array && array.Count <= 32, "parameters must be an array of at most 32 entries.");
            foreach (var item in (JArray)token)
            {
                JObject spec = Obj(item, "parameter");
                Keys(spec, "name", "type", "value");
                var value = new ShaderFXParameter { name = Text(spec, "name"), type = Enum(spec, "type", ShaderFXParameterType.Float) };
                Require(spec["value"] != null, "Parameter value is required.");
                switch (value.type)
                {
                    case ShaderFXParameterType.Curve:
                        Require(spec["value"].Type == JTokenType.String, "Curve value must be linear, easeIn, easeOut, easeInOut, one or a keys(...) string.");
                        value.curveValue = WhimTexCurveTexture.Parse((string)spec["value"]);
                        break;
                    case ShaderFXParameterType.Gradient: value.gradientValue = ReadGradient(spec["value"]); break;
                    case ShaderFXParameterType.Bool:
                        Require(spec["value"].Type == JTokenType.Boolean, "Bool value must be true or false.");
                        value.floatValue = (bool)spec["value"] ? 1f : 0f;
                        break;
                    case ShaderFXParameterType.Enum:
                    case ShaderFXParameterType.Float: value.floatValue = Number(spec["value"], "value", -1000000, 1000000); break;
                    case ShaderFXParameterType.Color: value.colorValue = AgentJson.Color(spec["value"]); break;
                    case ShaderFXParameterType.Vector2:
                    case ShaderFXParameterType.Point:
                    case ShaderFXParameterType.Vector3:
                    case ShaderFXParameterType.Normal:
                        int components = value.type == ShaderFXParameterType.Vector2 || value.type == ShaderFXParameterType.Point ? 2 : 3;
                        Require(spec["value"] is JArray values && values.Count == components, "Wrong vector component count.");
                        value.vectorValue = Vector4.zero;
                        for (int i=0;i<components;i++) value.vectorValue[i] = Number(spec["value"][i], "component", -1000000, 1000000);
                        if (value.type == ShaderFXParameterType.Point)
                            Require(value.vectorValue.x >= 0 && value.vectorValue.x <= 1 && value.vectorValue.y >= 0 && value.vectorValue.y <= 1,
                                "Point coordinates must be in the normalized canvas range 0..1.");
                        if (value.type == ShaderFXParameterType.Normal) value.vectorValue = ShaderFXParameter.NormalizeNormal(value.vectorValue);
                        break;
                    case ShaderFXParameterType.Vector:
                        Require(spec["value"] is JArray vector && vector.Count == 4, "Vector value must have four components.");
                        value.vectorValue = new Vector4(Number(spec["value"][0], "x", -1000000, 1000000), Number(spec["value"][1], "y", -1000000, 1000000),
                            Number(spec["value"][2], "z", -1000000, 1000000), Number(spec["value"][3], "w", -1000000, 1000000));
                        break;
                    case ShaderFXParameterType.Texture2D:
                        if (spec["value"] is JObject layerSource)
                        {
                            Keys(layerSource, "layer");
                            string layerId = Text(layerSource, "layer");
                            Require(owner != null && owner.FindLayer(layerId)?.Behaviour != null, "Texture source layer not found.", "invalid_target");
                            value.textureSource = ShaderFXTextureSource.Layer;
                            value.textureLayerId = layerId;
                            break;
                        }
                        string path = Text(spec, "value");
                        if (path == "self" || path == "none")
                        {
                            value.textureSource = path == "self" ? ShaderFXTextureSource.Self : ShaderFXTextureSource.None;
                            break;
                        }
                        value.textureSource = ShaderFXTextureSource.Texture;
                        Require(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal), "Texture value must be a project asset path.");
                        ValidateSegments(path);
                        value.textureValue = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        Require(value.textureValue != null, "Texture parameter asset not found.");
                        Require(!string.Equals(path, AssetDatabase.GetAssetPath(owner), StringComparison.OrdinalIgnoreCase), "An FX cannot sample its own document output.", "invalid_target");
                        break;
                    case ShaderFXParameterType.Transform2D:
                        JObject area = Obj(spec["value"], "Transform2D value");
                        Keys(area, "position", "size", "rotation", "matrix");
                        var dimensions = owner != null ? new Vector2(owner.width, owner.height) : Vector2.one;
                        if (area["matrix"] != null)
                        {
                            Require(area["position"] == null && area["size"] == null && area["rotation"] == null,
                                "matrix cannot be combined with position, size or rotation.");
                            Require(area["matrix"] is JArray && ((JArray)area["matrix"]).Count == 9, "matrix must contain nine row-major numbers.");
                            var a = (JArray)area["matrix"];
                            var m = new ProjectiveMatrix {
                                m00=TransformNumber(a[0]),m01=TransformNumber(a[1]),m02=TransformNumber(a[2]),
                                m10=TransformNumber(a[3]),m11=TransformNumber(a[4]),m12=TransformNumber(a[5]),
                                m20=TransformNumber(a[6]),m21=TransformNumber(a[7]),m22=TransformNumber(a[8]) };
                            Require(value.transformValue.TrySetMatrix(m), "Transform2D matrix must be invertible with no horizon crossing its rectangle.");
                            break;
                        }
                        foreach (string field in new[] { "position", "size" })
                        {
                            if (area[field] == null) continue;
                            Require(area[field] is JArray pair && pair.Count == 2, field + " must have two components.");
                            var v = TransformVector(area[field], field);
                            if (field == "position") value.transformValue.EditPosition(v, dimensions);
                            else
                            {
                                Require(Math.Abs(v.x) >= 0.00001 && Math.Abs(v.y) >= 0.00001, "Transform2D size cannot be zero.");
                                value.transformValue.EditSize(v, dimensions);
                            }
                        }
                        if (area["rotation"] != null) value.transformValue.EditRotation(TransformNumber(area["rotation"]), dimensions);
                        break;
                }
                result.Add(value);
            }
            return result;
        }

        private static JArray LiveFxSnapshot(Layer layer, TextureCompositor owner)
        {
            var result = new JArray();
            if (layer.modifiers == null) return result;
            for (int i = 0; i < layer.modifiers.Count; i++)
            {
                var modifier = layer.modifiers[i];
                var entry = new JObject { ["index"] = i, ["assetPath"] = modifier == null ? null : AssetDatabase.GetAssetPath(modifier) };
                if (modifier is ShaderFX fx)
                {
                    entry["type"] = "shaderFX"; entry["embedded"] = fx.EmbeddedOwner == owner;
                    entry["code"] = fx.Code; entry["diagnostics"] = fx.Diagnostics;
                    entry["pendingChanges"] = fx.HasPendingChanges;
                    entry["lastApplyFailed"] = fx.LastApplyFailed;
                    var parameters = new JArray();
                    foreach (var p in fx.Parameters)
                    {
                        if (p == null) continue;
                        JToken value = p.type == ShaderFXParameterType.Bool ? new JValue(p.BoolValue) :
                            p.type == ShaderFXParameterType.Curve ? new JValue(WhimTexCurveTexture.Format(p.curveValue)) :
                            p.type == ShaderFXParameterType.Gradient ? GradientSnapshot(p.gradientValue ?? new WhimTexGradient()) :
                            p.type == ShaderFXParameterType.Transform2D ? FxTransformSnapshot(p.transformValue) :
                            p.type == ShaderFXParameterType.Color ? (JToken)Json(p.colorValue) :
                            p.type == ShaderFXParameterType.Vector2 ? new JArray(p.vectorValue.x, p.vectorValue.y) :
                            p.type == ShaderFXParameterType.Point ? new JArray(p.vectorValue.x, p.vectorValue.y) :
                            p.type == ShaderFXParameterType.Vector3 || p.type == ShaderFXParameterType.Normal ? new JArray(p.vectorValue.x, p.vectorValue.y, p.vectorValue.z) :
                            p.type == ShaderFXParameterType.Vector ? new JArray(p.vectorValue.x, p.vectorValue.y, p.vectorValue.z, p.vectorValue.w) :
                            p.type == ShaderFXParameterType.Texture2D ? (p.textureSource == ShaderFXTextureSource.Layer
                                ? (JToken)new JObject { ["layer"] = p.textureLayerId }
                                : p.textureSource == ShaderFXTextureSource.Self ? new JValue("self")
                                : p.textureSource == ShaderFXTextureSource.None ? new JValue("none")
                                : new JValue(p.textureValue == null ? "" : AssetDatabase.GetAssetPath(p.textureValue))) : new JValue(p.floatValue);
                        parameters.Add(new JObject { ["name"] = p.name, ["id"] = p.id, ["type"] = p.type.ToString(), ["value"] = value,
                            ["minimum"] = p.hasMinimum ? (JToken)new JValue(p.minimum) : JValue.CreateNull(),
                            ["maximum"] = p.hasMaximum ? (JToken)new JValue(p.maximum) : JValue.CreateNull() });
                    }
                    entry["parameters"] = parameters;
                    entry["catalogPath"] = fx.CatalogPath;
                }
                else entry["type"] = modifier is Material ? "material" : "empty";
                result.Add(entry);
            }
            return result;
        }

        private static void ApplyLiveEditSettings(TextureCompositor document, Layer layer, JObject changes)
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
        }

        private static JObject ApplyLiveEdit(LiveJob job, JObject request, bool trial)
        {
            if (trial) Keys(request, "apiVersion", "op", "jobId", "changes", "view", "maxSize", "outputPath");
            else Keys(request, "apiVersion", "op", "jobId", "changes");
            Layer target = job.document.FindLayer(job.layerId);
            Require(target != null && IsLayerContentLocked(job.document, target), "Edit lock is no longer active.", "job_closed");
            Require(LiveLayerRevision(target) == job.targetRevision, "Target changed. Unlock and inspect before starting another edit.", "revision_conflict");
            JObject changes = Obj(request["changes"], "changes");
            var created = new List<ShaderFX>();
            TextureCompositor probe = null;
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
                    Require(!string.Equals(AssetDatabase.GetAssetPath(file.sourceTexture), AssetDatabase.GetAssetPath(job.document), StringComparison.OrdinalIgnoreCase),
                        "A document cannot sample its own output.", "invalid_target");
                if (trial)
                {
                    RequireGraphics();
                    string view = Text(request, "view", "composite");
                    Require(view == "composite" || view == "layer", "view must be composite or layer.");
                    int size = Int(request, "maxSize", 1024, 1, 4096);
                    if (view == "composite") image = probe.ComposePreview(size);
                    else
                    {
                        rt = probe.RenderAgentLayerPreview(candidate, size);
                        Require(rt != null, "No preview generated.", "render_failed");
                        image = HdrUtility.ReadLinear(rt);
                    }
                    var result = LiveStatus(job);
                    result["outputPath"] = WriteLivePng(image, Text(request, "outputPath", "Temp/WhimTex/Agent/edit-" + Guid.NewGuid().ToString("N") + ".png"));
                    result["applied"] = false; result["fx"] = LiveFxSnapshot(candidate, job.document);
                    return result;
                }
                LiveChange(job.document, "Complete Agent Edit", () =>
                {
                    ApplyLiveEditSettings(job.document, target, changes);
                    if (target?.Behaviour is DrawingLayerBehaviour drawing) drawing.SetColorRange(target.colorRange);
                    target.modifiers = new List<Object>(candidate.modifiers);
                    foreach (var fx in created) job.document.AdoptAgentShaderFX(fx, "Complete Agent Edit");
                });
                committed = true;
                job.state = "completed"; job.completion = request.ToString(Formatting.None); job.error = null;
                NotifyLiveLockChanged(job.document);
                var completed = LiveStatus(job);
                completed["fx"] = LiveFxSnapshot(target, job.document);
                return completed;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (image != null) Object.DestroyImmediate(image);
                if (probe != null) Object.DestroyImmediate(probe);
                if (!committed) foreach (var fx in created) if (fx != null && !AssetDatabase.Contains(fx)) Object.DestroyImmediate(fx);
            }
        }
    }
}
