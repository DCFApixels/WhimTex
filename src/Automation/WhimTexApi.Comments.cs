using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static string CommentRevision(WhimTexComment comment)
        {
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(comment))));
        }

        private static JObject CommentSnapshot(WhimTexDocument document, WhimTexComment comment, WhimTexDocument linkDocument = null)
        {
            string link = WhimTexWindow.CreateCommentLink(linkDocument ?? document, comment.id, out bool persistentLink);
            Color color = WhimTexUserSettings.GetCommentColor(comment);
            var messages = new JArray();
            foreach (var message in comment.messages)
            {
                Color authorColor = WhimTexUserSettings.GetCommentMessageAuthorColor(comment, message);
                messages.Add(new JObject { ["id"] = message.id, ["author"] = message.author, ["text"] = message.text,
                    ["authorColor"] = new JArray(authorColor.r, authorColor.g, authorColor.b),
                    ["createdUtc"] = new DateTime(message.createdUtcTicks, DateTimeKind.Utc).ToString("O"),
                    ["editedUtc"] = message.editedUtcTicks == 0 ? null : new DateTime(message.editedUtcTicks, DateTimeKind.Utc).ToString("O"),
                    ["attachments"] = AttachmentSnapshots(document, message), ["workId"] = message.agentWorkId,
                    ["agentMessage"] = message.agentContentHash != null && message.agentContentHash == AgentMessageHash(message) });
            }
            return new JObject
            {
                ["id"] = comment.id, ["number"] = comment.number, ["revision"] = CommentRevision(comment),
                ["feedbackRevision"] = FeedbackRevision(comment),
                ["createdUtc"] = new DateTime(WhimTexCommentQuery.Created(comment), DateTimeKind.Utc).ToString("O"),
                ["lastActivityUtc"] = new DateTime(WhimTexCommentQuery.Activity(comment), DateTimeKind.Utc).ToString("O"),
                ["link"] = link, ["persistentLink"] = persistentLink,
                ["anchorUv"] = new JArray(comment.anchor.x, comment.anchor.y),
                ["anchorPixels"] = new JArray(comment.anchor.x * document.width, comment.anchor.y * document.height),
                ["coordinates"] = "bottom-left canvas", ["resolved"] = comment.resolved, ["status"] = comment.Status.ToString(),
                ["color"] = new JArray(color.r, color.g, color.b), ["useAuthorColor"] = comment.useAuthorColor, ["messages"] = messages
            };
        }

        private static JArray CommentSnapshots(WhimTexDocument document)
        {
            document.EnsureCommentNumbers();
            var result = new JArray();
            if (document.comments != null)
                for (int i = 0; i < document.comments.Count; i++)
                    result.Add(CommentSnapshot(document, document.comments[i]));
            return result;
        }

        private static WhimTexComment CheckComment(WhimTexDocument document, JObject check)
        {
            string id = Text(check, "commentId");
            Require(!string.IsNullOrEmpty(id), "commentId is required.");
            var comment = document.FindComment(id);
            Require(comment != null, "Comment no longer exists: " + id, "comment_not_found");
            bool feedback = check["expectedFeedbackRevision"] != null;
            Require(!feedback || check["expectedRevision"] == null, "Use expectedRevision or expectedFeedbackRevision, not both.");
            string expected = Text(check, feedback ? "expectedFeedbackRevision" : "expectedRevision");
            Require(!string.IsNullOrEmpty(expected), "The comment's expectedRevision is required.", "revision_required");
            Require(expected == (feedback ? FeedbackRevision(comment) : CommentRevision(comment)), "Comment changed; reread the discussion: " + id, "comment_revision_conflict");
            return comment;
        }

        private static JArray CheckComments(WhimTexDocument document, JToken token)
        {
            if (token == null) return null;
            Require(token is JArray checks && checks.Count > 0 && checks.Count <= 256, "commentChecks must contain 1..256 checks.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in (JArray)token)
            {
                var check = Obj(item, "comment check");
                Keys(check, "commentId", "expectedRevision", "expectedFeedbackRevision");
                var comment = CheckComment(document, check);
                Require(ids.Add(comment.id), "Duplicate comment check.");
            }
            return (JArray)token.DeepClone();
        }

        private static void ApplyCommentOperation(WhimTexDocument document, JObject operation, Dictionary<string, Layer> aliases, string workId, CanvasSelection attachmentSelection)
        {
            if (Text(operation, "op") == "commentCreate")
            {
                Keys(operation, "op", "anchorUv", "text", "author", "attachments");
                Vector2 anchor = Vector(operation["anchorUv"], "anchorUv");
                Require(anchor.x >= 0 && anchor.x <= 1 && anchor.y >= 0 && anchor.y <= 1,
                    "anchorUv must be inside the canvas (0..1, bottom-left origin).");
                string text = CommentMessageText(operation, "text");
                string author = CommentMessageAuthor(operation);
                var created = document.AddComment(anchor, text, author,
                    attachments: ParseCommentAttachments(document, operation["attachments"], aliases, attachmentSelection));
                var message = created.messages[0];
                message.agentWorkId = workId; message.agentContentHash = AgentMessageHash(message);
                return;
            }
            Keys(operation, "op", "commentId", "expectedRevision", "expectedFeedbackRevision", "reply", "author", "resolved", "status", "attachments");
            var comment = CheckComment(document, operation);
            Require(operation["status"] == null || operation["resolved"] == null, "Use status or resolved, not both.");
            WhimTexCommentStatus? status = null;
            if (operation["status"] != null)
            {
                Require(operation["status"].Type == JTokenType.String &&
                    (Text(operation, "status") == "InProgress" || Text(operation, "status") == "AwaitingReview" || Text(operation, "status") == "Resolved"),
                    "status must be InProgress, AwaitingReview or Resolved.");
                status = System.Enum.Parse<WhimTexCommentStatus>(Text(operation, "status"));
            }
            Require(operation["author"] == null || operation["reply"] != null, "author requires a reply.");
            Require(operation["attachments"] == null || operation["reply"] != null, "attachments requires a reply.");
            if (operation["reply"] != null)
            {
                string reply = CommentMessageText(operation, "reply");
                string author = CommentMessageAuthor(operation);
                var message = document.AddCommentMessage(comment.id, reply, author, ParseCommentAttachments(document, operation["attachments"], aliases, attachmentSelection));
                message.agentWorkId = workId; message.agentContentHash = AgentMessageHash(message);
            }
            if (operation["resolved"] != null) document.SetCommentResolved(comment.id, Bool(operation, "resolved"));
            if (status.HasValue) document.SetCommentStatus(comment.id, status.Value);
        }

        private static string CommentMessageText(JObject operation, string key)
        {
            Require(operation[key]?.Type == JTokenType.String, key + " must be a string.");
            string text = (string)operation[key];
            Require(!string.IsNullOrWhiteSpace(text) && text.Length <= WhimTexDocument.MaximumCommentTextLength,
                key + " must contain 1..16384 nonblank text characters.");
            return text;
        }

        private static string CommentMessageAuthor(JObject operation)
        {
            string author = Text(operation, "author", "Assistant");
            Require(!string.IsNullOrWhiteSpace(author) && author.Length <= WhimTexDocument.MaximumCommentAuthorLength,
                "author must contain 1..128 nonblank characters.");
            return author;
        }

        private static JObject OperationResult(WhimTexDocument document, JObject operation, int index, Layer layer)
        {
            var result = new JObject { ["index"] = index, ["layerId"] = layer?.Id, ["name"] = layer?.layerName };
            string op = Text(operation, "op");
            if (op == "comment" || op == "commentCreate")
            {
                var comment = op == "commentCreate" ? document.comments.Last() : document.FindComment(Text(operation, "commentId"));
                result["commentId"] = comment.id;
                if (op == "commentCreate" || operation["reply"] != null) result["messageId"] = comment.messages.Last().id;
            }
            return result;
        }

        public static string CommentsFile(string requestPath) => Respond(() => Comments(Parse(ReadRequestFile(requestPath))));
        public static string CommentsJson(string json) => Respond(() => Comments(Parse(json)));

        private static JObject Comments(JObject request)
        {
            if (Text(request, "op") == "open")
            {
                Keys(request, "apiVersion", "op", "link");
                Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
                Require(WhimTexWindow.TryOpenCommentLink(Text(request, "link"), out var window, out string id, out string error), error, "comment_link_unavailable");
                var opened = Success(); opened["sessionId"] = window.AgentSessionId; opened["commentId"] = id;
                opened["applied"] = false; opened["navigated"] = true; return opened;
            }
            Keys(request, "apiVersion", "op", "assetPath", "assistantSessionId", "headlessSessionId", "commentIds",
                "includeResolved", "focusCommentId", "radius", "maxSize", "workId", "maskRef", "filter", "sort", "descending");
            Require(Int(request, "apiVersion", 0, 0, int.MaxValue) == ProtocolVersion, "apiVersion must be 1.");
            string op = Text(request, "op", "list");
            Require(op == "list" || op == "render" || op == "work" || op == "mask" || op == "context", "Comments supports list, render, mask, context or work.");
            Require(op == "mask" || request["maskRef"] == null, "maskRef requires op:mask.");
            Require(op == "work" || request["workId"] == null, "workId requires op:work.");
            Require(op == "render" || op == "context" || request["focusCommentId"] == null && request["radius"] == null && request["maxSize"] == null,
                "focusCommentId, radius and maxSize require render.");
            Require(request["radius"] == null || request["focusCommentId"] != null, "radius requires focusCommentId.");
            int sources = (request["assetPath"] != null ? 1 : 0) + (request["assistantSessionId"] != null ? 1 : 0) + (request["headlessSessionId"] != null ? 1 : 0);
            Require(sources == 1, "Supply exactly one source: assetPath, assistantSessionId or headlessSessionId.");
            WhimTexDocumentBuild build;
            WhimTexDocument document;
            WhimTexDocument linkDocument;
            string revision;
            if (request["assetPath"] != null)
            {
                build = WhimTexDocumentBuild.Open(DocumentPath(Text(request, "assetPath")));
                document = build.Document;
                linkDocument = document;
                revision = null;
            }
            else
            {
                var source = request["assistantSessionId"] != null
                    ? LiveWindow(Text(request, "assistantSessionId")).AgentDocument
                    : GetTiffLiveSession(Text(request, "headlessSessionId")).working.Document;
                LiveReady(source);
                linkDocument = source;
                revision = Revision(source);
                build = op == "render" ? WhimTexDocumentBuild.Copy(source) : null;
                document = build?.Document ?? source;
            }
            using (build)
            {
                if (op == "mask")
                {
                    Keys(request, "apiVersion", "op", "assetPath", "assistantSessionId", "headlessSessionId", "maskRef");
                    var exported = Success(); exported["revision"] = revision ?? Revision(document);
                    exported["width"] = document.width; exported["height"] = document.height; exported["applied"] = false;
                    ExportCommentMask(document, request, exported); return exported;
                }
                if (op == "work")
                {
                    Keys(request, "apiVersion", "op", "assetPath", "assistantSessionId", "headlessSessionId", "workId");
                    var work = Success(); work["workId"] = Text(request, "workId");
                    work["receipts"] = AgentWorkSnapshot(document, Text(request, "workId"));
                    work["revision"] = revision ?? Revision(document); work["applied"] = false; return work;
                }
                var chosen = new JArray();
                HashSet<string> ids = null;
                if (request["commentIds"] != null)
                {
                    Require(request["commentIds"] is JArray array && array.Count > 0 && array.Count <= 256, "commentIds must contain 1..256 IDs.");
                    ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var id in (JArray)request["commentIds"])
                    {
                        Require(id.Type == JTokenType.String && document.FindComment((string)id) != null,
                            "An explicitly requested comment no longer exists.", "comment_not_found");
                        Require(ids.Add((string)id), "Duplicate comment ID.");
                    }
                }
                document.EnsureCommentNumbers();
                var query = ParseCommentQuery(request, ids != null);
                if (query.NeedsLinkContext) query.linkPrefix = WhimTexWindow.CreateCommentLinkPrefix(linkDocument, out _);
                var matching = new List<WhimTexComment>();
                foreach (var item in document.comments)
                    if ((ids == null || ids.Contains(item.id)) && query.Matches(item)) matching.Add(item);
                query.Sort(matching);
                foreach (var item in matching) chosen.Add(CommentSnapshot(document, item, linkDocument));
                var result = Success();
                result["revision"] = revision ?? Revision(document);
                result["width"] = document.width; result["height"] = document.height;
                result["totalCount"] = document.comments.Count; result["matchedCount"] = chosen.Count; result["comments"] = chosen;
                result["applied"] = false;
                if (op == "render" || op == "context") RenderCommentContext(document, chosen, request, result);
                if (op == "context") BuildCommentContextBundle(document, chosen, result);
                return result;
            }
        }

        private static void RenderCommentContext(WhimTexDocument document, JArray comments, JObject request, JObject result)
        {
            RequireGraphics(); ValidateAgentBudget(document);
            Require(comments.Count <= 128, "Render at most 128 comments at once; select commentIds.", "resource_limit");
            int maxSize = Int(request, "maxSize", 1024, 64, 4096);
            var bounds = new RectInt(0, 0, document.width, document.height);
            if (request["focusCommentId"] != null)
            {
                string id = Text(request, "focusCommentId");
                Require(comments.Any(c => (string)c["id"] == id), "focusCommentId must belong to the requested comments.");
                var anchor = document.FindComment(id).anchor;
                Require(anchor.x >= 0 && anchor.x <= 1 && anchor.y >= 0 && anchor.y <= 1,
                    "Comment lies outside the canvas; use the overview and its anchor coordinates.", "comment_outside_canvas");
                int radius = Int(request, "radius", 128, 1, 16384);
                int x = Mathf.Clamp(Mathf.FloorToInt(anchor.x * document.width), 0, document.width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(anchor.y * document.height), 0, document.height - 1);
                int left = Mathf.Max(0, x - radius), bottom = Mathf.Max(0, y - radius);
                bounds = new RectInt(left, bottom, Mathf.Min(document.width, x + radius + 1) - left,
                    Mathf.Min(document.height, y + radius + 1) - bottom);
            }
            Texture2D source = null, linear = null, image = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                source = document.ComposeCanvas(Mathf.Max(document.width, document.height));
                float scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(bounds.width, bounds.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(bounds.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(bounds.height * scale));
                target = ProfiledRenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, target, new Vector2(bounds.width / (float)document.width, bounds.height / (float)document.height),
                    new Vector2(bounds.x / (float)document.width, bounds.y / (float)document.height));
                linear = HdrUtility.ReadLinear(target); image = HdrUtility.ToLdr(linear);
                string stem = "Temp/WhimTex/Agent/comments-" + Guid.NewGuid().ToString("N");
                result["imagePath"] = WriteLiveBytes(image.EncodeToPNG(), stem + ".png");
                result["cropBounds"] = LiveRect(bounds);
                result["imageWidth"] = width; result["imageHeight"] = height;
                var pixels = image.GetPixels32();
                foreach (JObject item in comments)
                {
                    var comment = document.FindComment((string)item["id"]);
                    float x = (comment.anchor.x * document.width - bounds.x) * width / bounds.width;
                    float y = (bounds.yMax - comment.anchor.y * document.height) * height / bounds.height;
                    bool visible = x >= 0 && x <= width && y >= 0 && y <= height;
                    item["inView"] = visible;
                    item["imagePoint"] = new JArray(x, y);
                    if (visible) DrawCommentBadge(pixels, width, height, Mathf.RoundToInt(x), Mathf.RoundToInt(y),
                        (int)item["number"], WhimTexUserSettings.GetCommentColor(comment));
                }
                image.SetPixels32(pixels); image.Apply(false, false);
                result["annotatedPath"] = WriteLiveBytes(image.EncodeToPNG(), stem + "-annotated.png");
                result["imageCoordinates"] = "top-left image pixels; cropBounds uses bottom-left canvas pixels";
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) ProfiledRenderTexture.ReleaseTemporary(target);
                if (source != null) Object.DestroyImmediate(source);
                if (linear != null) Object.DestroyImmediate(linear);
                if (image != null) Object.DestroyImmediate(image);
            }
        }

        private static void DrawCommentBadge(Color32[] pixels, int width, int height, int x, int y, int number, Color color)
        {
            string digits = number.ToString();
            int boxWidth = digits.Length * 8 + 6, boxHeight = 18;
            int left = Mathf.Clamp(x + 5, 0, Mathf.Max(0, width - boxWidth));
            int top = Mathf.Clamp(y - boxHeight - 5, 0, Mathf.Max(0, height - boxHeight));
            Color32 fill = color, border = new Color32(25, 25, 25, 255);
            fill.a = 255;
            Color32 ink = color.grayscale > .55f ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
            int endX = Mathf.Clamp(x, left, left + boxWidth - 1), endY = Mathf.Clamp(y, top, top + boxHeight - 1);
            int steps = Mathf.Max(Mathf.Abs(endX - x), Mathf.Abs(endY - y));
            for (int i = 0; i <= steps; i++)
                Put(Mathf.RoundToInt(Mathf.Lerp(x, endX, steps == 0 ? 0 : i / (float)steps)),
                    Mathf.RoundToInt(Mathf.Lerp(y, endY, steps == 0 ? 0 : i / (float)steps)), border);
            for (int py = 0; py < boxHeight; py++)
                for (int px = 0; px < boxWidth; px++)
                    Put(left + px, top + py, px == 0 || py == 0 || px == boxWidth - 1 || py == boxHeight - 1 ? border : fill);
            // Compact bitmap digits keep diagnostic labels independent of editor font assets.
            string[] glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
                "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111" };
            for (int d = 0; d < digits.Length; d++)
                for (int row = 0; row < 5; row++)
                    for (int column = 0; column < 3; column++)
                        if (glyphs[digits[d] - '0'][row * 3 + column] == '1')
                            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                                Put(left + 4 + d * 8 + column * 2 + dx, top + 4 + row * 2 + dy, ink);
            void Put(int px, int py, Color32 value)
            { if (px >= 0 && px < width && py >= 0 && py < height) pixels[(height - 1 - py) * width + px] = value; }
        }
    }
}
