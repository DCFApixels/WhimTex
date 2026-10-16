using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = WhimTex.Tests.ApiJson;

public static class AgentReloadTests
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static string Key(string id) => "WhimTex.Tests.AgentReload." + id;
    static object Read(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target) ?? target.GetType().GetProperty(name, Flags).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(Flags)
        .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
    static object Editor(WhimTexWindow window) => Read(window, "activeCommentEditor");
    static object Draft(WhimTexWindow window) => Read(Editor(window), "state");
    static TextField Input(WhimTexWindow window) => ((VisualElement)Read(Editor(window), "card")).Q<TextField>("commentText");
    static Json Request(string op, params object[] fields)
    { var r = new Json { ["apiVersion"] = 1, ["op"] = op }; for (int i = 0; i < fields.Length; i += 2) r[(string)fields[i]] = Json.FromObject(fields[i + 1]); return r; }
    static Json Live(Json request)
    { var r = Json.Parse(WhimTexApi.LiveJson(request.ToString())); if (!(bool)r["success"]) throw new InvalidOperationException(r.ToString()); return r; }

    public static string Begin(string id) => TestContext.Run("Prepare live operation for actual script reload", t =>
    {
        var jobs = (IDictionary)typeof(WhimTexApi).GetField("liveJobs", Flags).GetValue(null);
        t.True(jobs.Values.Cast<object>().All(j => (string)Read(j, "state") != "pending"), "No borrowed pending jobs may be interrupted by this test");
        t.Equal(0, ((IDictionary)typeof(WhimTexApi).GetField("recoverableLiveJobs", Flags).GetValue(null)).Count, "No borrowed interrupted operations");
        UnityBReload.Begin("Agent", id, t);
        var window = ScriptableObject.CreateInstance<WhimTexWindow>(); window.name = Key(id) + ".window";
        var doc = (WhimTexDocument)Read(window, "AgentDocument"); doc.name = Key(id) + ".document";
        doc.width = 48; doc.height = 32; doc.layers.Clear();
        var pixels = new Texture2D(48, 32, TextureFormat.RGBA32, false, true) { name = Key(id) + ".pixels" };
        pixels.SetPixels(Enumerable.Repeat(new Color(.2f, .1f, .4f, 1), 48 * 32).ToArray()); pixels.Apply();
        var drawing = new DrawingLayerBehaviour(); var layer = new Layer(drawing);
        typeof(Layer).GetMethod("AssignNewId", Flags).Invoke(layer, null);
        typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", Flags).Invoke(drawing, new object[] { pixels }); doc.layers.Add(layer);
        byte[] coverage = new byte[48 * 32]; for (int y = 8; y < 24; y++) for (int x = 12; x < 30; x++) coverage[y * 48 + x] = 255;
        doc.AddComment(Vector2.one * .5f, "Owned reload feedback", attachments: new List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForMask(WhimTexCommentMask.Capture(coverage, 48, 32)) });
        window.ShowUtility(); window.CreateGUI();
        var toolField = typeof(WhimTexWindow).GetField("canvasTool", Flags);
        toolField.SetValue(window, Enum.Parse(toolField.FieldType, "Comment"));
        Call(window, "RefreshCanvasComments");
        Call(window, "OpenCanvasComment", doc.comments[0].id, doc.comments[0].anchor);
        Call(window, "EditCanvasCommentMessage", Editor(window), doc.comments[0].messages[0].id);
        Input(window).value = "Unposted edit with mask across reload";
        ((List<WhimTexCommentAttachment>)Read(Draft(window), "attachments")).Add(null);
        var state = Draft(window);
        state.GetType().GetField("positioned").SetValue(state, true);
        state.GetType().GetField("sized").SetValue(state, true);
        state.GetType().GetField("rect").SetValue(state, new Rect(40, 90, 360, 420));
        var second = doc.AddComment(new Vector2(.8f, .2f), "Second reload feedback");
        Call(window, "OpenCanvasComment", second.id, second.anchor);
        Input(window).value = "Independent reply across reload";
        var secondState = Draft(window);
        secondState.GetType().GetField("positioned").SetValue(secondState, true);
        secondState.GetType().GetField("sized").SetValue(secondState, true);
        secondState.GetType().GetField("rect").SetValue(secondState, new Rect(420, 120, 300, 360));
        // Do not call the capture method: the actual OnDisable/reload lifecycle must retain this draft.
        string session = (string)Read(window, "AgentSessionId");
        var context = Json.Parse(WhimTexApi.CommentsJson(Request("context", "assistantSessionId", session, "maxSize", 64).ToString()));
        t.True((bool)context["success"], context.ToString());
        var job = Live(Request("begin", "sessionId", session, "requestId", id, "source", "merged", "area", "commentMask",
            "maskRef", context["masks"][0]["maskRef"], "commentChecks", context["commentChecks"], "selectionMode", "guide", "padding", 0,
            "applicationMask", new Json { ["fromGuide"] = true, ["expandPixels"] = 1, ["featherPixels"] = 2 }));
        var edit = Live(Request("lock", "sessionId", session, "requestId", id + "-edit", "layerId", layer.Id));
        SessionState.SetString(Key(id) + ".job", job.ToString());
        SessionState.SetString(Key(id) + ".edit", edit.ToString());
        SessionState.SetString(Key(id) + ".mask", Convert.ToBase64String(File.ReadAllBytes((string)job["capture"]["applicationMaskPath"])));
        UnityBReload.Arm("Agent", id);
    });
    public static string Trigger(string id) => UnityBReload.Trigger("Agent", id);
    public static string ReloadPoll(string id) => UnityBReload.ReloadPoll("Agent", id);
    public static string Verify(string id) => TestContext.Run("Resume original generation after native domain reload", t =>
    {
        UnityBReload.Verify("Agent", id, t);
        var window = Resources.FindObjectsOfTypeAll<WhimTexWindow>().Single(w => w.name == Key(id) + ".window");
        var doc = (WhimTexDocument)Read(window, "AgentDocument");
        t.Equal(Key(id) + ".document", doc.name, "The original owned document survives in its own window");
        Call(window, "RefreshCanvasComments");
        t.Equal(2, window.rootVisualElement.Query<VisualElement>("commentEditor").ToList().Count, "Native reload restores both open discussion cards");
        t.Equal(2, window.rootVisualElement.Query<VisualElement>("commentConnector").ToList().Count, "Every restored card has its own connector");
        t.Equal("Independent reply across reload", Input(window).value, "The second draft and focused card survive reload");
        t.Equal(new Rect(420, 120, 300, 360), (Rect)Read(Draft(window), "rect"), "The second layout survives independently");
        Call(window, "OpenCanvasComment", doc.comments[0].id, doc.comments[0].anchor);
        t.Equal("Unposted edit with mask across reload", Input(window)?.value,
            "Native reload restores the in-progress message edit automatically");
        t.Equal(doc.comments[0].messages[0].id, (string)Read(Draft(window), "messageId"), "Reload retains which message is being edited");
        var draft = (List<WhimTexCommentAttachment>)Read(Draft(window), "attachments");
        t.Equal(2, draft.Count, "Reload retains an attached mask and an empty object slot");
        t.True(draft[0].mask.Rasterize(48, 32).SequenceEqual(doc.comments[0].messages[0].attachments[0].mask.Rasterize(48, 32)),
            "Reload preserves the exact unposted mask");
        t.True(draft[1] == null, "An empty draft slot remains empty after reload");
        var state = Draft(window);
        t.Equal(new Rect(40, 90, 360, 420), (Rect)state.GetType().GetField("rect").GetValue(state), "Reload retains the card's chosen layout");
        t.Equal("Owned reload feedback", doc.comments[0].messages[0].text, "The unposted edit never changes the stored discussion");
        string session = (string)Read(window, "AgentSessionId");
        var old = Json.Parse(SessionState.GetString(Key(id) + ".job", "")); string jobId = (string)old["jobId"];
        var edit = Json.Parse(SessionState.GetString(Key(id) + ".edit", ""));
        t.Equal("interrupted", (string)Live(Request("status", "jobId", jobId))["state"], "Journal survives actual reload");
        var sessions = Json.Parse(WhimTexApi.LiveSessions());
        t.Equal(2, sessions["sessions"].Single(s => (string)s["sessionId"] == session)["recoverableJobs"].Count, "Both operations are discoverable in the new session");
        var resumed = Live(Request("resume", "sessionId", session, "jobId", jobId));
        t.True((bool)resumed["resumed"], "Original source fingerprint survives reload");
        t.Equal((string)old["layerId"], (string)resumed["layerId"], "Resume uses the same reservation");
        t.Equal(2, doc.layers.Count, "No duplicate layer created");
        t.Equal(old["capture"].ToString(), resumed["capture"].ToString(), "Frozen capture is retained, not recaptured");
        t.Equal(SessionState.GetString(Key(id) + ".mask", ""), Convert.ToBase64String(File.ReadAllBytes((string)resumed["capture"]["applicationMaskPath"])), "Application PNG is unchanged");
        Live(Request("cancel", "sessionId", session, "layerId", (string)edit["layerId"]));
        var cancelled = Json.Parse(WhimTexApi.LiveJson(Request("resume", "sessionId", session, "jobId", (string)edit["jobId"]).ToString()));
        t.Equal("recovery_unavailable", (string)cancelled["errorCode"], "Interrupted edit cancellation cannot revive a lock");
        var green = new Texture2D(48, 32, TextureFormat.RGBA32, false, false);
        string path = Path.GetFullPath("Temp/WhimTex/agent-reload-" + id + ".png");
        try { green.SetPixels(Enumerable.Repeat(Color.green, 48 * 32).ToArray()); green.Apply(); File.WriteAllBytes(path, green.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(green); }
        var preview = Live(Request("preview", "jobId", jobId, "imagePath", path, "review", true, "maxSize", 64));
        t.Equal(0, (int)preview["review"]["outsideApplicationPixels"], "Recovered mask still clips candidate outside its area");
        Live(Request("complete", "jobId", jobId, "imagePath", path));
        t.Equal("completed", (string)Live(Request("status", "jobId", jobId))["state"], "Recovered operation completes normally");
        Call(window, "PostCanvasComment", Editor(window));
        t.Equal("Unposted edit with mask across reload", doc.comments[0].messages[0].text,
            "A restored edit can be saved without a false stale-message conflict");
        t.Equal(1, doc.comments[0].messages.Count, "Saving the restored edit never creates a duplicate reply");
        t.True(doc.comments[0].messages[0].editedUtcTicks > 0, "Saving after reload records an edit timestamp");
    });
    public static string Cleanup(string id) => UnityBReload.Cleanup("Agent", id, () =>
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>().Where(w => w.name == Key(id) + ".window"))
        { Undo.ClearUndo(window); window.DiscardChanges(); window.Close(); UnityBReload.DestroyOwned(window); }
        foreach (var doc in Resources.FindObjectsOfTypeAll<WhimTexDocument>().Where(d => d.name == Key(id) + ".document")) UnityBReload.DestroyOwned(doc);
        foreach (var pixels in Resources.FindObjectsOfTypeAll<Texture2D>().Where(p => p.name == Key(id) + ".pixels")) UnityBReload.DestroyOwned(pixels);
        foreach (string suffix in new[] { ".job", ".edit", ".mask" }) SessionState.EraseString(Key(id) + suffix);
    });
}
