using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public sealed class WhimTexGradientField : VisualElement
    {
        private readonly Object owner;
        private readonly string propertyPath;
        private readonly Image image;
        private readonly Button button;
        private readonly Color[] ramp = new Color[512], pixels = new Color[1024];
        private Texture2D preview;
        private string displayedJson;

        public WhimTexGradientField(string label, Object owner, string propertyPath)
        {
            this.owner = owner; this.propertyPath = propertyPath;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexGradientWindow.uss");
            if (sheet != null) styleSheets.Add(sheet);
            AddToClassList("whimtex-gradient-test-row"); Add(new Label(label));
            button = new Button(() => WhimTexGradientWindow.Open(owner, propertyPath));
            button.AddToClassList("whimtex-gradient-test-field");
            var background = new VisualElement { pickingMode = PickingMode.Ignore };
            background.AddToClassList("whimtex-gradient-test-fill");
            background.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
            button.Add(background);
            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("whimtex-gradient-test-fill"); button.Add(image); Add(button);
            this.AddManipulator(new ContextualMenuManipulator(e =>
            {
                e.menu.AppendAction("Copy", _ => CopyValue(), owner != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Paste", _ => PasteValue(), owner != null && TryReadClipboard(out _) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));
            button.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey) || e.altKey) return;
                if (e.keyCode == KeyCode.C) { CopyValue(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.V) { PasteValue(); e.StopPropagation(); }
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += Refresh;
                WhimTexGradientBinding.Changed += OnChanged; Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= Refresh;
                WhimTexGradientBinding.Changed -= OnChanged;
                if (preview != null) Object.DestroyImmediate(preview);
                preview = null; displayedJson = null;
            });
        }
        private void OnChanged(Object target, string path)
        {
            if (target == owner && path == propertyPath) Refresh();
        }
        public void CopyValue()
        {
            EditorGUIUtility.systemCopyBuffer = WhimTexGradientClipboard.Write(WhimTexGradientBinding.Read(owner, propertyPath));
        }
        public bool PasteValue()
        {
            if (owner == null || !TryReadClipboard(out var value)) return false;
            WhimTexGradientBinding.Read(owner, propertyPath);
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(owner, "Paste Gradient");
            WhimTexGradientBinding.Write(owner, propertyPath, value);
            Undo.IncrementCurrentGroup();
            Refresh();
            return true;
        }
        internal static bool TryReadClipboard(out WhimTexGradient value)
        {
            return WhimTexGradientClipboard.TryRead(EditorGUIUtility.systemCopyBuffer, out value);
        }
        public void Refresh()
        {
            WhimTexGradient gradient;
            try { gradient = WhimTexGradientBinding.Read(owner, propertyPath); }
            catch (ArgumentException) { button.SetEnabled(false); return; }
            button.SetEnabled(true);
            string json = JsonUtility.ToJson(gradient);
            if (preview != null && displayedJson == json) return;
            gradient.Bake(ramp);
            for (int i = 0; i < ramp.Length; i++)
            {
                Color c = gradient.ColorSpace == ColorSpace.Linear ? ramp[i].gamma : ramp[i];
                pixels[i] = pixels[i + 512] = c;
            }
            if (preview == null) preview = new Texture2D(512, 2, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            preview.SetPixels(pixels); preview.Apply(false, false);
            image.image = preview; image.MarkDirtyRepaint(); displayedJson = json;
            if (owner is EditorWindow window) window.Repaint();
            else UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
