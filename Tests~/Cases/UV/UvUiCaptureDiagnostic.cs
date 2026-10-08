// Test-only manual screenshot producer; human approved Unity ReadScreenPixel only on created test windows.
// No OS-native calls, no borrowed/user window enumeration, no automated visual verdict.
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
public static class UvUiCaptureDiagnostic
{
    static string Key(string runId)
    {
        if (!Guid.TryParseExact(runId, "D", out _)) throw new ArgumentException("Expected per-run GUID");
        return "WhimTex.Tests.UnityD.UvCapture." + runId;
    }
    public static string Start(string runId)
    {
        string key = Key(runId);
        if (AppDomain.CurrentDomain.GetData(key) != null)
            return TestContext.Result("failed", 0, "Duplicate owned capture run").ToJson();
        var state = new object[] { null };
        AppDomain.CurrentDomain.SetData(key, state);
        try { AsyncD.Start(runId, (context, token) => Execute(context, token, state)); }
        catch { AppDomain.CurrentDomain.SetData(key, null); throw; }
        return Poll(runId);
    }
    public static string Poll(string runId)
    {
        string json = AsyncD.Poll(runId);
        var result = JsonUtility.FromJson<TestResult>(json);
        if (result.status != "passed") return json;
        var state = AppDomain.CurrentDomain.GetData(Key(runId)) as object[];
        return state != null && state[0] is string report ? report :
            TestContext.Result("failed", result.checks, "Owned screenshot artifact missing").ToJson();
    }
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static async Task<string> Cleanup(string runId)
    {
        string key = Key(runId);
        try { return await AsyncD.Cleanup(runId); }
        finally { AppDomain.CurrentDomain.SetData(key, null); }
    }
    static async Task Execute(TestContext context, CancellationToken token, object[] state)
    {
        WhimTexWindow window = null;
        WhimTexDocument document = null;
        Mesh mesh = null;
        var previousFocus = EditorWindow.focusedWindow;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = typeof(WhimTexWindow);
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            window.name = "WhimTex UV " + Guid.NewGuid().ToString("N");
            document = (WhimTexDocument)type.GetField("activeDocument", flags).GetValue(window);
            document.width = 512; document.height = 512;
            document.layers.Add(new Layer(new ColorFillLayerBehaviour { color = new Color(.24f,.25f,.3f,1) }));
            mesh = new Mesh { name = "UV smoke mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]{new Vector3(.08f,.1f,0),new Vector3(.48f,.1f,0),new Vector3(.48f,.85f,0),new Vector3(.08f,.85f,0),
                new Vector3(.6f,.18f,0),new Vector3(.92f,.25f,0),new Vector3(.85f,.55f,0),new Vector3(.62f,.5f,0),
                new Vector3(.61f,.7f,0),new Vector3(.9f,.65f,0),new Vector3(.85f,.9f,0)};
            var uvs = new Vector2[mesh.vertexCount];
            for (int i=0;i<uvs.Length;i++) uvs[i] = new Vector2(mesh.vertices[i].x, mesh.vertices[i].y);
            mesh.uv = uvs; mesh.triangles = new[]{0,1,2,0,2,3,4,5,6,4,6,7,8,9,10};
            typeof(WhimTexDocument).GetField("uvReferenceMesh", flags).SetValue(document, mesh);
            type.GetField("uvEnabled", flags).SetValue(window, true);
            type.GetField("uvExpanded", flags).SetValue(window, true);
            type.GetField("postFxExpanded", flags).SetValue(window, false);
            type.GetField("brushesExpanded", flags).SetValue(window, false);
            type.GetField("canvasTool", flags).SetValue(window, Enum.Parse(type.GetNestedType("CanvasTool", flags), "UvIslandSelect"));
            type.GetField("marqueeShape", flags).SetValue(window, Enum.Parse(type.GetNestedType("MarqueeShape", flags), "UvIsland"));
            window.position = new Rect(180,150,1100,760);
            window.ShowUtility(); window.CreateGUI();
            type.GetMethod("RefreshUvReference", flags).Invoke(window, null);
            await AsyncD.Tick(window.rootVisualElement, token);
            await Task.Delay(200, token);
            token.ThrowIfCancellationRequested();
            context.True(window.rootVisualElement.panel != null, "Created UV fixture is attached before manual capture");
            byte[] png = CaptureCreatedWindow(window, context);
            state[0] = CaptureReport(context.Checks, png);
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (window != null) window.DiscardChanges(); },
                () => { if (window != null) window.Close(); },
                () => { if (window != null) UnityEngine.Object.DestroyImmediate(window); },
                () => { if (document != null) Undo.ClearUndo(document); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); },
                () => { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); },
                () => { if (previousFocus != null) previousFocus.Focus(); });
        }
    }
    // Private: only Execute's created fixture can reach this producer.
    static byte[] CaptureCreatedWindow(WhimTexWindow window, TestContext context)
    {
        var rect = window.position;
        int width = (int)rect.width, height = (int)rect.height;
        var texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
        try
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.InternalEditorUtility", true);
            var read = type.GetMethod("ReadScreenPixel", flags, null, new[]{typeof(Vector2),typeof(int),typeof(int)}, null);
            if (read == null) throw new MissingMethodException(type.FullName, "ReadScreenPixel");
            texture.SetPixels((Color[])read.Invoke(null, new object[]{rect.position,width,height}));
            texture.Apply();
            byte[] png = ImageConversion.EncodeToPNG(texture);
            var decoded = new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                context.True(ImageConversion.LoadImage(decoded, png), "Created capture PNG decodes (artifact sanity only)");
                context.True(decoded.width == width && decoded.height == height,
                    "Created capture PNG dimensions equal the owned window rect (not visual equivalence)");
            }
            finally { AsyncD.CleanupOwned(() => UnityEngine.Object.DestroyImmediate(decoded)); }
            var fixture = new MigrationD();
            using (AsyncD.OwnCleanup(fixture))
                File.WriteAllBytes(Path.Combine(fixture.TempFolder(), "WhimTexUvPreview.png"), png);
            return png;
        }
        finally { AsyncD.CleanupOwned(() => UnityEngine.Object.DestroyImmediate(texture)); }
    }
    static string CaptureReport(int checks, byte[] png)
    {
        // Public token API preserves nested artifacts in an ephemeral Pipeline assembly.
        var objectType = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true);
        var arrayType = Type.GetType("Newtonsoft.Json.Linq.JArray, Newtonsoft.Json", true);
        var valueType = Type.GetType("Newtonsoft.Json.Linq.JValue, Newtonsoft.Json", true);
        var item = objectType.GetProperty("Item", new[] { typeof(string) });
        object Text(string value) => valueType.GetConstructor(new[] { typeof(string) }).Invoke(new object[] { value });
        void Put(object target, string key, object value) => item.SetValue(target, value, new object[] { key });
        string diagnostic = TestContext.Result("skipped", checks,
            "Original owned UV screen-pixel producer ran; manual PNG review remains pending. No original image correctness assertions or green visual-equivalence verdict.").ToJson();
        object result = objectType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { diagnostic });
        object artifact = Activator.CreateInstance(objectType);
        Put(artifact, "name", Text("WhimTexUvPreview.png"));
        Put(artifact, "encoding", Text("base64"));
        Put(artifact, "content", Text(Convert.ToBase64String(png)));
        object artifacts = Activator.CreateInstance(arrayType);
        arrayType.GetMethod("Add", new[] { typeof(object) }).Invoke(artifacts, new[] { artifact });
        Put(result, "artifacts", artifacts);
        return result.ToString();
    }
}
