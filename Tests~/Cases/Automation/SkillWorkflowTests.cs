// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// Unity Pipeline run_script, entry SkillWorkflowTests.Run.
// Behavioral traces for whimtex-live. Own unsaved window only; no user assets or Unity internal reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SkillWorkflowTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("SkillWorkflowTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
    static string Encode(object value) => (string)Json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    sealed class Node
    {
        readonly object token;
        internal Node(object token) { this.token = token; }
        internal Node this[object key] => new Node(token.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(token, new[] { key }));
        public override string ToString() => token?.ToString() ?? "";
        internal int Count => (int)token.GetType().GetProperty("Count").GetValue(token);
    }
    private static void ExecuteRun()
    {
        var jsonType = typeof(WhimTexApi).GetMethod("SetNoise", Any).GetParameters()[1].ParameterType;
        Node Parse(string json) => new Node(jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json }));
        int checks = 0;
        var report = new StringBuilder();
        void Check(bool ok, string message) { context.True(ok, message); }
        Node Success(string json) { var n = Parse(json); Check(n["success"].ToString() == "True", json); return n; }
        Node Failure(string json, string code) { var n = Parse(json); Check(n["success"].ToString() == "False" && n["errorCode"].ToString() == code, json); return n; }
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        var doc = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
        doc.width = doc.height = 32;
        string session = (string)typeof(TextureCompositorWindow).GetProperty("AgentSessionId", Any).GetValue(window);
        string scope = "\"sessionId\":" + Encode(session);
        Node Live(string fields) => Success(WhimTexApi.LiveJson("{\"apiVersion\":1," + fields + "}"));
        Node Inspect() => Live("\"op\":\"inspect\"," + scope);
        string Batch(string ops) => "{\"apiVersion\":1," + scope + ",\"expectedRevision\":" + Encode(Inspect()["document"]["revision"].ToString()) + ",\"operations\":[" + ops + "]}";
        string Fx(string layer, string edit) => "{\"op\":\"fx\",\"layer\":" + Encode(layer) + ",\"edits\":[" + edit + "]}";
        string Job(Node job) => "\"jobId\":" + Encode(job["jobId"].ToString());
        void Cancel(Node job) => Live("\"op\":\"cancel\"," + scope + ",\"layerId\":" + Encode(job["layerId"].ToString()));
        Color[] Pixels() { var t = doc.ComposeCanvas(); try { return t.GetPixels(); } finally { Object.DestroyImmediate(t); } }
        string Link(string layer) => (string)typeof(ShaderFX).GetProperty("CatalogPath", Any).GetValue(doc.layers.First(l => l.Id == layer).modifiers[0]);
        var files = new List<string>();
        try
        {
            var catalog = Success(WhimTexApi.FxCatalog("Color/Levels"));
            Check(catalog["presets"].Count == 1, "Levels resolved unambiguously");
            string preset = catalog["presets"][0]["id"].ToString(), presetPath = catalog["presets"][0]["path"].ToString();
            Success(WhimTexApi.FxCatalog(null, preset));
            string addPreset = "{\"op\":\"add\",\"presetId\":" + Encode(preset) + "}";
            string set = "{\"op\":\"set\",\"index\":0,\"parameters\":{\"_Gamma\":1.2}}";
            var added = Success(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"add\",\"type\":\"noise\",\"as\":\"noise\"}," + Fx("@noise", addPreset))));
            string layer = added["document"]["layers"][0]["id"].ToString();
            Check(Link(layer) == presetPath, "presetId keeps the source link");
            var before = Pixels();
            Success(WhimTexApi.AssistantExecuteJson(Batch(Fx(layer, set))));
            Check(Link(layer) == presetPath, "set keeps the source link");
            Check(before.Zip(Pixels(), (a,b) => Mathf.Abs(a.r-b.r)).Max() > .005f, "set changes rendered output");
            report.AppendLine("Existing linked FX: add/set succeeds, preserves catalog link, changes rendered pixels.");

            var locked = Success(WhimTexApi.LiveLock(Guid.NewGuid().ToString("N"), layer, session));
            string lockedBefore = Inspect()["document"]["revision"].ToString();
            Failure(WhimTexApi.AssistantExecuteJson(Batch(Fx(layer, set))), "layer_locked");
            Failure(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"preview\"," + Job(locked) + ",\"changes\":{\"fx\":[" + set + "]}}"), "invalid_request");
            Check(Inspect()["document"]["revision"].ToString() == lockedBefore && Link(layer) == presetPath, "rejected lock routes leave content/link unchanged");
            Live("\"op\":\"unlock\"," + Job(locked));
            report.AppendLine("Lock + shared set -> layer_locked; lock preview + set -> invalid_request; no content changes.");

            var displacement = Success(WhimTexApi.FxCatalog("Distortion/Displacement Map"));
            Check(displacement["presets"].Count == 1, "Displacement Map resolved unambiguously");
            string displacementId = displacement["presets"][0]["id"].ToString();
            string displacementPath = displacement["presets"][0]["path"].ToString();
            Success(WhimTexApi.FxCatalog(null, displacementId));
            string addDisplacement = "{\"op\":\"add\",\"presetId\":" + Encode(displacementId) + ",\"parameters\":{\"_StrengthX\":2,\"_StrengthY\":3}}";
            var job = Success(WhimTexApi.LiveBegin(Guid.NewGuid().ToString("N"), "Procedural", sessionId: session));
            string reserved = job["layerId"].ToString();
            string pendingBefore = Inspect()["document"]["revision"].ToString();
            Failure(WhimTexApi.AssistantExecuteJson(Batch(Fx(reserved, addDisplacement))), "layer_locked");
            string noise = "\"layer\":{\"type\":\"noise\",\"settings\":{\"noise\":{\"scale\":6,\"seed\":472}}}";
            Failure(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"complete\"," + Job(job) + ",\"layer\":{\"type\":\"noise\",\"fx\":[" + addDisplacement + "]}}"), "invalid_request");
            Check(Inspect()["document"]["revision"].ToString() == pendingBefore, "rejected preset completion leaves reservation unchanged");
            var preview = Live("\"op\":\"preview\"," + Job(job) + "," + noise + ",\"maxSize\":32");
            files.Add(preview["outputPath"].ToString());
            Check(File.Exists(files.Last()) && Inspect()["document"]["revision"].ToString() == pendingBefore, "noise preview renders without publishing");
            Live("\"op\":\"complete\"," + Job(job) + "," + noise);
            var baseNoise = Pixels();
            Success(WhimTexApi.AssistantExecuteJson(Batch(Fx(reserved, addDisplacement))));
            Check(Link(reserved) == displacementPath && doc.layers.First(l=>l.Id==reserved).Behaviour is NoiseLayerBehaviour, "complete then preset keeps procedural content and link");
            Check(baseNoise.Zip(Pixels(), (a,b) => Mathf.Abs(a.r-b.r)).Max() > .005f, "linked displacement changes rendered noise");
            report.AppendLine("Reservation + preset insertion is rejected. Procedural preview/complete, then fresh inspect + preset batch succeeds.");

            var selection = typeof(TextureCompositorWindow).GetProperty("AgentSelection", Any).GetValue(window);
            selection.GetType().GetMethod("All", Any).Invoke(selection, null);
            foreach (string mode in new[] {"strict", "guide"})
            {
                var selected = Success(WhimTexApi.LiveBegin(Guid.NewGuid().ToString("N"), "Selection", area:"selection", sessionId:session, selectionMode:mode));
                files.Add(selected["capture"]["maskPath"].ToString());
                string revision = Inspect()["document"]["revision"].ToString();
                Failure(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"complete\"," + Job(selected) + "," + noise + "}"), "invalid_request");
                Check(Inspect()["document"]["revision"].ToString() == revision, "selection parameter rejection preserves reservation");
                Cancel(selected);
            }
            selection.GetType().GetMethod("Clear", Any).Invoke(selection, null);
            report.AppendLine("Selection strict/guide + procedural layer completion -> invalid_request; no raster fallback is performed.");

            foreach (float gamma in new[] {.9f, 1.1f})
            {
                Success(WhimTexApi.AssistantExecuteJson(Batch(Fx(layer, "{\"op\":\"set\",\"index\":0,\"parameters\":{\"_Gamma\":" + Encode(gamma) + "}}"))));
                string revision = Inspect()["document"]["revision"].ToString();
                var rendered = Success(WhimTexApi.RenderProbeJson("{\"apiVersion\":1,\"assistantSessionId\":" + Encode(session) + ",\"layer\":" + Encode(layer) + ",\"stage\":\"afterFx\",\"index\":0}"));
                files.Add(rendered["outputPath"].ToString());
                Check(Link(layer) == presetPath && Inspect()["document"]["revision"].ToString() == revision, "render probe preserves linked live state");
            }
            report.AppendLine("Linked parameter iteration: fresh inspect + set + render probe succeeds; set is a live edit, not a detached trial.");
            return;
        }
        finally
        {
            window.DiscardChanges();
            Object.DestroyImmediate(window);
            Undo.ClearUndo(doc);
            if (doc != null) Object.DestroyImmediate(doc);
            string root = Path.GetFullPath("Temp/WhimTex/Agent") + Path.DirectorySeparatorChar;
            foreach (string file in files)
                if (!string.IsNullOrEmpty(file) && Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file)) File.Delete(file);
        }
    }
}

