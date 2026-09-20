using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class OutputSettingsLayoutSmoke
{
    public static async Task<string> Main()
    {
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        var type = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexOutputSettingsWindow", true);
        var documentField = type.GetField("document", BindingFlags.Instance | BindingFlags.NonPublic);
        EditorWindow window = null;
        try
        {
            using (var data = new SerializedObject(document))
            {
                data.FindProperty("outputSettings.storage").intValue = (int)TextureCompositor.OutputStorage.LinearRgba32;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            type.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { document });
            foreach (EditorWindow candidate in Resources.FindObjectsOfTypeAll(type))
                if ((TextureCompositor)documentField.GetValue(candidate) == document) window = candidate;
            if (window == null) throw new Exception("No settings window");
            foreach (int width in new[] { 420, 760 })
            {
                window.position = new Rect(100, 100, width, width == 420 ? 380 : 900);
                window.Repaint();
                await Task.Delay(400);
                var root = window.rootVisualElement;
                var footer = root.Q<VisualElement>("output-preview-footer");
                if (footer == null || footer.worldBound.height < 100 || footer.worldBound.yMax > root.worldBound.yMax + 1)
                    throw new Exception("Preview footer is missing or outside the window");
                if (root.Q<ScrollView>().Contains(footer)) throw new Exception("Preview scrolls with settings");
                var overlay = root.Q<Label>("output-preview-info");
                if (overlay.worldBound.yMax > footer.worldBound.yMax + 1) throw new Exception("Preview info overflow");
                var shadow = root.Q<Label>(className: "whimtex-output-preview-shadow");
                if (shadow == null || shadow.text != overlay.text || string.IsNullOrEmpty(shadow.text))
                    throw new Exception("Shadow label text missing or out of sync");
                if (Mathf.Abs(shadow.worldBound.x - overlay.worldBound.x - 1) > .1f ||
                    Mathf.Abs(shadow.worldBound.y - overlay.worldBound.y - 2) > .1f ||
                    Mathf.Abs(shadow.worldBound.width - overlay.worldBound.width) > .1f)
                    throw new Exception("Shadow offset or wrapping width incorrect");
                float column = root.Q<VisualElement>("output-row-Storage").ElementAt(1).worldBound.x;
                foreach (var row in root.Query<VisualElement>(className: "whimtex-output-row").ToList())
                {
                    if (row.worldBound.height < 1) continue;
                    var control = row.ElementAt(1);
                    if (control.worldBound.width < 50 || control.worldBound.xMax > row.worldBound.xMax + 1)
                        throw new Exception(width + "px overflow/collapse: " + row.name + " " + control.worldBound);
                    if (Mathf.Abs(control.worldBound.x - column) > 4)
                        throw new Exception("Misaligned field: " + row.name);
                }
                var storageLabel = root.Q<VisualElement>("output-row-Storage").ElementAt(0);
                var advancedLabel = root.Q<VisualElement>("output-row-Read/Write").ElementAt(0);
                if (advancedLabel.resolvedStyle.paddingLeft <= storageLabel.resolvedStyle.paddingLeft)
                    throw new Exception("Advanced indentation missing");
                float checkX = root.Q<VisualElement>("output-row-sRGB (Color Texture)").Q<Toggle>().Q(className: "unity-toggle__checkmark").worldBound.x;
                float alphaX = root.Q<VisualElement>("output-row-Alpha Is Transparency").Q<Toggle>().Q(className: "unity-toggle__checkmark").worldBound.x;
                if (Mathf.Abs(checkX - alphaX) > 1) throw new Exception("Checkbox misalignment");
            }
            var srgb = window.rootVisualElement.Q<VisualElement>("output-row-sRGB (Color Texture)").Q<Toggle>();
            srgb.value = true;
            using (var data = new SerializedObject(document))
                if (data.FindProperty("outputSettings.storage").intValue != (int)TextureCompositor.OutputStorage.SrgbRgba32)
                    throw new Exception("sRGB edit not saved");
            if (window.rootVisualElement.Q<VisualElement>("output-row-Compression") != null ||
                window.rootVisualElement.Q<VisualElement>("output-row-Format") != null)
                throw new Exception("Legacy output compression controls should not be shown");
            var footerElement = window.rootVisualElement.Q<VisualElement>("output-preview-footer");
            var surface = window.rootVisualElement.Q<VisualElement>(className: "whimtex-output-preview-surface");
            var resizePreview = type.GetMethod("ResizePreview", BindingFlags.Instance | BindingFlags.NonPublic);
            resizePreview.Invoke(window, new object[] { 110f, false });
            await Task.Delay(150);
            if (Mathf.Abs(footerElement.resolvedStyle.height - 120) > 1 || surface.resolvedStyle.display == DisplayStyle.None)
                throw new Exception("Preview did not stop at minimum height before collapse");
            resizePreview.Invoke(window, new object[] { 90f, false });
            await Task.Delay(150);
            if (Mathf.Abs(footerElement.resolvedStyle.height - 20) > 1 || surface.resolvedStyle.display != DisplayStyle.None)
                throw new Exception("Collapsed preview must leave only the header");
            var infoLabel = window.rootVisualElement.Q<Label>("output-preview-info");
            infoLabel.text = "unchanged while hidden";
            type.GetMethod("RefreshOutputInfo", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            if (infoLabel.text != "unchanged while hidden") throw new Exception("Hidden preview is still updating");
            resizePreview.Invoke(window, new object[] { 40f, true });
            await Task.Delay(150);
            if (surface.resolvedStyle.display != DisplayStyle.None) throw new Exception("Reopened below drag threshold");
            resizePreview.Invoke(window, new object[] { 50f, true });
            await Task.Delay(150);
            if (surface.resolvedStyle.display == DisplayStyle.None || Mathf.Abs(footerElement.resolvedStyle.height - 120) > 1 ||
                infoLabel.text == "unchanged while hidden") throw new Exception("Preview did not restore and refresh");
            var header = window.rootVisualElement.Q<VisualElement>(className: "whimtex-output-preview-title");
            foreach (float y in new[] { 2f, 10f, 18f })
            {
                var hit = header.panel.Pick(new Vector2(header.worldBound.center.x, header.worldBound.y + y));
                if (hit != header) throw new Exception("Preview header is not fully draggable");
            }
            Vector2 dragStart = header.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = dragStart }))
                header.SendEvent(down);
            await Task.Delay(50);
            using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = dragStart + Vector2.up * 40 }))
                header.SendEvent(move);
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = dragStart + Vector2.up * 40 }))
                header.SendEvent(up);
            await Task.Delay(150);
            if (surface.resolvedStyle.display != DisplayStyle.None) throw new Exception("Pointer drag did not collapse preview");
            if (header.HasPointerCapture(PointerId.mousePointerId)) throw new Exception("Pointer capture was not released");
            dragStart = header.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = dragStart }))
                header.SendEvent(down);
            await Task.Delay(50);
            using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = dragStart - Vector2.up * 50 }))
                header.SendEvent(move);
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = dragStart - Vector2.up * 50 }))
                header.SendEvent(up);
            await Task.Delay(150);
            if (surface.resolvedStyle.display == DisplayStyle.None) throw new Exception("Pointer drag did not restore preview");
            var applyButton = window.rootVisualElement.Q<Button>(className: "whimtex-output-apply");
            var refreshApply = type.GetMethod("RefreshApplyState", BindingFlags.Instance | BindingFlags.NonPublic);
            typeof(TextureCompositor).GetField("savedOutputSettings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document,
                typeof(TextureCompositor).GetMethod("CaptureOutputSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(document, null));
            type.GetField("lastDirtyCount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, int.MinValue);
            EditorUtility.ClearDirty(document);
            refreshApply.Invoke(window, null);
            if (applyButton.ClassListContains("whimtex-output-apply--pending")) throw new Exception("Clean document highlighted");
            EditorUtility.SetDirty(document);
            refreshApply.Invoke(window, null);
            if (!applyButton.ClassListContains("whimtex-output-apply--pending")) throw new Exception("Dirty document not highlighted");
            type.GetMethod("CreateGUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            applyButton = window.rootVisualElement.Q<Button>(className: "whimtex-output-apply");
            if (!applyButton.ClassListContains("whimtex-output-apply--pending")) throw new Exception("Highlight lost on view rebuild");
            EditorUtility.ClearDirty(document);
            refreshApply.Invoke(window, null);
            if (applyButton.ClassListContains("whimtex-output-apply--pending")) throw new Exception("Highlight not cleared with dirty state");
            return "PASS: two window sizes, layout/binding, duplicate shadow, preview collapse/reopen and pointer capture; Apply highlight tracks dirty state and survives view rebuild.";
        }
        finally
        {
            if (window != null) window.Close();
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
