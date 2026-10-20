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
            internal List<ShaderFXParameter> values;
            internal WeakReference<WhimTexDocument> document;
        }
        private static Snapshot snapshot;

        internal static ShaderFXParameterControl ContainerControl(ShaderFXParameter parameter, int groupId) =>
            parameter.controls.Find(c => c.containers != null && Array.Exists(c.containers,
                scope => scope.id == groupId && (scope.kind == ShaderFXContainerKind.Group || scope.kind == ShaderFXContainerKind.Foldout || scope.kind == ShaderFXContainerKind.Tab)));

        internal static List<ShaderFXParameter> Parameters(IReadOnlyList<ShaderFXParameter> parameters, int groupId)
        {
            var result = new List<ShaderFXParameter>();
            foreach (var parameter in parameters)
                if (parameter != null && ContainerControl(parameter, groupId) != null) result.Add(parameter);
            return result;
        }

        internal static void Copy(ShaderParameterViewSource source, int groupId)
        {
            var values = Parameters(source.Parameters, groupId);
            if (values.Count == 0) return;
            var copies = new List<ShaderFXParameter>(values.Count);
            foreach (var value in values) copies.Add(value.Copy());
            var marker = "WhimTex parameter group: " + Guid.NewGuid().ToString("N");
            GUIUtility.systemCopyBuffer = marker;
            var document = source.Document;
            snapshot = new Snapshot { marker = marker, imageRevision = ImageClipboard.Revision, groupId = groupId,
                values = copies, document = document != null ? new WeakReference<WhimTexDocument>(document) : null };
        }

        private static string Label(ShaderFXParameter parameter, int groupId) =>
            ContainerControl(parameter, groupId)?.label ?? UnityEditor.ObjectNames.NicifyVariableName(parameter.name);

        private static bool Compatible(ShaderFXParameter target, int targetGroup, ShaderFXParameter value, int valueGroup)
        {
            if (target.type != value.type) return false;
            var a = ContainerControl(target, targetGroup); var b = ContainerControl(value, valueGroup);
            if (a.type != b.type) return false;
            if (a.type != ShaderFXParameterType.Enum) return true;
            if (a.optionNames.Length != b.optionNames.Length) return false;
            for (int i = 0; i < a.optionNames.Length; i++)
                if (a.optionNames[i] != b.optionNames[i] || a.optionValues[i] != b.optionValues[i]) return false;
            return true;
        }

        internal static bool TryPaste(ShaderParameterViewSource source, int groupId, out List<ShaderFXParameter> values)
        {
            values = null;
            var copied = snapshot;
            if (copied == null || copied.marker != GUIUtility.systemCopyBuffer || copied.imageRevision != ImageClipboard.Revision) return false;
            var targets = Parameters(source.Parameters, groupId);
            if (targets.Count == 0 || targets.Count != copied.values.Count) return false;
            var used = new HashSet<ShaderFXParameter>();
            var prepared = new List<ShaderFXParameter>(targets.Count);
            foreach (var target in targets)
            {
                var matches = copied.values.FindAll(p => p.name == target.name);
                if (matches.Count == 0) matches = copied.values.FindAll(p => Label(p, copied.groupId) == Label(target, groupId));
                if (matches.Count != 1 || !used.Add(matches[0]) || !Compatible(target, groupId, matches[0], copied.groupId)) return false;
                var value = matches[0];
                if (value.type == ShaderFXParameterType.Texture2D)
                {
                    if (!source.SupportsLayerTextures && (value.textureSource == ShaderFXTextureSource.Layer || value.textureSource == ShaderFXTextureSource.Self)) return false;
                    if (value.textureSource == ShaderFXTextureSource.Layer &&
                        (source.Document == null || copied.document == null || !copied.document.TryGetTarget(out var document) ||
                        document == null || document != source.Document || document.FindLayer(value.textureLayerId) == null)) return false;
                    if (value.textureSource == ShaderFXTextureSource.Texture && !ReferenceEquals(value.textureValue, null) && value.textureValue == null) return false;
                }
                var next = target.Copy();
                ShaderFXMetadata.CopyValue(value, next);
                if (next.type == ShaderFXParameterType.Float) next.floatValue = target.Clamp(next.floatValue);
                next.vectorValue = target.ClampInterval(next.vectorValue);
                prepared.Add(next);
            }
            values = prepared;
            return true;
        }
    }
}
