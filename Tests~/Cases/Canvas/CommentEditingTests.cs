using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class CommentEditingTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type State = typeof(WhimTexWindow).GetNestedType("CommentEditorState", Flags);
    static readonly Type Content = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexCommentContent", true);
    static readonly Type Query = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexCommentQuery", true);
    static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }
    static object Read(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target) ?? target.GetType().GetProperty(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static WhimTexCommentMessage Copy(WhimTexCommentMessage message) => JsonUtility.FromJson<WhimTexCommentMessage>(JsonUtility.ToJson(message));
    static bool Same(WhimTexCommentMessage current, WhimTexCommentMessage original) =>
        (bool)Call(Content, "Equals", current.text, current.attachments, original, false);
    static bool Dirty(object state) => (bool)Call(typeof(WhimTexWindow), "CommentDraftHasChanges", state);
    static void Reject(TestContext t, Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (WhimTexDocumentException) { rejected = true; }
        t.True(rejected, label);
    }

    static void Drafts(TestContext t, WhimTexDocument document)
    {
        var mask = WhimTexCommentMask.Capture(new byte[] { 255, 13, 0, 255 }, 2, 2);
        var thread = document.AddComment(Vector2.one * .5f, "Initial text", "Author", attachments: new List<WhimTexCommentAttachment>
        {
            WhimTexCommentAttachment.ForMask(mask),
            new WhimTexCommentAttachment { assetGuid = "01234567890123456789012345678901", localId = 17, name = "Reference" }
        });
        var message = thread.messages[0];
        var state = Activator.CreateInstance(State, true);
        Set(state, "id", thread.id); Set(state, "document", document);
        Call(state, "BeginEdit", message);
        t.True((bool)Read(state, "IsEditing") && !Dirty(state), "An unchanged edit is active but can be cancelled without blocking");
        var original = (WhimTexCommentMessage)Read(state, "OriginalMessage");
        t.True(!ReferenceEquals(message.attachments, Read(state, "attachments")), "Draft attachments are independently owned");
        t.True(ReferenceEquals(original, Read(state, "OriginalMessage")), "Repeated comparisons reuse the parsed reload snapshot");
        Set(state, "text", "  Initial text\n");
        t.True(!Dirty(state), "Whitespace discarded on save does not make an unchanged edit dirty");
        Set(state, "text", "Changed text"); t.True(Dirty(state), "Text changes retain the draft");

        message.authorColor = Color.red; message.authorId = "01234567890123456789012345678902";
        t.True(Same(message, original), "Updating the author color and profile does not create a content conflict");
        var equivalent = Copy(message);
        t.True(Same(equivalent, original), "Undo/reload object replacement with equal content does not create a conflict");
        foreach (var mutate in new Action<WhimTexCommentMessage>[]
        {
            m => m.text += "!", m => m.attachments.Reverse(), m => m.attachments.RemoveAt(0),
            m => m.attachments[1].localId++, m => m.attachments[1].name += "!", m => m.attachments[1].assetGuid = "other",
            m => m.attachments[0].mask.localToCanvas.m02 += .1,
            m => m.attachments[0].mask.coverageDeflate += "!", m => m.attachments[0].mask.sha256 += "!",
            m => m.attachments[0].mask.width++, m => m.attachments[0].mask.height++, m => m.attachments[0].mask.version++,
            m => m.attachments[0].mask.id = Guid.NewGuid().ToString("N")
        })
        {
            var changed = Copy(message); mutate(changed);
            t.True(!Same(changed, original), "Changed message text, reference or mask conflicts with the original edit");
        }
        var restored = JsonUtility.FromJson(JsonUtility.ToJson(state), State);
        t.True(Dirty(restored) && (bool)Read(restored, "IsEditing"), "Reload retains the edited draft and mode");
        t.True(Same(message, (WhimTexCommentMessage)Read(restored, "OriginalMessage")), "Reload restores the original content guard independently of author cosmetics");
        Set(restored, "text", message.text);
        ((List<WhimTexCommentAttachment>)Read(restored, "attachments")).Add(null);
        t.True(Dirty(restored), "An added empty attachment field keeps the draft configured");
        Call(restored, "ClearDraft");
        t.True(!(bool)Read(restored, "IsEditing") && !(bool)Read(restored, "HasDraft") && !Dirty(restored), "Cancel clears only the local draft");
        Call(restored, "BeginReply"); long created = (long)Read(restored, "draftCreatedUtcTicks");
        Call(restored, "BeginReply");
        t.Equal(created, (long)Read(restored, "draftCreatedUtcTicks"), "Refocusing a new reply keeps its draft date");
        t.True((bool)Read(restored, "IsEditing") && !Dirty(restored), "An untouched new reply can be discarded");
        Call(restored, "ClearDraft");
        t.Equal("Initial text", message.text, "Cancelling drafts never edits the published message");

        message.agentWorkId = "work"; message.agentContentHash = "old-provenance";
        string before = JsonUtility.ToJson(thread);
        Reject(t, () => Call(document, "EditCommentMessage", thread.id, message.id, " ", message.attachments), "Invalid text is rejected");
        t.Equal(before, JsonUtility.ToJson(thread), "A rejected edit rolls back text, attachments, timestamps and provenance");
        var attachments = Copy(message).attachments;
        long creation = message.createdUtcTicks;
        Call(document, "EditCommentMessage", thread.id, message.id, "Saved text", attachments);
        t.Equal("Saved text", message.text, "Model owns the committed edit");
        t.Equal("Author", message.author, "Edit preserves authorship");
        t.Equal(creation, message.createdUtcTicks, "Edit preserves creation time");
        t.True(message.editedUtcTicks >= creation && message.agentContentHash == null, "Edit records its time and invalidates the untouched-agent marker");
        attachments[0].mask.localToCanvas.m02 += 1;
        t.True(!message.attachments[0].mask.localToCanvas.Equals(attachments[0].mask.localToCanvas), "A committed mask is independent of the editable draft");
        Reject(t, () => Call(document, "EditCommentMessage", thread.id, "missing", "Text", attachments), "Deleted messages cannot be silently recreated by Save");
    }

    static void Queries(TestContext t, WhimTexDocument document)
    {
        var first = document.comments[0];
        var second = document.AddComment(Vector2.zero, "Another note", "Beta");
        var query = Activator.CreateInstance(Query, true);
        const string prefix = "whimtex://comment/session/01234567890123456789012345678901/";
        Set(query, "linkPrefix", prefix); Set(query, "status", "All");
        bool Matches(WhimTexComment item) => (bool)Call(query, "Matches", item);
        string before = JsonUtility.ToJson(document);
        foreach (string term in new[] { "  SAVED  ", "thor", "#" + first.number, " \n" + prefix + first.id + "\t" })
        { Set(query, "search", term); t.True(Matches(first), "Substring text, author, number and trimmed exact links match"); }
        foreach (string term in new[] { prefix + first.id + "x", (prefix + first.id).ToUpperInvariant(), prefix + first.id + "/", first.id })
        { Set(query, "search", term); t.True(!Matches(first), "Partial or modified links do not match a discussion ID"); }
        Set(query, "search", null); Set(query, "author", "BETA");
        t.True(!Matches(first) && Matches(second), "Author filter remains case insensitive");
        Set(query, "author", null);
        document.SetCommentStatus(second.id, WhimTexCommentStatus.AwaitingReview);
        Set(query, "status", "AwaitingReview"); t.True(Matches(second) && !Matches(first), "Explicit status filtering is unchanged");
        Set(query, "status", "Open"); t.True(Matches(second) && Matches(first), "Open includes work and review");
        Set(query, "status", "All"); Set(query, "activityTime", false);
        Set(query, "fromUtcTicks", second.messages[0].createdUtcTicks);
        t.True(Matches(second), "Time lower bound is inclusive");
        Set(query, "toUtcTicks", second.messages[0].createdUtcTicks);
        t.True(!Matches(second), "Time upper bound is exclusive");
        Set(query, "fromUtcTicks", 0L); Set(query, "toUtcTicks", 0L); Set(query, "valid", false);
        t.True(!Matches(first) && !Matches(second), "An invalid custom range cannot broaden results");
        Set(query, "valid", true); Set(query, "sort", "Author");
        var items = new List<WhimTexComment> { second, first }; Call(query, "Sort", items);
        t.Equal(first.id, items[0].id, "Author sort remains stable");
        document.SetCommentStatus(second.id, WhimTexCommentStatus.InProgress);
        t.Equal(before, JsonUtility.ToJson(document), "Queries never modify document data");
    }

    static void Revisions(TestContext t, WhimTexDocument document)
    {
        var thread = document.comments[0];
        string Revision() => (string)Call(typeof(WhimTexApi), "FeedbackRevision", thread);
        string before = Revision();
        var reply = document.AddCommentMessage(thread.id, "Agent reply", "Agent", new List<WhimTexCommentAttachment>
        { WhimTexCommentAttachment.ForMask(WhimTexCommentMask.Capture(new byte[] { 255 }, 1, 1)) });
        reply.agentWorkId = "reply-work";
        reply.agentContentHash = (string)Call(typeof(WhimTexApi), "AgentMessageHash", reply);
        t.Equal(before, Revision(), "An untouched agent reply with a mask does not change feedbackRevision");
        thread.color = Color.green; reply.authorColor = Color.blue;
        t.Equal(before, Revision(), "Discussion and author colors remain cosmetic for agent feedback");
        reply.attachments[0].mask.localToCanvas.m02 += .2;
        t.True(before != Revision(), "An edited agent attachment changes feedbackRevision");
        string withReply = Revision();
        thread.messages[0].text += " Updated";
        t.True(withReply != Revision(), "Root text changes always invalidate agent feedback");
    }

    static void RetainedOrder(TestContext t)
    {
        var list = new ScrollView();
        var a = new VisualElement(); var b = new VisualElement(); var c = new VisualElement();
        void Place(int index, VisualElement row) => Call(typeof(WhimTexWindow), "PlaceCommentRow", list, index, row);
        Place(0, a); Place(1, b); Place(2, c);
        for (int i = 0; i < 20; i++) { Place(0, a); Place(1, b); Place(2, c); }
        t.True(list.childCount == 3 && list.ElementAt(0) == a && list.ElementAt(1) == b && list.ElementAt(2) == c,
            "Repeated list reconciliation retains the existing elements and order");
        Place(0, c); Place(1, a); Place(2, b);
        t.True(list.childCount == 3 && list.ElementAt(0) == c && list.ElementAt(1) == a && list.ElementAt(2) == b,
            "Reordering does not duplicate message elements");
        a.RemoveFromHierarchy(); Place(0, c); Place(1, b);
        t.True(list.childCount == 2 && list.ElementAt(1) == b, "Deletion keeps surviving rows");
        list.Clear();
    }

    static float OutlineDistance(Rect rect, float radius, Vector2 point)
    {
        Vector2 delta = point - rect.center;
        Vector2 q = new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) - rect.size * .5f + Vector2.one * radius;
        return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
    }

    static void ConnectorContact(TestContext t)
    {
        foreach (var rect in new[] { new Rect(17, 31, 296, 180), new Rect(-80, -60, 248, 100), new Rect(0, 0, 8, 8) })
        foreach (float radius in new[] { 0f, 4f })
        {
            float perimeter = 2 * (rect.width + rect.height - 4 * radius) + 2 * Mathf.PI * radius;
            Vector2 Point(float distance) => (Vector2)Call(typeof(WhimTexWindow), "CommentOutlinePoint", rect, radius, distance);
            float Boundary(Vector2 point) => OutlineDistance(rect, radius, point);
            for (int i = 0; i < 80; i++)
            {
                float distance = perimeter * i / 80;
                Vector2 point = Point(distance);
                float recovered = (float)Call(typeof(WhimTexWindow), "CommentContactOffset", rect, radius, point);
                t.Near(0, Vector2.Distance(point, Point(recovered)), .001, "Contact follows the complete rounded outline, including every corner");
                t.Near(0, Vector2.Distance(point, Point(distance - perimeter)), .001, "A short accent can wrap across the outline seam");
                t.Near(0, Boundary(point), .001, "Every sampled accent point stays on the rounded rectangle boundary");
                if (radius > 0)
                {
                    object[] args = { rect, radius, distance, Vector2.zero };
                    Call(typeof(WhimTexWindow), "CommentOutlinePoint", args);
                    var normal = (Vector2)args[3];
                    t.Near(1, normal.magnitude, .001, "Contact thickness uses a unit outward normal");
                    t.Near(3, Boundary(point + normal * 3), .001, "Outer accent stays 3 px outside every rounded edge");
                    t.Near(-2, Boundary(point - normal * 2), .001, "Inner accent stays 2 px inside every rounded edge");
                }
            }
            foreach (var direction in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down, new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), Vector2.one })
            {
                Vector2 anchor = rect.center + Vector2.Scale(direction, rect.size);
                float offset = (float)Call(typeof(WhimTexWindow), "CommentContactOffset", rect, radius, anchor);
                Vector2 end = Point(offset);
                t.True(float.IsFinite(end.x) && float.IsFinite(end.y), "External markers produce finite contact coordinates");
                for (int n = -11; n < 11; n++)
                    t.True(Vector2.Distance(Point(offset + n), Point(offset + n + 1)) <= 1.001f, "The accent remains continuous around corners");
            }
        }
    }

    static void ConnectorArrows(TestContext t)
    {
        var rect = new Rect(80, 60, 296, 180);
        const float radius = 4;
        Vector2 Outer(float position)
        {
            object[] args = { rect, radius, position, Vector2.zero };
            var point = (Vector2)Call(typeof(WhimTexWindow), "CommentOutlinePoint", args);
            return point + (Vector2)args[3] * 3;
        }
        float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        foreach (float gap in new[] { 0f, 1f, 4f, 8f, 20f, 100f, 1000f })
        for (int along = -20; along <= 40; along += 2)
        foreach (var anchor in new[]
        {
            new Vector2(rect.xMin - gap, rect.yMin + along), new Vector2(rect.xMax + gap, rect.yMin + along),
            new Vector2(rect.xMin + along, rect.yMin - gap), new Vector2(rect.xMin + along, rect.yMax + gap),
            new Vector2(rect.xMin - gap, rect.yMax - along), new Vector2(rect.xMax + gap, rect.yMax - along)
        })
        {
            float offset = (float)Call(typeof(WhimTexWindow), "CommentContactOffset", rect, radius, anchor);
            var arrow = Call(typeof(WhimTexWindow), "CommentContactArrow", rect, radius, offset, anchor);
            if (!(bool)Read(arrow, "visible")) continue;
            Vector2 a = Outer((float)Read(arrow, "start")), b = Outer((float)Read(arrow, "end"));
            Vector2 ca = (Vector2)Read(arrow, "startControl"), cb = (Vector2)Read(arrow, "endControl");
            Vector2 ta = (Vector2)Read(arrow, "tipStart"), tb = (Vector2)Read(arrow, "tipEnd");
            Vector2 center = (a + b) * .5f, tip = (ta + tb) * .5f, axis = (anchor - center).normalized;
            t.Near(1.5, Vector2.Distance(ta, tb) - 1, .001, "The centered outline leaves the same 1.5 px colored core in the neck and connecting line");
            t.Near(0, Cross(tip - center, axis), .001, "The line points toward the center of the adjusted arrow base");
            t.True(Vector2.Distance(tip, center) <= 25.001f && Vector2.Dot(anchor - tip, axis) > 0,
                "The arrow stays short and never extends beyond a nearby marker");
            for (int i = 0; i <= 24; i++)
            {
                float u = i / 24f, v = 1 - u;
                Vector2 first = a * (v * v) + ca * (2 * u * v) + ta * (u * u);
                Vector2 second = b * (v * v) + cb * (2 * u * v) + tb * (u * u);
                t.True(OutlineDistance(rect, radius, first) >= 2.997f && OutlineDistance(rect, radius, second) >= 2.997f,
                    "Curved arrow sides never cut into the 5 px contact band, including diagonal corner attachments");
            }
            t.True(Cross(ca - a, ta - ca) <= .001f && Cross(cb - b, tb - cb) >= -.001f,
                "Both arrow sides stay concave, without an outward bulge");
            t.True(Vector2.Dot(ta - ca, axis) > 0 && Vector2.Dot(tb - cb, axis) > 0,
                "Short arrows do not fold back at the neck");
            t.Near(0, Cross(ta - ca, axis), .001, "First curved side joins the line tangentially");
            t.Near(0, Cross(tb - cb, axis), .001, "Second curved side joins the line tangentially");
        }
        foreach (float gap in new[] { 12f, 24f, 100f })
        foreach (var direction in new[] { Vector2.one, new Vector2(-1, 1), new Vector2(1, -1), -Vector2.one })
        {
            Vector2 corner = rect.center + Vector2.Scale(rect.size * .5f, direction);
            Vector2 anchor = corner + direction * gap;
            float offset = (float)Call(typeof(WhimTexWindow), "CommentContactOffset", rect, radius, anchor);
            var arrow = Call(typeof(WhimTexWindow), "CommentContactArrow", rect, radius, offset, anchor);
            t.True((bool)Read(arrow, "visible"), "Diagonal arrows remain visible at all four corners");
        }
    }

    public static string Run() => TestContext.Run("Comment editing, content conflicts, pure queries and retained row ordering", t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        try { Drafts(t, document); Queries(t, document); Revisions(t, document); RetainedOrder(t); ConnectorContact(t); ConnectorArrows(t); }
        finally { UnityEngine.Object.DestroyImmediate(document); }
    });
}
