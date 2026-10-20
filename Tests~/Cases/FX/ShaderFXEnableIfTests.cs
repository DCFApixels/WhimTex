using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderFXEnableIfTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly A = typeof(ShaderFX).Assembly;
    static Type T(string name) => A.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Parse(string code) => (List<ShaderFXParameter>)T("ShaderFXMetadata").GetMethod("Parse", F)
        .Invoke(null, new object[] { code, false, null });
    const string Code = @"
// @control(_Amount)
// @param bool _Visible = true
// @param enum _Mode = Basic {Basic: 0, Advanced: 1}
// @group(Settings; _Enabled)
// @param hidden bool _Enabled = false
// @enableif _Enabled == 1
// @header(Correction)
// @helpbox(Conditional controls)
// @param preview(Preview) float _Amount = 0.25 [0 .. 1]
// @enableif _Mode != 0
// @if _Visible == 1
// @param float _Detail = 0.5 [0 .. 1]
// @endif
// @endenableif
// @preview(Preview)
// @endenableif
// @if _Visible == 1
// @enableif _Enabled == 1
// @param float _Extra = 0.75 [0 .. 1]
// @endenableif
// @endif
// @endgroup
float4 Preview(float2 uv) { return float4(uv.x, _Amount, 0, 1); }
float4 ApplyFX(float2 uv, float4 color) { return color * _Amount; }
";

    public static string Run() => TestContext.Run("Enableif metadata, inspector and persistence", t =>
    {
        var values = Parse(Code);
        var amount = values.Find(p => p.name == "_Amount");
        var detail = values.Find(p => p.name == "_Detail");
        t.Equal(1, amount.controls[0].enableIf.Length, "One availability condition");
        t.True(string.IsNullOrEmpty(amount.controls[0].visibleIfParameter), "Availability does not imply hiding");
        t.Equal(2, detail.controls[0].enableIf.Length, "Nested availability conditions retained");
        t.Equal("_Visible", detail.controls[0].visibleIfParameter, "Visibility composes with availability");
        var copy = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", F).Invoke(detail, null);
        t.True(!ReferenceEquals(copy.controls[0].enableIf, detail.controls[0].enableIf), "Copied control owns its condition array");
        var native = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(detail));
        for (int i = 0; i < 2; i++) t.True(detail.controls[0].enableIf[i].Equals(native.controls[0].enableIf[i]), "Native condition roundtrip");
        object[] args = { Code, false, null, null };
        T("ShaderFXMetadata").GetMethod("ParseWithPreviews", F).Invoke(null, args);
        t.Equal(1, ((List<ShaderFXParameterControl>)args[3])[0].enableIf.Length, "Standalone preview retains availability");
        foreach (string invalid in new[] {
            "// @endenableif",
            "// @enableif _Enabled == 1\n// @param float _Amount",
            "// @param bool _Enabled\n// @enableif _Enabled > 0\n// @param float _Amount\n// @endenableif",
            "// @param bool _Enabled\n// @enableif _Enabled == NaN\n// @param float _Amount\n// @endenableif",
            "// @enableif _Missing == 1\n// @param float _Amount\n// @endenableif",
            "// @param float2 _Vector\n// @enableif _Vector == 1\n// @param float _Amount\n// @endenableif",
            "// @param bool _Enabled\n// @if _Enabled == 1\n// @enableif _Enabled == 1\n// @param float _Amount\n// @endif\n// @endenableif",
            "// @param bool _Enabled\n// @enableif _Enabled == 1\n// @group(A)\n// @param float _Amount\n// @endgroup\n// @endenableif",
            "// @group(A)\n// @param bool _Enabled\n// @enableif _Enabled == 1\n// @param float _Amount\n// @endgroup\n// @endenableif",
            "// @param bool _Driver\n// @group(A; _Enabled)\n// @enableif _Driver == 1\n// @param bool _Enabled\n// @endenableif\n// @endgroup",
            "// @param bool _Enabled\n// @enableif _Enabled == 1\n// @param float _Amount\n// @endenableif extra"
        })
        {
            bool rejected = false;
            try { Parse(invalid); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            t.True(rejected, "Malformed availability rejected: " + invalid);
        }
        t.Equal(0, Parse("// @enableif _Missing == 1\n// @endenableif").Count, "Empty blocks have no value dependencies");
        ShaderFX fx = null;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        VisualElement view = null;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, Code, values });
            values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            view = (VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new object[] { fx }, null);
            var headerView = (VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new object[] { fx, true, null, null, -1 }, null);
            void Refresh() { view.GetType().GetMethod("Refresh", F).Invoke(view, null); headerView.GetType().GetMethod("Refresh", F).Invoke(headerView, null); }
            Slider Field(string label) => view.Query<Slider>().ToList().Find(f => f.label == label);
            var amountField = Field("Amount"); var detailField = Field("Detail"); var extraField = Field("Extra");
            t.True(amountField != null && detailField != null && extraField != null, "Disabled controls remain in the inspector");
            t.True(!amountField.enabledInHierarchy && amountField.parent.style.display.value == DisplayStyle.Flex, "Disabled row stays visible");
            t.True(!headerView.enabledSelf, "FX header control obeys availability");
            t.True(!amountField.parent.Q<Label>(className: "whimtex-fx-parameter-header").enabledInHierarchy &&
                !amountField.parent.Q<HelpBox>().enabledInHierarchy, "Row heading and helpbox share disabled state");
            var preview = view.Q<Image>(className: "whimtex-fx-preview--standalone");
            t.True(preview != null && !preview.enabledInHierarchy, "Standalone preview stays present and disabled");
            var linked = view.Q<Toggle>(className: "whimtex-fx-parameter-group-toggle");
            t.True(linked.enabledInHierarchy, "Driver header remains available");
            values.Find(p => p.name == "_Enabled").floatValue = 1; Refresh();
            t.True(amountField.enabledInHierarchy && extraField.enabledInHierarchy && preview.enabledInHierarchy && headerView.enabledSelf, "Outer condition enables controls and previews");
            t.True(!detailField.enabledInHierarchy, "All nested conditions must pass");
            values.Find(p => p.name == "_Mode").floatValue = 1; Refresh();
            t.True(detailField.enabledInHierarchy, "Not-equal enum condition enables detail");
            values.Find(p => p.name == "_Visible").floatValue = 0; Refresh();
            t.True(detailField.parent.ClassListContains("whimtex-fx-conditional-parameter--hidden") && amountField.enabledInHierarchy, "Visibility remains independent");
            var parent = new VisualElement(); parent.Add(view); parent.SetEnabled(false); Refresh();
            t.True(!amountField.enabledInHierarchy && !linked.enabledInHierarchy, "Availability cannot override a disabled ancestor");
            parent.SetEnabled(true); values.Find(p => p.name == "_Enabled").floatValue = 0; Refresh();
            t.Near(.25, values.Find(p => p.name == "_Amount").floatValue, 0, "Availability refresh leaves the stored value untouched");
            string exported = (string)T("ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { fx, "Tests/Availability" });
            var restored = Parse(exported).Find(p => p.name == "_Detail").controls[0];
            t.True(restored.enableIf.Length == 2 && restored.enableIf[1].notEqual && restored.visibleIfParameter == "_Visible", "Preset retains both condition types");
            var layer = new Layer(new ColorFillLayerBehaviour());
            typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null); layer.fx.Add(fx); document.layers.Add(layer);
            foreach (var mode in new[] { WhimTexJsonWriteMode.Full, WhimTexJsonWriteMode.FullOptimized, WhimTexJsonWriteMode.Compact })
            {
                string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using (var saved = WhimTexDocumentJson.Read(json))
                {
                    var p = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(saved.Document.layers[0].fx[0]);
                    t.True(p.Find(v => v.name == "_Detail").controls[0].enableIf.Length == 2, "JSON retains availability in " + mode);
                    t.Near(.25, p.Find(v => v.name == "_Amount").floatValue, 0, "JSON preserves disabled value in " + mode);
                }
            }
            layer.fx.Clear(); document.layers.Clear();
        }
        finally
        {
            view?.RemoveFromHierarchy(); document.layers.Clear();
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            UnityEngine.Object.DestroyImmediate(document);
        }
    });
}
