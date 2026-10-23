// Native Undo/Redo on owned utility windows/documents. No saved assets or user document edits.
// Each edit/Undo/Redo cycle runs synchronously after UI setup, so user input cannot interleave.
// Public UI events and package-private reflection only; no Unity-internal reflection.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public static class TextOutlineUndoTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Package = typeof(WhimTexDocument).Assembly;
    static readonly Type ObjectID = Package.GetType("DCFApixels.WhimTex.UnityObjectID", true);
    public static string Start(string id) => AsyncD.Start(id, Run);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static object Call(object owner, string name, params object[] args)
    {
        var type = owner as Type ?? owner.GetType();
        if (type.Assembly != Package) throw new InvalidOperationException("Reflection outside package");
        var method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
        try { return method.Invoke(owner is Type ? null : owner, args); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    static object Read(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
    static void Write(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static object Id(Object value) => Call(ObjectID, "FromObject", value);
    static void Click(Button button)
    {
        if (button == null) throw new InvalidOperationException("Missing conversion button");
        using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
    }
    static string FontName()
    {
        var names = Font.GetOSInstalledFontNames();
        foreach (string name in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica" })
            if (names.Any(n => n == name || n.StartsWith(name + " - ", StringComparison.OrdinalIgnoreCase))) return name;
        throw new InvalidOperationException("A static outline font is required");
    }
    static Color[] Pixels(WhimTexDocument doc)
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var image = doc.ComposeCanvas();
        try { return image.GetPixels(); }
        finally { Object.DestroyImmediate(image); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }
    static double Difference(Color[] a, Color[] b)
    {
        double max = 0;
        for (int i = 0; i < a.Length; i++)
            for (int c = 0; c < 4; c++) max = Math.Max(max, Math.Abs(a[i][c] - b[i][c]));
        return max;
    }
    sealed class Snapshot
    {
        internal readonly string model;
        internal readonly TextureTransform transform;
        internal readonly Color[] pixels;
        internal Snapshot(Fixture f)
        {
            pixels = Pixels(f.doc);
            model = JsonUtility.ToJson(f.Layer.Behaviour);
            transform = f.Layer.transform;
        }
        internal void Check(Fixture f, string label)
        {
            f.t.Equal(model, JsonUtility.ToJson(f.Layer.Behaviour), label + ": full layout/curves restored");
            f.t.Equal(transform, f.Layer.transform, label + ": transform and tiling restored");
            f.Shell(label);
            f.t.Near(0, Difference(pixels, Pixels(f.doc)), .00001, label + ": RGBA render restored");
        }
    }
    sealed class Fixture : IDisposable
    {
        internal readonly TestContext t;
        internal readonly WhimTexDocument doc;
        internal readonly WhimTexWindow window;
        internal readonly string layerId;
        readonly List<object> effectIds = new List<object>();
        readonly List<string> effectData = new List<string>();
        readonly List<string> effectOwners = new List<string>();
        readonly List<TextureTransform> groupTransforms = new List<TextureTransform>();
        readonly List<string> groupIds = new List<string>();
        readonly List<string> groupData = new List<string>();
        readonly List<Action> prefs = new List<Action>();
        readonly UnityEngine.TextCore.FaceInfo face = FontEngine.GetFaceInfo();
        internal Layer Layer => (Layer)Call(doc, "FindLayer", layerId);
        internal TextLayerBehaviour Text => (TextLayerBehaviour)Layer.Behaviour;
        internal VisualElement Root => window.rootVisualElement;

        internal Fixture(TestContext t, TextLayoutMode mode, TextOverflowMode overflow, bool grouped, VectorBooleanOperation operation)
        {
            this.t = t;
            try
            {
            foreach (string key in new[] { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool",
                "DCFApixels.WhimTex.Canvas.PaintToolSettings" })
            {
                bool had = EditorPrefs.HasKey(key); string value = EditorPrefs.GetString(key);
                prefs.Add(() => { if (had) EditorPrefs.SetString(key, value); else EditorPrefs.DeleteKey(key); });
            }
            const string scale = "DCFApixels.WhimTex.Canvas.PaintingScale";
            bool hadScale = EditorPrefs.HasKey(scale); float savedScale = EditorPrefs.GetFloat(scale);
            prefs.Add(() => { if (hadScale) EditorPrefs.SetFloat(scale, savedScale); else EditorPrefs.DeleteKey(scale); });
            doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave; doc.name = "Text outline Undo " + Guid.NewGuid().ToString("N");
            doc.width = 256; doc.height = 160; doc.outputPrecision = WhimTexOutputPrecision.Float32;
            var text = new TextLayerBehaviour { text = "O B é\nText 123", fontFamily = FontName(), fontSize = 32,
                layoutMode = mode, overflow = overflow, wrapping = TextWrapping.Words,
                frameSize = new Vector2(132, 66), layoutOffset = new Vector2(7.25f, -3.5f),
                alignment = TextAnchor.MiddleCenter, casing = TextCasing.SmallCaps, characterHorizontalScale = 1.15f,
                spacing = new TextSpacing { character = .03f, word = .07f, line = .1f, paragraph = .08f },
                color = new Color(.3f, .6f, .9f, .8f) };
            var layer = new Layer(text) { layerName = "Retain editable Text", opacity = .73f, blendMode = BlendMode.Normal };
            layer.transform.positionF = new Vector2(9.25f, -2.5f);
            layer.transform.scaleF = new Vector2(-.92f, 1.08f); layer.transform.rotation = -13;
            layer.transform.tiling = TransformTilingMode.Clip;
            if (grouped)
            {
                var outer = new Layer(new BooleanGroupLayerBehaviour { referenceSize = new Vector2(256, 160) });
                outer.transform.positionF = new Vector2(3, 2); outer.transform.rotation = 11;
                var inner = new Layer(new BooleanGroupLayerBehaviour { referenceSize = new Vector2(256, 160) });
                inner.transform.scaleF = new Vector2(.9f, .85f); inner.transform.rotation = 7;
                layer.booleanOperation = operation;
                inner.children.Add(layer);
                if (operation == VectorBooleanOperation.Subtract)
                    inner.children.Add(new Layer(new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Rectangle }));
                outer.children.Add(inner); doc.layers.Add(outer);
            }
            else doc.layers.Add(layer);
            Call(doc, "NormalizeModel"); layerId = layer.Id;
            AddFX(layer);
            if (grouped)
            {
                AddFX(doc.layers[0]);
                foreach (var group in new[] { doc.layers[0], doc.layers[0].children[0] })
                { groupIds.Add(group.Id); groupTransforms.Add(group.transform); groupData.Add(JsonUtility.ToJson(group.Behaviour)); }
            }
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            Call(window, "SetDocument", doc);
            }
            catch (Exception original)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
                throw;
            }
        }
        void AddFX(Layer owner)
        {
            var fx = ScriptableObject.CreateInstance<ShaderFX>(); fx.hideFlags = HideFlags.HideAndDontSave;
            effectIds.Add(Id(fx)); effectOwners.Add(owner.Id);
            Write(fx, "code", "float4 ApplyFX(float2 uv, float4 color) { return color * float4(.65, .8, .9, .7); }");
            t.True((bool)Call(fx, "ApplyCore", false), "Owned tint FX compiles");
            owner.fx.Add(fx); effectData.Add(EditorJsonUtility.ToJson(fx));
        }
        internal async Task Open(CancellationToken token)
        {
            window.ShowUtility(); window.position = new Rect(80, 80, 1150, 760); window.CreateGUI();
            Call(window, "SelectOnlyLayer", layerId); Call(window, "RefreshToolkitInterface", true);
            for (int i = 0; i < 3; i++) await AsyncD.Tick(Root, token);
            t.True(Root.Q<TextField>("textContent") != null, "Text properties are attached in owned window");
        }
        internal void Shell(string label)
        {
            t.Equal(layerId, Layer.Id, label + ": stable layer ID");
            t.Equal("Retain editable Text", Layer.layerName, label + ": layer name");
            t.Equal(.73f, Layer.opacity, label + ": opacity");
            t.Equal(BlendMode.Normal, Layer.blendMode, label + ": blend mode");
            t.True(Layer.enabled, label + ": visibility");
            for (int i = 0; i < groupIds.Count; i++)
            {
                var group = (Layer)Call(doc, "FindLayer", groupIds[i]);
                t.Equal(groupTransforms[i], group.transform, label + ": parent transform " + i);
                t.Equal(groupData[i], JsonUtility.ToJson(group.Behaviour), label + ": parent Boolean style " + i);
            }
            for (int i = 0; i < effectIds.Count; i++)
            {
                var owner = (Layer)Call(doc, "FindLayer", effectOwners[i]);
                t.Equal(1, owner.fx.Count, label + ": FX list length");
                t.True(owner.fx[0] != null && effectIds[i].Equals(Id(owner.fx[0])), label + ": same live native FX object");
                t.Equal(effectData[i], EditorJsonUtility.ToJson(owner.fx[0]), label + ": complete FX data");
            }
        }
        internal void Cycle(string label, Action change, Action verify = null, bool visibleChange = true)
        {
            var before = new Snapshot(this);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            change(); Undo.FlushUndoRecordObjects();
            verify?.Invoke();
            var after = new Snapshot(this);
            t.True(before.model != after.model || !before.transform.Equals(after.transform), label + ": actual model changed");
            if (visibleChange) t.True(Difference(before.pixels, after.pixels) > .001, label + ": render actually changed");
            Shell(label + " applied");
            // No await/input between registering our operation and consuming its history.
            for (int cycle = 0; cycle < 2; cycle++)
            {
                Undo.PerformUndo(); before.Check(this, label + " Undo " + cycle);
                Undo.PerformRedo(); after.Check(this, label + " Redo " + cycle);
            }
            if (Layer.Behaviour is TextLayerBehaviour text)
            {
                t.Equal(text.text, Root.Q<TextField>("textContent").value, label + ": content binding restored");
                t.Equal(text.layoutOffset, Root.Q<Vector2Field>("textLayoutOffset").value, label + ": layout binding restored");
            }
            Undo.IncrementCurrentGroup();
        }
        public void Dispose()
        {
            var actions = new List<Action> {
                () => { if (window != null) Call(window, "EndTextEditing", false); },
                () => { if (doc != null) Undo.ClearUndo(doc); if (window != null) Undo.ClearUndo(window); },
                () => { if (window != null && window.rootVisualElement.panel != null) { window.DiscardChanges(); window.Close(); } },
                () => { if (window != null) Object.DestroyImmediate(window); },
                () => { if (doc != null) Object.DestroyImmediate(doc); }
            };
            foreach (var id in effectIds) actions.Add(() => { var fx = (Object)Call(id, "Resolve"); if (fx != null) { Undo.ClearUndo(fx); Object.DestroyImmediate(fx); } });
            actions.AddRange(prefs);
            actions.Add(() => {
                var error = string.IsNullOrEmpty(face.familyName) ? FontEngine.UnloadFontFace() :
                    FontEngine.LoadFontFace(face.familyName, face.styleName, face.pointSize);
                t.Equal(FontEngineError.Success, error, "Caller font face restored");
            });
            AsyncD.CleanupOwned(actions.ToArray());
        }
    }
    static async Task Run(TestContext t, CancellationToken token)
    {
        t.Equal(System.IO.Path.GetFullPath("D:/DCFA/Projects/Test6.6"),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")), "Assigned Editor only");
        for (int scenario = 0; scenario < 3; scenario++)
        {
            token.ThrowIfCancellationRequested();
            using var f = new Fixture(t, scenario == 0 ? TextLayoutMode.Point : TextLayoutMode.Frame,
                scenario == 1 ? TextOverflowMode.Clip : TextOverflowMode.Ellipsis,
                scenario == 2, VectorBooleanOperation.Subtract);
            await f.Open(token);
            f.Cycle("Convert " + scenario, () => Click(f.Root.Q<Button>("convert-text-vector")),
                () => t.True(f.Layer.Behaviour is VectorLayerBehaviour && f.Layer.transform.tiling == TransformTilingMode.Unbounded,
                    "Actual conversion command creates fitted Vector"), false);
        }
        foreach (var operation in new[] { VectorBooleanOperation.Combine, VectorBooleanOperation.Union, VectorBooleanOperation.Subtract })
        {
            token.ThrowIfCancellationRequested();
            using var f = new Fixture(t, TextLayoutMode.Frame, TextOverflowMode.Clip, true, operation);
            await f.Open(token);
            f.Cycle(operation + " content", () => f.Root.Q<TextField>("textContent").value = "New O\n123");
            f.Cycle(operation + " layout offset", () => f.Root.Q<Vector2Field>("textLayoutOffset").value = new Vector2(-11.5f, 8.25f));
            f.Cycle(operation + " frame", () => f.Root.Q<Vector2Field>("textFrameSize").value = new Vector2(91, 49));
            f.Cycle(operation + " character scale", () => f.Root.Q<FloatField>("textCharacterHorizontalScale").value = .8f);
            f.Cycle(operation + " Point layout", () => f.Root.Q<EnumField>("textLayoutMode").value = TextLayoutMode.Point);
            Call(f.window, "SetCanvasTool", Enum.Parse(Read(f.window, "canvasTool").GetType(), "Text"));
            f.Cycle(operation + " inline session", () => {
                Call(f.window, "FocusTextInput", false);
                f.Root.Q<TextField>("canvasTextInput").value = "Inline edit";
                f.Root.Q<TextField>("canvasTextInput").value = "Inline O B";
                Call(f.window, "EndTextEditing", false);
            });
        }
    }
}
