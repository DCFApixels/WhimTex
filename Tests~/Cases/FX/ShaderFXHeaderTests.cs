using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityC;

public sealed class ShaderFXHeaderTestWindow : EditorWindow { }

public static class ShaderFXHeaderTests
{
    sealed class MenuCapture : ContextualMenuManager
    {
        public override void DisplayMenuIfEventMatches(EventBase evt, IEventHandler handler) { }
        protected override void DoDisplayMenu(DropdownMenu menu, EventBase triggerEvent) { }
    }

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);

    public static string Start(string id)
    {
        var job = AsyncFixture.Create(id);
        try
        {
            var scope = job.Scope;
            var document = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            document.width = document.height = 16;
            Layer layer = new ColorFillLayerBehaviour(); document.layers.Add(layer);
            const string source = "// @control(_Opacity)\n// @param hidden float _Opacity = 1 [0 .. 1]\n" +
                "// @param toggle(float) _Switch = 1\n" +
                "// @group(Group; _GEnabled)\n// @param hidden label(Enabled) toggle(float) _GEnabled = true\n" +
                "// @param label(Amount) float _GAmount = 0.2\n// @param hidden label(Secret) float _GSecret = 0.3\n// @endgroup\n" +
                "// @foldout(Foldout; _FEnabled)\n// @param hidden label(Enabled) toggle(float) _FEnabled = true\n" +
                "// @param label(Amount) float _FAmount = 0.4\n// @param hidden label(Secret) float _FSecret = 0.5\n// @endfoldout\n" +
                "float4 ApplyFX(PinnedUV uv, float4 color) { return color; }";
            var parameters = (List<ShaderFXParameter>)Type("ShaderFXMetadata").GetMethod("Parse", F)
                .Invoke(null, new object[] { source, false, null });
            var effect = scope.Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, source, parameters }));
            effect.name = "A long effect name for narrow header layout";
            layer.fx.Add(effect);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(document, null);
            var window = scope.Own(ScriptableObject.CreateInstance<ShaderFXHeaderTestWindow>());
            window.titleContent = new GUIContent("FX Header Check");
            var root = window.rootVisualElement;
            var reference = (Foldout)Type("WhimTexUI").GetMethod("CreateInspectorSection", F).Invoke(null,
                new object[] { "Transform", "reference", Enum.Parse(Type("LayerActionIcon+Kind"), "Transform"), false, null, true });
            root.Add(reference);
            var nativeToggle = new Toggle { name = "nativeReference" }; root.Add(nativeToggle);
            var view = (VisualElement)Activator.CreateInstance(Type("LayerShaderFXView"), F, null,
                new object[] { layer, document, (Action<string, Action>)((_, change) => change()) }, null);
            root.Add(view);
            job.OwnCleanup(() => { root.Clear(); Undo.ClearUndo(effect); Undo.ClearUndo(document); });
            window.ShowUtility(); window.position = new Rect(100, 100, 400, 500);
            job.Schedule(root, () =>
            {
                try
                {
                    CheckGeometry(job.Context, root, reference, view);
                    CheckEvents(job.Context, view);
                    CheckParameterMenus(job.Context, view, effect);
                    window.position = new Rect(100, 100, 240, 500);
                    job.Schedule(root, () =>
                    {
                        try { CheckGeometry(job.Context, root, reference, view); job.Pass(); }
                        catch (Exception error) { job.Fail(error); }
                    }, 250);
                }
                catch (Exception error) { job.Fail(error); }
            }, 350);
        }
        catch (Exception error) { job.Fail(error); }
        return AsyncFixture.Poll(id);
    }

    static void CheckGeometry(TestContext t, VisualElement root, Foldout reference, VisualElement view)
    {
        var card = view.Q<Foldout>(className: "whimtex-layer-fx-foldout");
        var arrow = card.Q<Toggle>(className: "unity-foldout__toggle");
        var referenceArrow = reference.Q<Toggle>(className: "unity-foldout__toggle");
        var header = card.Q(className: "whimtex-layer-fx-header");
        var icon = header.Q<Image>("fxIcon");
        var active = header.Q<Toggle>(className: "whimtex-fx-visibility-toggle");
        var title = header.Q<Label>(className: "whimtex-layer-fx-name");
        var control = header.Q(className: "whimtex-fx-effect-control");
        var menu = card.Q<ToolbarMenu>(className: "whimtex-inspector-section-menu");
        t.Near(referenceArrow.worldBound.height, arrow.worldBound.height, .1, "FX header matches section height");
        t.Near(referenceArrow.Q(className: "unity-toggle__checkmark").worldBound.xMin,
            arrow.Q(className: "unity-toggle__checkmark").worldBound.xMin, .1, "Native section arrows align");
        t.Near(reference.Q(className: "whimtex-inspector-section-icon").worldBound.xMin, icon.worldBound.xMin, .1,
            "FX icon aligns with the section icon");
        t.Near(arrow.worldBound.center.y, icon.worldBound.center.y, .6, "Icon is vertically centered");
        t.Near(arrow.worldBound.center.y, active.worldBound.center.y, .6, "Enabled checkbox is vertically centered");
        t.Near(root.Q<Toggle>("nativeReference").Q(className: "unity-toggle__checkmark").worldBound.width,
            active.Q(className: "unity-toggle__checkmark").worldBound.width, .1, "Enabled checkbox uses native dimensions");
        t.True(icon.worldBound.xMax <= active.worldBound.xMin && active.worldBound.xMax <= title.worldBound.xMin,
            "Icon, enabled and title do not overlap");
        t.True(title.worldBound.width > 0 && title.worldBound.xMax <= control.worldBound.xMin + .1f &&
            control.worldBound.xMax <= menu.worldBound.xMin + .1f, "Narrow header retains title, control and menu space");
        t.Near(arrow.worldBound.height, header.worldBound.height, .1, "Drag header fills the native header height");
        t.True(!card.contentContainer.Contains(header) && !card.contentContainer.Contains(menu), "Header and menu survive collapse");
        t.True(arrow.resolvedStyle.backgroundColor.a < 1 && arrow.resolvedStyle.backgroundColor.r == 0,
            "FX header keeps its dark background");
    }

    static void CheckEvents(TestContext t, VisualElement view)
    {
        var card = view.Q<Foldout>(className: "whimtex-layer-fx-foldout");
        var arrow = card.Q<Toggle>(className: "unity-foldout__toggle");
        var code = card.Q<Foldout>("shaderFXCodeFoldout");
        var active = card.Q<Toggle>(className: "whimtex-fx-visibility-toggle");
        t.True(code != null && card.panel != null, "Real inline Code foldout is attached to an Editor panel");
        int nestedEvents = 0;
        card.RegisterCallback<ChangeEvent<bool>>(evt => { if (evt.target == code) nestedEvents++; });
        code.value = true;
        t.True(card.value && arrow.value, "Opening Code preserves parent expansion and arrow");
        code.value = false;
        t.True(card.value && arrow.value && nestedEvents == 2, "Closing Code bubbles its event without changing FX arrow");
        active.value = false;
        t.True(card.value && arrow.value, "Enabled checkbox does not collapse the effect");
        active.value = true;
        var nested = new Toggle(); card.Add(nested); nested.value = true; nested.value = false;
        t.True(card.value && arrow.value, "Nested boolean parameters do not change FX expansion");
        nested.RemoveFromHierarchy();
        arrow.value = false;
        t.True(!card.value && card.contentContainer.style.display.value == DisplayStyle.None, "Native arrow collapses the actual body");
        arrow.value = true;
        t.True(card.value && card.contentContainer.style.display.value == DisplayStyle.Flex, "Native arrow restores the body");
        var title = card.Q<Label>(className: "whimtex-layer-fx-name");
        PointerGesture(title, false);
        t.True(!card.value && !arrow.value, "Header click collapses the real foldout");
        PointerGesture(title, false);
        t.True(card.value && arrow.value, "Second header click reopens it");
        PointerGesture(title, true);
        t.True(card.value && arrow.value, "Dragging the header does not toggle expansion");
    }

    static void PointerGesture(VisualElement title, bool move)
    {
        var data = new PublicInput.Pointer { button = 0, pressedButtons = 1, position = title.worldBound.center };
        using (var evt = PointerDownEvent.GetPooled(data)) { evt.target = title; title.SendEvent(evt); }
        if (move)
        {
            data.position += new Vector3(12, 0);
            using (var evt = PointerMoveEvent.GetPooled(data)) { evt.target = title; title.SendEvent(evt); }
        }
        data.pressedButtons = 0;
        using (var evt = PointerUpEvent.GetPooled(data)) { evt.target = title; title.SendEvent(evt); }
    }

    static DropdownMenu HeaderMenu(VisualElement target)
    {
        var menu = new DropdownMenu();
        using (var evt = ContextualMenuPopulateEvent.GetPooled(null, menu, target, new MenuCapture()))
        {
            target.SendEvent(evt);
        }
        return menu;
    }

    static void CheckRightClickMenu(TestContext t, VisualElement target, VisualElement header, string name)
    {
        string actions = null;
        int requests = 0;
        void Capture(ContextualMenuPopulateEvent evt)
        {
            requests++;
            actions = string.Join(",", evt.menu.MenuItems().OfType<DropdownMenuAction>().Select(a => a.name));
            evt.menu.MenuItems().Clear();
            evt.StopImmediatePropagation();
        }
        header.RegisterCallback<ContextualMenuPopulateEvent>(Capture);
        try
        {
            var data = new PublicInput.Pointer { button = 1, pressedButtons = 2, position = target.worldBound.center };
            using (var evt = PointerDownEvent.GetPooled(data)) { evt.target = target; target.SendEvent(evt); }
            data.pressedButtons = 0;
            using (var evt = PointerUpEvent.GetPooled(data)) { evt.target = target; target.SendEvent(evt); }
            t.Equal(1, requests, name + " right-click requests exactly one menu");
            t.Equal("Copy,Paste,Reset", actions, name + " right-click opens the container menu");
        }
        finally { header.UnregisterCallback<ContextualMenuPopulateEvent>(Capture); }
    }

    static void CheckParameterMenus(TestContext t, VisualElement view, ShaderFX effect)
    {
        List<ShaderFXParameter> Values() => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
        ShaderFXParameter Value(string name) => Values().Find(p => p.name == name);
        DropdownMenuAction Action(DropdownMenu menu, string name) => menu.MenuItems().OfType<DropdownMenuAction>().Single(a => a.name == name);
        void CheckMenu(VisualElement target)
        {
            var menu = HeaderMenu(target);
            t.Equal("Copy,Paste,Reset", string.Join(",", menu.MenuItems().OfType<DropdownMenuAction>().Select(a => a.name)),
                "Full container header has the same three actions");
            t.True(menu.MenuItems()[2] is DropdownMenuSeparator, "Shared menu separates Reset");
        }
        var groupHeader = view.Query<VisualElement>(className: "whimtex-fx-parameter-group-header").ToList()
            .Single(h => h.Q<Label>(className: "whimtex-fx-parameter-group-title").text == "Group");
        var foldout = view.Q<Foldout>(className: "whimtex-fx-parameter-foldout");
        var toggle = foldout.Q<Toggle>(className: "whimtex-fx-parameter-foldout-toggle");
        var title = toggle.Q<Label>(className: "whimtex-fx-parameter-group-title");
        var enabled = toggle.Q<Toggle>(className: "whimtex-fx-parameter-group-toggle");
        CheckRightClickMenu(t, groupHeader, groupHeader, "Group header");
        CheckRightClickMenu(t, title, toggle, "Foldout title");
        CheckRightClickMenu(t, toggle.Q(className: "unity-toggle__checkmark"), toggle, "Foldout arrow");
        CheckRightClickMenu(t, enabled, toggle, "Foldout checkbox");
        CheckMenu(groupHeader);
        CheckMenu(toggle);
        CheckMenu(toggle.Q(className: "unity-toggle__checkmark"));
        CheckMenu(title);
        CheckMenu(enabled);
        t.Equal(0, HeaderMenu(foldout.contentContainer).MenuItems().Count, "Body does not acquire the header menu");
        foldout.value = false;
        CheckRightClickMenu(t, title, toggle, "Collapsed foldout title");
        CheckMenu(toggle);
        t.True(!foldout.value, "Context menu leaves the collapsed state unchanged");

        var clipboardType = Type("ShaderParameterGroupClipboard");
        var snapshotField = clipboardType.GetField("snapshot", F);
        object borrowed = snapshotField.GetValue(null);
        try
        {
            var snapshot = Activator.CreateInstance(snapshotField.FieldType, true);
            void Set(string name, object value) => snapshotField.FieldType.GetField(name, F).SetValue(snapshot, value);
            int group = Value("_GEnabled").controls[0].groupId;
            var copied = Values().Where(p => p.name.StartsWith("_G"))
                .Select(p => (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", F).Invoke(p, null)).ToList();
            copied.Find(p => p.name == "_GEnabled").floatValue = 0;
            copied.Find(p => p.name == "_GAmount").floatValue = .8f;
            copied.Find(p => p.name == "_GSecret").floatValue = .9f;
            Set("marker", GUIUtility.systemCopyBuffer);
            Set("imageRevision", Type("ImageClipboard").GetProperty("Revision", F).GetValue(null));
            Set("groupId", group); Set("values", copied);
            snapshotField.SetValue(null, snapshot);
            var paste = Action(HeaderMenu(toggle), "Paste"); paste.UpdateActionStatus(null);
            t.Equal(DropdownMenuAction.Status.Normal, paste.status, "Group values can paste into a matching foldout");
            Undo.IncrementCurrentGroup(); paste.Execute(); Undo.FlushUndoRecordObjects();
            t.Equal(.8f, Value("_FAmount").floatValue, "Foldout uses shared parameter paste");
            t.Equal(.9f, Value("_FSecret").floatValue, "Hidden foldout values are included");
            t.Equal(0f, Value("_FEnabled").floatValue, "Header checkbox is included");
            t.Equal(.2f, Value("_GAmount").floatValue, "Other container is unchanged");
            t.True(!foldout.value, "Paste does not expand the foldout");
            Undo.PerformUndo(); t.Equal(.4f, Value("_FAmount").floatValue, "One Undo restores foldout values");
            Undo.PerformRedo(); t.Equal(.8f, Value("_FAmount").floatValue, "One Redo restores pasted values");
            Undo.IncrementCurrentGroup(); Action(HeaderMenu(toggle), "Reset").Execute(); Undo.FlushUndoRecordObjects();
            t.Equal(.4f, Value("_FAmount").floatValue, "Foldout Reset restores declared values");
            t.Equal(.5f, Value("_FSecret").floatValue, "Foldout Reset includes hidden values");
            Undo.PerformUndo(); t.Equal(.9f, Value("_FSecret").floatValue, "One Undo reverses the entire Reset");
            t.True(!foldout.value, "Value edits and Undo preserve collapse state");
        }
        finally { snapshotField.SetValue(null, borrowed); foldout.value = true; }
    }
}
