using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderParameterGroupClipboard
    {
        private sealed class Snapshot
        {
            internal string marker;
            internal uint imageRevision;
            internal int groupId;
            internal List<ShaderParameter> values;
            internal WeakReference<WhimTexDocument> document;
        }
        private static Snapshot snapshot;

        internal static ShaderParameterControl ContainerControl(ShaderParameter parameter, int groupId) =>
            parameter.controls.Find(c => c.containers != null && Array.Exists(c.containers,
                scope => scope.id == groupId && (scope.kind == ShaderContainerKind.Group || scope.kind == ShaderContainerKind.Foldout || scope.kind == ShaderContainerKind.Tab)));

        internal static List<ShaderParameter> Parameters(IReadOnlyList<ShaderParameter> parameters, int groupId)
        {
            var result = new List<ShaderParameter>();
            foreach (var parameter in parameters)
                if (parameter != null && ContainerControl(parameter, groupId) != null) result.Add(parameter);
            return result;
        }

        internal static void Copy(ShaderParameterViewSource source, int groupId)
        {
            var values = Parameters(source.Parameters, groupId);
            if (values.Count == 0) return;
            var copies = new List<ShaderParameter>(values.Count);
            foreach (var value in values) copies.Add(value.Copy());
            var marker = "WhimTex parameter group: " + Guid.NewGuid().ToString("N");
            GUIUtility.systemCopyBuffer = marker;
            var document = source.Document;
            snapshot = new Snapshot { marker = marker, imageRevision = ImageClipboard.Revision, groupId = groupId,
                values = copies, document = document != null ? new WeakReference<WhimTexDocument>(document) : null };
        }

        private static string Label(ShaderParameter parameter, int groupId) =>
            ContainerControl(parameter, groupId)?.label ?? UnityEditor.ObjectNames.NicifyVariableName(parameter.name);

        private static bool Compatible(ShaderParameter target, int targetGroup, ShaderParameter value, int valueGroup)
        {
            if (target.type != value.type || target.ValueType != value.ValueType) return false;
            var a = ContainerControl(target, targetGroup); var b = ContainerControl(value, valueGroup);
            if (a.type != b.type) return false;
            if (a.type != ShaderParameterType.Enum) return true;
            if (a.optionNames.Length != b.optionNames.Length) return false;
            for (int i = 0; i < a.optionNames.Length; i++)
                if (a.optionNames[i] != b.optionNames[i] || a.optionValues[i] != b.optionValues[i]) return false;
            return true;
        }

        internal static bool TryPaste(ShaderParameterViewSource source, int groupId, out List<ShaderParameter> values)
        {
            values = null;
            var copied = snapshot;
            if (copied == null || copied.marker != GUIUtility.systemCopyBuffer || copied.imageRevision != ImageClipboard.Revision) return false;
            var targets = Parameters(source.Parameters, groupId);
            if (targets.Count == 0 || targets.Count != copied.values.Count) return false;
            var used = new HashSet<ShaderParameter>();
            var prepared = new List<ShaderParameter>(targets.Count);
            foreach (var target in targets)
            {
                var matches = copied.values.FindAll(p => p.name == target.name);
                if (matches.Count == 0) matches = copied.values.FindAll(p => Label(p, copied.groupId) == Label(target, groupId));
                if (matches.Count != 1 || !used.Add(matches[0]) || !Compatible(target, groupId, matches[0], copied.groupId)) return false;
                var value = matches[0];
                if (value.type == ShaderParameterType.Texture2D)
                {
                    if (!source.SupportsLayerTextures && (value.textureSource == ShaderTextureSource.Layer || value.textureSource == ShaderTextureSource.Self)) return false;
                    if (value.textureSource == ShaderTextureSource.Layer &&
                        (source.Document == null || copied.document == null || !copied.document.TryGetTarget(out var document) ||
                        document == null || document != source.Document || document.FindLayer(value.textureLayerId) == null)) return false;
                    if (value.textureSource == ShaderTextureSource.Texture && !ReferenceEquals(value.textureValue, null) && value.textureValue == null) return false;
                }
                var next = target.Copy();
                ShaderMetadata.CopyValue(value, next);
                if (next.type == ShaderParameterType.Float) next.floatValue = target.Clamp(next.floatValue);
                next.vectorValue = target.ClampInterval(next.vectorValue);
                prepared.Add(next);
            }
            values = prepared;
            return true;
        }
    }
}
