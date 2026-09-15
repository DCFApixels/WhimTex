using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed class WhimTexGradientTestWindow : EditorWindow
    {
        [SerializeField] private WhimTexGradient gradient = new WhimTexGradient();
        [SerializeField] private WhimTexGradient secondGradient = new WhimTexGradient();

        [MenuItem("Window/WhimTex Gradient Test")]
        public static void Open()
        {
            var window = GetWindow<WhimTexGradientTestWindow>();
            window.titleContent = new GUIContent("Gradient Test");
            window.minSize = new Vector2(300, 80);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new WhimTexGradientField("Gradient 1", this, nameof(gradient)));
            rootVisualElement.Add(new WhimTexGradientField("Gradient 2", this, nameof(secondGradient)));
        }
    }
}
