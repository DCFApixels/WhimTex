using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Internal foundation for window-independent TIFF authoring. No agent commands are
    // routed here yet. Unity objects/rendering stay on the Editor main thread.
    internal sealed class WhimTexDocumentBuild : IDisposable
    {
        internal TextureCompositor Document { get; private set; }

        private WhimTexDocumentBuild(TextureCompositor document) => Document = document;

        internal static WhimTexDocumentBuild Create(int width, int height)
        {
            if (width < 1 || height < 1 || width > 16384 || height > 16384)
                throw new WhimTexDocumentException("Canvas dimensions must be between 1 and 16384.");
            var document = ScriptableObject.CreateInstance<TextureCompositor>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = width; document.height = height;
            return new WhimTexDocumentBuild(document);
        }

        // Opening restores the editable pixels, but does not compile shaders or render.
        internal static WhimTexDocumentBuild Open(string path)
        {
            if (!WhimTexDocumentFile.TryLoad(path, out var document, out string error, false))
                throw new WhimTexDocumentException(error);
            return new WhimTexDocumentBuild(document);
        }

        internal static WhimTexDocumentBuild Copy(TextureCompositor source) =>
            new(WhimTexDocumentFile.CreateEditableCopy(source));

        private void Prepare()
        {
            if (Document == null) throw new ObjectDisposedException(nameof(WhimTexDocumentBuild));
            if (!string.IsNullOrEmpty(Document.documentLoadWarning))
                throw new WhimTexDocumentException(Document.documentLoadWarning);
            var seen = new HashSet<ShaderFX>();
            void Visit(List<Layer> layers)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    if (layer.modifiers != null)
                        foreach (var modifier in layer.modifiers)
                            if (modifier is ShaderFX effect && seen.Add(effect) && !AssetDatabase.Contains(effect))
                            {
                                effect.RestoreDocumentOwner(Document);
                                effect.RestoreDocumentShader();
                            }
                    Visit(layer.children);
                }
            }
            Visit(Document.layers);
        }

        // The caller owns/disposes the returned preview, independently of the session.
        internal Texture2D Render() { Prepare(); return Document.Compose(); }
        internal string Save(string path) { Prepare(); return WhimTexDocumentFile.Save(Document, path); }

        public void Dispose()
        {
            if (Document == null) return;
            UnityEngine.Object.DestroyImmediate(Document);
            Document = null;
        }
    }
}
