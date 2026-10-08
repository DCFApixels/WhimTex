using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class LayerClipboardWindowTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("LayerClipboardWindowTests", Body);
    static void Body()
    {
        // Unity Pipeline eval_file: unshown temporary windows, not the user's open documents.
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var type = typeof(DCFApixels.WhimTex.WhimTexWindow);
        var clipboard = type.Assembly.GetType("DCFApixels.WhimTex.LayerClipboard", true);
        var first = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexWindow>());
        var second = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexWindow>());
        var source = (DCFApixels.WhimTex.WhimTexDocument)type.GetField("activeDocument", Flags).GetValue(first);
        var target = (DCFApixels.WhimTex.WhimTexDocument)type.GetField("activeDocument", Flags).GetValue(second);
        string savedClipboard = GUIUtility.systemCopyBuffer;
        
        var priorClipboardSnapshot = clipboard.GetField("snapshot", Flags).GetValue(null);
        var priorClipboardMarker = clipboard.GetField("marker", Flags).GetValue(null);
        var priorClipboardRevision = clipboard.GetField("revision", Flags).GetValue(null);
        clipboard.GetField("snapshot", Flags).SetValue(null, null);
        object savedArea = type.GetField("areaClipboard", Flags).GetValue(null);
        type.GetField("areaClipboard", Flags).SetValue(null, null);
        int checks = 0;
        Undo.IncrementCurrentGroup();
        int testGroup = Undo.GetCurrentGroup();
        void Check(bool ok, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
        object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Flags).Invoke(owner, args);
        void Command(DCFApixels.WhimTex.WhimTexWindow window, string name)
        {
            using var evt = UnityEngine.UIElements.ExecuteCommandEvent.GetPooled(name);
            evt.target = window.rootVisualElement;
            Call(window, "ExecuteAreaCommand", evt);
        }
        void Key(DCFApixels.WhimTex.WhimTexWindow window, KeyCode key, bool shift = false, UnityEngine.UIElements.VisualElement targetElement = null)
        {
            var systemEvent = new Event { type = EventType.KeyDown, keyCode = key, modifiers = EventModifiers.Control | (shift ? EventModifiers.Shift : EventModifiers.None) };
            using var evt = UnityEngine.UIElements.KeyDownEvent.GetPooled(systemEvent);
            evt.target = targetElement ?? window.rootVisualElement;
            Call(window, "OnToolkitKeyDown", evt);
        }
        try
        {
            source.width = source.height = target.width = target.height = 8;
            var layer = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = Color.red });
            layer.layerName = "Red";
            source.layers.Add(layer);
            Call(source, "NormalizeModel");
            Call(first, "SelectOnlyLayer", layer.Id);
            Command(first, "Copy");
            Check(clipboard.GetProperty("Current", Flags).GetValue(null) != null, "Copy command without area copies layer data");
            Command(second, "Paste");
            Check(target.layers.Count == 1 && target.layers[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "Paste command in another window preserves layer type");
            Check(target.layers[0].layerName == "Red", "Layer name preserved by window paste");
            Check((string)type.GetField("selectedLayerId", Flags).GetValue(second) == target.layers[0].Id, "Pasted layer is selected");
            Key(first, KeyCode.C);
            Key(second, KeyCode.V);
            Check(target.layers.Count == 2 && target.layers[0].Id != target.layers[1].Id, "Keyboard copy/paste creates independent layer");
        
            var text = new UnityEngine.UIElements.TextField();
            Check(!(bool)Call(first, "CanHandleAreaCommand", "Copy", text), "Command leaves text field copy alone");
            var beforeText = clipboard.GetProperty("Current", Flags).GetValue(null);
            Key(first, KeyCode.C, false, text);
            Check(object.ReferenceEquals(beforeText, clipboard.GetProperty("Current", Flags).GetValue(null)), "Key leaves text copy alone");
        
            object selection = Call(first, "GetAreaSelection");
            Call(selection, "All");
            Key(first, KeyCode.C);
            Check(clipboard.GetProperty("Current", Flags).GetValue(null) == null, "Area copy replaces layer clipboard");
            Check(type.GetField("areaClipboard", Flags).GetValue(null) != null, "Selected area stored as pixels");
            Key(second, KeyCode.V);
            Check(target.layers.Count == 3 && target.layers[0].Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour, "Selected area pastes as Drawing Layer");
            Call(selection, "Clear");
            Key(first, KeyCode.C, true);
            Check(clipboard.GetProperty("Current", Flags).GetValue(null) == null, "Copy Merged without selection stays pixel copy");
            Key(first, KeyCode.C);
            Check(type.GetField("areaClipboard", Flags).GetValue(null) == null, "Layer copy replaces old area pixels");
            Call(Call(second, "GetAreaSelection"), "All");
            Key(second, KeyCode.V);
            Check(target.layers.Count == 4 && target.layers[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "Destination area selection does not clip copied layers");
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(first); first = null;
            Key(second, KeyCode.V);
            Check(target.layers.Count == 5 && target.layers[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour, "Clipboard still pastes after closing original window");
            return;
        }
        finally
        {
            clipboard.GetMethod("Clear", Flags).Invoke(null, null);
            GUIUtility.systemCopyBuffer = savedClipboard;
            clipboard.GetField("snapshot", Flags).SetValue(null, priorClipboardSnapshot);
            clipboard.GetField("marker", Flags).SetValue(null, priorClipboardMarker);
            clipboard.GetField("revision", Flags).SetValue(null,
                (string)priorClipboardMarker == savedClipboard
                    ? typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.ImageClipboard", true).GetProperty("Revision", Flags).GetValue(null) : priorClipboardRevision);
            type.GetField("areaClipboard", Flags).SetValue(null, savedArea);
            Undo.RevertAllDownToGroup(testGroup);
            if (first != null) { first.DiscardChanges(); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(first); }
            if (second != null) { second.DiscardChanges(); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(second); }
            Undo.IncrementCurrentGroup();
        }
        
    }
}
