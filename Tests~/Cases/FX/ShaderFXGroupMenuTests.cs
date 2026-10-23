using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderFXGroupMenuTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Values(ShaderFX effect) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static int Group(ShaderFX effect, string name) => Values(effect).Find(p => p.name == name).controls[0].groupId;
    static DropdownMenu Menu(object view, int group)
    {
        var menu = new DropdownMenu(); Call(view, "BuildContainerMenu", menu, group); return menu;
    }
    static DropdownMenuAction Action(DropdownMenu menu, string name)
    {
        var action = menu.MenuItems().OfType<DropdownMenuAction>().Single(a => a.name == name);
        action.UpdateActionStatus(null); return action;
    }
    static string Declarations(string prefix, string title, string maximum) =>
        "// @group(" + title + "; " + prefix + "Enabled)\n" +
        "// @param hidden label(Enabled) bool " + prefix + "Enabled = true\n" +
        "// @if " + prefix + "Enabled == 1\n" +
        "// @param label(Amount) float " + prefix + "Amount = 0.1 [0 .. " + maximum + "]\n// @endif\n" +
        "// @param hidden label(Secret) float " + prefix + "Secret = 0.2\n" +
        "// @param label(Tint) color " + prefix + "Tint = (1,1,1,1)\n" +
        "// @param label(Profile) curve " + prefix + "Profile\n" +
        "// @param label(Palette) gradient " + prefix + "Palette\n" +
        "// @param label(Frame) transform2D " + prefix + "Frame = (0.5,0.5,1,1,0)\n" +
        "// @param label(Source) texture2D " + prefix + "Source = none\n// @endgroup\n";

    static string TabDeclarations(string prefix, string title, string maximum, bool header) =>
        "// @tab(" + title + (header ? "; " + prefix + "Enabled" : "") + ")\n" +
        "// @param label(Enabled) bool " + prefix + "Enabled = false\n" +
        "// @enableif " + prefix + "Enabled == 1\n" +
        "// @param label(Amount) float " + prefix + "Amount = 0.1 [0 .. " + maximum + "]\n// @endenableif\n" +
        "// @foldout(Details)\n// @param label(Detail) float " + prefix + "Detail = 0.3\n" +
        "// @param hidden label(Secret) float " + prefix + "Secret = 0.2\n// @endfoldout\n// @endtab\n";

    public static string Run() => TestContext.Run("FX group/tab Copy/Paste/Reset and Undo", t =>
    {
        var clipboardType = Type("ShaderParameterGroupClipboard");
        var snapshotField = clipboardType.GetField("snapshot", F);
        object previousSnapshot = snapshotField.GetValue(null);
        string previousClipboard = GUIUtility.systemCopyBuffer;
        WhimTexDocument document = null, otherDocument = null;
        ShaderFX effect = null, other = null;
        Exception primaryFailure = null;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            otherDocument = ScriptableObject.CreateInstance<WhimTexDocument>(); otherDocument.hideFlags = HideFlags.HideAndDontSave;
            string code = Declarations("_A", "First", "1") + Declarations("_B", "Second", "0.5") +
                "// @group\n// @param float _Plain = 42\n// @endgroup\n" +
                "// @group(Different)\n// @param bool _Different = false\n// @endgroup\n" +
                "// @tabs\n" + TabDeclarations("_TabA", "One", "1", false) + TabDeclarations("_TabB", "Two", "0.5", true) + "// @endtabs\n" +
                "float4 ApplyFX(float2 uv, float4 color) { return color * _AAmount; }";
            ShaderFX Create(WhimTexDocument owner)
            {
                var parsed = (List<ShaderFXParameter>)Type("ShaderFXMetadata").GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
                return (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                    new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                    .Invoke(null, new object[] { owner, code, parsed });
            }
            effect = Create(document); other = Create(otherDocument);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null);
            var viewType = Type("ShaderFXParameterView");
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { effect }, null);
            var otherView = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { other }, null);
            ShaderFXParameter P(string name) => Values(effect).Find(p => p.name == name);
            int first = Group(effect, "_AEnabled"), second = Group(effect, "_BEnabled"), plain = Group(effect, "_Plain");
            var groups = view.Query<VisualElement>(className: "whimtex-fx-parameter-group").ToList();
            t.Equal(6, groups.Count, "Named/unnamed groups and two nested tab foldouts keep their layout");
            t.True(groups[2].Q(className: "whimtex-fx-parameter-group-header") == null, "Unnamed group has no header");
            t.Equal(0, Menu(view, plain).MenuItems().Count, "Unnamed group cannot produce group actions");
            var menu = Menu(view, first);
            t.Equal("Copy,Paste,Reset", string.Join(",", menu.MenuItems().OfType<DropdownMenuAction>().Select(a => a.name)), "Exactly three group actions");
            t.True(menu.MenuItems()[2] is DropdownMenuSeparator, "Reset is separated from Copy and Paste");
            GUIUtility.systemCopyBuffer = "Unrelated clipboard text";
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(view, first), "Paste").status, "Foreign clipboard disables Paste");
            var shader = typeof(ShaderFX).GetField("compiledShader", F).GetValue(effect);
            var defaults = Values(effect).ToDictionary(p => p.name, p => JsonUtility.ToJson(p));
            P("_AEnabled").floatValue = 0; P("_AAmount").floatValue = .8f; P("_ASecret").floatValue = .7f;
            P("_ATint").colorValue = Color.magenta; P("_AProfile").curveValue = AnimationCurve.EaseInOut(0, .1f, 1, .9f);
            P("_APalette").gradientValue.Mode = WhimTexGradientMode.Classic;
            P("_AFrame").transformValue = new ShaderFXTransform { position = new Double2(.2, .3), size = new Double2(.7, .8), rotation = 35 };
            Action(Menu(view, first), "Copy").Execute();
            t.True(GUIUtility.systemCopyBuffer.StartsWith("WhimTex parameter group:"), "Copy uses the ordinary clipboard channel");
            P("_APalette").gradientValue.Mode = WhimTexGradientMode.Linear; P("_AAmount").floatValue = .3f;
            var unrelated = Values(effect).Where(p => !p.name.StartsWith("_B")).ToDictionary(p => p.name, p => JsonUtility.ToJson(p));
            var ids = Values(effect).ToDictionary(p => p.name, p => p.id);
            Undo.IncrementCurrentGroup();
            var paste = Action(Menu(view, second), "Paste");
            t.Equal(DropdownMenuAction.Status.Normal, paste.status, "Different prefixes with matching fields can paste");
            paste.Execute(); Undo.FlushUndoRecordObjects();
            t.Equal(.5f, P("_BAmount").floatValue, "Paste respects destination hard bounds");
            t.Equal(0f, P("_BEnabled").floatValue, "Linked header value is included");
            t.Equal(.7f, P("_BSecret").floatValue, "Hidden values are included");
            t.Equal(Color.magenta, P("_BTint").colorValue, "Color is copied");
            t.Equal(WhimTexGradientMode.Classic, P("_BPalette").gradientValue.Mode, "Clipboard is a snapshot, not a live source reference");
            t.True(!ReferenceEquals(P("_AProfile").curveValue, P("_BProfile").curveValue) &&
                !ReferenceEquals(P("_APalette").gradientValue, P("_BPalette").gradientValue), "Mutable values are independently cloned");
            t.Near(.1, P("_BProfile").curveValue.Evaluate(0), .00001, "Curve values are copied");
            t.Equal(35d, P("_BFrame").transformValue.rotation, "Transform is copied");
            foreach (var p in Values(effect)) t.Equal(ids[p.name], p.id, "Paste retains destination identity: " + p.name);
            foreach (var p in Values(effect).Where(p => !p.name.StartsWith("_B"))) t.Equal(unrelated[p.name], JsonUtility.ToJson(p), "Paste leaves other groups untouched: " + p.name);
            Undo.PerformUndo();
            foreach (var p in Values(effect).Where(p => p.name.StartsWith("_B"))) t.Equal(defaults[p.name], JsonUtility.ToJson(p), "One Undo restores the destination group: " + p.name);
            Undo.PerformRedo(); t.Equal(.7f, P("_BSecret").floatValue, "Redo restores the same pasted values");
            Undo.ClearUndo(effect);
            string pasted = JsonUtility.ToJson(P("_BFrame"));
            Undo.IncrementCurrentGroup(); Action(Menu(view, second), "Reset").Execute(); Undo.FlushUndoRecordObjects();
            foreach (var p in Values(effect).Where(p => p.name.StartsWith("_B"))) t.Equal(defaults[p.name], JsonUtility.ToJson(p), "Reset restores every declared group default: " + p.name);
            foreach (var p in Values(effect).Where(p => !p.name.StartsWith("_B"))) t.Equal(unrelated[p.name], JsonUtility.ToJson(p), "Reset leaves other groups untouched: " + p.name);
            Undo.PerformUndo(); t.Equal(pasted, JsonUtility.ToJson(P("_BFrame")), "One Undo reverses group Reset");
            Undo.PerformRedo(); Undo.ClearUndo(effect);
            t.True(ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", F).GetValue(effect)), "Group value edits reuse the compiled shader");
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(view, Group(effect, "_Different")), "Paste").status, "Incompatible groups reject Paste");
            string clipboard = GUIUtility.systemCopyBuffer; Call(view, "CopyContainer", plain);
            t.Equal(clipboard, GUIUtility.systemCopyBuffer, "Unnamed Copy is guarded even from a stale callback");
            P("_Plain").floatValue = 99; Call(view, "ResetContainer", plain); t.Equal(99f, P("_Plain").floatValue, "Unnamed Reset is guarded");
            P("_BSecret").floatValue = .9f; GUIUtility.systemCopyBuffer = "Changed while menu was open"; paste.Execute();
            t.Equal(.9f, P("_BSecret").floatValue, "Stale Paste rechecks the clipboard before editing");

            var layer = new Layer(new ColorFillLayerBehaviour()); typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null); document.layers.Add(layer);
            P("_ASource").textureSource = ShaderFXTextureSource.Layer; P("_ASource").textureLayerId = layer.Id;
            Call(view, "CopyContainer", first);
            t.Equal(DropdownMenuAction.Status.Normal, Action(Menu(view, second), "Paste").status, "Same-document texture references can paste");
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(otherView, Group(other, "_BEnabled")), "Paste").status, "Layer references cannot bind unrelated documents");

            int Tab(string name) => P(name).controls[0].containers.Single(c => c.kind == ShaderFXContainerKind.Tab).id;
            int tabA = Tab("_TabAEnabled"), tabB = Tab("_TabBEnabled");
            var tabs = view.Query<Button>(className: "whimtex-fx-tab").ToList();
            var pages = view.Query<VisualElement>(className: "whimtex-fx-tab-page").ToList();
            var toggle = tabs[1].parent.Q<Toggle>(className: "whimtex-fx-tab-toggle");
            t.True(toggle != null && !toggle.value, "Tab header promotes a non-hidden bool to a checkbox");
            t.Equal("Two", tabs[1].text, "Tab retains its native label alongside the checkbox");
            t.Equal(0, pages[1].Query<Toggle>().ToList().Count(field => field.label == "Enabled"), "Linked checkbox has no duplicate body field");
            t.Equal(1, pages[0].Query<Toggle>().ToList().Count(field => field.label == "Enabled"), "Unlinked Enabled remains an ordinary body field");
            Undo.ClearUndo(effect);
            P("_TabBEnabled").floatValue = 0; Call(view, "Refresh");
            var tabDefaults = Values(effect).Where(p => p.name.StartsWith("_TabB")).ToDictionary(p => p.name, p => JsonUtility.ToJson(p));
            P("_TabAAmount").floatValue = .8f; P("_TabADetail").floatValue = .9f; P("_TabASecret").floatValue = .7f;
            var tabUnrelated = Values(effect).Where(p => !p.name.StartsWith("_TabB")).ToDictionary(p => p.name, p => JsonUtility.ToJson(p));
            t.Equal("Copy,Paste,Reset", string.Join(",", Menu(view, tabA).MenuItems().OfType<DropdownMenuAction>().Select(a => a.name)), "Tabs have the same actions as groups");
            t.True(Menu(view, tabA).MenuItems()[2] is DropdownMenuSeparator, "Tab Reset has the same separator");
            Action(Menu(view, tabA), "Copy").Execute();
            var tabPaste = Action(Menu(view, tabB), "Paste");
            t.Equal(DropdownMenuAction.Status.Normal, tabPaste.status, "Compatible tabs with different header layouts can paste");
            Undo.IncrementCurrentGroup(); tabPaste.Execute(); Undo.FlushUndoRecordObjects();
            t.Equal(.5f, P("_TabBAmount").floatValue, "Tab paste includes disabled values and respects destination bounds");
            t.Equal(.9f, P("_TabBDetail").floatValue, "Tab paste includes nested foldout values");
            t.Equal(.7f, P("_TabBSecret").floatValue, "Tab paste includes hidden descendants");
            t.Equal(0f, P("_TabBEnabled").floatValue, "Tab paste includes the linked Enabled value");
            foreach (var p in Values(effect)) t.Equal(ids[p.name], p.id, "Tab paste preserves identity: " + p.name);
            foreach (var p in Values(effect).Where(p => !p.name.StartsWith("_TabB")))
                t.Equal(tabUnrelated[p.name], JsonUtility.ToJson(p), "Tab paste leaves other tabs/global fields untouched: " + p.name);
            Undo.PerformUndo();
            foreach (var p in Values(effect).Where(p => p.name.StartsWith("_TabB")))
                t.Equal(tabDefaults[p.name], JsonUtility.ToJson(p), "One Undo restores tab values: " + p.name);
            Undo.PerformRedo(); t.Equal(.7f, P("_TabBSecret").floatValue, "One Redo restores pasted tab values");
            Undo.ClearUndo(effect);
            Undo.IncrementCurrentGroup(); Action(Menu(view, tabB), "Reset").Execute(); Undo.FlushUndoRecordObjects();
            foreach (var p in Values(effect).Where(p => p.name.StartsWith("_TabB")))
                t.Equal(tabDefaults[p.name], JsonUtility.ToJson(p), "Tab Reset restores all declared defaults: " + p.name);
            foreach (var p in Values(effect).Where(p => !p.name.StartsWith("_TabB")))
                t.Equal(tabUnrelated[p.name], JsonUtility.ToJson(p), "Tab Reset leaves other scopes untouched: " + p.name);
            Undo.PerformUndo(); t.Equal(.7f, P("_TabBSecret").floatValue, "One Undo reverses tab Reset");
            Undo.PerformRedo(); Undo.ClearUndo(effect);
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(view, first), "Paste").status, "A tab cannot paste into an incompatible group");
            t.Equal(0, Menu(view, P("_TabAEnabled").controls[0].containers[0].id).MenuItems().Count, "Whole tabs block has no page-value menu");
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { "// @param broken _AAmount" });
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(view, first), "Reset").status, "Malformed metadata disables group Reset");
            string before = JsonUtility.ToJson(effect); Call(view, "ResetContainer", first);
            t.Equal(before, JsonUtility.ToJson(effect), "Rejected Reset makes no partial changes");
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            var failures = new List<Exception>();
            void Cleanup(System.Action action)
            {
                try { action(); }
                catch (Exception error) { failures.Add(error); }
            }
            Cleanup(() => snapshotField.SetValue(null, previousSnapshot));
            Cleanup(() =>
            {
                if (GUIUtility.systemCopyBuffer != previousClipboard) GUIUtility.systemCopyBuffer = previousClipboard;
            });
            foreach (var value in new UnityEngine.Object[] { effect, other, document, otherDocument })
            {
                if (value == null) continue;
                Cleanup(() => Undo.ClearUndo(value));
                Cleanup(() => { if (value != null) UnityEngine.Object.DestroyImmediate(value); });
            }
            if (failures.Count != 0)
            {
                if (primaryFailure != null) failures.Insert(0, primaryFailure);
                throw new AggregateException("Owned FX group-menu cleanup unfinished", failures);
            }
        }
    });
}
