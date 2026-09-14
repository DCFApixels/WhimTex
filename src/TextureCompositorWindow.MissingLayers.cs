using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private void BuildMissingBehaviourInspector(VisualElement root, Layer layer)
        {
            root.Add(new HelpBox("This layer's behaviour is missing. Its name, GUID, common settings and children are still intact. " +
                "Choose a replacement. Compatible saved behaviour settings will be transferred; unavailable fields keep their new defaults.", HelpBoxMessageType.Warning));
            var record = MissingLayerRecovery.FindRecord(compositor, layer.BehaviourId);
            var names = new List<string>();
            var types = new List<LayerTypeRegistry.Entry>();
            foreach (var descriptor in LayerTypeRegistry.Entries)
            {
                if (layer.children != null && layer.children.Count > 0 && descriptor.BehaviourType != typeof(GroupLayerBehaviour)) continue;
                types.Add(descriptor);
                names.Add(descriptor.MenuName);
            }
            var content = new VisualElement();
            content.AddToClassList("whimtex-inspector-section-content");
            root.Add(content);
            var replacement = new DropdownField("Replace with", names, 0);
            var transfer = new Toggle("Transfer saved settings") { value = record?.Data != null };
            transfer.SetEnabled(record?.Data != null);
            content.Add(replacement);
            content.Add(transfer);
            var feedback = new HelpBox("", HelpBoxMessageType.Info);
            content.Add(feedback);
            var apply = new Button { text = "Replace Behaviour" };
            content.Add(apply);
            LayerBehaviour draft = null;
            void Prepare()
            {
                draft = null;
                apply.SetEnabled(false);
                try
                {
                    var candidate = types[replacement.index].CreateBehaviour();
                    var report = MissingLayerRecovery.Copy(transfer.value ? record : null, candidate, compositor);
                    feedback.text = transfer.value ? report.Summary : record?.Error ?? "No saved behaviour settings will be transferred. Common layer settings are kept.";
                    feedback.messageType = report.Skipped.Count > 0 || !transfer.value ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
                    draft = candidate;
                    apply.SetEnabled(true);
                }
                catch (Exception e) { feedback.text = e.Message; feedback.messageType = HelpBoxMessageType.Warning; }
            }
            replacement.RegisterValueChangedCallback(_ => Prepare());
            transfer.RegisterValueChangedCallback(_ => Prepare());
            apply.clicked += () =>
            {
                if (GetSelectedLayer() != layer || compositor.FindLayer(layer.Id) != layer || layer.Behaviour != null) return;
                Prepare();
                if (draft == null) return;
                ExecuteContextChange("Replace Layer Behaviour", () =>
                {
                    layer.SetBehaviour(draft);
                    if (draft is DrawingLayerBehaviour drawing && drawing.StoredTexture == null)
                    {
                        try
                        {
                            drawing.InitializeCanvas(compositor.width, compositor.height);
                            drawing.InvalidatePaintSurface();
                            drawing.MakeTexturePersistent(compositor);
                            UnityEditor.Undo.RegisterCreatedObjectUndo(drawing.StoredTexture, "Replace Layer Behaviour");
                        }
                        catch
                        {
                            drawing.InvalidatePaintSurface();
                            if (drawing.StoredTexture != null) DestroyImmediate(drawing.StoredTexture, true);
                            throw;
                        }
                    }
                    if (draft is ShaderProcessorLayerBehaviour && layer.modifiers.Count == 0) compositor.AddEmbeddedShaderFX(layer);
                });
                if (layer.Behaviour == draft) ShowNotification(new GUIContent("Layer behaviour restored."));
            };
            Prepare();
        }
    }
}
