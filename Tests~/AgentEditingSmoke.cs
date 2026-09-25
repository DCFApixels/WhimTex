using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class AgentEditingSmoke
{
    public sealed class Node
    {
        readonly object token;
        public Node(object value) { token = value; }
        Node At(object key) => new Node(token.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(token, new[] { key }));
        public Node this[int index] => At(index);
        public Node this[string key] => At(key);
        public int Count => (int)token.GetType().GetProperty("Count").GetValue(token);
        public Node presets => At("presets"); public Node document => At("document"); public Node layers => At("layers");
        public Node fx => At("fx"); public Node parameters => At("parameters"); public Node linearMaximum => At("linearMaximum");
        public Node effect => At("effect");
        public string path => At("path").ToString(); public string error => At("error").ToString();
        public string jobId => At("jobId").ToString(); public string errorCode => At("errorCode").ToString();
        public string revision => At("revision").ToString(); public string id => At("id").ToString();
        public string type => At("type").ToString(); public string code => At("code").ToString();
        public bool success => bool.Parse(At("success").ToString()); public bool enabled => bool.Parse(At("enabled").ToString());
        public float value => (float)At("value");
        public static explicit operator float(Node value) => float.Parse(value.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        public override string ToString() => token?.ToString();
    }
    [Serializable] public class Quoted { public string value; }
    public static string Q(string value) { string s = JsonUtility.ToJson(new Quoted { value = value }); return s.Substring(9, s.Length - 10); }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static string Main()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var jsonType = typeof(WhimTexApi).GetMethod("SetNoise", Flags).GetParameters()[1].ParameterType;
        Node Parse(string json) => new Node(jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json }));
        Node Read(string json) { var r = Parse(json); Check(r.success, json); return r; }
        string id = "agent-edit-" + Guid.NewGuid().ToString("N");
        string folder = "Assets/" + id;
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        var document = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Flags).GetValue(window);
        document.width = document.height = 48;
        string session = (string)typeof(TextureCompositorWindow).GetProperty("AgentSessionId", Flags).GetValue(window);
        Node Inspect() => Read(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"inspect\",\"sessionId\":" + Q(session) + "}"));
        string Batch(string operations, bool dry = false) => "{\"apiVersion\":1,\"sessionId\":" + Q(session) + ",\"expectedRevision\":" + Q((string)Inspect().document.revision) + ",\"dryRun\":" + (dry ? "true" : "false") + ",\"operations\":[" + operations + "]}";
        string FxEdit(string layer, string edits) => "{\"op\":\"fx\",\"layer\":" + Q(layer) + ",\"edits\":[" + edits + "]}";
        string code = "// @param float _Amount = 1 [0 .. 1]\nfloat4 ApplyFX(float2 uv,float4 color){return float4(color.rgb * _Amount,color.a);}";
        string add = "{\"op\":\"add\",\"type\":\"color\",\"as\":\"fill\",\"settings\":{\"color\":[1,0.5,0.25,1]}}";
        string addFx = FxEdit("@fill", "{\"op\":\"add\",\"code\":" + Q(code) + "}");
        Color[] Pixels() { var image = document.Compose(); try { return image.GetPixels(); } finally { UnityEngine.Object.DestroyImmediate(image); } }
        float Difference(Color[] a, Color[] b) { float total = 0; for (int i = 0; i < a.Length; i++) total += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].a - b[i].a); return total; }
        try
        {
            var catalog = Read(WhimTexApi.FxCatalog("Color/Negative"));
            Check((int)catalog.presets.Count == 1, "Catalog filter");
            Read(WhimTexApi.FxCatalog(null, (string)catalog.presets[0].id));
            var installed = Read(WhimTexApi.FxCatalog());
            int builtins = 0;
            for (int i = 0; i < installed.presets.Count; i++)
            {
                var preset = installed.presets[i];
                if (!preset.path.StartsWith("Packages/com.dcfapixels.whimtex/src/FXPresets/", StringComparison.Ordinal)) continue;
                var detail = Read(WhimTexApi.FxCatalog(null, preset.id)).presets[0];
                Check(string.IsNullOrEmpty(detail.error), "Built-in metadata: " + preset.path + " " + detail.error);
                Read(WhimTexApi.AssistantExecuteJson(Batch(add + "," + FxEdit("@fill", "{\"op\":\"add\",\"presetId\":" + Q(preset.id) + "}"), true)));
                builtins++;
            }
            Check(builtins > 20, "Built-in catalog was exercised");
            string dry = Batch(add + "," + addFx, true);
            Read(WhimTexApi.AssistantExecuteJson(dry));
            Check(document.layers.Count == 0, "Dry run does not edit live document");
            var created = Read(WhimTexApi.AssistantExecuteJson(Batch(add + "," + addFx)));
            string layer = (string)created.document.layers[0].id;
            var locked = Read(WhimTexApi.LiveLock(id + "-lock", layer, session));
            try
            {
                var rejected = Parse(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"delete\",\"layer\":" + Q(layer) + "}")));
                Check(!rejected.success && rejected.errorCode == "layer_locked", "Pending lock blocks structural batch");
                Check(document.layers.Count == 1, "Locked document unchanged");
            }
            finally { Read(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"unlock\",\"jobId\":" + Q(locked.jobId) + "}")); }
            string set = FxEdit(layer, "{\"op\":\"set\",\"index\":0,\"parameters\":{\"_Amount\":0.25}}");
            var changed = Read(WhimTexApi.AssistantExecuteJson(Batch(set)));
            Check((float)changed.document.layers[0].fx[0].parameters[0].value == .25f, "Parameter edited");
            Check((string)changed.document.layers[0].fx[0].code == code, "Code retained");
            Undo.PerformUndo();
            Check((float)Inspect().document.layers[0].fx[0].parameters[0].value == 1, "Undo restores FX values");
            Undo.PerformRedo();
            Check((float)Inspect().document.layers[0].fx[0].parameters[0].value == .25f, "Redo restores FX values");
            string probe = "{\"apiVersion\":1,\"assistantSessionId\":" + Q(session) + ",\"layer\":" + Q(layer) + ",\"stage\":\"afterFx\",\"index\":0}";
            var rendered = Read(WhimTexApi.RenderProbeJson(probe));
            Check(Math.Abs((float)rendered.linearMaximum[0] - .25f) < .003f, "FX probe reads exact stage");
            var before = Read(WhimTexApi.RenderProbeJson(probe.Replace("afterFx", "beforeFx")));
            Check(Math.Abs((float)before.linearMaximum[0] - 1) < .003f, "Input probe excludes FX");
            Read(WhimTexApi.AssistantExecuteJson(Batch(FxEdit(layer, "{\"op\":\"add\",\"presetId\":" + Q((string)catalog.presets[0].id) + "},{\"op\":\"move\",\"index\":1,\"toIndex\":0},{\"op\":\"set\",\"index\":0,\"enabled\":false}"))));
            Check(!(bool)Inspect().document.layers[0].fx[0].enabled, "Enable toggle");
            string invalid = Batch(FxEdit(layer, "{\"op\":\"set\",\"index\":1,\"parameters\":{\"_Amount\":3}}"));
            Check(!(bool)Parse(WhimTexApi.AssistantExecuteJson(invalid)).success, "Hard range rejected");
            Check(!(bool)Parse(WhimTexApi.AssistantExecuteJson(Batch(FxEdit(layer, "{\"op\":\"applyAll\"}")))).success, "Rasterize consent required");
            Read(WhimTexApi.AssistantExecuteJson(Batch(FxEdit(layer, "{\"op\":\"applyAll\",\"allowRasterize\":true}"))));
            Check(document.layers[0].Behaviour is DrawingLayerBehaviour && document.layers[0].modifiers.Count == 0, "ApplyAll bakes prefix");
            Undo.PerformUndo();
            Check(document.layers[0].Behaviour is ColorFillLayerBehaviour && document.layers[0].modifiers.Count == 2, "ApplyAll one-step Undo");
            Undo.PerformRedo();
            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"duplicate\",\"layer\":" + Q(layer) + ",\"as\":\"copy\"},{\"op\":\"delete\",\"layer\":\"@copy\"}")));
            Check(document.layers.Count == 1, "Duplicate/delete");
            string badCompile = Batch("{\"op\":\"duplicate\",\"layer\":" + Q(layer) + "}," + FxEdit(layer, "{\"op\":\"add\",\"code\":\"float4 ApplyFX(float2 uv,float4 color){return MISSING_VALUE;}\"}"));
            Check(!Parse(WhimTexApi.AssistantExecuteJson(badCompile)).success && document.layers.Count == 1, "Runtime compile error rolls back preceding duplicate");
            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"transform\",\"layer\":" + Q(layer) + ",\"transform\":{\"tiling\":\"Clip\"}},{\"op\":\"stroke\",\"layer\":" + Q(layer) + ",\"points\":[[24,24]],\"brush\":{\"size\":6,\"hardness\":1,\"color\":[0,0,0,1]}}")));
            var sharp = Pixels();
            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"blurStroke\",\"layer\":" + Q(layer) + ",\"points\":[[20,20],[24,24]],\"size\":8,\"source\":\"AllLayers\",\"tiled\":true}")));
            Check(Difference(sharp, Pixels()) > .1f, "Blur modifies actual pixels");
            Undo.PerformUndo(); Check(Difference(sharp, Pixels()) < .01f, "Blur Undo restores pixels");
            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"healStroke\",\"layer\":" + Q(layer) + ",\"points\":[[20,20],[24,24]],\"size\":6,\"quality\":\"Fast\",\"source\":\"CurrentAndBelow\",\"tiled\":true}")));
            Check(Difference(sharp, Pixels()) > .1f, "Healing modifies actual pixels");
            Undo.PerformUndo(); Check(Difference(sharp, Pixels()) < .01f, "Healing Undo restores pixels");
            string currentRequest = Batch("{\"op\":\"duplicate\",\"layer\":" + Q(layer) + "}");
            Read(WhimTexApi.AssistantExecuteJson(currentRequest));
            Check(!Parse(WhimTexApi.AssistantExecuteJson(currentRequest)).success, "Stale revision prevents replay");
            Undo.PerformUndo();
            // Same operation payload works on a detached TIFF live session and path-based dry run.
            string shared = add + "," + addFx + ",{\"op\":\"duplicate\",\"layer\":\"@fill\",\"as\":\"other\"}," + FxEdit("@other", "{\"op\":\"copy\",\"sourceLayer\":\"@fill\",\"sourceIndex\":0},{\"op\":\"remove\",\"index\":0}") + ",{\"op\":\"merge\",\"layer\":\"@fill\",\"others\":[\"@other\"]}";
            Read(WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":\"Assets/" + id + ".tiff\",\"create\":true,\"width\":48,\"height\":48,\"dryRun\":true,\"operations\":[" + shared + "]}"));
            Read(WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"begin\",\"sessionId\":" + Q(id) + ",\"assetPath\":\"Assets/" + id + ".tiff\",\"create\":true,\"width\":48,\"height\":48}"));
            var live = Read(WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"preview\",\"sessionId\":" + Q(id) + ",\"operations\":[" + shared + "]}"));
            Check((int)live.document.layers.Count == 1 && (string)live.document.layers[0].type == "drawing", "Headless merge");
            Read(WhimTexApi.RenderProbeJson("{\"apiVersion\":1,\"headlessSessionId\":" + Q(id) + ",\"channel\":\"a\"}"));
            Read(WhimTexApi.ExecuteJson("{\"apiVersion\":1,\"assetPath\":" + Q(folder + "/Roundtrip.tiff") + ",\"create\":true,\"width\":48,\"height\":48,\"operations\":[" + shared + "]}"));
            var saved = Read(WhimTexApi.Inspect(folder + "/Roundtrip.tiff"));
            Check(saved.document.layers.Count == 1 && saved.document.layers[0].type == "drawing", "Batch TIFF roundtrip");
            Read(WhimTexApi.RenderProbeJson("{\"apiVersion\":1,\"assetPath\":" + Q(folder + "/Roundtrip.tiff") + "}"));
            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"add\",\"type\":\"color\",\"as\":\"uv\",\"settings\":{\"fillMode\":\"UV\"}},{\"op\":\"convertToDrawing\",\"layer\":\"@uv\"}")));
            Check(document.layers[0].Behaviour is DrawingLayerBehaviour, "Convert to Drawing");
            var glitch = Read(WhimTexApi.FxCatalog("Digital Glitch"));
            Read(WhimTexApi.AssistantExecuteJson(Batch(FxEdit(document.layers[0].Id, "{\"op\":\"add\",\"presetId\":" + Q(glitch.presets[0].id) + "}"))));
            Check(Inspect().document.layers[0].fx[0].parameters.Count > 32, "Large built-in preset compiles and inserts");

            // Contract regressions: empty headless operations keep the candidate; malformed ones fail.
            var kept = Read(WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"preview\",\"sessionId\":" + Q(id) + ",\"operations\":[]}"));
            Check(kept.document.layers.Count == 1 && kept.document.layers[0].id == live.document.layers[0].id, "Empty headless preview keeps working model");
            foreach (string malformed in new[] { "null", "{}", "false", "\"bad\"" })
                foreach (string op in new[] { "preview", "complete" })
                    Check(!Parse(WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":" + Q(op) + ",\"sessionId\":" + Q(id) + ",\"operations\":" + malformed + "}")).success,
                        "Malformed headless operations rejected: " + op + " " + malformed);

            string savedPath = folder + "/Roundtrip.tiff";
            string diskRevision = Read(WhimTexApi.Inspect(savedPath)).document.revision;
            string pathBatch = "{\"apiVersion\":1,\"assetPath\":" + Q(savedPath) + ",\"expectedRevision\":" + Q(diskRevision);
            Read(WhimTexApi.ExecuteJson(pathBatch + ",\"save\":false,\"operations\":[" + add + "]}"));
            Check(Read(WhimTexApi.Inspect(savedPath)).document.revision == diskRevision, "save:false discards edits after returning");
            int documentsBefore = Resources.FindObjectsOfTypeAll<TextureCompositor>().Length;
            for (int attempt = 0; attempt < 3; attempt++)
                Check(!Parse(WhimTexApi.ExecuteJson(pathBatch.Replace(Q(diskRevision), Q("stale")) + ",\"operations\":[]}")).success, "Stale path revision rejected");
            Check(Resources.FindObjectsOfTypeAll<TextureCompositor>().Length == documentsBefore, "Rejected revisions release transient documents");

            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"add\",\"type\":\"group\",\"as\":\"group\",\"transform\":{\"position\":[3,7],\"rotation\":12}}")));
            var group = Inspect().document.layers[0];
            Check(group.type == "group" && (float)group["transform"]["position"][1] == 7 && (float)group["transform"]["rotation"] == 12,
                "Group local transform is included in inspect");

            // Imported resolution is not necessarily the File layer's original source resolution.
            string imagePath = folder + "/Source.png";
            var original = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(imagePath, original.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(original); }
            AssetDatabase.ImportAsset(imagePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
            importer.maxTextureSize = 32; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Read(WhimTexApi.AssistantExecuteJson(Batch("{\"op\":\"add\",\"type\":\"file\",\"settings\":{\"source\":" + Q(imagePath) + "}}")));
            var fileLayer = Inspect().document.layers[0];
            Check((float)fileLayer["sourceSize"][0] == 64 && (float)fileLayer["importedSourceSize"][0] == 32,
                "Inspect distinguishes original and imported resolution");

            // Bind only our test window to our test file; do not touch the user's open windows.
            typeof(TextureCompositorWindow).GetMethod("BindDocumentFile", Flags).Invoke(window, new object[] { savedPath });
            Check(Inspect().document["assetPath"].ToString() == savedPath, "Assistant inspect returns bound TIFF path");
            bool foundSession = false;
            var sessions = Read(WhimTexApi.LiveSessions())["sessions"];
            for (int i = 0; i < sessions.Count; i++)
                if (sessions[i]["sessionId"].ToString() == session)
                    foundSession = sessions[i]["assetPath"].ToString() == savedPath;
            Check(foundSession, "Assistant discovery returns bound TIFF path");
            var emptyBatch = Read(WhimTexApi.AssistantExecuteJson(Batch("")));
            Check(emptyBatch.document["assetPath"].ToString() == savedPath, "Assistant batch returns bound TIFF path");
            string ownSource = "{\"op\":\"add\",\"type\":\"file\",\"settings\":{\"source\":" + Q(savedPath) + "}}";
            var ownFile = Parse(WhimTexApi.AssistantExecuteJson(Batch(ownSource, true)));
            Check(!ownFile.success && ownFile.errorCode == "invalid_target", "Assistant dry-run rejects its own TIFF as File source");
            string ownFx = FxEdit(document.layers[0].Id, "{\"op\":\"add\",\"code\":" + Q("// @param texture2D _Source = none\nfloat4 ApplyFX(float2 uv,float4 color){return color;}") + ",\"parameters\":{\"_Source\":" + Q(savedPath) + "}}");
            var ownEffect = Parse(WhimTexApi.AssistantExecuteJson(Batch(ownFx, true)));
            Check(!ownEffect.success && ownEffect.errorCode == "invalid_target", "Assistant dry-run rejects its own TIFF in FX parameters");
            var reservation = Read(WhimTexApi.LiveBegin(id + "-path", sessionId: session));
            try { Check(reservation["context"]["assetPath"].ToString() == savedPath, "Reservation captures bound TIFF path"); }
            finally { Read(WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"cancel\",\"sessionId\":" + Q(session) + ",\"layerId\":" + Q(reservation["layerId"].ToString()) + "}")); }
            var failedSave = Parse(WhimTexApi.ExecuteJson(pathBatch + ",\"operations\":[" + add + "]}"));
            Check(!failedSave.success && bool.Parse(failedSave["saveMayBePartial"].ToString()), "Save to another open document fails without replacing it");
            Check(failedSave["recovery"].ToString().Contains("An empty batch cannot recover lost edits"), "Save failure does not promise recovery through empty batch");
            Check(Read(WhimTexApi.Inspect(savedPath)).document.revision == diskRevision, "Failed save leaves original TIFF unchanged");
            return "PASS AgentEditingSmoke: " + checks + " checks; FX edits/catalog, probes, rasterize consent, Undo/Redo, structure, repair and shared backends.";
        }
        finally
        {
            WhimTexApi.TiffLiveJson("{\"apiVersion\":1,\"op\":\"cancel\",\"sessionId\":" + Q(id) + "}");
            typeof(TextureCompositorWindow).GetField("compositor", Flags).SetValue(window, null);
            Undo.ClearUndo(document);
            UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(document);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }
    }
}
