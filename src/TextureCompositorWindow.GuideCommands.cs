using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [UnityEngine.Serialization.FormerlySerializedAs("previewGuidesHidden")]
        [SerializeField] private bool canvasGuidesHidden;
        [UnityEngine.Serialization.FormerlySerializedAs("previewGuidesLocked")]
        [SerializeField] private bool canvasGuidesLocked;
        [UnityEngine.Serialization.FormerlySerializedAs("previewGuidesSnap")]
        [SerializeField] private bool canvasGuidesSnap = true;
        private int selectedCanvasGuide = -1, canvasGuidesRevision;
        private readonly List<CanvasGuide[]> canvasGuideUndo = new List<CanvasGuide[]>();
        private readonly List<CanvasGuide[]> canvasGuideRedo = new List<CanvasGuide[]>();
        private const int MaxCanvasGuides = 256;
        private Button canvasGuidesButton;

        private Button BuildGuidesButton()
        {
            canvasGuidesButton = new Button(() => SetCanvasGuidesHidden(!canvasGuidesHidden))
            {
                text = "Guides"
            };
            canvasGuidesButton.AddToClassList("whimtex-channel-button");
            canvasGuidesButton.AddToClassList("whimtex-guides-button");
            RefreshCanvasGuidesButton();
            return canvasGuidesButton;
        }

        private void RefreshCanvasGuidesButton()
        {
            if (canvasGuidesButton == null) return;
            canvasGuidesButton.EnableInClassList("whimtex-channel-button--enabled", !canvasGuidesHidden);
            canvasGuidesButton.tooltip = canvasGuidesHidden
                ? "Show guides and restore guide snapping."
                : "Hide guides and temporarily disable guide snapping.";
        }

        private void SetCanvasGuidesHidden(bool hidden)
        {
            if (canvasGuidesHidden == hidden) return;
            canvasGuideManipulator?.Cancel();
            canvasGuidesHidden = hidden;
            selectedCanvasGuide = -1;
            RefreshCanvasGuidesButton();
            RefreshCanvasGuides();
        }

        private void RememberCanvasGuides()
        {
            if (canvasGuideUndo.Count == 64) canvasGuideUndo.RemoveAt(0);
            canvasGuideUndo.Add(canvasGuides.ToArray());
            canvasGuideRedo.Clear();
            canvasGuidesRevision++;
        }

        private void RefreshCanvasGuides()
        {
            canvasGuideOverlay?.MarkDirtyRepaint();
            RefreshCanvasPointerCursor();
        }

        private void RestoreCanvasGuides(bool redo)
        {
            if (canvasGuidesLocked) return;
            var source = redo ? canvasGuideRedo : canvasGuideUndo;
            var destination = redo ? canvasGuideUndo : canvasGuideRedo;
            if (source.Count == 0) return;
            canvasGuideManipulator?.Cancel();
            destination.Add(canvasGuides.ToArray());
            canvasGuides.Clear();
            canvasGuides.AddRange(source[source.Count - 1]);
            source.RemoveAt(source.Count - 1);
            selectedCanvasGuide = -1;
            canvasGuidesRevision++;
            RefreshCanvasGuides();
        }

        private void DeleteCanvasGuide(int index)
        {
            if (canvasGuidesLocked || index < 0 || index >= canvasGuides.Count) return;
            canvasGuideManipulator?.Cancel();
            RememberCanvasGuides();
            canvasGuides.RemoveAt(index);
            selectedCanvasGuide = -1;
            RefreshCanvasGuides();
        }

        private void ShowCanvasGuideMenu(int index = -1)
        {
            canvasGuideManipulator?.Cancel();
            int revision = canvasGuidesRevision;
            TextureCompositor document = compositor;
            bool Current() => this != null && document == compositor && revision == canvasGuidesRevision;
            bool Editable() => Current() && !canvasGuidesLocked && index >= 0 && index < canvasGuides.Count;
            var menu = new GenericMenu();
            if (index >= 0)
            {
                selectedCanvasGuide = index;
                if (!canvasGuidesLocked)
                {
                    menu.AddItem(new GUIContent("Edit Guide…"), false, () =>
                    {
                        if (Editable()) CanvasGuideSettingsWindow.Open(this, index);
                    });
                    if (canvasGuides.Count < MaxCanvasGuides)
                        menu.AddItem(new GUIContent("Duplicate Guide"), false, () =>
                        {
                            if (!Editable() || canvasGuides.Count >= MaxCanvasGuides) return;
                            RememberCanvasGuides();
                            CanvasGuide copy = canvasGuides[index];
                            copy.position += 16f / Mathf.Max(.00001f, toolkitCanvas.PixelScale);
                            canvasGuides.Add(copy);
                            selectedCanvasGuide = canvasGuides.Count - 1;
                            RefreshCanvasGuides();
                        });
                    else menu.AddDisabledItem(new GUIContent("Duplicate Guide"));
                    menu.AddItem(new GUIContent("Delete Guide"), false, () => { if (Editable()) DeleteCanvasGuide(index); });
                }
                else menu.AddDisabledItem(new GUIContent("Guide is locked"));
                menu.AddSeparator("");
            }
            menu.AddItem(new GUIContent("Show Guides"), !canvasGuidesHidden, () =>
            {
                if (!Current()) return;
                SetCanvasGuidesHidden(!canvasGuidesHidden);
            });
            menu.AddItem(new GUIContent("Lock Guides"), canvasGuidesLocked, () =>
            {
                if (!Current()) return;
                canvasGuideManipulator?.Cancel();
                canvasGuidesLocked = !canvasGuidesLocked;
                selectedCanvasGuide = -1;
                RefreshCanvasGuides();
            });
            menu.AddItem(new GUIContent("Snap to Guides"), canvasGuidesSnap, () =>
            {
                if (Current()) canvasGuidesSnap = !canvasGuidesSnap;
            });
            menu.AddSeparator("");
            if (!canvasGuidesLocked && canvasGuideUndo.Count > 0)
                menu.AddItem(new GUIContent("Undo Guide Change"), false, () => { if (Current()) RestoreCanvasGuides(false); });
            else menu.AddDisabledItem(new GUIContent("Undo Guide Change"));
            if (!canvasGuidesLocked && canvasGuideRedo.Count > 0)
                menu.AddItem(new GUIContent("Redo Guide Change"), false, () => { if (Current()) RestoreCanvasGuides(true); });
            else menu.AddDisabledItem(new GUIContent("Redo Guide Change"));
            if (!canvasGuidesLocked && canvasGuides.Count > 0)
                menu.AddItem(new GUIContent("Clear Guides"), false, () =>
                {
                    if (!Current() || canvasGuidesLocked) return;
                    RememberCanvasGuides();
                    canvasGuides.Clear();
                    selectedCanvasGuide = -1;
                    RefreshCanvasGuides();
                });
            else menu.AddDisabledItem(new GUIContent("Clear Guides"));
            menu.ShowAsContext();
            RefreshCanvasGuides();
        }

        private bool HandleCanvasGuideKey(KeyDownEvent evt)
        {
            if (toolkitCanvas == null || toolkitCanvas.panel?.focusController?.focusedElement != toolkitCanvas ||
                !CanMoveCanvasGuides || canvasGuidesHidden || canvasGuidesLocked ||
                selectedCanvasGuide < 0 || selectedCanvasGuide >= canvasGuides.Count ||
                canvasGuideManipulator?.IsDragging == true) return false;
            bool action = evt.ctrlKey || evt.commandKey;
            if (action || evt.altKey) return false;
            if (evt.keyCode == KeyCode.Escape) selectedCanvasGuide = -1;
            else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) DeleteCanvasGuide(selectedCanvasGuide);
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
                CanvasGuide guide = canvasGuides[selectedCanvasGuide];
                float delta = Vector2.Dot(canvasViewport.ToCanvasDelta(direction), guide.normal) * (evt.shiftKey ? 10f : 1f);
                if (Mathf.Abs(delta) > .00001f)
                {
                    RememberCanvasGuides();
                    guide.position += delta;
                    canvasGuides[selectedCanvasGuide] = guide;
                }
            }
            RefreshCanvasGuides();
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "DCFApixels.WhimTex", sourceAssembly: null, sourceClassName: "TextureCompositorWindow+PreviewGuideSettingsWindow")]
        private sealed class CanvasGuideSettingsWindow : EditorWindow
        {
            private TextureCompositorWindow owner;
            private TextureCompositor document;
            private int index, revision;

            internal static void Open(TextureCompositorWindow owner, int index)
            {
                var window = CreateInstance<CanvasGuideSettingsWindow>();
                window.owner = owner; window.document = owner.compositor;
                window.index = index; window.revision = owner.canvasGuidesRevision;
                window.titleContent = new GUIContent("Guide");
                window.minSize = new Vector2(300f, 150f);
                window.maxSize = new Vector2(420f, 200f);
                window.ShowUtility();
            }

            private void CreateGUI()
            {
                if (owner == null || index < 0 || index >= owner.canvasGuides.Count) { Close(); return; }
                CanvasGuide guide = owner.canvasGuides[index];
                float initialAngle = Mathf.Repeat(Mathf.Atan2(guide.normal.x, guide.normal.y) * Mathf.Rad2Deg, 180f);
                float radians = initialAngle * Mathf.Deg2Rad;
                Vector2 initialNormal = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
                var position = new FloatField("Position (px)") { value = guide.position * Vector2.Dot(initialNormal, guide.normal), isDelayed = true };
                var angle = new FloatField("Angle (°)") { value = initialAngle, isDelayed = true };
                var message = new HelpBox("0° is horizontal, 90° is vertical, relative to the canvas. Position is the signed distance from its top-left corner.", HelpBoxMessageType.Info);
                rootVisualElement.Add(position); rootVisualElement.Add(angle); rootVisualElement.Add(message);
                rootVisualElement.Add(new Button(() =>
                {
                    if (owner == null || owner.compositor != document || owner.canvasGuidesRevision != revision || owner.canvasGuidesLocked)
                    { Close(); return; }
                    if (float.IsNaN(position.value) || float.IsInfinity(position.value) ||
                        float.IsNaN(angle.value) || float.IsInfinity(angle.value)) return;
                    float a = Mathf.Repeat(angle.value, 180f) * Mathf.Deg2Rad;
                    var edited = new CanvasGuide { normal = new Vector2(Mathf.Sin(a), Mathf.Cos(a)), position = position.value };
                    if (edited.normal != guide.normal || edited.position != guide.position)
                    {
                        owner.RememberCanvasGuides();
                        owner.canvasGuides[index] = edited;
                        owner.RefreshCanvasGuides();
                    }
                    Close();
                }) { text = "Apply" });
                rootVisualElement.Add(new Button(Close) { text = "Cancel" });
            }
        }
    }
}
