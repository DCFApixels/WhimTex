using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class WhimTexCommentAttachment
    {
        public string layerId;
        public string assetGuid;
        public long localId;
        public string name;
        [SerializeReference] public WhimTexCommentMask mask;
        public bool IsLayer => !string.IsNullOrEmpty(layerId);

        public static WhimTexCommentAttachment ForMask(WhimTexCommentMask mask) => new WhimTexCommentAttachment
        { mask = mask, name = "Selection" };

        public static WhimTexCommentAttachment ForLayer(Layer layer) => new WhimTexCommentAttachment
        { layerId = layer.Id, name = layer.layerName };

        public static WhimTexCommentAttachment ForAsset(Object asset)
        {
            if (asset == null || !EditorUtility.IsPersistent(asset) ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id)) return null;
            return new WhimTexCommentAttachment { assetGuid = guid, localId = id, name = asset.name };
        }

        public Object ResolveAsset()
        {
            if (string.IsNullOrEmpty(assetGuid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (string.IsNullOrEmpty(path)) return null;
            bool Matches(Object item) => item != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out string _, out long id) && id == localId;
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (Matches(main)) return main;
            if (AssetDatabase.IsValidFolder(path)) return null;
            foreach (var item in AssetDatabase.LoadAllAssetsAtPath(path)) if (Matches(item)) return item;
            return null;
        }

        internal void Validate()
        {
            bool layer = !string.IsNullOrEmpty(layerId), asset = !string.IsNullOrEmpty(assetGuid);
            if ((layer ? 1 : 0) + (asset ? 1 : 0) + (mask != null ? 1 : 0) != 1 ||
                !asset && localId != 0 || layer && layerId.Length > 128 ||
                asset && !Guid.TryParseExact(assetGuid, "N", out _) || string.IsNullOrWhiteSpace(name) || name.Length > 1024)
                throw new WhimTexDocumentException("Comments: invalid layer, asset or selection mask attachment.");
            mask?.Validate();
        }

        internal static List<WhimTexCommentAttachment> Copy(List<WhimTexCommentAttachment> source)
        {
            var result = new List<WhimTexCommentAttachment>();
            if (source != null) foreach (var item in source)
                if (item != null) result.Add(new WhimTexCommentAttachment
                { layerId = item.layerId, assetGuid = item.assetGuid, localId = item.localId, name = item.name, mask = item.mask?.Copy() });
            return result;
        }
    }
}
