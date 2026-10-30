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
public static class LayerClipboardTests
{
    public static string RunNaming() => WhimTex.Tests.UnityC.FixtureContext.Run("Shared copy naming", () =>
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        var scope = WhimTex.Tests.UnityC.FixtureContext.Scope;
        var t = WhimTex.Tests.UnityC.FixtureContext.Context;
        object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, f).Invoke(owner, args);
        var source = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var target = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var window = scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        source.width = source.height = target.width = target.height = 16;
        typeof(WhimTexWindow).GetField("activeDocument", f).SetValue(window, target);
        Layer group = new GroupLayerBehaviour { layerName = "Group" };
        group.children.Add(new ColorFillLayerBehaviour { layerName = "First" });
        group.children.Add(new ColorFillLayerBehaviour { layerName = "Second" });
        source.layers.Add(group); Call(source, "NormalizeModel");
        string original = EditorJsonUtility.ToJson(source);
        var snapshot = scope.Own((WhimTexDocument)Call(source, "CaptureLayerClipboard", new List<Layer> { group, group.children[0] }));
        string clipboard = EditorJsonUtility.ToJson(snapshot);
        target.layers.Add(new ColorFillLayerBehaviour { layerName = "Existing Copy 40" });
        Call(target, "NormalizeModel");
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        try
        {
            void Paste(bool preserve = false) => Call(window, "PasteCopiedLayers", snapshot, false, preserve);
            void Names(string root, string first, string second)
            {
                t.Equal(root, target.layers[0].layerName, "Root copy name");
                t.Equal(first, target.layers[0].children[0].layerName, "First child copy name");
                t.Equal(second, target.layers[0].children[1].layerName, "Second child copy name");
            }
            Paste(); Names("Group Copy 41", "First Copy 42", "Second Copy 43");
            string pastedId = target.layers[0].Id;
            Undo.PerformUndo(); t.Equal(1, target.layers.Count, "One Undo removes the paste");
            Undo.PerformRedo(); Names("Group Copy 41", "First Copy 42", "Second Copy 43");
            t.Equal(pastedId, target.layers[0].Id, "Redo keeps identities and allocated names");
            Call(window, "DuplicateLayers", new List<Layer> { target.layers[0] });
            Names("Group Copy 41 Copy 44", "First Copy 42 Copy 45", "Second Copy 43 Copy 46");
            Paste(); Names("Group Copy 47", "First Copy 48", "Second Copy 49");
            Undo.PerformUndo();
            Paste(); Names("Group Copy 47", "First Copy 48", "Second Copy 49");
            t.Equal(original, EditorJsonUtility.ToJson(source), "Copying does not rename or advance the source document");
            t.Equal(clipboard, EditorJsonUtility.ToJson(snapshot), "Repeated pastes never rename the clipboard snapshot");
            Paste(preserve: true); Names("Group", "First", "Second");
            Paste(); Names("Group Copy 50", "First Copy 51", "Second Copy 52");
            var another = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            another.width = another.height = 16;
            try
            {
                Call(another, "PasteLayers", snapshot);
                t.Equal("Group Copy 1", another.layers[0].layerName, "Other documents use their own copy counter");
            }
            finally { Undo.ClearUndo(another); }
        }
        finally { Undo.FlushUndoRecordObjects(); Undo.ClearUndo(target); Undo.IncrementCurrentGroup(); }
    });

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("LayerClipboardTests", Body);
    static void Body()
    {
        // Run with Unity Pipeline eval_file. Only transient documents; no user assets are changed.
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var docType = typeof(DCFApixels.WhimTex.WhimTexDocument);
        var source = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>());
        var destination = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>());
        DCFApixels.WhimTex.WhimTexDocument snapshot = null;
        DCFApixels.WhimTex.WhimTexDocument orphanSnapshot = null;
        var clipboard = docType.Assembly.GetType("DCFApixels.WhimTex.LayerClipboard", true);
        string savedClipboard = GUIUtility.systemCopyBuffer;
        
        var priorClipboardSnapshot = clipboard.GetField("snapshot", Flags).GetValue(null);
        var priorClipboardMarker = clipboard.GetField("marker", Flags).GetValue(null);
        var priorClipboardRevision = clipboard.GetField("revision", Flags).GetValue(null);
        clipboard.GetField("snapshot", Flags).SetValue(null, null);
        Undo.IncrementCurrentGroup();
        int testGroup = Undo.GetCurrentGroup();
        int checks = 0;
        void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
        object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Flags).Invoke(owner, args);
        System.Collections.Generic.List<DCFApixels.WhimTex.Layer> Layers(params DCFApixels.WhimTex.Layer[] values) => new System.Collections.Generic.List<DCFApixels.WhimTex.Layer>(values);
        Texture2D Pixels(DCFApixels.WhimTex.Layer layer) => (Texture2D)layer.Behaviour.GetType().GetProperty("StoredTexture", Flags).GetValue(layer.Behaviour);
        try
        {
            source.hideFlags = destination.hideFlags = HideFlags.HideAndDontSave;
            source.width = 16; source.height = 8;
            destination.width = 32; destination.height = 32;
            var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(4, 2, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave });
            var colors = new Color[] { new Color(1, .25f, 2, .5f), Color.red, Color.green, Color.blue, Color.clear, Color.white, Color.black, Color.yellow };
            texture.SetPixels(colors); texture.Apply(false, false);
            var drawing = (DCFApixels.WhimTex.DrawingLayerBehaviour)typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetMethod("FromMergedTexture", Flags).Invoke(null, new object[] { texture });
            DCFApixels.WhimTex.Layer paint = drawing;
            paint.layerName = "Paint"; paint.opacity = .6f; paint.enabled = false;
            paint.colorRange = DCFApixels.WhimTex.LayerColorRange.HDR;
            paint.transform.position = new Vector2(.1f, .2f);
            paint.transform.scale = new Vector2(.75f, .5f);
            var paintSurface = (RenderTexture)Call(drawing, "EnsurePaintSurface", 16, 8);
            var previousRenderTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = paintSurface;
                GL.Clear(false, true, new Color(2, .125f, 1, .5f));
                drawing.GetType().GetField("paintSurfaceDirty", Flags).SetValue(drawing, true);
            }
            finally { RenderTexture.active = previousRenderTarget; }
            Check(texture.GetPixel(0, 0).r == 1, "Test contains a fresh GPU stroke not yet in stored pixels");
            var blur = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.BlurLayerBehaviour { radius = 9 });
            blur.layerName = "Blur";
            var group = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.GroupLayerBehaviour());
            group.layerName = "Group"; group.children = Layers(blur, paint);
            source.layers = Layers(group);
            Call(source, "NormalizeModel");
            var effect = (DCFApixels.WhimTex.ShaderFX)Call(source, "AddEmbeddedShaderFX", paint);
            Call(effect, "SetDraftCode", "float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color; }");
            Check((bool)Call(effect, "Apply"), "Inline FX compiles before copying");
            string originalCode = (string)effect.GetType().GetField("code", Flags).GetValue(effect);
            string originalId = paint.Id;
            int copyUndoGroup = Undo.GetCurrentGroup();
            snapshot = (DCFApixels.WhimTex.WhimTexDocument)Call(source, "CaptureLayerClipboard", Layers(group, paint));
            Check(Undo.GetCurrentGroup() == copyUndoGroup, "Copy does not add Undo entries");
            Check(snapshot.layers.Count == 1 && snapshot.layers[0].children.Count == 2, "Group and selected child copied once");
            var snapshotPaint = snapshot.layers[0].children[1];
            var snapshotBlur = snapshot.layers[0].children[0];
            Check(snapshotPaint.Id != originalId, "Snapshot identities are independent");
            Check(Pixels(snapshotPaint) != texture, "Snapshot owns its pixels");
            Check(Pixels(snapshotPaint).width == 4 && Pixels(snapshotPaint).height == 2, "Original Drawing resolution preserved");
            Check(Pixels(snapshotPaint).GetPixel(0, 0).r == 4, "HDR pixels preserved");
            Check(snapshotPaint.fx[0] != effect, "Embedded FX copied independently");
            Check((string)effect.GetType().GetField("code", Flags).GetValue(snapshotPaint.fx[0]) == originalCode, "FX code preserved");
            Check(snapshotPaint.layerName == "Paint" && snapshotPaint.opacity == .6f && !snapshotPaint.enabled, "Common settings preserved");
            Check(snapshotPaint.transform.position == paint.transform.position && snapshotPaint.transform.scale == paint.transform.scale, "Transform preserved");
            Check(((DCFApixels.WhimTex.BlurLayerBehaviour)snapshotBlur.Behaviour).radius == 9, "Behaviour settings preserved");
        
            texture.SetPixel(0, 0, Color.black); texture.Apply(false, false);
            Call(effect, "SetDraftCode", "changed after copy");
            Check(Pixels(snapshotPaint).GetPixel(0, 0).r == 4, "Later source paint does not change clipboard");
            Check((string)effect.GetType().GetField("code", Flags).GetValue(snapshotPaint.fx[0]) == originalCode, "Later source FX edits do not change clipboard");
        
            // A Previous effect copied without its source must not bind to unrelated destination layers.
            orphanSnapshot = (DCFApixels.WhimTex.WhimTexDocument)Call(source, "CaptureLayerClipboard", Layers(blur));
            var orphanEffect = orphanSnapshot.layers[0].Behaviour;
            Check(orphanEffect.GetType().GetField("inputMode", Flags).GetValue(orphanEffect).ToString() == "Specific", "Uncopied Previous target becomes explicit missing target");
            Check(string.IsNullOrEmpty((string)orphanEffect.GetType().GetProperty("TargetLayerId", Flags).GetValue(orphanEffect)), "Uncopied target cleared");
        
            // Specific targets inside the copied set follow the new identities.
            var targetProperty = blur.Behaviour.GetType().GetProperty("TargetLayerId", Flags);
            targetProperty.SetValue(blur.Behaviour, paint.Id);
            var inputField = blur.Behaviour.GetType().GetField("inputMode", Flags);
            inputField.SetValue(blur.Behaviour, Enum.Parse(inputField.FieldType, "Specific"));
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(orphanSnapshot);
            orphanSnapshot = (DCFApixels.WhimTex.WhimTexDocument)Call(source, "CaptureLayerClipboard", Layers(group));
            var remappedBlur = orphanSnapshot.layers[0].children[0].Behaviour;
            Check((string)targetProperty.GetValue(remappedBlur) == orphanSnapshot.layers[0].children[1].Id, "Internal Specific target remapped");
        
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source); source = null;
            Check(Pixels(snapshotPaint) != null && Pixels(snapshotPaint).GetPixel(0, 0).r == 4, "Clipboard survives source document destruction");
            Call(destination, "PasteLayers", snapshot);
            Check(destination.layers.Count == 1 && destination.layers[0].layerName == "Group Copy 1", "Paste assigns the shared Copy number to the root");
            var pasted = destination.layers[0].children[1];
            Check(pasted.Id != snapshotPaint.Id, "Paste generates new IDs");
            Check(Pixels(pasted) != Pixels(snapshotPaint) && Pixels(pasted).GetPixel(0, 0).r == 4, "Paste owns independent HDR pixels");
            Check(Pixels(pasted).width == 4 && Pixels(pasted).height == 2, "Different canvas does not resize stored pixels");
            Check(pasted.fx[0] != snapshotPaint.fx[0], "Paste owns its embedded FX");
            Check((DCFApixels.WhimTex.WhimTexDocument)pasted.fx[0].GetType().GetProperty("EmbeddedOwner", Flags).GetValue(pasted.fx[0]) == destination, "Pasted FX owned by destination");
            string firstPasteId = destination.layers[0].Id;
            Undo.PerformUndo();
            Check(destination.layers.Count == 0, "One Undo removes entire paste");
            Undo.PerformRedo();
            Check(destination.layers.Count == 1 && destination.layers[0].Id == firstPasteId, "Redo restores pasted group identity");
            Check(Pixels(destination.layers[0].children[1]).GetPixel(0, 0).r == 4, "Redo restores Drawing pixels");
            Call(destination, "PasteLayers", snapshot);
            Check(destination.layers.Count == 2 && destination.layers[0].Id != destination.layers[1].Id, "Repeated paste creates unique copies");
            Check(Pixels(destination.layers[0].children[1]) != Pixels(destination.layers[1].children[1]), "Repeated pastes do not share Drawing storage");
            Call(destination, "DuplicateLayers", Layers(destination.layers[0]));
            Check(destination.layers.Count == 3, "Existing Duplicate still works");
            Check(destination.layers[0].layerName != destination.layers[1].layerName, "Duplicate still allocates Copy name");
        
            clipboard.GetMethod("Copy", Flags).Invoke(null, new object[] { destination, Layers(destination.layers[0]) });
            var current = (DCFApixels.WhimTex.WhimTexDocument)clipboard.GetProperty("Current", Flags).GetValue(null);
            Check(current != null, "Layer clipboard becomes available across windows");
            var ownedPixels = Pixels(current.layers[0].children[1]);
            GUIUtility.systemCopyBuffer = "new external text";
            Check(clipboard.GetProperty("Current", Flags).GetValue(null) == null, "External copy invalidates layer clipboard");
            Check(current == null && ownedPixels == null, "Invalidated clipboard releases owned resources");
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
            Undo.RevertAllDownToGroup(testGroup);
            if (snapshot != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(snapshot);
            if (orphanSnapshot != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(orphanSnapshot);
            if (source != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source);
            if (destination != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(destination);
            Undo.IncrementCurrentGroup();
        }
        
    }
}
