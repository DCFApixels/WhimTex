using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private bool previewGuidesHidden, previewGuidesLocked;
        [SerializeField] private bool previewGuidesSnap = true;
        private int selectedPreviewGuide = -1, previewGuidesRevision;
        private readonly List<PreviewGuide[]> previewGuideUndo = new List<PreviewGuide[]>();
        private readonly List<PreviewGuide[]> previewGuideRedo = new List<PreviewGuide[]>();
        private const int MaxPreviewGuides = 256;
        private Button previewGuidesButton;

        private Button BuildGuidesButton()
        {
            previewGuidesButton = new Button(() => SetPreviewGuidesHidden(!previewGuidesHidden))
            {
                text = "Guides"
            };
            previewGuidesButton.AddToClassList("whimtex-channel-button");
            previewGuidesButton.AddToClassList("whimtex-guides-button");
            RefreshPreviewGuidesButton();
            return previewGuidesButton;
        }

        private void RefreshPreviewGuidesButton()
        {
            if (previewGuidesButton == null) return;
            previewGuidesButton.EnableInClassList("whimtex-channel-button--enabled", !previewGuidesHidden);
            previewGuidesButton.tooltip = previewGuidesHidden
                ? "Show guides and restore guide snapping."
                : "Hide guides and temporarily disable guide snapping.";
        }

        private void SetPreviewGuidesHidden(bool hidden)
        {
            if (previewGuidesHidden == hidden) return;
            previewGuideManipulator?.Cancel();
            previewGuidesHidden = hidden;
            selectedPreviewGuide = -1;
            RefreshPreviewGuidesButton();
            RefreshPreviewGuides();
        }

        private void RememberPreviewGuides()
        {
            if (previewGuideUndo.Count == 64) previewGuideUndo.RemoveAt(0);
            previewGuideUndo.Add(previewGuides.ToArray());
            previewGuideRedo.Clear();
            previewGuidesRevision++;
        }

        private void RefreshPreviewGuides()
        {
            previewGuideOverlay?.MarkDirtyRepaint();
            RefreshPreviewPointerCursor();
        }

        private void RestorePreviewGuides(bool redo)
        {
            if (previewGuidesLocked) return;
            var source = redo ? previewGuideRedo : previewGuideUndo;
            var destination = redo ? previewGuideUndo : previewGuideRedo;
            if (source.Count == 0) return;
            previewGuideManipulator?.Cancel();
            destination.Add(previewGuides.ToArray());
            previewGuides.Clear();
            previewGuides.AddRange(source[source.Count - 1]);
            source.RemoveAt(source.Count - 1);
            selectedPreviewGuide = -1;
            previewGuidesRevision++;
            RefreshPreviewGuides();
        }

        private void DeletePreviewGuide(int index)
        {
            if (previewGuidesLocked || index < 0 || index >= previewGuides.Count) return;
            previewGuideManipulator?.Cancel();
            RememberPreviewGuides();
            previewGuides.RemoveAt(index);
            selectedPreviewGuide = -1;
            RefreshPreviewGuides();
        }

        private void ShowPreviewGuideMenu(int index = -1)
        {
            previewGuideManipulator?.Cancel();
            int revision = previewGuidesRevision;
            TextureCompositor document = compositor;
            bool Current() => this != null && document == compositor && revision == previewGuidesRevision;
            bool Editable() => Current() && !previewGuidesLocked && index >= 0 && index < previewGuides.Count;
            var menu = new GenericMenu();
            if (index >= 0)
            {
                selectedPreviewGuide = index;
                if (!previewGuidesLocked)
                {
                    menu.AddItem(new GUIContent("Edit Guide…"), false, () =>
                    {
                        if (Editable()) PreviewGuideSettingsWindow.Open(this, index);
                    });
                    if (previewGuides.Count < MaxPreviewGuides)
                        menu.AddItem(new GUIContent("Duplicate Guide"), false, () =>
                        {
                            if (!Editable() || previewGuides.Count >= MaxPreviewGuides) return;
                            RememberPreviewGuides();
                            PreviewGuide copy = previewGuides[index];
                            copy.position += 16f / Mathf.Max(.00001f, toolkitPreviewCanvas.PixelScale);
                            previewGuides.Add(copy);
                            selectedPreviewGuide = previewGuides.Count - 1;
                            RefreshPreviewGuides();
                        });
                    else menu.AddDisabledItem(new GUIContent("Duplicate Guide"));
                    menu.AddItem(new GUIContent("Delete Guide"), false, () => { if (Editable()) DeletePreviewGuide(index); });
                }
                else menu.AddDisabledItem(new GUIContent("Guide is locked"));
                menu.AddSeparator("");
            }
            menu.AddItem(new GUIContent("Show Guides"), !previewGuidesHidden, () =>
            {
                if (!Current()) return;
                SetPreviewGuidesHidden(!previewGuidesHidden);
            });
            menu.AddItem(new GUIContent("Lock Guides"), previewGuidesLocked, () =>
            {
                if (!Current()) return;
                previewGuideManipulator?.Cancel();
                previewGuidesLocked = !previewGuidesLocked;
                selectedPreviewGuide = -1;
                RefreshPreviewGuides();
            });
            menu.AddItem(new GUIContent("Snap to Guides"), previewGuidesSnap, () =>
            {
                if (Current()) previewGuidesSnap = !previewGuidesSnap;
            });
            menu.AddSeparator("");
            if (!previewGuidesLocked && previewGuideUndo.Count > 0)
                menu.AddItem(new GUIContent("Undo Guide Change"), false, () => { if (Current()) RestorePreviewGuides(false); });
            else menu.AddDisabledItem(new GUIContent("Undo Guide Change"));
            if (!previewGuidesLocked && previewGuideRedo.Count > 0)
                menu.AddItem(new GUIContent("Redo Guide Change"), false, () => { if (Current()) RestorePreviewGuides(true); });
            else menu.AddDisabledItem(new GUIContent("Redo Guide Change"));
            if (!previewGuidesLocked && previewGuides.Count > 0)
                menu.AddItem(new GUIContent("Clear Guides"), false, () =>
                {
                    if (!Current() || previewGuidesLocked) return;
                    RememberPreviewGuides();
                    previewGuides.Clear();
                    selectedPreviewGuide = -1;
                    RefreshPreviewGuides();
                });
            else menu.AddDisabledItem(new GUIContent("Clear Guides"));
            menu.ShowAsContext();
            RefreshPreviewGuides();
        }

        private bool HandlePreviewGuideKey(KeyDownEvent evt)
        {
            if (toolkitPreviewCanvas == null || toolkitPreviewCanvas.panel?.focusController?.focusedElement != toolkitPreviewCanvas ||
                !CanMovePreviewGuides || previewGuidesHidden || previewGuidesLocked ||
                selectedPreviewGuide < 0 || selectedPreviewGuide >= previewGuides.Count ||
                previewGuideManipulator?.IsDragging == true) return false;
            bool action = evt.ctrlKey || evt.commandKey;
            if (action || evt.altKey) return false;
            if (evt.keyCode == KeyCode.Escape) selectedPreviewGuide = -1;
            else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) DeletePreviewGuide(selectedPreviewGuide);
            else
            {
                Vector2 direction;
                switch (evt.keyCode)
                {
                    case KeyCode.LeftArrow: direction = Vector2.left; break;
                    case KeyCode.RightArrow: direction = Vector2.right; break;
                    case KeyCode.UpArrow: direction = Vector2.down; break;
                    case KeyCode.DownArrow: direction = Vector2.up; break;
                    default: return false;
                }
                PreviewGuide guide = previewGuides[selectedPreviewGuide];
                float delta = Vector2.Dot(previewViewport.ToCanvasDelta(direction), guide.normal) * (evt.shiftKey ? 10f : 1f);
                if (Mathf.Abs(delta) > .00001f)
                {
                    RememberPreviewGuides();
                    guide.position += delta;
                    previewGuides[selectedPreviewGuide] = guide;
                }
            }
            RefreshPreviewGuides();
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private sealed class PreviewGuideSettingsWindow : EditorWindow
        {
            private TextureCompositorWindow owner;
            private TextureCompositor document;
            private int index, revision;

            internal static void Open(TextureCompositorWindow owner, int index)
            {
                var window = CreateInstance<PreviewGuideSettingsWindow>();
                window.owner = owner; window.document = owner.compositor;
                window.index = index; window.revision = owner.previewGuidesRevision;
                window.titleContent = new GUIContent("Guide");
                window.minSize = new Vector2(300f, 150f);
                window.maxSize = new Vector2(420f, 200f);
                window.ShowUtility();
            }

            private void CreateGUI()
            {
                if (owner == null || index < 0 || index >= owner.previewGuides.Count) { Close(); return; }
                PreviewGuide guide = owner.previewGuides[index];
                float initialAngle = Mathf.Repeat(Mathf.Atan2(guide.normal.x, guide.normal.y) * Mathf.Rad2Deg, 180f);
                float radians = initialAngle * Mathf.Deg2Rad;
                Vector2 initialNormal = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
                var position = new FloatField("Position (px)") { value = guide.position * Vector2.Dot(initialNormal, guide.normal), isDelayed = true };
                var angle = new FloatField("Angle (°)") { value = initialAngle, isDelayed = true };
                var message = new HelpBox("0° is horizontal, 90° is vertical, relative to the canvas. Position is the signed distance from its top-left corner.", HelpBoxMessageType.Info);
                rootVisualElement.Add(position); rootVisualElement.Add(angle); rootVisualElement.Add(message);
                rootVisualElement.Add(new Button(() =>
                {
                    if (owner == null || owner.compositor != document || owner.previewGuidesRevision != revision || owner.previewGuidesLocked)
                    { Close(); return; }
                    if (float.IsNaN(position.value) || float.IsInfinity(position.value) ||
                        float.IsNaN(angle.value) || float.IsInfinity(angle.value)) return;
                    float a = Mathf.Repeat(angle.value, 180f) * Mathf.Deg2Rad;
                    var edited = new PreviewGuide { normal = new Vector2(Mathf.Sin(a), Mathf.Cos(a)), position = position.value };
                    if (edited.normal != guide.normal || edited.position != guide.position)
                    {
                        owner.RememberPreviewGuides();
                        owner.previewGuides[index] = edited;
                        owner.RefreshPreviewGuides();
                    }
                    Close();
                }) { text = "Apply" });
                rootVisualElement.Add(new Button(Close) { text = "Cancel" });
            }
        }
    }
}
