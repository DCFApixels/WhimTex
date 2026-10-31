using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class ShaderFXStackClipboardTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, F).Invoke(target, args);
    static object Clipboard(string method, params object[] args) => Type("ShaderFXClipboard").GetMethod(method, F).Invoke(null, args);
    static List<ShaderFXParameter> Values(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
    static DropdownMenuAction Action(VisualElement view, string name, int index = -1)
    {
        var menu = new DropdownMenu();
        if (index < 0) Call(view, "BuildStackContextMenu", menu);
        else Call(view, "BuildContextMenu", menu, index);
        var action = menu.MenuItems().OfType<DropdownMenuAction>().Single(a => a.name == name);
        action.UpdateActionStatus(null);
        return action;
    }

    public static string Run() => TestContext.Run("FX list clipboard, insertion order and atomic Undo/Redo", t =>
    {
        var owned = new HashSet<Object>();
        var views = new List<VisualElement>();
        var clipboardType = Type("ShaderFXClipboard");
        var fields = new[] { "snapshot", "sourceDocument", "marker", "imageRevision" }.Select(n => clipboardType.GetField(n, F)).ToArray();
        var borrowed = fields.Select(f => f.GetValue(null)).ToArray();
        string systemText = GUIUtility.systemCopyBuffer;
        fields[0].SetValue(null, null);
        try
        {
            T Own<T>(T value) where T : Object { owned.Add(value); return value; }
            WhimTexDocument Document()
            {
                var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
                doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 16;
                doc.layers.Add(new Layer(new ColorFillLayerBehaviour()));
                Call(doc, "NormalizeModel"); return doc;
            }
            ShaderFX Effect(WhimTexDocument doc, string name, float value, string textureLayer)
            {
                const string code = "// @param color _Tint = (1,1,1,1)\n// @param texture2D _Mask = self\n" +
                    "float4 ApplyFX(PinnedUV uv, float4 color) { return color * _Tint; }";
                var parameters = (List<ShaderFXParameter>)Type("ShaderFXMetadata").GetMethod("Parse", F)
                    .Invoke(null, new object[] { code, false, null });
                var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                    new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                    .Invoke(null, new object[] { doc, code, parameters }));
                fx.name = name;
                Values(fx)[0].colorValue = new Color(value, -value, 2 * value, .3f);
                Values(fx)[1].textureSource = ShaderFXTextureSource.Layer;
                Values(fx)[1].textureLayerId = textureLayer;
                doc.layers[0].fx.Add(fx); return fx;
            }
            VisualElement View(WhimTexDocument doc)
            {
                var view = (VisualElement)Activator.CreateInstance(Type("LayerShaderFXView"), F, null,
                    new object[] { doc.layers[0], doc, (Action<string, Action>)((name, change) =>
                    { Undo.RegisterCompleteObjectUndo(doc, name); change(); EditorUtility.SetDirty(doc); }) }, null);
                views.Add(view); return view;
            }
            List<ShaderFX> Snapshot(WhimTexDocument doc, IReadOnlyList<ShaderFX> values)
            {
                Clipboard("Clear");
                var snapshots = (List<ShaderFX>)Clipboard("CreateSnapshot", values);
                fields[0].SetValue(null, snapshots);
                fields[1].SetValue(null, new WeakReference<WhimTexDocument>(doc));
                fields[2].SetValue(null, systemText);
                fields[3].SetValue(null, Type("ImageClipboard").GetProperty("Revision", F).GetValue(null));
                return snapshots;
            }
            void OwnPasted(WhimTexDocument doc) { foreach (var fx in doc.layers[0].fx) Own(fx); }
            void Order(WhimTexDocument doc, string expected) => t.Equal(expected,
                string.Join(",", doc.layers[0].fx.Select(fx => fx.name)), "Exact FX order");

            var source = Document();
            var a = Effect(source, "A", 2, source.layers[0].Id);
            var b = Effect(source, "B", 3, source.layers[0].Id); b.Active = false;
            var c = Effect(source, "C", 4, source.layers[0].Id);
            var destination = Document();
            Effect(destination, "Before", 1, null); Effect(destination, "After", 1, null);
            var empty = Document();
            t.Equal(DropdownMenuAction.Status.Disabled, Action(View(empty), "Copy All").status, "Empty stack cannot copy");
            t.Equal(DropdownMenuAction.Status.Normal, Action(View(source), "Copy All").status, "Nonempty stack can copy all");
            t.Equal(DropdownMenuAction.Status.Disabled, Action(View(empty), "Paste FX As New").status, "Empty clipboard cannot paste");

            var snapshots = Snapshot(source, source.layers[0].fx);
            t.Equal(3, snapshots.Count, "Snapshot includes disabled effects");
            var originalColor = Values(a)[0].colorValue;
            Values(a)[0].colorValue = Color.black; b.Active = true;
            t.Equal(originalColor, Values(snapshots[0])[0].colorValue, "Copy captures HDR and alpha independently of later edits");
            t.True(!snapshots[1].Active, "Copy captures disabled state independently");
            var sameDocument = (List<ShaderFX>)Clipboard("CreatePasteValues", source);
            foreach (var fx in sameDocument) Own(fx);
            t.True(sameDocument.All(fx => Values(fx)[1].textureLayerId == source.layers[0].Id), "Same-document layer links are preserved");
            t.True(sameDocument.Select((fx, i) => !ReferenceEquals(fx, snapshots[i])).All(x => x), "Every paste creates new effects");

            string originalName = destination.layers[0].layerName;
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(destination, "Before FX Paste");
            destination.layers[0].layerName = "Keep this change"; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            var targetView = View(destination);
            var paste = Action(targetView, "Paste 3 FX As New", 0);
            t.Equal(DropdownMenuAction.Status.Normal, paste.status, "Row menu exposes list size");
            paste.Execute(); OwnPasted(destination);
            Order(destination, "Before,A,B,C,After");
            t.Equal(originalColor, Values(destination.layers[0].fx[1])[0].colorValue, "Inserted HDR/alpha remain exact");
            t.True(!destination.layers[0].fx[2].Active, "Disabled state survives insertion");
            t.True(destination.layers[0].fx.Skip(1).Take(3).All(fx => string.IsNullOrEmpty(Values(fx)[1].textureLayerId)),
                "Cross-document copies clear unavailable layer links");
            t.True(destination.layers[0].fx.Skip(1).Take(3).All(fx => ReferenceEquals(destination,
                typeof(ShaderFX).GetProperty("EmbeddedOwner", F).GetValue(fx))), "All copies belong to destination");
            Undo.PerformUndo(); Order(destination, "Before,After");
            t.Equal("Keep this change", destination.layers[0].layerName, "One Undo removes the whole list but preserves the previous edit");
            Undo.PerformUndo(); t.Equal(originalName, destination.layers[0].layerName, "Previous edit is a separate Undo step");
            Undo.PerformRedo(); t.Equal("Keep this change", destination.layers[0].layerName, "Redo restores previous edit separately");
            Undo.PerformRedo(); OwnPasted(destination); Order(destination, "Before,A,B,C,After");
            t.Equal(originalColor, Values(destination.layers[0].fx[1])[0].colorValue, "Redo restores exact HDR values");
            t.True(!destination.layers[0].fx[2].Active, "Redo restores disabled state");

            Action(View(destination), "Paste 3 FX As New").Execute(); OwnPasted(destination);
            Order(destination, "Before,A,B,C,After,A,B,C");
            t.True(destination.layers[0].fx[1] != destination.layers[0].fx[5], "Repeated pastes are independent");
            Values(destination.layers[0].fx[5])[0].colorValue = Color.yellow;
            t.Equal(originalColor, Values(destination.layers[0].fx[1])[0].colorValue, "Editing a repeated copy leaves earlier copies intact");
            Undo.PerformUndo(); Order(destination, "Before,A,B,C,After");
            Undo.PerformRedo(); OwnPasted(destination); Order(destination, "Before,A,B,C,After,A,B,C");

            Snapshot(source, new[] { c });
            t.Equal(DropdownMenuAction.Status.Normal, Action(View(destination), "Paste FX As New").status, "Single-effect paste keeps its existing label");
            Action(View(destination), "Paste FX As New", 0).Execute(); OwnPasted(destination);
            Order(destination, "Before,C,A,B,C,After,A,B,C");
            Undo.PerformUndo(); Order(destination, "Before,A,B,C,After,A,B,C");
            Undo.PerformRedo(); OwnPasted(destination);
            var noValues = (List<ShaderFX>)Clipboard("CreateSnapshot", new ShaderFX[] { null });
            t.Equal(0, noValues.Count, "Missing entries are not copied as effects");
            Clipboard("CopyAll", source, Array.Empty<ShaderFX>());
            t.Equal("Paste FX As New", clipboardType.GetProperty("PasteLabel", F).GetValue(null), "Empty copy preserves the clipboard");
            var expired = Snapshot(source, source.layers[0].fx);
            fields[2].SetValue(null, "invalid-test-marker-" + Guid.NewGuid().ToString("N"));
            t.True(clipboardType.GetProperty("Current", F).GetValue(null) == null && expired.All(fx => fx == null),
                "Clipboard invalidation releases every snapshot");
            t.Equal(systemText, GUIUtility.systemCopyBuffer, "Test never writes system clipboard");
        }
        finally
        {
            Clipboard("Clear");
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, borrowed[i]);
            foreach (var view in views) view.Clear();
            foreach (var obj in owned) if (obj != null) Undo.ClearUndo(obj);
            foreach (var obj in owned) if (obj != null && !(obj is WhimTexDocument)) Object.DestroyImmediate(obj);
            foreach (var obj in owned) if (obj != null && obj is WhimTexDocument) Object.DestroyImmediate(obj);
        }
    });
}
