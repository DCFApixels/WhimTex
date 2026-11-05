using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexGradientSession : ScriptableObject
    {
        public WhimTexGradient gradient = new WhimTexGradient();
        [NonSerialized] public Action<WhimTexGradient> changed;
        [NonSerialized] internal WhimTexDocument document;
        [NonSerialized] internal Func<int> channelSource;
    }

    public sealed class WhimTexGradientValueField : BaseField<WhimTexGradient>
    {
        private readonly Image image;
        private Texture2D preview;
        private readonly Color[] pixels = new Color[1024];
        private readonly Color[] ramp = new Color[512];
        private string snapshot;
        private WhimTexGradientSession session;
        private WhimTexGradientSession detachedSession;

        public WhimTexGradientValueField(string label) : base(label, new Button())
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexGradientWindow.uss");
            if (sheet != null) styleSheets.Add(sheet);
            var button = this.Q<Button>(className: inputUssClassName);
            button.clicked += OpenEditor;
            WhimTexGradientField.ConfigureAppearance(this, labelElement, button);
            var background = new VisualElement { pickingMode = PickingMode.Ignore };
            background.AddToClassList("whimtex-gradient-test-fill");
            background.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
            button.Add(background);
            image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("whimtex-gradient-test-fill"); button.Add(image);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            this.AddManipulator(new ContextualMenuManipulator(e =>
            {
                e.menu.AppendAction("Copy", _ => EditorGUIUtility.systemCopyBuffer = WhimTexGradientClipboard.Write(value));
                e.menu.AppendAction("Paste", _ =>
                {
                    if (WhimTexGradientField.TryReadClipboard(out var copy)) this.value = copy;
                }, WhimTexGradientField.TryReadClipboard(out _) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (!(e.ctrlKey || e.commandKey)) return;
                if (e.keyCode == KeyCode.C)
                    EditorGUIUtility.systemCopyBuffer = WhimTexGradientClipboard.Write(value);
                else if (e.keyCode == KeyCode.V && WhimTexGradientField.TryReadClipboard(out var copy)) this.value = copy;
                else return;
                e.StopPropagation();
            });
        }
        private void OpenEditor()
        {
            CloseDetachedSession();
            session = ScriptableObject.CreateInstance<WhimTexGradientSession>();
            session.hideFlags = HideFlags.HideAndDontSave;
            session.gradient = GradientUtility.Create(value);
            session.document = WhimTexColorPicker.FindDocument(this);
            session.channelSource = WhimTexColorChannels.FindSource(this, session.document);
            session.changed = ApplySessionGradient;
            WhimTexGradientWindow.Open(session, "gradient");
        }
        private void ApplySessionGradient(WhimTexGradient next) => value = GradientUtility.Create(next);
        private void OnAttached(AttachToPanelEvent evt)
        {
            if (evt.target != this) return;
            if (session != null && session.document != WhimTexColorPicker.FindDocument(this)) CloseDetachedSession();
            EditorApplication.delayCall -= CloseDetachedSession;
            detachedSession = null;
            if (session != null) session.changed = ApplySessionGradient;
            SetValueWithoutNotify(value ?? new WhimTexGradient());
        }
        private void OnDetached(DetachFromPanelEvent evt)
        {
            if (evt.target != this) return;
            if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            preview = null; snapshot = null;
            detachedSession = session;
            if (detachedSession == null) return;
            detachedSession.changed = null;
            EditorApplication.delayCall -= CloseDetachedSession;
            EditorApplication.delayCall += CloseDetachedSession;
        }
        private void CloseDetachedSession()
        {
            EditorApplication.delayCall -= CloseDetachedSession;
            var closing = detachedSession;
            detachedSession = null;
            if (closing == null) return;
            if (session == closing) session = null;
            WhimTexGradientWindow.CloseSession(closing);
            if (closing != null) { Undo.ClearUndo(closing); UnityEngine.Object.DestroyImmediate(closing); }
        }
        public override void SetValueWithoutNotify(WhimTexGradient newValue)
        {
            base.SetValueWithoutNotify(newValue);
            if (image == null || newValue == null) return;
            string json = JsonUtility.ToJson(newValue);
            if (preview != null && snapshot == json) return;
            newValue.Bake(ramp);
            for (int i = 0; i < 512; i++) pixels[i] = pixels[i+512] = newValue.ColorSpace == ColorSpace.Linear ? ramp[i].gamma : ramp[i];
            if (preview == null) preview = new Texture2D(512,2,TextureFormat.RGBA32,false)
                { hideFlags=HideFlags.HideAndDontSave, filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Clamp };
            preview.SetPixels(pixels); preview.Apply(false,false); image.image=preview; image.MarkDirtyRepaint(); snapshot=json;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
