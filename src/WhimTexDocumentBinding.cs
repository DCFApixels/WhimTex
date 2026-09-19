using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Separate transient object: Undo on the document must not roll back the disk revision
    // or Save As destination. It survives domain reload through the document's reference.
    internal sealed class WhimTexDocumentBinding : ScriptableObject
    {
        public TextureCompositor owner;
        public string guid, path;
        public long length, writeTicks;
        public bool dirty;
    }
}
