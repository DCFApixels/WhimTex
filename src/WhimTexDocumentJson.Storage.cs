using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        private static readonly byte[] StorageSignature = Encoding.ASCII.GetBytes("WTDocument\0");

        internal static byte[] SerializeStorage(WhimTexDocument document, WhimTexDocumentContainer container)
        {
            PrepareDrawing(document.layers);
            byte[] comments = document.EncodeCommentsBlock(container);
            using var storage = new StorageContext(container);
            var written = WriteCore(document, null, new WhimTexJsonWriteOptions
            { Mode = WhimTexJsonWriteMode.Full, Storage = storage, AllowDataLoss = true });
            var root = Parse(written.Json);
            ((JObject)root["document"]).Remove("comments");
            root["writeMode"] = document.jsonWriteMode.ToString();
            byte[] json = new UTF8Encoding(false, true).GetBytes(root.ToString(Formatting.None));
            if (json.Length > MaxCharacters - StorageSignature.Length)
                throw new WhimTexDocumentException("The document model exceeds the 64 MiB budget.");
            var result = new byte[StorageSignature.Length + json.Length];
            Buffer.BlockCopy(StorageSignature, 0, result, 0, StorageSignature.Length);
            Buffer.BlockCopy(json, 0, result, StorageSignature.Length, json.Length);
            document.WriteCommentsBlock(container, comments);
            return result;
        }

        private static void PrepareDrawing(List<Layer> layers)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                if (layer.Behaviour is DrawingLayerBehaviour drawing)
                {
                    foreach (var texture in drawing.StoredPixelTextures) { }
                    drawing.ValidateTiledPixels();
                }
                PrepareDrawing(layer.children);
            }
        }

        internal static WhimTexDocumentSerializer.ModelReadResult DeserializeStorage(byte[] bytes,
            WhimTexDocumentContainer container, string sourcePath, bool deferDrawingTextures)
        {
            if (bytes == null || bytes.Length < StorageSignature.Length || bytes.Length > MaxCharacters)
                throw new WhimTexDocumentException("Invalid document model length.");
            for (int i = 0; i < StorageSignature.Length; i++)
                if (bytes[i] != StorageSignature[i])
                    throw new WhimTexDocumentException("Unsupported document format. This editor expects documentVersion 1.");
            string json = new UTF8Encoding(false, true).GetString(bytes, StorageSignature.Length, bytes.Length - StorageSignature.Length);
            using var storage = new StorageContext(container, sourcePath, deferDrawingTextures);
            using var read = ReadCore(json, false, null, storage);
            read.Document.ReadCommentsBlock(container);
            read.Document.ResetUndoTrackingAfterLoad();
            var document = read.TakeDocument();
            storage.TransferOwnership();
            return new WhimTexDocumentSerializer.ModelReadResult(document, new List<string>(), new List<string>(),
                new List<string>(read.Warnings));
        }

        internal sealed class StorageContext : IDisposable
        {
            private readonly WhimTexDocumentContainer container;
            private readonly string path;
            private readonly bool deferDrawing;
            private readonly Dictionary<UnityObjectID, JObject> textures = new();
            private readonly Dictionary<string, Texture2D> loaded = new(StringComparer.Ordinal);
            private readonly Dictionary<string, DrawingLayerBehaviour> deferred = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> sources = new(StringComparer.Ordinal);
            private readonly List<Texture2D> owned = new();
            private long textureBytes, sourceBytes;

            internal StorageContext(WhimTexDocumentContainer container, string path = null, bool deferDrawing = false)
            { this.container = container; this.path = path; this.deferDrawing = deferDrawing && !string.IsNullOrEmpty(path); }

            internal bool TryWrite(object value, out JToken token)
            {
                token = null;
                if (value is ShaderFX || value is FileLayerBehaviour file && file.HasExternalSource ||
                    value is UnityEngine.Object asset && AssetDatabase.Contains(asset)) container.HasExternalInputs = true;
                if (value is not Texture2D texture || AssetDatabase.Contains(texture)) return false;
                var textureId = UnityObjectID.FromObject(texture);
                if (!textures.TryGetValue(textureId, out var descriptor))
                {
                    WhimTexDocumentLimits.CheckTexture(texture.GetRawTextureData<byte>().Length, ref textureBytes, "Drawing texture");
                    byte[] metadata = WhimTexDocumentSerializer.EncodeTexture(texture, container, textures.Count);
                    descriptor = new JObject { ["$texture"] = Convert.ToBase64String(metadata) };
                    textures.Add(textureId, descriptor);
                }
                token = descriptor.DeepClone();
                return true;
            }

            internal JToken StoreCode(string code)
            {
                if (code == null) return JValue.CreateNull();
                if (!sources.TryGetValue(code, out var block))
                {
                    byte[] bytes = ShaderGraphSources.Utf8.GetBytes(code);
                    sourceBytes += bytes.Length;
                    if (sourceBytes > ShaderGraphSources.MaximumArchiveBytes)
                        throw new WhimTexDocumentException("The shared shader source table exceeds 32 MiB.");
                    block = "shader-source:sha256:" + ShaderGraphSources.Digest(bytes);
                    if (container.Contains(block))
                    {
                        if (container.LengthOf(block) != bytes.Length || !container.Get(block).AsSpan().SequenceEqual(bytes))
                            throw new WhimTexDocumentException("Shader source digest conflict.");
                    }
                    else container.SetCompressed(block, block, bytes, System.IO.Compression.CompressionLevel.Optimal);
                    sources.Add(code, block);
                }
                return new JObject { ["$code"] = block };
            }

            internal bool TryRead(JToken token, Type declared, object parent, string key, out object value)
            {
                value = null;
                if (token is not JObject node) return false;
                if (node["$code"] != null)
                {
                    CheckKeys(node, "$code");
                    if (declared != typeof(string) || node["$code"].Type != JTokenType.String)
                        throw new WhimTexDocumentException("Invalid shader source reference.");
                    string block = (string)node["$code"];
                    if (!sources.TryGetValue(block, out string code))
                    {
                        long length = container.LengthOf(block);
                        if (length < 0 || length > ShaderGraphSources.MaximumArchiveBytes - sourceBytes)
                            throw new WhimTexDocumentException("The shared shader source table exceeds 32 MiB.");
                        byte[] bytes = container.Get(block);
                        if (block != "shader-source:sha256:" + ShaderGraphSources.Digest(bytes))
                            throw new WhimTexDocumentException("Invalid shader source digest.");
                        sourceBytes += bytes.Length;
                        code = ShaderGraphSources.Utf8.GetString(bytes);
                        sources.Add(block, code);
                    }
                    value = code;
                    return true;
                }
                if (node["$texture"] == null) return false;
                CheckKeys(node, "$texture");
                if (declared != typeof(Texture2D) || node["$texture"].Type != JTokenType.String || ((string)node["$texture"]).Length > 2048)
                    throw new WhimTexDocumentException("Invalid embedded texture descriptor.");
                string identity = (string)node["$texture"];
                if (loaded.TryGetValue(identity, out var existing)) { value = existing; return true; }
                if (deferred.TryGetValue(identity, out var previous))
                {
                    var materialized = previous.StoredTexture;
                    TrackTexture(identity, materialized);
                    deferred.Remove(identity);
                    value = materialized;
                    return true;
                }
                var drawing = deferDrawing && key == "pixels" ? parent as DrawingLayerBehaviour : null;
                var texture = WhimTexDocumentSerializer.DecodeTexture(Convert.FromBase64String(identity), container, path, drawing);
                if (drawing != null && texture == null) deferred.Add(identity, drawing);
                else TrackTexture(identity, texture);
                value = texture;
                return true;
            }

            private void TrackTexture(string identity, Texture2D texture)
            {
                loaded.Add(identity, texture);
                if (texture == null || AssetDatabase.Contains(texture)) return;
                owned.Add(texture);
                WhimTexDocumentLimits.CheckTexture(texture.GetRawTextureData<byte>().Length, ref textureBytes, "Drawing texture");
            }

            internal void AfterRead(object value)
            {
                if (value is ISerializationCallbackReceiver receiver) receiver.OnAfterDeserialize();
                if (value is ShaderFX fx) fx.ResetUndoTrackingAfterLoad();
            }

            internal void TransferOwnership() => owned.Clear();
            public void Dispose()
            {
                foreach (var texture in owned) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                owned.Clear();
            }
        }
    }
}
