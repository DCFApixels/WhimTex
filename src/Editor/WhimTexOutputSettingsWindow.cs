using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexOutputSettingsWindow : EditorWindow
    {
        [SerializeField] private TextureCompositor document;
        private SerializedObject data;
        private bool wasPersistent;

        private void OnInspectorUpdate()
        {
            if (data != null && (document == null || AssetDatabase.Contains(document) != wasPersistent))
                CreateGUI();
        }

        internal static void Open(TextureCompositor document)
        {
            if (document == null) return;
            foreach (var existing in Resources.FindObjectsOfTypeAll<WhimTexOutputSettingsWindow>())
                if (existing.document == document) { existing.Show(); existing.Focus(); return; }
            var window = CreateInstance<WhimTexOutputSettingsWindow>();
            window.document = document;
            window.titleContent = new GUIContent("Output Settings");
            window.minSize = new Vector2(340, 380);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            ReleaseView();
            if (document == null)
            {
                rootVisualElement.Add(new HelpBox("The document is no longer available. Reopen settings from WhimTex or its asset.", HelpBoxMessageType.Info));
                return;
            }
            WhimTexUI.ApplyWindowStyles(rootVisualElement);
            rootVisualElement.focusable = true;
            wasPersistent = AssetDatabase.Contains(document);
            data = new SerializedObject(document);
            var reference = new ObjectField("Document") { objectType = typeof(TextureCompositor), value = document };
            reference.SetEnabled(false);
            rootVisualElement.Add(reference);
            var scroll = new ScrollView();
            scroll.AddToClassList("whimtex-output-settings-scroll");
            scroll.Add(TextureCompositorEditor.BuildOutputSettings(data));
            rootVisualElement.Add(scroll);
            var note = new HelpBox(AssetDatabase.Contains(document)
                ? "Changes belong to this document. Apply saves it and updates the embedded texture and sprite."
                : "Settings belong to this unsaved document. Save it in WhimTex to create the output texture and sprite.", HelpBoxMessageType.Info);
            rootVisualElement.Add(note);
            var apply = new Button(() =>
            {
                if (document == null) return;
                rootVisualElement.Focus();
                data.ApplyModifiedProperties();
                if (!AssetDatabase.Contains(document))
                { ShowNotification(new GUIContent("Save the document in WhimTex first.")); return; }
                document.TrySaveWithOutput();
            }) { text = "Apply & Save Output" };
            apply.SetEnabled(AssetDatabase.Contains(document));
            rootVisualElement.Add(apply);
            rootVisualElement.Bind(data);
            scroll.RegisterCallback<SerializedPropertyChangeEvent>(_ => { if (document != null) document.MarkChanged(); });
        }

        private void OnDisable() => ReleaseView();
        private void ReleaseView()
        {
            rootVisualElement.Unbind();
            rootVisualElement.Clear();
            data?.Dispose();
            data = null;
        }
    }

    [InitializeOnLoad]
    internal static class WhimTexOutputSettingsHeader
    {
        private sealed class Entry
        {
            internal Object target;
            internal TextureCompositor document;
        }
        private static ConditionalWeakTable<Editor, Entry> cache = new ConditionalWeakTable<Editor, Entry>();

        static WhimTexOutputSettingsHeader()
        {
            Editor.finishedDefaultHeaderGUI += Draw;
            EditorApplication.projectChanged += () => cache = new ConditionalWeakTable<Editor, Entry>();
        }

        private static void Draw(Editor editor)
        {
            if (editor == null || editor.targets.Length != 1 || !(editor.target is Texture2D || editor.target is Sprite)) return;
            var entry = cache.GetValue(editor, _ => new Entry());
            if (entry.target != editor.target)
            {
                entry.target = editor.target;
                entry.document = TextureCompositor.FindDocument(editor.target);
            }
            if (entry.document == null || (editor.target != entry.document.OutputTexture && !entry.document.IsOutputSprite(editor.target as Sprite))) return;
            if (GUILayout.Button("WhimTex Output Settings…")) WhimTexOutputSettingsWindow.Open(entry.document);
        }
    }
}
