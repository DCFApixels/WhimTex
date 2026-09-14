using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class ModifierEditorWindow : EditorWindow
    {
        [SerializeField] private TextureCompositor compositor;
        [SerializeField] private string layerId;

        [NonSerialized] private Layer layer;
        [NonSerialized] private ListView modifiersList;
        [NonSerialized] private bool applyingChange;
        [NonSerialized] private bool interfaceBuilt;
        [NonSerialized] private bool refreshRequested;
        [NonSerialized] private TextureCompositor boundCompositor;
        [NonSerialized] private string boundLayerId;
        private readonly List<UnityEngine.Object> displayedModifiers = new List<UnityEngine.Object>();

        public static void Open(Layer layer, TextureCompositor compositor)
        {
            ModifierEditorWindow window = CreateInstance<ModifierEditorWindow>();
            window.titleContent = WhimTexBranding.WindowTitle("FX — " + layer?.layerName);
            window.compositor = compositor;
            window.layer = layer;
            window.layerId = layer?.Id;
            window.minSize = new Vector2(320f, 260f);
            window.RefreshInterface();
            window.ShowUtility();
        }

        private void OnEnable()
        {
            titleContent = WhimTexBranding.WindowTitle(titleContent.text);
            TextureCompositor.Changed += OnCompositorChanged;
            WhimTexApi.LiveEditLocksChanged += RefreshAgentLock;
        }

        private void OnDisable()
        {
            TextureCompositor.Changed -= OnCompositorChanged;
            WhimTexApi.LiveEditLocksChanged -= RefreshAgentLock;
        }

        public void CreateGUI()
        {
            interfaceBuilt = false;
            RefreshInterface();
            RefreshAgentLock();
        }

        private void RefreshAgentLock() => rootVisualElement.SetEnabled(!WhimTexApi.IsLayerContentLocked(compositor, layer));

        private void Update()
        {
            if (refreshRequested && (modifiersList == null || !WhimTexUI.HasPointerCaptureWithin(modifiersList)))
                RefreshInterface();
        }

        private void RefreshInterface()
        {
            refreshRequested = false;
            bool valid = ResolveLayer();
            if (interfaceBuilt && boundCompositor == compositor && boundLayerId == layerId &&
                valid == (modifiersList != null))
            {
                if (valid)
                    RefreshModifierItems();
                return;
            }
            interfaceBuilt = true;
            boundCompositor = compositor;
            boundLayerId = layerId;
            modifiersList = null;
            displayedModifiers.Clear();
            VisualElement root = rootVisualElement;
            root.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            if (!valid)
            {
                WhimTexUI.AddHelpBox(
                    root,
                    "The edited layer no longer exists in this compositor.",
                    HelpBoxMessageType.Info);
                root.Add(WhimTexUI.CreateButton("Close", Close));
                return;
            }

            WhimTexUI.AddHelpBox(
                root,
                "Materials and Shader FX are applied in list order after the layer transform. Select an entry and click Edit to open its Inspector.",
                HelpBoxMessageType.Info);

            layer.modifiers ??= new List<UnityEngine.Object>();
            modifiersList = new ListView(layer.modifiers, 22f, MakeModifierField, BindModifierField)
            {
                selectionType = SelectionType.Single,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly
            };
            modifiersList.style.flexGrow = 1f;
            modifiersList.style.minHeight = 120f;
            modifiersList.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && compositor != null)
                    Undo.RecordObject(compositor, "Reorder Layer Modifiers");
            }, TrickleDown.TrickleDown);
            modifiersList.itemIndexChanged += (_, _) =>
            {
                if (compositor == null)
                    return;
                applyingChange = true;
                try
                {
                    compositor.MarkChanged();
                }
                finally
                {
                    applyingChange = false;
                }
                RememberModifierItems();
            };
            root.Add(modifiersList);
            RememberModifierItems();

            VisualElement buttons = WhimTexUI.CreateRow();
            buttons.AddToClassList("whimtex-modifier-buttons");
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 6f;
            buttons.Add(WhimTexUI.CreateButton("Add", AddModifier, 64f));
            buttons.Add(WhimTexUI.CreateButton("New Shader FX", CreateShaderFX));
            buttons.Add(WhimTexUI.CreateButton("Preset ▾", () => ShaderFXCatalog.ShowMenu(entry =>
            {
                if (!ResolveLayer()) return;
                ApplyChange("Add Catalog FX", () => compositor.AddCatalogShaderFX(layer, entry));
                RefreshModifierItems();
            })));
            buttons.Add(WhimTexUI.CreateButton("Edit", EditSelectedModifier));
            buttons.Add(WhimTexUI.CreateButton("Remove", RemoveSelectedModifier, 72f));
            buttons.Add(WhimTexUI.CreateButton("Close", Close, 64f));
            root.Add(buttons);
        }

        private VisualElement MakeModifierField()
        {
            ObjectField field = new ObjectField
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = false
            };
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt =>
            {
                if (!(field.userData is int index) ||
                    layer == null ||
                    index < 0 ||
                    index >= layer.modifiers.Count)
                {
                    return;
                }

                if (evt.newValue != null && !(evt.newValue is Material) && !(evt.newValue is ShaderFX))
                {
                    field.SetValueWithoutNotify(layer.modifiers[index]);
                    return;
                }
                ApplyChange("Edit Layer Modifier", () => layer.modifiers[index] = evt.newValue);
                RememberModifierItems();
            });
            return field;
        }

        private void BindModifierField(VisualElement element, int index)
        {
            ObjectField field = (ObjectField)element;
            field.userData = index;
            field.SetValueWithoutNotify(index >= 0 && index < layer.modifiers.Count
                ? layer.modifiers[index]
                : null);
        }

        private void AddModifier()
        {
            if (!ResolveLayer())
                return;
            ApplyChange("Add Layer Modifier", () => layer.modifiers.Add(null));
            RefreshModifierItems();
            modifiersList?.SetSelection(layer.modifiers.Count - 1);
        }

        private void CreateShaderFX()
        {
            if (!ResolveLayer())
                return;
            ApplyChange("Add Shader FX", () => compositor.AddEmbeddedShaderFX(layer));
            RefreshModifierItems();
            modifiersList?.SetSelection(layer.modifiers.Count - 1);
        }

        private void EditSelectedModifier()
        {
            if (!ResolveLayer() || modifiersList == null)
                return;
            int index = modifiersList.selectedIndex;
            if (index >= 0 && index < layer.modifiers.Count && layer.modifiers[index] != null)
            {
                Selection.activeObject = layer.modifiers[index];
                EditorGUIUtility.PingObject(layer.modifiers[index]);
            }
        }

        private void RemoveSelectedModifier()
        {
            if (!ResolveLayer() || modifiersList == null)
                return;

            int[] selected = modifiersList.selectedIndices.OrderByDescending(index => index).ToArray();
            if (selected.Length == 0)
                return;

            ApplyChange("Remove Layer Modifier", () =>
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    int index = selected[i];
                    if (index >= 0 && index < layer.modifiers.Count)
                        layer.modifiers.RemoveAt(index);
                }
            });
            RefreshModifierItems();
        }

        private void RememberModifierItems()
        {
            displayedModifiers.Clear();
            displayedModifiers.AddRange(layer.modifiers);
        }

        private void RefreshModifierItems()
        {
            if (modifiersList == null)
                return;
            bool changed = displayedModifiers.Count != layer.modifiers.Count;
            for (int i = 0; !changed && i < displayedModifiers.Count; i++)
                changed = displayedModifiers[i] != layer.modifiers[i];

            if (!ReferenceEquals(modifiersList.itemsSource, layer.modifiers))
                modifiersList.itemsSource = layer.modifiers;
            else if (changed)
                modifiersList.RefreshItems();
            RememberModifierItems();
        }

        private void ApplyChange(string undoName, Action change)
        {
            if (compositor == null || change == null || WhimTexApi.IsLayerContentLocked(compositor, layer))
                return;

            Undo.RecordObject(compositor, undoName);
            applyingChange = true;
            try
            {
                change();
                compositor.MarkChanged();
            }
            finally
            {
                applyingChange = false;
            }
        }

        private bool ResolveLayer()
        {
            if (compositor == null || string.IsNullOrEmpty(layerId))
                return false;

            Layer resolved = compositor.FindLayer(layerId);
            if (resolved == null)
            {
                layer = null;
                return false;
            }
            layer = resolved;
            layer.modifiers ??= new List<UnityEngine.Object>();
            return true;
        }

        private void OnCompositorChanged(TextureCompositor changedCompositor)
        {
            if (changedCompositor != compositor || applyingChange)
                return;
            if (TextureCompositor.IsRefreshingUndo)
            {
                OnUndoRedo();
                return;
            }
            refreshRequested = true;
        }

        private void OnUndoRedo()
        {
            RefreshInterface();
        }
    }
}
