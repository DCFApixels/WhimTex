using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class HeightPreviewUiTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, F).Invoke(obj, args);
    static object Get(object obj, string field) => obj.GetType().GetField(field, F)?.GetValue(obj) ?? obj.GetType().GetProperty(field, F)?.GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, F).SetValue(obj, value);
    public static string Start(string id)
    {
        var job = AsyncFixture.Create(id);
        try
        {
            var asm = typeof(WhimTexDocument).Assembly;
            var window = job.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());
            var doc = job.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = 512; doc.height = 1024;
            Layer layer = new NoiseLayerBehaviour(); doc.layers.Add(layer); Call(doc, "NormalizeModel");
            job.OwnCleanup(() => Call(layer, "ReleaseTransientResources"));
            var root = window.rootVisualElement;
            asm.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ApplyWindowStyles", F).Invoke(null, new object[] { root });
            root.style.flexDirection = FlexDirection.Row;
            var panels = new List<VisualElement>();
            foreach (int width in new[] { 200, 300, 400 })
            {
                var state = Activator.CreateInstance(asm.GetType("DCFApixels.WhimTex.LayerPreviewPanel+ViewState"), true);
                Set(state, "height", 240f);
                var panel = (VisualElement)Activator.CreateInstance(asm.GetType("DCFApixels.WhimTex.LayerPreviewPanel"), F, null, new[] { state, (object)(width != 300), false }, null);
                job.Scope.OwnDisposable((IDisposable)panel); panels.Add(panel);
                panel.style.width = width; panel.style.height = 240; panel.style.flexShrink = 0;
                root.Add(panel); Call(panel, "Bind", doc, layer);
            }
            window.titleContent = new GUIContent("WhimTex Height preview check");
            window.ShowUtility(); window.position = new Rect(90, 90, 900, 280);
            int undo = Undo.GetCurrentGroup(); bool dirty = EditorUtility.IsDirty(doc);
            bool settingsChecked = false;
            void CheckLayout(VisualElement panel)
            {
                var header = (VisualElement)Get(panel, "Header");
                var controls = (VisualElement)Get(panel, "HeaderControls");
                job.Context.True(controls.worldBound.xMin >= header.worldBound.xMin && controls.worldBound.xMax <= header.worldBound.xMax + .5f, "Controls fit even at 200 px");
                var image = panel.Q<ToolbarToggle>("layer-preview-image");
                var height = panel.Q<ToolbarToggle>("layer-preview-height");
                job.Context.True(image.worldBound.width > 12 && height.worldBound.width > 12, "Both mode buttons have usable hitboxes");
                foreach (var toggle in new[] { image, height })
                {
                    job.Context.True(toggle.worldBound.yMin >= header.worldBound.yMin - 1 && toggle.worldBound.yMax <= header.worldBound.yMax + 1, "Native toolbar toggle fits header height");
                    var icon = toggle.Q(className: "whimtex-layer-preview-mode-icon");
                    job.Context.True(icon.worldBound.width >= 15 && icon.worldBound.height >= 15, "Mode icon is not squeezed");
                }
            }
            void ImageAgain()
            {
                try
                {
                    foreach (var p in panels)
                    {
                        CheckLayout(p);
                        job.Context.True(((RenderTexture)Get(p, "source")).height <= 256, "Image source is reduced again");
                        var renderer = Get(p, "heightRenderer");
                        job.Context.True(Get(renderer, "Geometry") == null && Get(renderer, "Output") == null, "Image releases all relief outputs");
                    }
                    job.Context.Equal(undo, Undo.GetCurrentGroup(), "View mode creates no Undo");
                    job.Context.Equal(dirty, EditorUtility.IsDirty(doc), "View mode does not dirty document");
                    job.Pass();
                }
                catch (Exception error) { job.Fail(error); }
            }
            void HeightReady()
            {
                try
                {
                    foreach (var p in panels)
                    {
                        CheckLayout(p);
                        job.Context.True(p.Q<ToolbarToggle>("layer-preview-height").value && !p.Q<ToolbarToggle>("layer-preview-image").value, "Mutually exclusive modes");
                        var source = (RenderTexture)Get(p, "source");
                        job.Context.True(source != null && source.width == 256 && source.height == 1024, "Native-depth source reaches real UI");
                        var renderer = Get(p, "heightRenderer");
                        var geometry = Get(renderer, "Geometry");
                        Call(p, "ToggleChannel", 1);
                        job.Context.True(ReferenceEquals(geometry, Get(renderer, "Geometry")), "Channel changes keep relief");
                        var retained = Get(p, "source"); Call(p, "Tick");
                        job.Context.True(ReferenceEquals(retained, Get(p, "source")), "Idle preview is reused");
                    }
                    var button = panels[0].Q<ToolbarButton>("layer-preview-height-settings");
                    using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
                    job.Context.True(((PopupWindowContent)Get(panels[0], "heightSettings")).editorWindow != null,
                        "Settings popup exists immediately after submit; focused=" + EditorWindow.focusedWindow?.titleContent?.text);
                    var popup = ((PopupWindowContent)Get(panels[0], "heightSettings")).editorWindow;
                    void CheckWhenLaidOut()
                    {
                        if (settingsChecked || popup == null || !(popup.rootVisualElement.Q<FloatField>("height-scale").worldBound.width > 20)) return;
                        settingsChecked = true; CheckSettings();
                    }
                    popup.rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ => CheckWhenLaidOut());
                    CheckWhenLaidOut();
                    job.Schedule(root, () => { if (!settingsChecked) job.Fail(new InvalidOperationException("Settings closed before first layout")); }, 1000);
                }
                catch (Exception error) { job.Fail(error); }
            }
            void CheckSettings()
            {
                try
                {
                    var p = panels[0];
                    var popup = ((PopupWindowContent)Get(p, "heightSettings")).editorWindow;
                    job.Context.True(popup != null, "Settings button opens its popup");
                    var area = popup.rootVisualElement;
                    var channel = area.Q<EnumField>("height-channel");
                    var scale = area.Q<FloatField>("height-scale");
                    var texture = area.Q<Toggle>("height-texture");
                    var range = area.Q<Toggle>("height-highlight-range");
                    foreach (var field in new VisualElement[] { channel, scale, texture, range })
                        job.Context.True(field.worldBound.yMax <= area.worldBound.yMax + .5f && field.worldBound.xMax <= area.worldBound.xMax + .5f,
                            "Native settings fields stay inside popup: " + field.name + " field=" + field.worldBound + " root=" + area.worldBound + " window=" + popup.position);
                    job.Context.True(scale.Q(className: "unity-base-field__input").worldBound.width > 50, "Height Scale input remains usable");
                    job.Context.True(texture.value, "Texture enabled by default");
                    job.Context.True(!range.value, "Range highlight disabled by default");
                    var geometry = Get(Get(p, "heightRenderer"), "Geometry");
                    var source = Get(p, "source");
                    range.value = true;
                    job.Context.True((bool)Get(Get(p, "state"), "highlightOutOfRange"), "Range toggle updates preview state");
                    job.Context.True(ReferenceEquals(geometry, Get(Get(p, "heightRenderer"), "Geometry")) && ReferenceEquals(source, Get(p, "source")), "Range toggle reuses geometry and source");
                    channel.value = (Enum)Enum.Parse(channel.value.GetType(), "A"); scale.value = 1.25f; texture.value = false;
                    var state = Get(p, "state");
                    job.Context.Equal("A", Get(state, "heightChannel").ToString(), "Channel setting applied");
                    job.Context.Equal(1.25f, (float)Get(state, "heightScale"), "Scale setting applied");
                    job.Context.True(!(bool)Get(state, "heightTexture"), "Neutral surface setting applied");
                    foreach (var panel in panels) panel.Q<ToolbarToggle>("layer-preview-image").value = true;
                    job.Context.True(popup == null, "Changing mode closes owned settings window");
                    job.Context.Equal(1.25f, (float)Get(state, "heightScale"), "Settings survive mode switch");
                    job.Context.True((bool)Get(state, "highlightOutOfRange"), "Range highlight survives mode switch");
                    job.Schedule(root, ImageAgain, 500);
                }
                catch (Exception error) { job.Fail(error); }
            }
            job.Schedule(root, () =>
            {
                try
                {
                    foreach (var p in panels) { CheckLayout(p); p.Q<ToolbarToggle>("layer-preview-height").value = true; }
                    job.Schedule(root, HeightReady, 500);
                }
                catch (Exception error) { job.Fail(error); }
            }, 600);
            return job.Read();
        }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);
}
