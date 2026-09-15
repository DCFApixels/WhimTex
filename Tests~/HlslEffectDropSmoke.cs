using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class HlslEffectDropSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(TextureCompositorWindow);
        var catalog = type.Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
        var inspect = catalog.GetMethod("InspectDroppedHlsl", BindingFlags.NonPublic | BindingFlags.Static);
        string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/Gain.hlsl";
        var entry = inspect.Invoke(null, new object[] { path });
        if (entry == null) throw new Exception("Effect not recognized");
        if (inspect.Invoke(null, new object[] { "Assets/NotAnEffect.png" }) != null)
            throw new Exception("Unrelated file accepted");
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        try
        {
            var document = (TextureCompositor)type.GetField("compositor", flags).GetValue(window);
            var drop = type.GetMethod("ApplyDroppedHlsl", flags);
            drop.Invoke(window, new object[] { entry, null });
            if (document.layers.Count != 1 || !(document.layers[0].Behaviour is ShaderProcessorLayerBehaviour))
                throw new Exception("Processor missing");
            var layer = document.layers[0];
            if (layer.modifiers.Count != 1) throw new Exception("Unexpected default FX");
            drop.Invoke(window, new object[] { entry, layer });
            if (document.layers.Count != 1 || layer.modifiers.Count != 2 || layer.modifiers[0] == layer.modifiers[1])
                throw new Exception("Append did not create independent FX");
            Undo.PerformUndo();
            if (document.layers.Count != 1 || document.layers[0].modifiers.Count != 1)
                throw new Exception("Append Undo failed");
            Undo.PerformUndo();
            if (document.layers.Count != 0) throw new Exception("Processor Undo failed");
            return "PASS: HLSL recognition, Processor creation, independent appended FX and both Undo operations.";
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
    }
}
