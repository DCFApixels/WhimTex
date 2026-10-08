using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class LayerFxEditorWindow : EditorWindow
    {
        [SerializeField] private WhimTexDocument activeDocument;
        [SerializeField] private string layerId;

        [NonSerialized] private Layer layer;
        [NonSerialized] private ListView fxList;
        [NonSerialized] private bool applyingChange;
        [NonSerialized] private bool interfaceBuilt;
        [NonSerialized] private bool refreshRequested;
        [NonSerialized] private WhimTexDocument boundDocument;
        [NonSerialized] private string boundLayerId;
        private readonly List<UnityEngine.Object> displayedFx = new List<UnityEngine.Object>();

        public static void Open(Layer layer, WhimTexDocument activeDocument)
        {
            LayerFxEditorWindow window = CreateInstance<LayerFxEditorWindow>();
            window.titleContent = WhimTexBranding.WindowTitle("FX — " + layer?.layerName);
            window.activeDocument = activeDocument;
            window.layer = layer;
            window.layerId = layer?.Id;
            window.minSize = new Vector2(320f, 260f);
            window.RefreshInterface();
            window.ShowUtility();
        }

        private void OnEnable()
        {
            titleContent = WhimTexBranding.WindowTitle(titleContent.text);
            WhimTexDocument.Changed += OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged += OnDocumentChanged;
            WhimTexApi.LiveEditLocksChanged += RefreshAgentLock;
        }

        private void OnDisable()
        {
            WhimTexDocument.Changed -= OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= OnDocumentChanged;
            WhimTexApi.LiveEditLocksChanged -= RefreshAgentLock;
        }

        public void CreateGUI()
        {
            interfaceBuilt = false;
            RefreshInterface();
            RefreshAgentLock();
        }

        private void RefreshAgentLock() => rootVisualElement.SetEnabled(!WhimTexApi.IsLayerContentLocked(activeDocument, layer));

        private void Update()
        {
            if (refreshRequested && (fxList == null || !WhimTexUI.HasPointerCaptureWithin(fxList)))
                RefreshInterface();
        }

        private void RefreshInterface()
        {
            refreshRequested = false;
            bool valid = ResolveLayer();
            if (interfaceBuilt && boundDocument == activeDocument && boundLayerId == layerId &&
                valid == (fxList != null))
            {
                if (valid)
                    RefreshFxItems();
                return;
            }
            interfaceBuilt = true;
            boundDocument = activeDocument;
            boundLayerId = layerId;
            fxList = null;
            displayedFx.Clear();
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
                    "The edited layer no longer exists in this document.",
                    HelpBoxMessageType.Info);
                root.Add(WhimTexUI.CreateButton("Close", Close));
                return;
            }

            WhimTexUI.AddHelpBox(
                root,
                "Materials and Shader FX are applied in list order after the layer transform. Select an entry and click Edit to open its Inspector.",
                HelpBoxMessageType.Info);

            layer.fx ??= new List<UnityEngine.Object>();
            fxList = new ListView(layer.fx, 22f, MakeFxField, BindFxField)
            {
                selectionType = SelectionType.Single,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showBorder = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly
            };
            fxList.style.flexGrow = 1f;
            fxList.style.minHeight = 120f;
            fxList.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && activeDocument != null)
                    Undo.RecordObject(activeDocument, "Reorder Layer FX");
            }, TrickleDown.TrickleDown);
            fxList.itemIndexChanged += (_, _) =>
            {
                if (activeDocument == null)
                    return;
                applyingChange = true;
                try
                {
                    activeDocument.MarkChanged();
                }
                finally
                {
                    applyingChange = false;
                }
                RememberFxItems();
            };
            root.Add(fxList);
            RememberFxItems();

            VisualElement buttons = WhimTexUI.CreateRow();
            buttons.AddToClassList("whimtex-layer-fx-buttons");
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 6f;
            buttons.Add(WhimTexUI.CreateButton("Add", AddFx, 64f));
            buttons.Add(WhimTexUI.CreateButton("New Shader FX", CreateShaderFX));
            buttons.Add(WhimTexUI.CreateButton("Preset ▾", () => ShaderFXCatalog.ShowMenu(entry =>
            {
                if (!ResolveLayer()) return;
                ApplyChange("Add Catalog FX", () => activeDocument.AddCatalogShaderFX(layer, entry));
                RefreshFxItems();
            })));
            buttons.Add(WhimTexUI.CreateButton("Edit", EditSelectedFx));
            buttons.Add(WhimTexUI.CreateButton("Remove", RemoveSelectedFx, 72f));
            buttons.Add(WhimTexUI.CreateButton("Close", Close, 64f));
            root.Add(buttons);
        }

        private VisualElement MakeFxField()
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
                    index >= layer.fx.Count)
                {
                    return;
                }

                if (evt.newValue != null && !(evt.newValue is Material) && !(evt.newValue is ShaderFX))
                {
                    field.SetValueWithoutNotify(layer.fx[index]);
                    return;
                }
                ApplyChange("Edit Layer FX", () => layer.fx[index] = evt.newValue);
                RememberFxItems();
            });
            return field;
        }

        private void BindFxField(VisualElement element, int index)
        {
            ObjectField field = (ObjectField)element;
            field.userData = index;
            field.SetValueWithoutNotify(index >= 0 && index < layer.fx.Count
                ? layer.fx[index]
                : null);
        }

        private void AddFx()
        {
            if (!ResolveLayer())
                return;
            ApplyChange("Add Layer FX", () => layer.fx.Add(null));
            RefreshFxItems();
            fxList?.SetSelection(layer.fx.Count - 1);
        }

        private void CreateShaderFX()
        {
            if (!ResolveLayer())
                return;
            ApplyChange("Add Shader FX", () => activeDocument.AddEmbeddedShaderFX(layer));
            RefreshFxItems();
            fxList?.SetSelection(layer.fx.Count - 1);
        }

        private void EditSelectedFx()
        {
            if (!ResolveLayer() || fxList == null)
                return;
            int index = fxList.selectedIndex;
            if (index >= 0 && index < layer.fx.Count && layer.fx[index] != null)
            {
                Selection.activeObject = layer.fx[index];
                EditorGUIUtility.PingObject(layer.fx[index]);
            }
        }

        private void RemoveSelectedFx()
        {
            if (!ResolveLayer() || fxList == null)
                return;

            int[] selected = fxList.selectedIndices.OrderByDescending(index => index).ToArray();
            if (selected.Length == 0)
                return;

            ApplyChange("Remove Layer FX", () =>
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    int index = selected[i];
                    if (index >= 0 && index < layer.fx.Count)
                        layer.fx.RemoveAt(index);
                }
            });
            RefreshFxItems();
        }

        private void RememberFxItems()
        {
            displayedFx.Clear();
            displayedFx.AddRange(layer.fx);
        }

        private void RefreshFxItems()
        {
            if (fxList == null)
                return;
            bool changed = displayedFx.Count != layer.fx.Count;
            for (int i = 0; !changed && i < displayedFx.Count; i++)
                changed = displayedFx[i] != layer.fx[i];

            if (!ReferenceEquals(fxList.itemsSource, layer.fx))
                fxList.itemsSource = layer.fx;
            else if (changed)
                fxList.RefreshItems();
            RememberFxItems();
        }

        private void ApplyChange(string undoName, Action change)
        {
            if (activeDocument == null || change == null || WhimTexApi.IsLayerContentLocked(activeDocument, layer))
                return;

            Undo.RecordObject(activeDocument, undoName);
            applyingChange = true;
            try
            {
                change();
                activeDocument.MarkChanged();
            }
            finally
            {
                applyingChange = false;
            }
        }

        private bool ResolveLayer()
        {
            if (activeDocument == null || string.IsNullOrEmpty(layerId))
                return false;

            Layer resolved = activeDocument.FindLayer(layerId);
            if (resolved == null)
            {
                layer = null;
                return false;
            }
            layer = resolved;
            layer.fx ??= new List<UnityEngine.Object>();
            return true;
        }

        private void OnDocumentChanged(WhimTexDocument changedDocument)
        {
            if (changedDocument != activeDocument || applyingChange)
                return;
            if (WhimTexDocument.IsRefreshingUndo)
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
