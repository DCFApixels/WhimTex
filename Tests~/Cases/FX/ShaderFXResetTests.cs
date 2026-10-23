using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderFXResetTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Values(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
    static bool CanReset(ShaderFX fx) => (bool)typeof(ShaderFX).GetProperty("CanResetParameters", F).GetValue(fx);
    static bool Reset(ShaderFX fx) => (bool)typeof(ShaderFX).GetMethod("ResetParameters", F).Invoke(fx, null);

    public static string Run() => TestContext.Run("FX block Reset defaults, identity, menus and Undo", t =>
    {
        const string code = "// @whimtex-effect Test/Reset\n// @control(_Amount)\n" +
            "// @param float _Amount = 0.25 [0 .. 1]\n" +
            "// @param label(Alternate) float _Amount [0 .. 1]\n" +
            "// @param hidden bool _Enabled = true\n" +
            "// @param enum _Mode = B {A:2, B:7}\n" +
            "// @param seed float _Seed = 17 [0 .. 100]\n" +
            "// @param float2 _Vector2 = (2,3)\n" +
            "// @param float3 _Vector3 = (2,3,4)\n" +
            "// @param float4 _Vector4 = (2,3,4,5)\n" +
            "// @param normal _Normal = (0,1,0)\n" +
            "// @param point _Point = (0.25,0.75)\n" +
            "// @param color _Color = (0.1,0.2,0.3,0.4)\n" +
            "// @param texture2D _Texture = self\n" +
            "// @param transform2D _Frame = (0.3,0.4,0.7,0.9,20)\n" +
            "// @param gradient _Gradient\n// @param curve _Curve\n" +
            "float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color * _Amount; }\n";
        var defaults = (List<ShaderFXParameter>)Type("ShaderFXMetadata").GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, code, defaults });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            fx.Active = false;
            var values = Values(fx);
            var expected = values.ToDictionary(p => p.name, p => JsonUtility.ToJson(p));
            var shader = typeof(ShaderFX).GetField("compiledShader", F).GetValue(fx);
            foreach (var p in values)
            {
                p.floatValue = 99; p.colorValue = Color.red; p.vectorValue = Vector4.one * -5;
                p.textureValue = Texture2D.blackTexture; p.textureSource = ShaderFXTextureSource.Layer; p.textureLayerId = "changed";
                p.gradientValue = null; p.curveValue = null; p.transformValue = new ShaderFXTransform { rotation = 165 };
            }
            var changed = values.ToDictionary(p => p.name, p => JsonUtility.ToJson(p));
            var layer = new Layer(new ColorFillLayerBehaviour());
            typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
            document.layers.Add(layer); layer.fx.Add(fx);
            string undoName = null;
            var viewType = Type("LayerShaderFXView");
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] {
                layer, document, new Action<string, Action>((name, change) => { undoName = name; change(); }) }, null);
            var menu = new DropdownMenu();
            viewType.GetMethod("BuildContextMenu", F).Invoke(view, new object[] { menu, 0 });
            var reset = menu.MenuItems().OfType<DropdownMenuAction>().Single(a => a.name == "Reset");
            reset.UpdateActionStatus(null);
            t.Equal(DropdownMenuAction.Status.Normal, reset.status, "Right-click menu offers Reset for a declared FX");
            Undo.IncrementCurrentGroup();
            reset.Execute(); Undo.FlushUndoRecordObjects();
            t.Equal("Reset FX", undoName, "Menu uses the existing layer change path");
            foreach (var p in Values(fx)) t.Equal(expected[p.name], JsonUtility.ToJson(p), "Default value, identity and controls restored: " + p.name);
            t.True(!fx.Active, "Reset preserves disabled state");
            t.Equal(code, typeof(ShaderFX).GetProperty("Code", F).GetValue(fx), "Reset does not modify code");
            t.True(ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", F).GetValue(fx)), "Reset reuses the compiled shader");
            t.True(!(bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(fx), "Reset needs no Apply/recompile");
            Undo.PerformUndo();
            foreach (var p in Values(fx)) t.Equal(changed[p.name], JsonUtility.ToJson(p), "One Undo restores every changed parameter: " + p.name);
            Undo.PerformRedo();
            foreach (var p in Values(fx)) t.Equal(expected[p.name], JsonUtility.ToJson(p), "Redo restores the same defaults: " + p.name);
            Undo.ClearUndo(fx);

            Values(fx).Reverse();
            t.True(Reset(fx), "Reset matches names rather than indices");
            foreach (var p in Values(fx)) t.Equal(expected[p.name], JsonUtility.ToJson(p), "Reordered parameters retain identity: " + p.name);
            Undo.ClearUndo(fx);
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(fx, new object[] { "// @param broken _Amount\n" });
            string before = JsonUtility.ToJson(fx);
            t.True(!CanReset(fx) && !Reset(fx), "Malformed declarations disable Reset");
            t.Equal(before, JsonUtility.ToJson(fx), "Rejected reset leaves the FX untouched");
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(fx, new object[] { "// @param float _Other = 1\n" });
            t.True(!CanReset(fx) && !Reset(fx), "Unapplied incompatible declarations cannot erase existing values");
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(fx, new object[] { "float4 ApplyFX(PinnedUV _pinnedUv, float4 c) { float2 uv = _pinnedUv; return c; }" });
            t.True(!CanReset(fx), "No declarations means nothing to reset");
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(fx, new object[] { code });
            document.layers.Remove(layer);
            Values(fx)[0].floatValue = 99;
            viewType.GetMethod("Reset", F).Invoke(view, new object[] { 0 });
            t.Equal(99f, Values(fx)[0].floatValue, "A stale menu cannot mutate a removed layer");
            document.layers.Add(layer); layer.fx[0] = null;
            t.True(!(bool)viewType.GetMethod("CanReset", F).Invoke(view, new object[] { 0 }), "Empty references have no parameter defaults");
        }
        finally
        {
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document);
        }
    });
}
