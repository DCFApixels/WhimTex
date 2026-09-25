using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        public static string RenderProbeFile(string requestPath) => Respond(() => RenderProbe(Parse(ReadRequestFile(requestPath))));
        public static string RenderProbeJson(string json) => Respond(() => RenderProbe(Parse(json)));

        private static JObject RenderProbe(JObject request)
        {
            Keys(request, "apiVersion", "assetPath", "assistantSessionId", "headlessSessionId", "layer", "stage", "index", "channel", "maxSize", "outputPath");
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            int sources = (request["assetPath"] != null ? 1 : 0) + (request["assistantSessionId"] != null ? 1 : 0) + (request["headlessSessionId"] != null ? 1 : 0);
            Require(sources == 1, "Supply exactly one document source: assetPath, assistantSessionId or headlessSessionId.");
            string stage = Text(request, "stage", "composite"), channel = Text(request, "channel", "rgba");
            Require(stage == "composite" || stage == "layer" || stage == "beforeFx" || stage == "afterFx", "Unknown render stage.");
            Require(channel == "rgba" || channel == "r" || channel == "g" || channel == "b" || channel == "a", "channel must be rgba/r/g/b/a.");
            int maxSize = Int(request, "maxSize", 1024, 1, 4096);
            Require(stage != "composite" || request["layer"] == null && request["index"] == null, "Composite does not take a layer/index.");
            Require(stage != "layer" || request["index"] == null, "Layer stage does not take an FX index.");
            RequireGraphics();
            WhimTexDocumentBuild build;
            if (request["assetPath"] != null)
                build = WhimTexDocumentBuild.Open(TiffPath(Text(request, "assetPath")));
            else
            {
                var source = request["assistantSessionId"] != null
                    ? ResolveLiveBeginWindow(Text(request, "assistantSessionId")).AgentDocument
                    : GetTiffLiveSession(Text(request, "headlessSessionId")).working.Document;
                Require(!TextureCompositorWindow.IsDocumentBusyForLiveApi(source), "Finish the current gesture first.", "document_busy");
                build = WhimTexDocumentBuild.Copy(source);
            }
            using (build)
            {
                Texture2D image = null;
                RenderTexture resized = null;
                var previous = RenderTexture.active;
                try
                {
                    var document = build.Document;
                    ValidateAgentBudget(document);
                    if (stage == "composite") image = build.Render();
                    else
                    {
                        var layer = document.FindLayer(Text(request, "layer"));
                        Require(layer != null, "Layer not found.", "layer_not_found");
                        int count = layer.modifiers.Count;
                        if (stage != "layer") count = RequiredFxIndex(layer, request) + (stage == "afterFx" ? 1 : 0);
                        image = document.RasterizeFXPrefix(layer, count);
                    }
                    var result = Success();
                    result["stage"] = stage; result["channel"] = channel;
                    result["sourceWidth"] = image.width; result["sourceHeight"] = image.height;
                    var pixels = image.GetPixels();
                    var min = new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    var max = new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    int nonFinite = 0;
                    foreach (var pixel in pixels)
                        for (int c = 0; c < 4; c++)
                            if (float.IsFinite(pixel[c])) { min[c] = Mathf.Min(min[c], pixel[c]); max[c] = Mathf.Max(max[c], pixel[c]); }
                            else nonFinite++;
                    result["nonFiniteComponents"] = nonFinite;
                    var minimum = new JArray(); var maximum = new JArray();
                    for (int c = 0; c < 4; c++) { minimum.Add(float.IsFinite(min[c]) ? (JToken)new JValue(min[c]) : JValue.CreateNull()); maximum.Add(float.IsFinite(max[c]) ? (JToken)new JValue(max[c]) : JValue.CreateNull()); }
                    result["linearMinimum"] = minimum; result["linearMaximum"] = maximum;
                    if (channel != "rgba")
                    {
                        int c = channel == "r" ? 0 : channel == "g" ? 1 : channel == "b" ? 2 : 3;
                        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(pixels[i][c], pixels[i][c], pixels[i][c], 1);
                        image.SetPixels(pixels); image.Apply(false, false);
                    }
                    if (Mathf.Max(image.width, image.height) > maxSize)
                    {
                        float scale = maxSize / (float)Mathf.Max(image.width, image.height);
                        resized = RenderTexture.GetTemporary(Mathf.Max(1, Mathf.RoundToInt(image.width * scale)), Mathf.Max(1, Mathf.RoundToInt(image.height * scale)), 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                        Graphics.Blit(image, resized);
                        Object.DestroyImmediate(image); image = HdrUtility.ReadLinear(resized);
                    }
                    result["width"] = image.width; result["height"] = image.height;
                    result["outputPath"] = WriteLivePng(image, Text(request, "outputPath", "Temp/WhimTex/Agent/probe-" + Guid.NewGuid().ToString("N") + ".png"));
                    result["applied"] = false;
                    return result;
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (resized != null) RenderTexture.ReleaseTemporary(resized);
                    if (image != null) Object.DestroyImmediate(image);
                }
            }
        }
    }
}
