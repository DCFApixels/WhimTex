using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderFXCodeInfoTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static string Run() => TestContext.Run("FX Code pixel-local state, reference and Compile naming", t =>
    {
        WhimTexDocument document = null;
        ShaderFX effect = null;
        SerializedObject data = null;
        VisualElement view = null;
        var shaders = new List<Shader>();
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, "float4 ApplyFX(float4 color) { return color; }", new List<ShaderFXParameter>() });
            effect.hideFlags = HideFlags.HideAndDontSave;
            data = new SerializedObject(effect);
            var editor = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXEditor", true);
            view = (VisualElement)editor.GetMethod("BuildView", F).Invoke(null, new object[] { effect, data, null, null, -1 });
            var code = view.Q<Foldout>("shaderFXCodeFoldout");
            var compile = code.Q<Button>("shaderFXCompile");
            var info = code.Q<VisualElement>(className: "whimtex-fx-code-info");
            var value = code.Q<Label>("shaderFXPixelLocal");
            var note = code.Q<Label>("shaderFXPixelLocalContext");
            t.Equal("Compile", compile.text, "Only the code-compilation command uses Compile");
            t.True(compile.tooltip.Contains("Does not bake"), "Compile tooltip distinguishes baking");
            t.Equal("Not compiled", value.text, "Fresh FX is not presented as confirmed nonlocal");
            t.True(ReferenceEquals(code, info.GetFirstAncestorOfType<Foldout>()), "Pixel-local row is inside Code");
            t.True(value.GetFirstAncestorOfType<TextField>() == null, "Status is outside the code text area");
            var area = code.Q<TextField>("shaderFXCode");
            string ElementPath(VisualElement element)
            {
                string result = "";
                for (; element != null; element = element.parent) result += element.GetType().Name + ":" + element.name + "/";
                return result;
            }
            var statusParent = value.parent.parent;
            t.True(ReferenceEquals(statusParent, area.parent) && ReferenceEquals(code, value.GetFirstAncestorOfType<Foldout>()),
                "Status and code area are siblings inside Code: " + ElementPath(value) + " versus " + ElementPath(area));
            var statusRow = value.hierarchy.parent;
            var physicalParent = statusRow.hierarchy.parent;
            t.True(ReferenceEquals(physicalParent, area.hierarchy.parent), "Status and code area share their physical layout container");
            int statusIndex = physicalParent.hierarchy.IndexOf(statusRow), areaIndex = physicalParent.hierarchy.IndexOf(area);
            t.True(statusIndex >= 0 && areaIndex > statusIndex, "Status precedes the code area inside Code: " + statusIndex + " before " + areaIndex);
            var reference = info.Q<Foldout>("shaderFXInputs");
            t.Equal("Shader Inputs", reference.text, "Reference is discoverable by name");
            t.True(!reference.value, "Long reference starts collapsed");
            t.Equal(5, reference.Query<Label>(className: "whimtex-fx-reference-heading").ToList().Count, "Reference is divided into five sections");
            t.True(reference.Query<Label>(className: "whimtex-fx-reference-bullet").ToList().Count >= 15, "Enumerated API entries are bulleted");
            var bypassNote = reference.Q<Label>(className: "whimtex-fx-reference-note");
            t.True(bypassNote != null && bypassNote.parent.ClassListContains("whimtex-fx-reference-section"), "Bypass warning is a separate note, not a list item");
            string bypassText = Regex.Replace(bypassNote.text, "<[^>]*>", "");
            t.True(bypassText.Contains("tex2D, .Sample, .Load") && bypassText.Contains("not recommended"), "Bypass note lists paths with commas and discourages their use");
            var paragraphs = reference.Query<Label>(className: "whimtex-fx-reference-text").ToList();
            string plain = "";
            foreach (var paragraph in paragraphs)
            {
                t.True(paragraph.enableRichText && paragraph.text.Contains("<mark=") && paragraph.text.Contains("<b>"), "Identifiers are highlighted with supported rich text");
                plain += Regex.Replace(paragraph.text, "<[^>]*>", "") + "\n";
            }
            foreach (string token in new[] { "ApplyFX(float4 color)", "ApplyFX(PinnedUV uv, float4 color)",
                "ApplyFX(float2 uv, float4 color)", "SampleInput(uv)", "SampleTexture(_Mask, uv)", "Layer, Self and None",
                "_MainTex_TexelSize", "_InputSize", "_CanvasSize", "_RenderScale", "_PixelScale", "@param px",
                "LayerToLocal", "_Name_Sample", "_Name_ToLocal", "#include", "FastNoiseLite", "bypass", "straight" })
                t.True(plain.Contains(token), "Current contract is documented: " + token);
            t.True(plain.Contains("SampleTexture(_MainTex, uv)") && plain.Contains("is equivalent to SampleInput(uv)"), "Reference explains pinned incoming-sampler reads");
            t.True(plain.Contains("metadata is not a texture sample"), "Size metadata is distinguished from image reads");

            void Refresh()
            {
                bool pending = (bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(effect);
                info.GetType().GetMethod("Refresh", F).Invoke(info, new object[] { effect, pending });
            }
            void Compile()
            {
                try { typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null); }
                finally
                {
                    var shader = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(effect);
                    if (shader != null && !shaders.Contains(shader)) shaders.Add(shader);
                }
                Refresh();
            }
            void Draft(string source) => typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { source });
            Compile();
            t.Equal("Yes", value.text, "Color-only compiled shader is pixel-local");
            t.True(value.ClassListContains("whimtex-fx-pixel-local-value--yes"), "Confirmed status has its own styling");
            effect.Active = false; Refresh();
            t.Equal("Yes", value.text, "Disabling FX does not change its compiled contract");
            Draft("float4 ApplyFX(float2 uv, float4 color) { return color; }"); Refresh();
            t.Equal("Yes", value.text, "Spatial draft cannot replace the compiled answer");
            t.True(note.text.Contains("draft changed"), "Pending state explains which shader is described");
            Compile();
            t.Equal("No", value.text, "Spatial signature is not pixel-local");
            t.True(!value.ClassListContains("whimtex-fx-pixel-local-value--yes"), "Old confirmed styling is cleared");
            Draft("float4 ApplyFX(float2 uv) { return 1; }");
            bool failed = false;
            try { Compile(); } catch (TargetInvocationException) { failed = true; }
            Refresh();
            t.True(failed, "Malformed signature fails compilation");
            t.Equal("Unavailable", value.text, "Failed compilation is not reported as a valid No");
            Draft("float4 ApplyFX(PinnedUV uv, float4 color) { return SampleInput(uv); }"); Compile();
            t.Equal("Yes", value.text, "Successful pinned compilation restores the status");
            t.Equal("Compiled shader", note.text, "Successful compilation clears pending context");
        }
        finally
        {
            view?.Unbind();
            data?.Dispose();
            if (effect != null) UnityEngine.Object.DestroyImmediate(effect);
            foreach (var shader in shaders) if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
            if (document != null) UnityEngine.Object.DestroyImmediate(document);
        }
    });
}
