using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// In-memory models and TIFF bytes only; no files, imports, windows or global Undo.
public static class DocumentCommentsTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static readonly Type Carrier = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier", true);
    static object Call(object target, string name, params object[] args)
    {
        Type type = target as Type ?? target.GetType();
        MethodInfo method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null ||
                (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).IsInstanceOfType(args[i])).All(match => match));
        try { return method.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static T Own<T>(List<Object> owned, T value) where T : Object
    { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
    static WhimTexDocument Document(List<Object> owned)
    {
        var document = Own(owned, ScriptableObject.CreateInstance<WhimTexDocument>());
        document.width = 8; document.height = 6; return document;
    }
    static WhimTexComment Thread(string id = "thread-a") => new WhimTexComment
    {
        id = id, anchor = new Vector2(.25f, .75f), color = new Color(.9f, .3f, .1f), resolved = true,
        messages = new List<WhimTexCommentMessage>
        {
            new WhimTexCommentMessage { id = id + "-first", author = "Me", text = "Обсуждение / 评论\nSecond line", createdUtcTicks = 0 },
            new WhimTexCommentMessage { id = id + "-reply", author = "Reviewer", text = "Reply", createdUtcTicks = DateTime.MaxValue.Ticks }
        }
    };
    static byte[] Encode(object value, WhimTexDocumentContainer container) => (byte[])Call(Serializer, "Serialize", value, container);
    static WhimTexDocument Decode(byte[] model, WhimTexDocumentContainer container, List<Object> owned, TestContext t)
    {
        object result = Call(Serializer, "Deserialize", model, container, typeof(WhimTexDocument), null, false);
        var document = Own(owned, (WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result));
        foreach (string name in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
            t.Equal(0, ((IReadOnlyList<string>)result.GetType().GetProperty(name, Flags).GetValue(result)).Count, "Roundtrip has no " + name);
        return document;
    }
    static void Reject(TestContext t, Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (WhimTexDocumentException) { rejected = true; }
        t.True(rejected, label);
    }
    static void SameComments(TestContext t, WhimTexDocument expected, WhimTexDocument actual, string label)
    {
        t.Equal(expected.comments.Count, actual.comments.Count, label + " thread count");
        for (int i = 0; i < expected.comments.Count; i++)
        {
            var a = expected.comments[i]; var b = actual.comments[i];
            t.Equal(a.id, b.id, label + " stable thread ID"); t.Equal(a.anchor, b.anchor, label + " UV anchor");
            t.Equal(a.number, b.number, label + " permanent comment number");
            t.Equal(a.color, b.color, label + " marker color");
            t.Equal(a.useAuthorColor, b.useAuthorColor, label + " automatic color mode");
            t.Equal(a.colorAuthorId ?? string.Empty, b.colorAuthorId ?? string.Empty, label + " color author identity");
            t.Equal(a.resolved, b.resolved, label + " resolution state"); t.Equal(a.messages.Count, b.messages.Count, label + " reply count");
            t.Equal(a.Status, b.Status, label + " workflow status");
            for (int j = 0; j < a.messages.Count; j++)
            {
                var x = a.messages[j]; var y = b.messages[j];
                t.Equal(x.id, y.id, label + " message ID"); t.Equal(x.author, y.author, label + " author");
                t.Equal(x.text, y.text, label + " Unicode/multiline text"); t.Equal(x.createdUtcTicks, y.createdUtcTicks, label + " UTC ticks");
                t.Equal(JsonUtility.ToJson(x), JsonUtility.ToJson(y), label + " attachments and agent provenance");
            }
        }
    }
    static void FactoriesAndValidation(TestContext t, List<Object> owned)
    {
        var document = Document(owned); document.ValidateComments();
        document.comments = null; document.ValidateComments(); document.NormalizeComments();
        t.True(document.comments != null && document.comments.Count == 0, "Missing comments normalize to an empty collection");
        long before = DateTime.UtcNow.Ticks;
        var note = document.AddComment(new Vector2(-.25f, 1.5f), "First");
        var reply = document.AddCommentMessage(note.id, "Second", "Author");
        t.True(Guid.TryParseExact(note.id, "N", out _) && Guid.TryParseExact(reply.id, "N", out _) && note.id != reply.id, "Factories create distinct stable IDs");
        t.Equal("User", note.messages[0].author, "Default author"); t.Equal("Author", reply.author, "Reply author");
        t.True(note.useAuthorColor, "New threads without an explicit color are automatic");
        t.Equal(WhimTexComment.ColorFromAuthor("User"), note.color, "Default thread color hashes its root author, not the reply author");
        t.True(note.messages[0].createdUtcTicks >= before && reply.createdUtcTicks <= DateTime.UtcNow.Ticks, "Factory times use current UTC ticks");
        t.True(ReferenceEquals(note, document.FindComment(note.id)) && ReferenceEquals(reply, note.messages[1]), "Factory results belong to the model");
        document.SetCommentResolved(note.id, true); t.True(note.resolved, "Resolve thread");
        document.SetCommentResolved(note.id, false); t.True(!note.resolved, "Reopen thread");
        t.Equal(WhimTexCommentStatus.InProgress, note.Status, "New and reopened discussions are In Progress");
        foreach (WhimTexCommentStatus from in Enum.GetValues(typeof(WhimTexCommentStatus)))
            foreach (WhimTexCommentStatus to in Enum.GetValues(typeof(WhimTexCommentStatus)))
            {
                document.SetCommentStatus(note.id, from);
                t.Equal(from != to, document.SetCommentStatus(note.id, to), "Any status transition is available");
                t.Equal(to, note.Status, "Requested status is applied");
                using var statusContainer = new WhimTexDocumentContainer();
                SameComments(t, document, Decode(Encode(document, statusContainer), statusContainer, owned, t), "Status TIFF roundtrip");
                using var statusJson = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false);
                SameComments(t, document, statusJson.Document, "Status JSON roundtrip");
            }
        document.SetCommentStatus(note.id, WhimTexCommentStatus.AwaitingReview);
        document.AddCommentMessage(note.id, "Review response");
        t.Equal(WhimTexCommentStatus.AwaitingReview, note.Status, "Reply preserves the explicit status");
        Reject(t, () => document.SetCommentStatus(note.id, (WhimTexCommentStatus)99), "Unknown status is rejected");
        t.Equal(WhimTexCommentStatus.AwaitingReview, note.Status, "Invalid status leaves discussion unchanged");
        document.RemoveComment(note.id); t.True(document.FindComment(note.id) == null && document.comments.Count == 0, "Delete thread with all replies");
        document.AddComment(Vector2.zero, "Fallback", " "); t.Equal("User", document.comments[0].messages[0].author, "Blank factory author uses the local default");
        t.Equal(2, document.comments[0].number, "Deleting the final thread never reuses its number");
        Reject(t, () => document.AddComment(Vector2.zero, " "), "Blank factory text is rejected");
        t.Equal(1, document.comments.Count, "Rejected thread creation does not mutate the collection");
        Reject(t, () => document.AddCommentMessage(document.comments[0].id, "\n"), "Blank reply text is rejected");
        t.Equal(1, document.comments[0].messages.Count, "Rejected reply creation does not mutate the thread");

        var bad = new (string label, Action<WhimTexDocument> mutate)[]
        {
            ("Null thread", d => d.comments[0] = null),
            ("Conflicting statuses", d => { d.comments[0].resolved = true; d.comments[0].awaitingReview = true; }),
            ("Blank thread ID", d => d.comments[0].id = " "),
            ("Duplicate thread ID", d => d.comments.Add(Thread())),
            ("Nonfinite X anchor", d => d.comments[0].anchor = new Vector2(float.NaN, .5f)),
            ("Nonfinite Y anchor", d => d.comments[0].anchor = new Vector2(.5f, float.PositiveInfinity)),
            ("Nonfinite color", d => d.comments[0].color = new Color(float.NaN, 0, 0)),
            ("HDR color", d => d.comments[0].color = new Color(2, 0, 0)),
            ("Transparent color", d => d.comments[0].color = new Color(1, 0, 0, 0)),
            ("Invalid color author ID", d => d.comments[0].colorAuthorId = "invalid"),
            ("Invalid message author ID", d => d.comments[0].messages[0].authorId = "invalid"),
            ("HDR message author color", d => d.comments[0].messages[0].authorColor = new Color(2, 0, 0)),
            ("Nonfinite message author color", d => d.comments[0].messages[0].authorColor = new Color(float.NaN, 0, 0)),
            ("Transparent message author color", d => d.comments[0].messages[0].authorColor = new Color(1, 0, 0, .5f)),
            ("Invalid missing message author color", d => d.comments[0].messages[0].authorColor = new Color(1, 0, 0, 0)),
            ("Null message list", d => d.comments[0].messages = null),
            ("Empty thread", d => d.comments[0].messages.Clear()),
            ("Null message", d => d.comments[0].messages[0] = null),
            ("Blank message ID", d => d.comments[0].messages[0].id = ""),
            ("Message/thread ID collision", d => d.comments[0].messages[0].id = d.comments[0].id),
            ("Repeated message ID", d => d.comments[0].messages[1].id = d.comments[0].messages[0].id),
            ("Cross-thread message ID collision", d => { var other = Thread("thread-b"); other.messages[0].id = d.comments[0].messages[0].id; d.comments.Add(other); }),
            ("Blank raw author", d => d.comments[0].messages[0].author = " "),
            ("Blank raw text", d => d.comments[0].messages[0].text = "\n\t"),
            ("Negative timestamp", d => d.comments[0].messages[0].createdUtcTicks = -1),
            ("Negative comment number", d => d.comments[0].number = -1),
            ("Repeated comment number", d => { d.comments[0].number = 4; var other = Thread("other"); other.number = 4; d.comments.Add(other); }),
            ("Edited before creation", d => { d.comments[0].messages[0].createdUtcTicks = 10; d.comments[0].messages[0].editedUtcTicks = 9; }),
            ("Overflow timestamp", d => d.comments[0].messages[0].createdUtcTicks = DateTime.MaxValue.Ticks + 1),
            ("Oversized author", d => d.comments[0].messages[0].author = new string('a', 129)),
            ("Oversized text", d => d.comments[0].messages[0].text = new string('x', 16385)),
            ("Too many messages", d => d.comments[0].messages = Enumerable.Range(0, 129).Select(i => new WhimTexCommentMessage { id = "m" + i, author = "Me", text = "Text" }).ToList()),
            ("Too many threads", d => d.comments = Enumerable.Range(0, 4097).Select(i => Thread("t" + i)).ToList())
        };
        foreach (var test in bad)
        {
            document.comments = new List<WhimTexComment> { Thread() }; test.mutate(document);
            Reject(t, document.ValidateComments, test.label + " rejects invalid model data");
        }
        document.comments = new List<WhimTexComment> { Thread() };
        document.comments[0].messages[0].author = new string('a', 128);
        document.comments[0].messages[0].text = new string('x', 16384);
        document.comments[0].anchor = new Vector2(-2, 3); document.ValidateComments();
        t.True(true, "Boundary text/author lengths, UTC endpoints and off-canvas anchors are valid");
        document.comments[0].messages = Enumerable.Range(0, 128).Select(i => new WhimTexCommentMessage { id = "boundary-m" + i, author = "Me", text = "Text" }).ToList();
        document.ValidateComments(); t.True(true, "Exactly 128 messages are valid");
        document.comments = Enumerable.Range(0, 4096).Select(i => Thread("boundary-t" + i)).ToList();
        document.ValidateComments(); t.True(true, "Exactly 4096 threads are valid");
    }
    static void StorageAndComposite(TestContext t, List<Object> owned)
    {
        var document = Document(owned);
        document.layers.Add(new Layer(new ColorFillLayerBehaviour { color = new Color(.125f, .25f, .5f, .75f) }));
        Call(document, "NormalizeModel");
        var baseline = Own(owned, document.ComposeCanvas());
        using var empty = new WhimTexDocumentContainer();
        byte[] emptyModel = Encode(document, empty);
        empty.Set(WhimTexDocumentContainer.DocumentBlock, emptyModel);
        t.True(!empty.Contains("comments"), "A document without notes has no optional comments block");
        var emptyCopy = Decode(emptyModel, empty, owned, t);
        t.Equal(0, emptyCopy.comments.Count, "Absent optional block loads as empty");

        document.comments.Add(Thread()); document.comments.Add(Thread("thread-b"));
        document.comments[1].anchor = new Vector2(-.5f, 1.25f); document.comments[1].resolved = false;
        document.comments[1].useAuthorColor = true;
        document.comments[1].colorAuthorId = "01234567890123456789012345678901";
        document.comments[1].messages[1].authorId = "01234567890123456789012345678902";
        document.comments[1].messages[1].authorColor = new Color(.9f, .6f, .2f);
        document.comments[0].messages[0].editedUtcTicks = DateTime.UtcNow.Ticks;
        document.comments[0].messages[0].attachments.Add(WhimTexCommentAttachment.ForLayer(document.layers[0]));
        document.comments[0].messages[0].attachments.Add(new WhimTexCommentAttachment
        { assetGuid = "01234567890123456789012345678901", localId = 4200001, name = "Missing asset retained" });
        var maskBytes = new byte[48]; maskBytes[9] = 37; maskBytes[30] = 255;
        document.comments[0].messages[0].attachments.Add(WhimTexCommentAttachment.ForMask(WhimTexCommentMask.Capture(maskBytes, 8, 6)));
        using var container = new WhimTexDocumentContainer();
        byte[] model = Encode(document, container);
        container.Set(WhimTexDocumentContainer.DocumentBlock, model);
        t.True(container.Contains("comments"), "Nonempty notes have their own named block");
        t.Equal(2, document.commentNumberSequence, "The document retains a numbering high-water mark");
        var read = Decode(model, container, owned, t); SameComments(t, document, read, "Tagged model");
        t.True(!ReferenceEquals(document.comments, read.comments) && !ReferenceEquals(document.comments[0].messages, read.comments[0].messages), "Deserialized comments own their collections");
        var copy = Own(owned, (WhimTexDocument)Call(typeof(WhimTexDocumentFile), "CreateEditableCopy", document));
        SameComments(t, document, copy, "Editable copy");
        copy.comments[0].anchor = Vector2.one; copy.comments[0].messages[0].text = "Independent";
        t.Equal(new Vector2(.25f, .75f), document.comments[0].anchor, "Copy anchor is independent");
        t.Equal("Обсуждение / 评论\nSecond line", document.comments[0].messages[0].text, "Copy message object is independent");
        var withComments = Own(owned, document.ComposeCanvas());
        t.True(baseline.GetRawTextureData<byte>().SequenceEqual(withComments.GetRawTextureData<byte>()), "Comments do not alter any composite pixel");

        byte[] emptyTiff = (byte[])Call(Carrier, "Write", empty, baseline, (bool?)false);
        byte[] file = (byte[])Call(Carrier, "Write", container, withComments, (bool?)false);
        t.True(WhimTexTiffImage.TryReadPixels(emptyTiff, out int ew, out int eh, out byte[] ep, out string ee), "Plain carrier image decodes: " + ee);
        t.True(WhimTexTiffImage.TryReadPixels(file, out int w, out int h, out byte[] pixels, out string error), "Commented carrier image decodes: " + error);
        t.True(w == ew && h == eh && pixels.SequenceEqual(ep), "Comments leave the TIFF image pixels and dimensions unchanged");
        object[] carrierArgs = { file, null, null };
        t.True((bool)Call(Carrier, "TryRead", carrierArgs), "TIFF carrier payload extracts: " + carrierArgs[2]);
        using var parsed = WhimTexDocumentContainer.Parse((byte[])carrierArgs[1]);
        t.True(parsed.Contains("comments"), "Serialized TIFF retains the optional block");
        var loaded = Decode(parsed.Get(WhimTexDocumentContainer.DocumentBlock), parsed, owned, t);
        SameComments(t, document, loaded, "TIFF roundtrip");
        t.True(maskBytes.SequenceEqual(loaded.comments[0].messages[0].attachments[2].mask.Rasterize(8, 6)), "TIFF retains exact mask coverage, holes and placement");

        // Replacing raw bytes recomputes the container checksum: rejection must come from the known block reader.
        foreach (byte[] invalid in new[] { new byte[] { 2, 0, 0, 0, 255 }, Encode(new List<string> { "wrong root" }, container) })
        {
            parsed.Set("comments", invalid);
            Reject(t, () => Decode(model, parsed, owned, t), "Malformed or mistyped known comments block rejects rather than disappearing");
        }
        foreach (Action<WhimTexComment> corrupt in new Action<WhimTexComment>[]
        {
            note => note.anchor = new Vector2(float.NaN, 0),
            note => note.messages[1].id = note.messages[0].id,
            note => note.messages[0].author = " "
        })
        {
            var invalid = Thread(); corrupt(invalid);
            parsed.Set("comments", Encode(new List<WhimTexComment> { invalid }, container));
            Reject(t, () => Decode(model, parsed, owned, t), "Structurally readable invalid comment data is rejected during loading");
        }
        byte[] unknown = (byte[])container.Get("comments").Clone();
        byte[] name = Encoding.UTF8.GetBytes("resolved"), replacement = Encoding.UTF8.GetBytes("unmapped");
        int field = Enumerable.Range(0, unknown.Length - name.Length + 1).First(i => unknown.Skip(i).Take(name.Length).SequenceEqual(name));
        Array.Copy(replacement, 0, unknown, field, replacement.Length); parsed.Set("comments", unknown);
        Reject(t, () => Decode(model, parsed, owned, t), "Unknown data in a known comments block cannot be silently dropped");

        // This is the public Unity serialization substrate used by owned document Undo, without touching the global stack.
        string snapshot = EditorJsonUtility.ToJson(document);
        document.comments.Clear(); EditorJsonUtility.FromJsonOverwrite(snapshot, document);
        SameComments(t, loaded, document, "Unity serialization snapshot");

        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            var options = new WhimTexJsonWriteOptions { Mode = mode };
            string json = WhimTexDocumentJson.Write(document, options).Json;
            using var fromJson = WhimTexDocumentJson.Read(json, false);
            SameComments(t, document, fromJson.Document, "JSON " + mode);
            using var fragment = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, document.layers, options).Json, false);
            t.Equal(0, fragment.Document.comments.Count, "Layer fragments omit document comments in " + mode);
            t.Equal(0, fragment.Document.commentNumberSequence, "Layer fragments omit comment numbering history in " + mode);
        }
        using (var oldColors = WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":3,\"layers\":[],\"document\":{\"comments\":[{\"id\":\"old-note\",\"color\":[0.1882353,0.3764706,0.5215687,1],\"messages\":[{\"id\":\"old-message\",\"text\":\"Old blue note\"}]}]}}", false))
        {
            t.True(!oldColors.Document.comments[0].useAuthorColor, "Comments without color mode retain manual colors");
            t.True(string.IsNullOrEmpty(oldColors.Document.comments[0].colorAuthorId), "Old comments do not acquire an owner implicitly");
        }
        document.comments.Clear();
        t.True(!WhimTexDocumentJson.Write(document).Json.Contains("\"comments\""), "Empty JSON omits comments");
        Encode(document, container);
        t.True(!container.Contains("comments"), "Removing the final thread removes the optional TIFF block");
        var withoutThreads = Decode(Encode(document, container), container, owned, t);
        t.Equal(3, withoutThreads.AddComment(Vector2.zero, "After reopen").number, "TIFF without threads retains the next unused number");
        t.True(!withoutThreads.RemoveCommentReply(withoutThreads.comments[0].id, withoutThreads.comments[0].messages[0].id), "Reply deletion cannot remove the root message");
    }
    static void Geometry(TestContext t, List<Object> owned)
    {
        var document = Document(owned); document.comments.Add(Thread());
        var frame = new RectInt(-2, 1, 12, 4);
        t.True(document.ApplyCanvasFrame(frame), "Crop/extend frame applies to notes");
        t.Near(4d / 12, document.comments[0].anchor.x, 2e-6, "Crop retains note pixel X");
        t.Near(3.5d / 4, document.comments[0].anchor.y, 2e-6, "Crop retains note pixel Y");
        Vector2 frameAnchor = document.comments[0].anchor;
        t.True(document.ApplyCanvasScale(18, 10), "Scale canvas applies");
        t.Equal(frameAnchor, document.comments[0].anchor, "Scale preserves normalized note position");
        bool invalidFrame = false;
        try { document.ApplyCanvasFrame(new RectInt(0, 0, 0, 4)); } catch (ArgumentOutOfRangeException) { invalidFrame = true; }
        t.True(invalidFrame, "Invalid canvas frame is rejected");
        t.Equal(frameAnchor, document.comments[0].anchor, "Rejected frame leaves notes unchanged");
        t.True(document.comments[0].resolved && document.comments[0].messages[1].text == "Reply", "Frame and scale retain thread state and replies");
        var expected = new[] { new Vector2(.8f, .7f), new Vector2(.2f, .3f), new Vector2(.3f, .2f), new Vector2(.7f, .8f) };
        var orientations = new[] { CanvasOrientation.FlipHorizontal, CanvasOrientation.FlipVertical, CanvasOrientation.RotateLeft, CanvasOrientation.RotateRight };
        for (int i = 0; i < orientations.Length; i++)
        {
            var oriented = Document(owned); oriented.comments.Add(Thread()); oriented.comments[0].anchor = new Vector2(.2f, .7f);
            t.True(oriented.ApplyCanvasOrientation(orientations[i]), "Apply " + orientations[i]);
            t.Near(expected[i].x, oriented.comments[0].anchor.x, 2e-6, orientations[i] + " note X");
            t.Near(expected[i].y, oriented.comments[0].anchor.y, 2e-6, orientations[i] + " note Y");
            t.Equal("thread-a", oriented.comments[0].id, "Orientation retains thread identity");
            t.Equal(2, oriented.comments[0].messages.Count, "Orientation retains replies");
        }
    }
    static void Receipts(TestContext t, List<Object> owned)
    {
        var document = Document(owned);
        document.agentReceipts.Add(new WhimTexAgentReceipt { workId = "work", requestId = "step", requestHash = "hash", resultJson = "{\"layerId\":\"id\"}", createdUtcTicks = DateTime.UtcNow.Ticks });
        using var container = new WhimTexDocumentContainer();
        var copy = Decode(Encode(document, container), container, owned, t);
        t.Equal("step", copy.agentReceipts[0].requestId, "Receipts persist without comments");
        t.True(!container.Contains("comments"), "Receipts do not create an empty comments block");
        using var json = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false);
        t.Equal(document.agentReceipts[0].resultJson, json.Document.agentReceipts[0].resultJson, "JSON retains the original operation receipt");
        document.layers.Add(new Layer(new ColorFillLayerBehaviour())); Call(document, "NormalizeModel");
        using var fragment = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, document.layers).Json, false);
        t.Equal(0, fragment.Document.agentReceipts.Count, "Layer fragments omit document receipt history");
        document.agentReceipts.Add(document.agentReceipts[0]);
        Reject(t, document.ValidateComments, "Duplicate work/request receipts are rejected");
        document.agentReceipts.RemoveAt(1); document.agentReceipts[0].resultJson = "broken";
        Reject(t, document.ValidateComments, "Malformed stored results are rejected before work recovery");
    }
    static void Masks(TestContext t, List<Object> owned)
    {
        var source = new byte[48]; source[9] = 1; source[10] = 128; source[20] = 255; source[37] = 87;
        var snapshot = WhimTexCommentMask.Capture(source, 8, 6);
        t.Equal(5, snapshot.width, "Mask crops to nonzero X bounds");
        t.Equal(4, snapshot.height, "Mask crops to nonzero Y bounds");
        t.True(source.SequenceEqual(snapshot.Rasterize(8, 6)), "All 8-bit coverage values and empty islands roundtrip");
        source[10] = 3;
        t.Equal((byte)128, snapshot.Rasterize(8, 6)[10], "Snapshot does not retain the mutable working selection");
        source[10] = 128;
        foreach (var orientation in new[] { CanvasOrientation.FlipHorizontal, CanvasOrientation.FlipVertical, CanvasOrientation.RotateLeft, CanvasOrientation.RotateRight })
        {
            var doc = Document(owned);
            doc.AddComment(Vector2.zero, "Mask", attachments: new List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForMask(snapshot.Copy()) });
            var mask = doc.comments[0].messages[0].attachments[0].mask;
            doc.ApplyCanvasOrientation(orientation);
            var actual = mask.Rasterize(doc.width, doc.height);
            for (int y = 0; y < 6; y++) for (int x = 0; x < 8; x++)
            {
                int px = x, py = y;
                if (orientation == CanvasOrientation.FlipHorizontal) px = 7 - x;
                if (orientation == CanvasOrientation.FlipVertical) py = 5 - y;
                if (orientation == CanvasOrientation.RotateLeft) { px = 5 - y; py = x; }
                if (orientation == CanvasOrientation.RotateRight) { px = y; py = 7 - x; }
                t.Equal(source[y * 8 + x], actual[py * doc.width + px], orientation + " exact coverage");
            }
            t.Equal(snapshot.coverageDeflate, mask.coverageDeflate, "Orientation preserves original compressed pixels");
        }
        var canvas = Document(owned);
        canvas.AddComment(Vector2.zero, "Mask", attachments: new List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForMask(snapshot.Copy()) });
        var retained = canvas.comments[0].messages[0].attachments[0].mask;
        canvas.ApplyCanvasFrame(new RectInt(2, 2, 3, 2));
        byte[] cropped = retained.Rasterize(3, 2);
        for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) t.Equal(source[(y + 2) * 8 + x + 2], cropped[y * 3 + x], "Crop placement");
        canvas.ApplyCanvasFrame(new RectInt(-2, -2, 8, 6));
        t.True(source.SequenceEqual(retained.Rasterize(8, 6)), "Expand restores retained off-canvas mask pixels");
        canvas.ApplyCanvasScale(24, 12); byte[] scaled = retained.Rasterize(24, 12);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 24; x++) t.Equal(source[(y / 2) * 8 + x / 3], scaled[y * 24 + x], "Nonuniform Scale samples original coverage");
        canvas.ApplyCanvasScale(8, 6);
        t.True(source.SequenceEqual(retained.Rasterize(8, 6)), "Repeated scaling does not degrade the snapshot");
        Reject(t, () => WhimTexCommentMask.Capture(new byte[48], 8, 6), "Empty selections cannot be attached");
        foreach (Action<WhimTexCommentMask> corrupt in new Action<WhimTexCommentMask>[] {
            m => m.coverageDeflate = "!", m => m.sha256 = new string('a', 44), m => m.width++,
            m => m.height = 16385, m => m.version = 2, m => m.localToCanvas = default,
            m => m.localToCanvas.m20 = .1, m => m.localToCanvas.m02 = double.NaN })
        {
            var bad = snapshot.Copy(); corrupt(bad);
            Reject(t, () => bad.Rasterize(8, 6), "Invalid mask data or placement is rejected");
        }
        var attachments = canvas.comments[0].messages[0].attachments;
        attachments.Add(WhimTexCommentAttachment.ForMask(retained.Copy()));
        Reject(t, canvas.ValidateComments, "Duplicate mask IDs are rejected"); attachments.RemoveAt(1);
        attachments[0].layerId = "invalid-mixed";
        Reject(t, canvas.ValidateComments, "Mask and layer cannot share one attachment"); attachments[0].layerId = null;
        var large = WhimTexCommentMask.Capture(Enumerable.Repeat((byte)255, 4096 * 4096).ToArray(), 4096, 4096);
        for (int i = 0; i < 4; i++) attachments.Add(WhimTexCommentAttachment.ForMask(large.Copy(true)));
        Reject(t, canvas.ValidateComments, "Aggregate decoded mask budget is enforced even with tiny compressed data");
        var full = Document(owned);
        full.AddComment(Vector2.zero, "Full mask", attachments: new List<WhimTexCommentAttachment>
        { WhimTexCommentAttachment.ForMask(WhimTexCommentMask.Capture(Enumerable.Repeat((byte)255, 48).ToArray(), 8, 6)) });
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            using var decoded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(full, new WhimTexJsonWriteOptions { Mode = mode }).Json, false);
            t.True(decoded.Document.comments[0].messages[0].attachments[0].mask.Rasterize(8, 6).All(c => c == 255), "Full-canvas mask retains identity placement in " + mode);
        }
    }
    public static string Run() => TestContext.Run("Local document comments: validation, optional TIFF storage, copies and canvas geometry", t =>
    {
        var owned = new List<Object>(); var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try { FactoriesAndValidation(t, owned); StorageAndComposite(t, owned); Geometry(t, owned); Receipts(t, owned); Masks(t, owned); }
        finally
        {
            var errors = new List<Exception>();
            for (int i = owned.Count - 1; i >= 0; i--)
                try { if (owned[i] != null) Object.DestroyImmediate(owned[i]); } catch (Exception error) { errors.Add(error); }
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            if (errors.Count > 0) throw new AggregateException("Owned comments test cleanup failed", errors);
        }
    });
}
