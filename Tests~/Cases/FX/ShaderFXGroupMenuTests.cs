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
        var menu = new DropdownMenu(); Call(view, "BuildGroupMenu", menu, group); return menu;
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

    public static string Run() => TestContext.Run("FX named group Copy/Paste/Reset and Undo", t =>
    {
        var clipboardType = Type("ShaderParameterGroupClipboard");
        var snapshotField = clipboardType.GetField("snapshot", F);
        object previousSnapshot = snapshotField.GetValue(null);
        string previousClipboard = GUIUtility.systemCopyBuffer;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        var otherDocument = ScriptableObject.CreateInstance<WhimTexDocument>(); otherDocument.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX effect = null, other = null;
        try
        {
            string code = Declarations("_A", "First", "1") + Declarations("_B", "Second", "0.5") +
                "// @group\n// @param float _Plain = 42\n// @endgroup\n" +
                "// @group(Different)\n// @param bool _Different = false\n// @endgroup\n" +
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
            t.Equal(4, groups.Count, "Named and unnamed groups keep the existing layout");
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
            string clipboard = GUIUtility.systemCopyBuffer; Call(view, "CopyGroup", plain);
            t.Equal(clipboard, GUIUtility.systemCopyBuffer, "Unnamed Copy is guarded even from a stale callback");
            P("_Plain").floatValue = 99; Call(view, "ResetGroup", plain); t.Equal(99f, P("_Plain").floatValue, "Unnamed Reset is guarded");
            P("_BSecret").floatValue = .9f; GUIUtility.systemCopyBuffer = "Changed while menu was open"; paste.Execute();
            t.Equal(.9f, P("_BSecret").floatValue, "Stale Paste rechecks the clipboard before editing");

            var layer = new Layer(new ColorFillLayerBehaviour()); typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null); document.layers.Add(layer);
            P("_ASource").textureSource = ShaderFXTextureSource.Layer; P("_ASource").textureLayerId = layer.Id;
            Call(view, "CopyGroup", first);
            t.Equal(DropdownMenuAction.Status.Normal, Action(Menu(view, second), "Paste").status, "Same-document texture references can paste");
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(otherView, Group(other, "_BEnabled")), "Paste").status, "Layer references cannot bind unrelated documents");
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { "// @param broken _AAmount" });
            t.Equal(DropdownMenuAction.Status.Disabled, Action(Menu(view, first), "Reset").status, "Malformed metadata disables group Reset");
            string before = JsonUtility.ToJson(effect); Call(view, "ResetGroup", first);
            t.Equal(before, JsonUtility.ToJson(effect), "Rejected Reset makes no partial changes");
        }
        finally
        {
            snapshotField.SetValue(null, previousSnapshot); GUIUtility.systemCopyBuffer = previousClipboard;
            if (effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); }
            if (other != null) { Undo.ClearUndo(other); UnityEngine.Object.DestroyImmediate(other); }
            Undo.ClearUndo(document); Undo.ClearUndo(otherDocument);
            UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(otherDocument);
        }
    });
}
