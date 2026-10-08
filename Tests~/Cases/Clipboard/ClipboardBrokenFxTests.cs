using System;
using System.Collections;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ClipboardBrokenFxTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Good = "// @param float _Amount = 1\n// @param texture2D _Map\nfloat4 ApplyFX(float2 uv,float4 color){return color;}";
    static int checks;
    static void Check(bool ok, string message) { T.True(ok, message); }
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, F).Invoke(obj, args);
    static object Get(object obj, string name) => obj.GetType().GetField(name, F).GetValue(obj);
    static bool Unavailable(ShaderFX fx) => (bool)typeof(ShaderFX).GetProperty("IsUnavailable", F).GetValue(fx);
    static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static IDisposable Read(string json) => (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", F).Invoke(null, new object[] { json, 16, 16 });
    static WhimTexDocument Doc(object data) => (WhimTexDocument)Get(data, "Document");
    static string Fixture(string code, bool active) =>
        "{\"format\":\"whimtex.document\",\"version\":2,\"document\":{\"width\":16,\"height\":16},\"layers\":[" +
        "{\"id\":\"group\",\"group\":true,\"behaviour\":{\"$type\":\"GroupLayerBehaviour\"},\"children\":[" +
        "{\"id\":\"color\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\",\"storedColor\":[0.2,0.4,0.6,1]},\"fx\":[" +
        "{\"$type\":\"ShaderFX\",\"$name\":\"Clipboard failure fixture\",\"active\":" + (active ? "true" : "false") + ",\"code\":" + Quote(code) +
        ",\"parameters\":[{\"name\":\"_Amount\",\"type\":\"Float\",\"floatValue\":0.37},{\"name\":\"_Map\",\"type\":\"Texture2D\",\"textureSource\":\"Layer\",\"textureLayerId\":\"source\"}]}," +
        "{\"$type\":\"ShaderFX\",\"code\":\"float4 ApplyFX(float2 uv,float4 color){return float4(color.rgb * 0.5,color.a);}\"}]}]}," +
        "{\"id\":\"source\",\"enabled\":false,\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}}]}";
    static Color Pixel(WhimTexDocument doc)
    {
        Texture2D texture = doc.ComposeCanvas();
        try { return texture.GetPixel(8, 8); }
        finally { Object.DestroyImmediate(texture); }
    }
    static void Same(Color a, Color b) => Check((a - b).maxColorComponent < .0001f && (b - a).maxColorComponent < .0001f, "Broken FX changed pixels.");
    private static string BodyRun()
    {
        checks = 0;
        string[] broken = { Good.Replace("return color;", "return missingClipboardFunction(color);"),
            Good.Replace("float _Amount = 1", "float _Amount = invalid"), "#include \"Assets/MissingClipboardFxFixture.hlsl\"\n" + Good };
        foreach (string code in broken)
        foreach (bool active in new[] { true, false })
        {
            using var data = Read(Fixture(code, active));
            Call(data, "Compile");
            var source = Doc(data);
            var layer = source.layers[0].children[0];
            var fx = (ShaderFX)layer.fx[0];
            Check(Unavailable(fx) && fx.Active == active, "Failure changed enabled state or was not marked.");
            Check(((IList)Get(data, "Warnings")).Count == 1, "Compilation warning missing.");
            Call(data, "Compile");
            Check(((IList)Get(data, "Warnings")).Count == 1, "Repeated compilation duplicated warning.");
            Check(!Unavailable((ShaderFX)layer.fx[1]), "Healthy later FX did not compile.");
            var actual = Pixel(source);
            layer.fx.RemoveAt(0); Same(actual, Pixel(source)); layer.fx.Insert(0, fx);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string json = WhimTexDocumentJson.Write(source, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var opened = WhimTexDocumentJson.Read(json);
                Check(opened.Warnings.Count == 1, "Open/paste disagree about broken FX.");
                Same(actual, Pixel(opened.Document));
                var destination = Scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
                destination.width = destination.height = 16;
                try
                {
                    using var clipboard = Read(json);
                    Call(clipboard, "Compile");
                    Undo.IncrementCurrentGroup();
                    Call(destination, "PasteLayers", Doc(clipboard));
                    var copyLayer = destination.layers[0].children[0];
                    var copyFx = (ShaderFX)copyLayer.fx[0];
                    Check(copyFx != fx && Unavailable(copyFx) && copyFx.Active == active, "Paste lost failed FX state/ownership.");
                    Check(copyLayer.Id != layer.Id && destination.layers[1].Id != source.layers[1].Id, "Layer IDs were not remapped.");
                    var parameters = (IList)Get(copyFx, "parameters");
                    Check((string)Get(parameters[1], "textureLayerId") == destination.layers[1].Id, "Failed FX texture target not remapped.");
                    Check((float)Get(parameters[0], "floatValue") == .37f, "Failed FX parameter value lost.");
                    Same(actual, Pixel(destination));
                    Undo.PerformUndo(); Check(destination.layers.Count == 0, "Paste Undo failed.");
                    Undo.PerformRedo(); Check(destination.layers.Count == 2, "Paste Redo failed.");
                    Same(actual, Pixel(destination));
                    var restoredFx = (ShaderFX)destination.layers[0].children[0].fx[0];
                    Check(Unavailable(restoredFx), "Redo lost warning state.");
                    Check(WhimTexDocumentJson.Write(destination).Json.Contains("0.37"), "Resave lost broken FX values.");
                    Call(restoredFx, "SetDraftCode", Good);
                    Check((bool)Call(restoredFx, "Apply") && !Unavailable(restoredFx), "Repair did not clear warning.");
                }
                finally { Undo.ClearUndo(destination); Object.DestroyImmediate(destination); }
            }
        }
        foreach (string json in new[] { Fixture(broken[0], true).Replace("\"textureLayerId\":\"source\"", "\"textureLayerId\":\"missing\""),
            Fixture(broken[0], true).Replace("\"id\":\"source\"", "\"id\":\"color\"") })
        {
            bool rejected = false;
            try { using var data = Read(json); Call(data, "Compile"); }
            catch (TargetInvocationException) { rejected = true; }
            Check(rejected, "Structural failure was softened.");
        }
        return null;
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}

