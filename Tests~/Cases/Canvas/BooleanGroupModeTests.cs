using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class BooleanGroupModeTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static string Start(string id) => AsyncD.Start(id, Run);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static object Call(object owner, string name, params object[] args)
    {
        var method = owner.GetType().GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        if (method.DeclaringType.Assembly != typeof(WhimTexWindow).Assembly) throw new InvalidOperationException("Only package methods are inspected");
        return method.Invoke(owner, args);
    }

    static async Task Run(TestContext t, CancellationToken token)
    {
        WhimTexWindow window = null;
        WhimTexDocument document = null;
        var restore = new List<Action>();
        foreach (string key in new[] { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings" })
        {
            bool had = EditorPrefs.HasKey(key); string value = EditorPrefs.GetString(key);
            restore.Add(() => { if (had) EditorPrefs.SetString(key, value); else EditorPrefs.DeleteKey(key); });
        }
        const string scale = "DCFApixels.WhimTex.Canvas.PaintingScale";
        bool hadScale = EditorPrefs.HasKey(scale); float oldScale = EditorPrefs.GetFloat(scale);
        restore.Add(() => { if (hadScale) EditorPrefs.SetFloat(scale, oldScale); else EditorPrefs.DeleteKey(scale); });
        const string dash = "DCFApixels.WhimTex.Vector.DashSegmentsExpanded";
        bool hadDash = EditorPrefs.HasKey(dash); bool oldDash = EditorPrefs.GetBool(dash);
        restore.Add(() => { if (hadDash) EditorPrefs.SetBool(dash, oldDash); else EditorPrefs.DeleteKey(dash); });
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 96;
            var nested = new Layer(new BooleanGroupLayerBehaviour());
            nested.children.Add(new Layer(new ShapeLayerBehaviour()) { booleanOperation = VectorBooleanOperation.Intersect });
            var group = new Layer(new BooleanGroupLayerBehaviour()) { booleanOperation = VectorBooleanOperation.Xor };
            group.children.Add(nested);
            group.children.Add(new Layer(new ShapeLayerBehaviour()));
            group.children.Add(new Layer(new ShapeLayerBehaviour()));
            foreach (var child in group.children) child.booleanOperation = VectorBooleanOperation.Subtract;
            group.children[2].booleanOperation = VectorBooleanOperation.Union;
            document.layers.Add(group); Call(document, "NormalizeModel");
            string groupId = group.Id;
            Layer Current() => (Layer)Call(document, "FindLayer", groupId);
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); Call(window, "SetDocument", document);
            window.ShowUtility(); window.position = new Rect(80, 80, 1050, 780); window.CreateGUI();
            Task Tick() => AsyncD.Tick(window.rootVisualElement, token);
            for (int i = 0; i < 5; i++) await Tick();
            Call(window, "SelectOnlyLayer", groupId); Call(window, "RefreshToolkitInterface", true); await Tick();
            BaseField<VectorBooleanOperation> Field() => window.rootVisualElement.Q<BaseField<VectorBooleanOperation>>("booleanGroupMode");
            Button Button(VectorBooleanOperation op) => Field().Q<Button>("strokeMode" + op);
            void Submit(VectorBooleanOperation op)
            {
                using var e = NavigationSubmitEvent.GetPooled(); e.target = Button(op); Button(op).SendEvent(e);
            }
            void Selected(VectorBooleanOperation op)
            {
                t.True(Field() != null, "Mode is in the group inspector");
                foreach (VectorBooleanOperation choice in Enum.GetValues(typeof(VectorBooleanOperation)))
                {
                    t.True(Button(choice) != null && !string.IsNullOrWhiteSpace(Button(choice).tooltip), "Choice has an icon button and tooltip: " + choice);
                    t.Equal(choice == op, Button(choice).ClassListContains("whimtex-stroke-style--selected"), "Selection: " + choice);
                }
            }
            Selected(VectorBooleanOperation.Subtract);
            t.True(Field().enabledInHierarchy && Field().worldBound.width > 100, "Mode is enabled and laid out");
            string nestedData = JsonUtility.ToJson(((BooleanGroupLayerBehaviour)Current().children[0].Behaviour));
            foreach (VectorBooleanOperation op in Enum.GetValues(typeof(VectorBooleanOperation)))
            {
                Undo.IncrementCurrentGroup();
                var before = Current().children.Select(c => c.booleanOperation).ToArray();
                Submit(op); await Tick(); Call(window, "RefreshToolkitInterface", true);
                Selected(op);
                foreach (var child in Current().children) t.Equal(op, child.booleanOperation, "Mode updates direct operands");
                t.Equal(VectorBooleanOperation.Xor, Current().booleanOperation, "Own operation in a parent group is untouched");
                t.Equal(VectorBooleanOperation.Intersect, Current().children[0].children[0].booleanOperation, "Nested operations are untouched");
                t.Equal(nestedData, JsonUtility.ToJson(Current().children[0].Behaviour), "Nested appearance is untouched");
                // Repeated choice must not add a second Undo record.
                Submit(op); Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
                t.True(before.SequenceEqual(Current().children.Select(c => c.booleanOperation)), "One Undo restores all original operations");
                Undo.PerformRedo(); await Tick(); Call(window, "RefreshToolkitInterface", true); Selected(op);
            }
            Current().children[1].booleanOperation = VectorBooleanOperation.Subtract;
            Call(window, "RefreshToolkitInterface", true); await Tick(); Selected((VectorBooleanOperation)(-1));
            t.True(!Field().Q<Label>("booleanGroupModeMixed").ClassListContains("whimtex-hidden"), "Mixed operations are visibly identified");
            Submit(VectorBooleanOperation.Union); await Tick(); Call(window, "RefreshToolkitInterface", true); Selected(VectorBooleanOperation.Union);
            t.True(Field().Q<Label>("booleanGroupModeMixed").ClassListContains("whimtex-hidden"), "One choice clears Mixed");
            EnumField Appearance() => window.rootVisualElement.Q<EnumField>("booleanGroupAppearance");
            t.True(!Appearance().enabledInHierarchy, "Boolean operations do not expose layer styling");
            Submit(VectorBooleanOperation.Combine); await Tick(); Call(window, "RefreshToolkitInterface", true);
            t.True(Appearance().enabledInHierarchy, "Combine enables the appearance choice");
            Undo.IncrementCurrentGroup(); Appearance().value = BooleanGroupAppearance.Layers;
            await Tick(); Undo.FlushUndoRecordObjects();
            t.Equal(BooleanGroupAppearance.Layers, ((BooleanGroupLayerBehaviour)Current().Behaviour).appearance, "Appearance updates model");
            Undo.PerformUndo(); await Tick();
            t.Equal(BooleanGroupAppearance.Group, ((BooleanGroupLayerBehaviour)Current().Behaviour).appearance, "Appearance Undo");
            Undo.PerformRedo(); await Tick(); Call(window, "RefreshToolkitInterface", true);
            Submit(VectorBooleanOperation.Union); await Tick(); Call(window, "RefreshToolkitInterface", true);
            t.True(!Appearance().enabledInHierarchy, "Union disables appearance choice");
            t.Equal(BooleanGroupAppearance.Layers, ((BooleanGroupLayerBehaviour)Current().Behaviour).appearance, "Union retains appearance preference");
            Submit(VectorBooleanOperation.Combine); await Tick(); Call(window, "RefreshToolkitInterface", true);
            Selected(VectorBooleanOperation.Combine);
            foreach (var foldout in window.rootVisualElement.Query<Foldout>().ToList())
                if (foldout.name == "transformSection" || foldout.name == "renderingSection") foldout.value = false;
            await Tick(); await Tick();
            var capture = new Preview { owner = window, pickingMode = PickingMode.Ignore };
            capture.style.position = Position.Absolute; capture.style.width = capture.style.height = 1;
            window.rootVisualElement.Add(capture);
            try
            {
                for (int i = 0; i < 40 && !capture.done.Task.IsCompleted; i++) { window.Repaint(); await Tick(); }
                t.True(capture.done.Task.IsCompleted, "Actual icon row preview captured"); await capture.done.Task;
            }
            finally { capture.RemoveFromHierarchy(); }
            // Removing operands must not leave a selected, actionable mode behind.
            var children = Current().children.ToArray(); Current().children.Clear();
            try
            {
                Call(window, "RefreshToolkitInterface", true); await Tick();
                t.True(!Field().enabledInHierarchy, "Empty group has no editable operation"); Selected((VectorBooleanOperation)(-1));
            }
            finally { Current().children.AddRange(children); }
        }
        finally
        {
            var cleanup = new List<Action>
            {
                () => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); },
                () => { if (window != null) { window.DiscardChanges(); window.Close(); } },
                () => { if (window != null) UnityEngine.Object.DestroyImmediate(window); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); }
            };
            cleanup.AddRange(restore); AsyncD.CleanupOwned(cleanup.ToArray());
        }
    }

    sealed class Preview : ImmediateModeElement
    {
        public WhimTexWindow owner;
        public readonly TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        protected override void ImmediateRepaint()
        {
            if (done.Task.IsCompleted) return;
            Texture2D texture = null;
            try
            {
                var rt = RenderTexture.active;
                int w = rt != null ? rt.width : Mathf.RoundToInt(owner.position.width * EditorGUIUtility.pixelsPerPoint);
                int h = rt != null ? rt.height : Mathf.RoundToInt(owner.position.height * EditorGUIUtility.pixelsPerPoint);
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); texture.Apply(false);
                System.IO.Directory.CreateDirectory("Temp/WhimTex");
                System.IO.File.WriteAllBytes("Temp/WhimTex/boolean-group-mode.png", texture.EncodeToPNG()); done.TrySetResult(true);
            }
            catch (Exception e) { done.TrySetException(e); }
            finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
