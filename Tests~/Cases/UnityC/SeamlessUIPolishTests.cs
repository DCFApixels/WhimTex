using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Mode = DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.SeamlessMode;
using Edges = DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.PoissonEdges;

public static class SeamlessUIPolishTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;


    static string Begin(WhimTex.Tests.UnityC.AsyncFixture job)
    {
        int checks = 0;
        void Check(bool condition, string message) { job.Context.True(condition, message); checks++; }
        var focus = EditorWindow.focusedWindow;
        var window = job.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());
        var doc = job.Scope.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        var model = MakeSeamlessLayerBehaviour.CreateDefault(); doc.layers.Add(model);
        var assembly = typeof(TextureCompositor).Assembly;
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
        void Cleanup() => job.DisposeOwned();
        job.OwnCleanup(() => { job.Scope.CloseWindow(window); Undo.ClearUndo(doc); job.Scope.Destroy(doc); if (focus != null) focus.Focus(); });
        
        try
        {
            window.ShowUtility(); window.position = new Rect(100, 100, 320, 900);
            var root = window.rootVisualElement;
            assembly.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ApplyWindowStyles", F).Invoke(null, new object[] { root });
            var bindings = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"), true);
            void Refresh() => bindings.GetType().GetMethod("Refresh", F).Invoke(bindings, new object[] { true });
            Action<string, Action> apply = (label, change) => { Undo.RegisterCompleteObjectUndo(doc, label); change(); Refresh(); };
            typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(doc, null);
            var targetView = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.EffectTargetSettingsView"), F, null,
                new object[] { doc, apply, bindings }, null);
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields", F).Invoke(null, new object[] { root, model, doc,
                apply, bindings, new Action<VisualElement, TargetedLayerBehaviour>((r, l) =>
                    targetView.GetType().GetMethod("Build", F).Invoke(targetView, new object[] { r, l })) });
            void Enabled(string id, bool expected) => Check(root.Q(id).enabledInHierarchy == expected, id + " availability");
            void Click(string id) { var b = root.Q<Button>(id); Check(b.enabledInHierarchy, id + " interactive"); WhimTex.Tests.UnityC.PublicInput.Click(b); }
            foreach (string label in new[] { "Copy Edges", "Mirror Direction", "Patch Edges", "Poisson Edges" })
                Check(root.Query<Label>().ToList().Exists(l => l.text == label), "Label " + label);
            var search = root.Q("quiltingSearchSettings");
            var correctionHelp = root.Q<HelpBox>("seamlessCorrectionHelp");
            string quiltHelpText = root.Q("quiltingOptions").Q<HelpBox>().text;
            foreach (var helpMode in new[] { Mode.Mirror, Mode.OffsetBlend, Mode.ScreenedPoisson, Mode.PatchQuilting })
                foreach (bool mirrorCorrection in new[] { false, true }) foreach (bool offsetCorrection in new[] { false, true })
                {
                    model.mode = helpMode; model.mirrorSeamCorrection = mirrorCorrection; model.offsetSeamCorrection = offsetCorrection; Refresh();
                    bool expected = helpMode == Mode.Mirror && mirrorCorrection || helpMode == Mode.OffsetBlend && offsetCorrection;
                    Check(correctionHelp.ClassListContains("whimtex-hidden") != expected, "Shared correction warning visibility");
                    Check(root.Q("quiltingOptions").Q<HelpBox>().text == quiltHelpText, "Quilting warning unchanged");
                }
            Check(quiltHelpText == "Check Tiled with Pencil selected before export: reduced previews can choose different patches.", "Original Quilting warning");
            model.mode = Mode.OffsetBlend; model.mirrorSeamCorrection = model.offsetSeamCorrection = true; Refresh();
            Check(search[0].name == "quiltingWidth" && search[1].name == "quiltingAlongSearch" &&
                search[2].name == "quiltingQuality" && search[3].name == "quiltingChannels" && search[4].Q<IntegerField>("quiltingSeed") != null, "Search order");
            model.leftEdge = model.rightEdge = model.topEdge = model.bottomEdge = false; Refresh();
            Enabled("seamlessEdgeWidth", false); Enabled("offsetContrastCompensation", false); Enabled("offsetSeamCorrection", true);
            Click("seamlessProcessing-left"); Enabled("seamlessEdgeWidth", true);
            model.offsetPoissonEdges = Edges.None; Refresh(); Enabled("offsetAutoRadius", false); Enabled("offsetCorrectionRadius", false);
            Click("offsetPoissonEdges-top"); Enabled("offsetAutoRadius", true); Enabled("offsetCorrectionRadius", false);
            root.Q<Toggle>("offsetAutoRadius").value = false; Enabled("offsetCorrectionRadius", true);
            model.mode = Mode.Mirror; model.horizontal = MakeSeamlessLayerBehaviour.HorizontalDirection.Off;
            model.vertical = MakeSeamlessLayerBehaviour.VerticalDirection.Off; Refresh();
            Enabled("mirrorBlendWidth", false); Enabled("mirrorContrastCompensation", false); Enabled("mirrorSeamCorrection", true);
            Click("seamlessEdge-left"); Enabled("mirrorBlendWidth", true);
            var mirrorAuto = root.Q<Toggle>("mirrorAutoRadius");
            var mirrorRadius = root.Q<Slider>("mirrorCorrectionRadius");
            Check(mirrorAuto.value, "Mirror auto default"); Enabled("mirrorCorrectionRadius", false);
            model.mirrorCorrectionRadius = .13f;
            root.Q<Slider>("mirrorBlendWidth").value = 40;
            Check(Math.Abs(mirrorRadius.value - 10) < .0001f, "Mirror auto tracks width");
            root.Q<Slider>("mirrorBlendWidth").value = .1f;
            Check(Math.Abs(mirrorRadius.value - .5f) < .0001f, "Mirror auto minimum");
            mirrorAuto.value = false; Enabled("mirrorCorrectionRadius", true);
            Check(Math.Abs(mirrorRadius.value - 13) < .0001f, "Mirror manual radius retained");
            model.mirrorPoissonEdges = Edges.None; Refresh(); Enabled("mirrorAutoRadius", false); Enabled("mirrorCorrectionRadius", false);
            Click("mirrorPoissonEdges-top"); Enabled("mirrorAutoRadius", true); Enabled("mirrorCorrectionRadius", true);
            model.mirrorSeamCorrection = false; Refresh();
            Check(mirrorAuto.ClassListContains("whimtex-hidden"), "Mirror auto hidden without correction");
            model.mirrorSeamCorrection = true; Refresh();
            model.mode = Mode.PatchQuilting; model.quiltingEdges = Edges.None; model.quiltingSeamCorrection = true;
            model.quiltingContrastCompensation = true; model.quiltingFeather = 0; model.quiltingContrast = .65f; Refresh();
            Enabled("quiltingQuality", false); Enabled("quiltingSeed", false); Enabled("quiltingFeather", false); Enabled("quiltingSeamCorrection", true);
            Click("quiltingEdges-left"); Enabled("quiltingQuality", true); Enabled("quiltingFeather", true);
            Enabled("quiltingContrastCompensation", false); Enabled("quiltingContrast", false);
            Check(root.Q("quiltingContrastSettings").tooltip.Contains("Feather"), "Disabled reason");
            var feather = root.Q<Slider>("quiltingFeather");
            Undo.IncrementCurrentGroup(); feather.value = 50; Undo.FlushUndoRecordObjects();
            Enabled("quiltingContrastCompensation", true); Enabled("quiltingContrast", true);
            Undo.PerformUndo(); Refresh(); Enabled("quiltingContrastCompensation", false);
            Undo.PerformRedo(); Refresh(); Enabled("quiltingContrastCompensation", true);
            Check(model.quiltingContrastCompensation && model.quiltingContrast == .65f, "Disabled settings preserved");
            model.quiltingPoissonEdges = Edges.None; Refresh(); Enabled("quiltingCorrectionRadius", false);
            Click("quiltingPoissonEdges-left"); Enabled("quiltingCorrectionRadius", true);
            model.mode = Mode.ScreenedPoisson; model.poissonEdges = Edges.None; Refresh(); Enabled("screenedSolveRadius", false);
            Click("poissonEdges-top"); Enabled("screenedSolveRadius", true);
            foreach (string id in new[] { "offsetCompensation", "mirrorCompensation", "quiltingContrast" })
                Check(root.Q<Slider>(id).label == "Strength (%)", "Strength label");
            foreach (string id in new[] { "screenedSolveRadius", "offsetCorrectionRadius", "mirrorCorrectionRadius", "quiltingCorrectionRadius" })
                Check(root.Q<Slider>(id).label == "Radius (%)", "Radius label");
            var modes = new[] { Mode.OffsetBlend, Mode.Mirror, Mode.ScreenedPoisson, Mode.PatchQuilting };
            int step = 0;
            void Prepare()
            {
                model.mode = modes[step % 4];
                model.offsetContrastCompensation = model.mirrorContrastCompensation = model.quiltingContrastCompensation = true;
                window.position = new Rect(100, 100, step < 4 ? 320 : 620, 900);
                Refresh(); job.Schedule(root, Measure, 150);
            }
            void Measure()
            {
                try
                {
                    var reference = root.Q("seamlessMethod");
                    var referenceLabel = reference.Q<Label>(className: "unity-base-field__label");
                    float labelX = referenceLabel.LocalToWorld(referenceLabel.contentRect.position).x;
                    float inputX = reference.Q(className: "unity-base-field__input").worldBound.xMin;
                    foreach (var field in root.Query<VisualElement>(className: "unity-base-field").ToList())
                    {
                        bool visible = true;
                        for (var p = field; p != null; p = p.parent) if (p.resolvedStyle.display == DisplayStyle.None) visible = false;
                        if (!visible) continue;
                        Check(float.IsFinite(field.worldBound.width) && field.worldBound.width > 0, "Laid out " + field.name);
                        Check(field.worldBound.xMin >= root.worldBound.xMin - 1 && field.worldBound.xMax <= root.worldBound.xMax + 1, "Horizontal bounds " + field.name);
                        var label = field.Q<Label>(className: "unity-base-field__label");
                        if (label == null || string.IsNullOrEmpty(label.text)) continue;
                        float actual = label.LocalToWorld(label.contentRect.position).x;
                        Check(Math.Abs(actual - labelX) < .5f, $"Label alignment {label.text}: {actual} vs {labelX}");
                        var input = field.Q(className: "unity-base-field__input");
                        Check(Math.Abs(input.worldBound.xMin - inputX) < .5f, $"Input alignment {label.text}: {input.worldBound.xMin} vs {inputX}");
                        Check(Math.Abs(label.worldBound.center.y - input.worldBound.center.y) < 1.1f, $"Vertical alignment {label.text}");
                    }
                    foreach (var label in root.Query<Label>(className: "whimtex-seamless-heading").ToList())
                    {
                        bool visible = true;
                        for (var p = (VisualElement)label; p != null; p = p.parent) if (p.resolvedStyle.display == DisplayStyle.None) visible = false;
                        if (visible) Check(Math.Abs(label.LocalToWorld(label.contentRect.position).x - labelX) < .5f, "Heading alignment " + label.text);
                    }
                    if (++step < 8) { Prepare(); return; }
                    job.Pass(); Cleanup();
                }
                catch (Exception e) { job.Fail(e); Cleanup(); }
            }
            Prepare(); return job.Read();
        }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }

    public static string Poll(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Poll(runId);
    public static string Result(string runId) => Poll(runId);
    public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cancel(runId);
    public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cleanup(runId);

    public static string Start(string runId)
    {
        var job = WhimTex.Tests.UnityC.AsyncFixture.Create(runId);
        try { return Begin(job); }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }
}
