using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

// Run with Unity Pipeline run_script; no scene or user document is edited.
public static class AgentResizeTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    // Resolve the public JSON library explicitly: some Editor packages embed another copy.
    sealed class Node
    {
        readonly object value;
        public Node(object value) { this.value = value; }
        public Node this[string key] => At(key);
        public Node this[int key] => At(key);
        Node At(object key) => new Node(value.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(value, new[] { key }));
        public override string ToString() => value?.ToString();
        public double Number => double.Parse(ToString(), System.Globalization.CultureInfo.InvariantCulture);
        public bool Bool => bool.Parse(ToString());
    }
    private static string BodyRun()
    {
        var parser = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.Linq.JObject").GetMethod("Parse", new[] { typeof(string) });
        Node Parse(string text) => new Node(parser.Invoke(null, new object[] { text }));
        int checks = 0;
        void Check(bool condition, string message) { T.True(condition, message); }
        Node Read(string text) { var n = Parse(text); Check(n["success"].Bool, text); return n; }
        string tag = "resize-test-" + Guid.NewGuid().ToString("N");
        string path = Scope.Assets + "/Resize.tiff";
        string Head(string op, string extra = "") => "{\"apiVersion\":1,\"sessionId\":\"" + tag + "\",\"op\":\"" + op + "\"" + extra + "}";
        string baseline = "{\"op\":\"add\",\"type\":\"group\",\"as\":\"g\",\"transform\":{\"position\":[32,-16],\"rotation\":15}}," +
            "{\"op\":\"add\",\"type\":\"color\",\"parent\":\"@g\",\"as\":\"c\",\"transform\":{\"position\":[8,4],\"scale\":[0.5,0.5]}}," +
            "{\"op\":\"fx\",\"layer\":\"@c\",\"edits\":[{\"op\":\"add\",\"code\":\"// @param float _Gain = 1 [0 .. 2]\\nfloat4 ApplyFX(float2 uv, float4 color) { return color * _Gain; }\"}]}";
        Node Trial(string ops) => Read(WhimTexApi.TiffLiveJson(Head("preview", ",\"operations\":[" + ops + "]")));
        bool active = false;
        try
        {
            Read(WhimTexApi.TiffLiveJson(Head("begin", ",\"assetPath\":\"" + path + "\",\"create\":true,\"width\":128,\"height\":64")));
            active = true;
            var empty = Trial("{\"op\":\"resize\",\"width\":64,\"height\":32}");
            Check(empty["document"]["width"].Number == 64, "Empty document resize");
            Check(empty["operations"][0]["layerId"].ToString() == "", "Document operation has no layer id");
            var trial = Trial(baseline + ",{\"op\":\"resize\",\"width\":64,\"height\":32}");
            Check(trial["document"]["layers"][0]["transform"]["position"][0].Number == 16, "Group position scales");
            Check(trial["document"]["layers"][1]["transform"]["position"][1].Number == 2, "Child position scales once");
            Check(trial["document"]["layers"][0]["transform"]["rotation"].Number == 15, "Proportional resize keeps TRS");
            var status = Read(WhimTexApi.TiffLiveJson(Head("status")));
            Check(status["width"].Number == 64 && status["height"].Number == 32, "Headless status uses candidate dimensions");
            var keep = Trial("");
            Check(keep["document"]["width"].Number == 64, "Empty replay retains candidate");
            foreach (string bad in new[] {
                "{\"op\":\"resize\",\"width\":0,\"height\":32}",
                "{\"op\":\"resize\",\"width\":64.5,\"height\":32}",
                "{\"op\":\"resize\",\"width\":64}",
                "{\"op\":\"resize\",\"width\":16384,\"height\":16384}",
                "{\"op\":\"resize\",\"width\":64,\"height\":32,\"layer\":\"bad\"}" })
                Check(!Parse(WhimTexApi.TiffLiveJson(Head("preview", ",\"operations\":[" + bad + "]")))["success"].Bool, "Reject invalid resize: " + bad);
            Check(Read(WhimTexApi.TiffLiveJson(Head("status")))["width"].Number == 64, "Rejected replay keeps working model");
            var unchanged = Trial(baseline + ",{\"op\":\"resize\",\"width\":64,\"height\":32,\"preserveLayout\":false}");
            Check(unchanged["document"]["layers"][0]["transform"]["position"][0].Number == 32, "preserveLayout=false retains pixel offsets");
            var stretched = Trial(baseline + ",{\"op\":\"resize\",\"width\":64,\"height\":64}");
            Check(stretched["document"]["layers"][0]["transform"]["matrix"][8].Number == 1, "Non-proportional resize retains normalized matrix");
            trial = Trial(baseline + ",{\"op\":\"resize\",\"width\":64,\"height\":32}");
            string group = trial["document"]["layers"][0]["id"].ToString();
            Read(WhimTexApi.TiffLiveJson(Head("complete"))); active = false;
            var saved = Read(WhimTexApi.Inspect(path));
            Check(saved["document"]["width"].Number == 64 && saved["document"]["layers"][0]["id"].ToString() == group, "Save/reopen retains dimensions and IDs");
            Read(WhimTexApi.TiffLiveJson(Head("begin", ",\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + saved["document"]["revision"] + "\""))); active = true;
            Read(WhimTexApi.TiffLiveJson(Head("cancel"))); active = false;
            string request = "{\"apiVersion\":1,\"assetPath\":\"" + path + "\",\"expectedRevision\":\"" + saved["document"]["revision"] + "\",\"operations\":[{\"op\":\"resize\",\"width\":32,\"height\":16}]";
            Read(WhimTexApi.ExecuteJson(request + ",\"dryRun\":true}"));
            Check(Read(WhimTexApi.Inspect(path))["document"]["width"].Number == 64, "Dry run doesn't save");
            Read(WhimTexApi.ExecuteJson(request + ",\"save\":false}"));
            Check(Read(WhimTexApi.Inspect(path))["document"]["width"].Number == 64, "save=false doesn't save");
            Read(WhimTexApi.ExecuteJson(request + "}"));
            Check(Read(WhimTexApi.Inspect(path))["document"]["width"].Number == 32, "Batch saves resize");
            Check(!Parse(WhimTexApi.ExecuteJson(request + "}"))["success"].Bool, "Stale revision rejected");
            // Test only our own transient WhimTex window. No Unity internal API is accessed.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var window = Scope.OwnWindow(ScriptableObject.CreateInstance<TextureCompositorWindow>());
            var field = typeof(TextureCompositorWindow).GetField("compositor", flags);
            var document = Scope.OwnObject((TextureCompositor)field.GetValue(window));
            string sid = (string)typeof(TextureCompositorWindow).GetProperty("AgentSessionId", flags).GetValue(window);
            Node InspectLive() => Read(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"inspect\",\"sessionId\":\"" + sid + "\"}"));
            Node Edit(string ops, bool dry = false) => Read(WhimTexApi.AssistantExecuteJson("{\"apiVersion\":1,\"sessionId\":\"" + sid + "\",\"expectedRevision\":\"" + InspectLive()["document"]["revision"] + "\",\"dryRun\":" + (dry ? "true" : "false") + ",\"operations\":[" + ops + "]}"));
            Exception bodyFailure = null;
            try
            {
                Edit("{\"op\":\"resize\",\"width\":128,\"height\":64}," + baseline);
                Edit("{\"op\":\"resize\",\"width\":64,\"height\":32}", true);
                Check(document.width == 128, "Assistant dry run leaves canvas unchanged");
                Edit("{\"op\":\"resize\",\"width\":64,\"height\":32}");
                Check(document.width == 64 && document.layers[0].transform.position.x == 16, "Assistant resize");
                Undo.PerformUndo();
                Check(document.width == 128 && document.layers[0].transform.position.x == 32, "One Undo restores dimensions and positions");
                Undo.PerformRedo();
                Check(document.width == 64 && document.layers[0].transform.position.x == 16, "Redo restores resize");
                var layer = document.layers[0].children[0];
                var effect = (ShaderFX)layer.fx[0];
                var parameters = (System.Collections.Generic.List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(effect);
                string revision = InspectLive()["document"]["revision"].ToString();
                parameters[0].id = Guid.NewGuid().ToString("N");
                typeof(ShaderFX).GetField("diagnostics", flags).SetValue(effect, "Diagnostic cache rebuilt");
                Check(InspectLive()["document"]["revision"].ToString() == revision, "UI parameter identity and diagnostics do not change document revision");
                Edit("{\"op\":\"fx\",\"layer\":\"" + layer.Id + "\",\"edits\":[{\"op\":\"set\",\"index\":0,\"parameters\":{\"_Gain\":0.5}}]}");
                Check(InspectLive()["document"]["revision"].ToString() != revision, "Editable FX parameter change still invalidates document revision");
            }
            catch (Exception error) { bodyFailure = error; throw; }
            finally
            {
                WhimTex.Tests.UnityA.UnityAScope.RunCleanup(bodyFailure,
                    () => { if (window != null) field.SetValue(window, null); },
                    () => { if (document != null) Undo.ClearUndo(document); },
                    () => WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window),
                    () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); });
            }
            return checks + " resize checks passed (Headless + Batch + Assistant Undo/Redo); temporary TIFF removed.";
        }
        finally
        {
            if (active) WhimTexApi.TiffLiveJson(Head("cancel"));
            if (File.Exists(path)) { /* Asset deletion is owned by UnityAScope. */ }
        }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
