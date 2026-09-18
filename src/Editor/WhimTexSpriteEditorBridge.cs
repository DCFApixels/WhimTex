using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DCFApixels.WhimTex.SpriteEditor")]

namespace DCFApixels.WhimTex
{
    internal static class WhimTexSpriteEditorBridge
    {
        internal static Action<TextureCompositor> Open;
        internal static bool Available => Open != null;
    }
}
