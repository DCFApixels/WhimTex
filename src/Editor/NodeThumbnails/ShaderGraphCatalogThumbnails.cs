using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphCatalogThumbnails
    {
        internal const string Folder = "Packages/com.dcfapixels.whimtex/src/Editor/NodeThumbnails/";
        internal const int DisplaySize = 32, RowHeight = 35;
        [Serializable] internal sealed class Manifest { public Entry[] entries; }
        [Serializable] internal sealed class Entry { public string kind; public Rect uv; }
        static Texture2D atlas;
        static Dictionary<ShaderGraphNodeKind, Rect> regions;

        internal static bool TryGet(ShaderGraphNodeKind? kind, out Texture2D texture, out Rect uv)
        {
            texture = null; uv = default;
            if (!kind.HasValue) return false;
            if (regions == null)
            {
                regions = new();
                atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Catalog.png");
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "Catalog.json");
                if (atlas != null && json != null)
                {
                    var manifest = JsonUtility.FromJson<Manifest>(json.text);
                    if (manifest?.entries != null) foreach (var entry in manifest.entries)
                        if (Enum.TryParse(entry.kind, out ShaderGraphNodeKind key)) regions[key] = entry.uv;
                }
            }
            if (atlas == null || !regions.TryGetValue(kind.Value, out uv)) return false;
            texture = atlas; return true;
        }

        internal static float Height(ShaderGraphNodeKind? kind, float compactHeight) =>
            WhimTexUserSettings.GraphCatalogThumbnails && TryGet(kind, out _, out _) ? RowHeight : compactHeight;

        internal static void Reset() { atlas = null; regions = null; }
        internal static ToolbarMenu CreateSelector(Action changed)
        {
            var menu = new ToolbarMenu { name = "catalog-display" };
            menu.style.flexShrink = 0;
            void Sync() { menu.text = WhimTexUserSettings.GraphCatalogThumbnails ? "Thumbnails" : "Compact"; changed(); }
            menu.text = WhimTexUserSettings.GraphCatalogThumbnails ? "Thumbnails" : "Compact";
            menu.menu.AppendAction("Compact", _ => WhimTexUserSettings.GraphCatalogThumbnails = false,
                _ => !WhimTexUserSettings.GraphCatalogThumbnails ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.menu.AppendAction("Thumbnails", _ => WhimTexUserSettings.GraphCatalogThumbnails = true,
                _ => WhimTexUserSettings.GraphCatalogThumbnails ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.RegisterCallback<AttachToPanelEvent>(_ => { WhimTexUserSettings.CatalogDisplayChanged += Sync; Sync(); });
            menu.RegisterCallback<DetachFromPanelEvent>(_ => WhimTexUserSettings.CatalogDisplayChanged -= Sync);
            return menu;
        }
    }

    internal sealed class ShaderGraphCatalogPreview : VisualElement
    {
        static StyleSheet sheet;
        readonly ShaderGraphCatalogIcon icon = new();
        readonly Image preview = new() { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
        internal ShaderGraphCatalogPreview()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-graph-catalog-preview");
            if (sheet == null) sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ShaderGraphCatalogThumbnails.Folder + "ShaderGraphCatalogThumbnails.uss");
            if (sheet != null) styleSheets.Add(sheet);
            Add(icon); Add(preview);
        }
        internal void Bind(ShaderGraphCatalogIcon.Appearance appearance)
        {
            icon.Bind(appearance);
            Texture2D texture = null; Rect uv = default;
            bool available = WhimTexUserSettings.GraphCatalogThumbnails && ShaderGraphCatalogThumbnails.TryGet(appearance.kind, out texture, out uv);
            EnableInClassList("whimtex-graph-catalog-preview--large", available);
            if (available)
            {
                preview.image = texture; preview.uv = uv;
            }
            else preview.image = null;
            preview.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            icon.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
