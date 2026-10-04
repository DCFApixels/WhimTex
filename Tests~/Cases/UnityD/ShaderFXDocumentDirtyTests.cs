// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// Unity run_script, entry ShaderFXDocumentDirtyTests.Run.
// Transient model/container round trips and hidden test windows only. No asset writes,
// user-document edits or Unity internal reflection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ShaderFXDocumentDirtyTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXDocumentDirtyTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteRun();
    });

    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Package = typeof(TextureCompositor).Assembly;
    static readonly List<Object> owned = new();
    static int checks, changes, refreshes;
    static TextureCompositor observed;
    static Type Type(string name) => Package.GetType("DCFApixels.WhimTex." + name, true);
    static object Invoke(Type type, object target, string name, params object[] args) => type.GetMethods(Any)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Call(object target, string name, params object[] args) => Invoke(target.GetType(), target, name, args);
    static object Get(object target, string name) => target.GetType().GetField(name, Any).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Any).SetValue(target, value);
    static void Check(bool condition, string message) { context.True(condition, message); }
    static void Changed(TextureCompositor doc) { if (doc == observed) changes++; }
    static void Refreshed(TextureCompositor doc) { if (doc == observed) refreshes++; }

    static object Entry(string file)
    {
        foreach (object entry in (IEnumerable)Invoke(Type("ShaderFXCatalog"), null, "GetEntries"))
            if (!(bool)Get(entry, "user") && !(bool)Get(entry, "assetPreset") &&
                ((string)Get(entry, "path")).EndsWith("/" + file, StringComparison.Ordinal)) return entry;
        throw new Exception("Missing built-in preset: " + file);
    }

    static TextureCompositor Source(string preset)
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); owned.Add(doc);
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 16;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = new Color(.2f, .4f, .6f, 1f) }));
        ShaderFX effect = null;
        if (preset == "inline")
        {
            effect = (ShaderFX)Invoke(typeof(ShaderFX), null, "CreateAgentDraft", doc,
                "float4 ApplyFX(float2 uv, float4 color) { return color * 0.75; }", new List<ShaderFXParameter>());
            Call(effect, "ApplyAgentDraft");
        }
        else if (preset != null) effect = (ShaderFX)Invoke(typeof(ShaderFX), null, "FromCatalog", doc, Entry(preset));
        if (effect != null) { owned.Add(effect); doc.layers[0].modifiers.Add(effect); Call(effect, "SuspendDocumentCatalogReload"); }
        return doc;
    }

    static TextureCompositor RoundTrip(TextureCompositor source)
    {
        using var container = new WhimTexDocumentContainer();
        byte[] model = (byte[])Invoke(Type("WhimTexDocumentSerializer"), null, "Serialize", source, container);
        var read = Invoke(Type("WhimTexDocumentSerializer"), null, "Deserialize", model, container,
            typeof(TextureCompositor), null, false);
        var doc = (TextureCompositor)read.GetType().GetProperty("Model", Any).GetValue(read);
        owned.Add(doc); doc.hideFlags = HideFlags.HideAndDontSave;
        // Only creates an in-memory binding; the path is never written or imported.
        Invoke(Type("WhimTexDocumentService"), null, "Bind", doc, "Temp/WhimTex/DirtySmoke-" + Guid.NewGuid().ToString("N") + ".tiff");
        foreach (ShaderFX effect in doc.layers[0].modifiers)
        {
            owned.Add(effect);
            Call(effect, "RestoreDocumentOwner", doc);
            Call(effect, "SuspendDocumentCatalogReload");
        }
        observed = doc; changes = refreshes = 0;
        return doc;
    }

    static TextureCompositorWindow Window(TextureCompositor doc)
    {
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>(); owned.Add(window);
        window.hideFlags = HideFlags.HideAndDontSave;
        Call(window, "SetCompositor", doc);
        return window;
    }

    static void Drain(ShaderFX effect)
    {
        if (!(bool)Get(effect, "notificationQueued")) return;
        var method = typeof(ShaderFX).GetMethod("SendNotification", Any);
        var callback = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), effect, method);
        EditorApplication.delayCall -= callback;
        method.Invoke(effect, null);
    }

    static void Clean(TextureCompositorWindow window, TextureCompositor doc)
    {
        Set(window, "temporaryDocumentDirty", false);
        Set(Get(doc, "documentBinding"), "dirty", false);
        Call(window, "UpdateUnsavedChangesState");
        changes = refreshes = 0;
    }

    static void AssertDirty(TextureCompositorWindow window, TextureCompositor doc, bool expected, string label)
    {
        Check(window.hasUnsavedChanges == expected, label + ": window dirty");
        Check((bool)Get(Get(doc, "documentBinding"), "dirty") == expected, label + ": document binding dirty");
    }

    private static void ExecuteRun()
    {
        checks = changes = refreshes = 0; owned.Clear();
        var originals = Resources.FindObjectsOfTypeAll<TextureCompositorWindow>()
            .Select(w => (window: w, document: Get(w, "compositor"), dirty: w.hasUnsavedChanges)).ToArray();
        var changedEvent = typeof(TextureCompositor).GetEvent("Changed", Any);
        var refreshedEvent = typeof(TextureCompositor).GetEvent("RenderResourcesChanged", Any);
        Action<TextureCompositor> onChanged = Changed, onRefreshed = Refreshed;
        changedEvent.GetAddMethod(true).Invoke(null, new object[] { onChanged });
        refreshedEvent.GetAddMethod(true).Invoke(null, new object[] { onRefreshed });
        try
        {
            var lazyDoc = Source(null);
            var lazyDrawing = new DrawingLayerBehaviour();
            lazyDoc.layers.Add(new Layer(lazyDrawing));
            Type deferredType = Type("WhimTexDocumentSerializer").GetNestedType("DeferredTextureInfo", Any);
            object deferred = Activator.CreateInstance(deferredType, Any, null, new object[] {
                "Temp/WhimTex/Absent-" + Guid.NewGuid().ToString("N") + ".tiff", "drawing", 16, 16,
                TextureFormat.RGBA32, 1, false, 0L, 0L }, null);
            Call(lazyDrawing, "SetDeferredTexture", deferred);
            Call(lazyDoc, "ResetUndoTrackingAfterLoad");
            Check((bool)lazyDrawing.GetType().GetProperty("HasDeferredTexture", Any).GetValue(lazyDrawing),
                "Undo baseline must not read deferred Drawing pixels");
            ((ISerializationCallbackReceiver)lazyDoc).OnAfterDeserialize();
            Check((bool)Get(lazyDoc, "undoDeserialized"), "real model Undo is still detected");
            Call(lazyDoc, "ResetUndoTrackingAfterLoad");
            foreach (string preset in new[] { null, "inline", "Levels.hlsl", "Normalize.hlsl" })
            {
                var source = Source(preset);
                var doc = RoundTrip(source);
                Check(!(bool)Get(doc, "undoDeserialized"), preset + ": file load is not model Undo");
                Check(!(bool)Call(doc, "NativeUndoVersionsChanged"), preset + ": loaded native Undo baseline");
                foreach (ShaderFX effect in doc.layers[0].modifiers)
                    Check(!(bool)Call(effect, "ConsumeUndoChanges"), preset + ": file load is not FX Undo");
                int dirtyBefore = EditorUtility.GetDirtyCount(doc);
                int undoBefore = Undo.GetCurrentGroup();
                Invoke(typeof(WhimTexDocumentFile), null, "CompileEmbeddedEffects", doc);
                Check(EditorUtility.GetDirtyCount(doc) == dirtyBefore, preset + ": restoration does not dirty owner");
                Check(Undo.GetCurrentGroup() == undoBefore, preset + ": restoration does not alter Undo group");
                var window = Window(doc);
                AssertDirty(window, doc, false, preset + " before delayed notification");
                foreach (ShaderFX effect in doc.layers[0].modifiers) Drain(effect);
                AssertDirty(window, doc, false, preset + " after delayed notification");
                Check(changes == 0 && refreshes == (preset == null ? 0 : 1), preset + ": render-only notification");
                var expected = source.ComposeCanvas(); var actual = doc.ComposeCanvas();
                try { Check((expected.GetPixel(8, 8) - actual.GetPixel(8, 8)).maxColorComponent < .002f &&
                    (actual.GetPixel(8, 8) - expected.GetPixel(8, 8)).maxColorComponent < .002f, preset + ": restored render matches"); }
                finally { Object.DestroyImmediate(expected); Object.DestroyImmediate(actual); }

                if (preset == null)
                {
                    Call(doc, "MarkChanged");
                    AssertDirty(window, doc, true, "ordinary layer edit");
                    continue;
                }
                var fx = (ShaderFX)doc.layers[0].modifiers[0];
                ((ISerializationCallbackReceiver)fx).OnAfterDeserialize();
                Check((bool)Call(fx, "ConsumeUndoChanges"), "real FX Undo is still detected");
                Check(!(bool)Call(fx, "ConsumeUndoChanges"), "FX Undo consumed once");
                // A restoration arriving after an edit must not clear dirty.
                fx.Active = !fx.Active; Drain(fx);
                AssertDirty(window, doc, true, "FX edit");
                Call(fx, "QueueNotification", false); Drain(fx);
                AssertDirty(window, doc, true, "resource refresh after edit");
                // Coalescing both orders must retain the content notification.
                for (int order = 0; order < 2; order++)
                {
                    Clean(window, doc);
                    if (order == 0) Call(fx, "QueueNotification", false);
                    fx.Active = !fx.Active;
                    Call(fx, "QueueNotification", false);
                    Drain(fx);
                    Check(changes == 1 && refreshes == 0, "coalesced edit takes priority");
                    AssertDirty(window, doc, true, "coalesced FX edit");
                }
                Clean(window, doc);
                Check((bool)Call(fx, "Apply"), "explicit Apply succeeds"); Drain(fx);
                AssertDirty(window, doc, true, "explicit Apply");

                if (preset != "inline")
                {
                    Clean(window, doc);
                    Call(fx, "ReloadCatalogSource", true); Drain(fx);
                    AssertDirty(window, doc, false, "same catalog force-recompile");
                    Check(changes == 0 && refreshes == 1, "same catalog emits render only");
                    // Simulate an older saved dependency revision without modifying a real preset.
                    Set(fx, "catalogDependencyHash", "old-saved-revision");
                    Call(fx, "ReloadCatalogSource", false); Drain(fx);
                    AssertDirty(window, doc, true, "catalog revision changed");
                }
            }
            // A changed catalog revision during OPEN (notification queued before SetCompositor).
            var stale = Source("Levels.hlsl");
            Set(stale.layers[0].modifiers[0], "catalogDependencyHash", "old-saved-revision");
            var updated = RoundTrip(stale);
            Invoke(typeof(WhimTexDocumentFile), null, "CompileEmbeddedEffects", updated);
            var updatedWindow = Window(updated);
            Drain((ShaderFX)updated.layers[0].modifiers[0]);
            AssertDirty(updatedWindow, updated, true, "changed catalog on open");

            // Missing source detaches to saved fallback: that IS a document change.
            Set(stale.layers[0].modifiers[0], "catalogGuid", "missing-dirty-smoke-guid");
            var fallback = RoundTrip(stale);
            Invoke(typeof(WhimTexDocumentFile), null, "CompileEmbeddedEffects", fallback);
            var fallbackWindow = Window(fallback);
            Drain((ShaderFX)fallback.layers[0].modifiers[0]);
            AssertDirty(fallbackWindow, fallback, true, "missing catalog fallback");
            Check((Shader)Get(fallback.layers[0].modifiers[0], "compiledShader") != null, "fallback compiled");
            Check((string)Get(fallback.layers[0].modifiers[0], "catalogGuid") == null, "fallback detached");

            foreach (var item in originals)
                Check(item.window != null && ReferenceEquals(Get(item.window, "compositor"), item.document) &&
                    item.window.hasUnsavedChanges == item.dirty, "user window untouched");
            return;
        }
        finally
        {
            changedEvent.GetRemoveMethod(true).Invoke(null, new object[] { onChanged });
            refreshedEvent.GetRemoveMethod(true).Invoke(null, new object[] { onRefreshed });
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (owned[i] == null) continue;
                if (owned[i] is TextureCompositorWindow window)
                {
                    Set(window, "temporaryDocumentDirty", false);
                    var doc = (TextureCompositor)Get(window, "compositor");
                    if (doc != null && Get(doc, "documentBinding") is object binding) Set(binding, "dirty", false);
                    Call(window, "UpdateUnsavedChangesState");
                }
                Object.DestroyImmediate(owned[i]);
            }
            owned.Clear(); observed = null;
        }
    }
}

