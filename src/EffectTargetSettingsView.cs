using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    // Shared retained target selector for the embedded inspector and standalone Edit windows.
    internal sealed class EffectTargetSettingsView
    {
        private readonly TextureCompositor compositor;
        private readonly Action<string, Action> applyChange;
        private readonly SpriteEditorUI.ValueBindings bindings;
        private string[] effectTargetIds;
        private string[] effectTargetLabels;
        private string effectTargetOptionsForLayerId;
        private string effectTargetOptionsForTargetId;

        internal EffectTargetSettingsView(TextureCompositor compositor,
            Action<string, Action> applyChange, SpriteEditorUI.ValueBindings bindings)
        {
            this.compositor = compositor;
            this.applyChange = applyChange;
            this.bindings = bindings;
        }

        internal void Build(VisualElement root, TargetedLayerBehaviour effect)
        {
            EnumField input = SpriteEditorUI.ConfigureField(new EnumField("Input", effect.inputMode));
            bindings.Track(input, () => (Enum)effect.inputMode);
            input.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Effect Input", () => effect.inputMode = (EffectInputMode)evt.newValue);
            });
            root.Add(input);

            EnsureEffectTargetOptions(effect);
            int selectedIndex = FindEffectTargetIndex(effect.TargetLayerId);
            PopupField<string> target = SpriteEditorUI.ConfigureField(
                new PopupField<string>("Target", new List<string>(effectTargetLabels), selectedIndex));
            target.AddToClassList("sprite-editor-effect-target");
            target.Q(className: "unity-base-popup-field__arrow")?.AddToClassList("sprite-editor-effect-target-arrow");
            target.Q(className: "unity-base-popup-field__text")?.AddToClassList("sprite-editor-effect-target-text");
            target.EnableInClassList("sprite-editor-effect-target--light", !EditorGUIUtility.isProSkin);
            VisualElement targetInput = target.Q(className: "unity-base-field__input");
            var preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            preview.AddToClassList("sprite-editor-effect-target-preview");
            var fallback = new LayerActionIcon(LayerActionIcon.Kind.Effects) { pickingMode = PickingMode.Ignore };
            fallback.AddToClassList("sprite-editor-effect-target-fallback");
            var groupIcon = new LayerActionIcon(LayerActionIcon.Kind.Group) { pickingMode = PickingMode.Ignore };
            groupIcon.AddToClassList("sprite-editor-effect-target-fallback");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("sprite-editor-effect-target-icon");
            icon.Add(preview);
            icon.Add(fallback);
            icon.Add(groupIcon);
            targetInput.Insert(0, icon);
            var selector = new VisualElement { pickingMode = PickingMode.Ignore };
            selector.AddToClassList("sprite-editor-effect-target-selector");
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("sprite-editor-effect-target-ring");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("sprite-editor-effect-target-dot");
            ring.Add(dot);
            selector.Add(ring);
            targetInput.Add(selector);
            target.RegisterValueChangedCallback(evt =>
            {
                EnsureEffectTargetOptions(effect);
                int nextIndex = Array.IndexOf(effectTargetLabels, evt.newValue);
                if (nextIndex < 0 || nextIndex >= effectTargetIds.Length)
                    return;
                applyChange("Change Effect Target", () =>
                {
                    effect.inputMode = EffectInputMode.Specific;
                    effect.TargetLayerId = effectTargetIds[nextIndex];
                    Invalidate();
                });
            });
            target.tooltip = "Select a source, or drop a layer/group from this document. A multi-selection uses its active layer.";
            target.AddManipulator(new TargetDropManipulator(this, effect));
            root.Add(target);
            HelpBox status = SpriteEditorUI.AddHelpBox(root, string.Empty, HelpBoxMessageType.Info);
            Layer RefreshThumbnail()
            {
                if (compositor == null) return null;
                Layer source = string.IsNullOrEmpty(effect.TargetLayerId) ? null : compositor.FindLayer(effect.TargetLayerId);
                Texture2D thumbnail = compositor.GetLayerThumbnail(source, 18);
                if (preview.image != thumbnail) preview.image = thumbnail;
                preview.EnableInClassList("sprite-editor-hidden", thumbnail == null);
                fallback.EnableInClassList("sprite-editor-hidden", source == null || thumbnail != null || source?.IsGroup == true);
                groupIcon.EnableInClassList("sprite-editor-hidden", !(source?.IsGroup == true) || thumbnail != null);
                return source;
            }
            target.schedule.Execute(() => RefreshThumbnail()).Every(200);
            void Refresh()
            {
                EnsureEffectTargetOptions(effect);
                Layer source = RefreshThumbnail();
                bool choicesChanged = target.choices.Count != effectTargetLabels.Length;
                for (int i = 0; !choicesChanged && i < effectTargetLabels.Length; i++)
                    choicesChanged = target.choices[i] != effectTargetLabels[i];
                if (choicesChanged)
                    target.choices = new List<string>(effectTargetLabels);
                string message = string.Empty;
                HelpBoxMessageType messageType = HelpBoxMessageType.Info;
                if (effect.inputMode == EffectInputMode.Previous)
                    message = "Uses the item directly below this effect. A group is read as the combined alpha of all visible descendants.";
                else if (string.IsNullOrEmpty(effect.TargetLayerId))
                {
                    message = "Select a source layer or group for this effect.";
                    messageType = HelpBoxMessageType.Warning;
                }
                else if (!compositor.IsUsableEffectTarget(effect, effect.TargetLayerId))
                {
                    message = "The selected target is missing or would create a cyclic effect dependency.";
                    messageType = HelpBoxMessageType.Error;
                }
                else if (source?.IsGroup == true)
                    message = "The selected group is read as the combined alpha of all visible descendant layers.";
                DisplayStyle display = message.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                if (status.style.display.value != display) status.style.display = display;
                if (display != DisplayStyle.None && status.text != message) status.text = message;
                if (status.messageType != messageType) status.messageType = messageType;
            }
            Refresh();
            bindings.Add(Refresh);
            bindings.Track(target, () =>
            {
                EnsureEffectTargetOptions(effect);
                return effectTargetLabels[FindEffectTargetIndex(effect.TargetLayerId)];
            });
        }

        private void EnsureEffectTargetOptions(TargetedLayerBehaviour effect)
        {
            if (effectTargetIds != null &&
                effectTargetOptionsForLayerId == effect.Id &&
                effectTargetOptionsForTargetId == effect.TargetLayerId)
            {
                return;
            }

            List<string> candidateIds = new List<string>();
            List<string> candidateLabels = new List<string>();
            compositor.GetEffectTargetOptions(effect, candidateIds, candidateLabels);

            bool hasCurrentTarget = false;
            for (int i = 0; i < candidateIds.Count; i++)
            {
                if (candidateIds[i] == effect.TargetLayerId)
                {
                    hasCurrentTarget = true;
                    break;
                }
            }

            bool includeUnavailableTarget = !string.IsNullOrEmpty(effect.TargetLayerId) && !hasCurrentTarget;
            int firstCandidateIndex = includeUnavailableTarget ? 2 : 1;
            effectTargetIds = new string[candidateIds.Count + firstCandidateIndex];
            effectTargetLabels = new string[candidateLabels.Count + firstCandidateIndex];
            effectTargetIds[0] = string.Empty;
            effectTargetLabels[0] = "None (Layer)";

            if (includeUnavailableTarget)
            {
                effectTargetIds[1] = effect.TargetLayerId;
                effectTargetLabels[1] = compositor.FindLayer(effect.TargetLayerId) == null
                    ? "<Missing target>"
                    : "<Unavailable target: cyclic dependency>";
            }

            for (int i = 0; i < candidateIds.Count; i++)
            {
                effectTargetIds[firstCandidateIndex + i] = candidateIds[i];
                effectTargetLabels[firstCandidateIndex + i] = candidateLabels[i];
            }

            effectTargetOptionsForLayerId = effect.Id;
            effectTargetOptionsForTargetId = effect.TargetLayerId;
        }

        private int FindEffectTargetIndex(string targetId)
        {
            for (int i = 0; i < effectTargetIds.Length; i++)
            {
                if (effectTargetIds[i] == targetId)
                    return i;
            }
            return 0;
        }

        internal void Invalidate()
        {
            effectTargetIds = null;
            effectTargetLabels = null;
            effectTargetOptionsForLayerId = null;
            effectTargetOptionsForTargetId = null;
        }

        private sealed class TargetDropManipulator : PointerManipulator
        {
            private readonly EffectTargetSettingsView owner;
            private readonly TargetedLayerBehaviour effect;

            internal TargetDropManipulator(EffectTargetSettingsView owner, TargetedLayerBehaviour effect)
            {
                this.owner = owner;
                this.effect = effect;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnUpdated);
                target.RegisterCallback<DragPerformEvent>(OnPerform);
                target.RegisterCallback<DragLeaveEvent>(OnLeave);
                target.RegisterCallback<DragExitedEvent>(OnExited);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Clear();
                target.UnregisterCallback<DragUpdatedEvent>(OnUpdated);
                target.UnregisterCallback<DragPerformEvent>(OnPerform);
                target.UnregisterCallback<DragLeaveEvent>(OnLeave);
                target.UnregisterCallback<DragExitedEvent>(OnExited);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            private Layer GetSource()
            {
                Layer source = TextureCompositorWindow.GetDraggedLayerForDocument(owner.compositor);
                return target.enabledInHierarchy && source != null &&
                    owner.compositor.TryFindLayer(effect, out _, out _) &&
                    owner.compositor.IsUsableEffectTarget(effect, source.Id) ? source : null;
            }

            private void OnUpdated(DragUpdatedEvent evt)
            {
                bool valid = GetSource() != null;
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                target.EnableInClassList("sprite-editor-effect-target--drop", valid);
                evt.StopImmediatePropagation();
            }

            private void OnPerform(DragPerformEvent evt)
            {
                evt.StopImmediatePropagation();
                Layer source = GetSource();
                Clear();
                if (source == null)
                    return;
                DragAndDrop.AcceptDrag();
                try
                {
                    owner.applyChange("Change Effect Target", () =>
                    {
                        effect.inputMode = EffectInputMode.Specific;
                        effect.TargetLayerId = source.Id;
                        owner.Invalidate();
                    });
                }
                finally
                {
                    TextureCompositorWindow.ClearDraggedLayerReference();
                }
            }

            private void Clear() => target.RemoveFromClassList("sprite-editor-effect-target--drop");
            private void OnLeave(DragLeaveEvent evt) => Clear();
            private void OnExited(DragExitedEvent evt) => Clear();
            private void OnDetach(DetachFromPanelEvent evt) => Clear();
        }
    }
}
