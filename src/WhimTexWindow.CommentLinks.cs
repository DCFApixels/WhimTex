using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        internal static string CreateCommentLink(WhimTexDocument document, string id, out bool persistent)
        {
            persistent = false;
            if (document?.FindComment(id) == null) return null;
            string prefix = CreateCommentLinkPrefix(document, out persistent);
            return prefix == null ? null : prefix + Uri.EscapeDataString(id);
        }

        internal static string CreateCommentLinkPrefix(WhimTexDocument document, out bool persistent)
        {
            persistent = false;
            if (document == null) return null;
            string path = WhimTexDocumentService.PathOf(document);
            string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
            { persistent = true; return "whimtex://comment/asset/" + guid + "/"; }
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (window.activeDocument == document) return "whimtex://comment/session/" + window.AgentSessionId + "/";
            return null;
        }

        internal static bool ParseCommentLink(string link, out string kind, out string target, out string id)
        {
            kind = target = id = null;
            if (!Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "whimtex" || uri.Host != "comment" ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return false;
            var segments = uri.AbsolutePath.Split('/');
            if (segments.Length != 4 || segments[0] != "" || segments[1] != "asset" && segments[1] != "session" ||
                !Guid.TryParseExact(segments[2], "N", out _)) return false;
            kind = segments[1]; target = segments[2]; id = Uri.UnescapeDataString(segments[3]);
            return !string.IsNullOrWhiteSpace(id) && id.Length <= 128;
        }

        internal static bool TryOpenCommentLink(string link, out WhimTexWindow window, out string id, out string error)
        {
            window = null; error = null;
            if (!ParseCommentLink(link, out string kind, out string target, out id))
            { error = "This is not a valid WhimTex comment link."; return false; }
            string path = kind == "asset" ? AssetDatabase.GUIDToAssetPath(target) : null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (kind == "session" ? candidate.AgentSessionId == target : !string.IsNullOrEmpty(path) &&
                    string.Equals(WhimTexDocumentService.PathOf(candidate.activeDocument), path, StringComparison.OrdinalIgnoreCase))
                { window = candidate; break; }
            if (window == null && kind == "asset")
            {
                if (string.IsNullOrEmpty(path) || !WhimTexDocumentFile.IsDocument(path))
                { error = "The linked document is not available in this project."; return false; }
                try
                {
                    using (var build = WhimTexDocumentBuild.Open(path))
                        if (build.Document.FindComment(id) == null)
                        { error = "The linked discussion is missing from the saved document."; return false; }
                    if (!TryOpenWhimTexDocumentPath(path, out error)) return false;
                }
                catch (Exception exception) { error = exception.Message; return false; }
                foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                    if (string.Equals(WhimTexDocumentService.PathOf(candidate.activeDocument), path, StringComparison.OrdinalIgnoreCase))
                    { window = candidate; break; }
            }
            if (window == null) { error = "This temporary comment link has expired. Reopen the document and copy a new link."; return false; }
            if (window.activeDocument.FindComment(id) == null) { error = "The linked discussion no longer exists."; return false; }
            window.Show(); window.Focus();
            if (window.toolkitCanvas == null) window.CreateGUI();
            window.SetCanvasTool(CanvasTool.Comment);
            window.ShowCommentOnCanvas(id); window.Repaint(); return true;
        }

        private void CopyCommentLink(string id)
        {
            string link = CreateCommentLink(activeDocument, id, out bool persistent);
            if (link == null) return;
            GUIUtility.systemCopyBuffer = link;
            ShowNotification(new GUIContent(persistent ? "Comment link copied" : "Temporary link copied — valid while this document session is open"));
        }

        private bool TryPasteCommentLink(string text)
        {
            if (text?.TrimStart().StartsWith("whimtex://", StringComparison.OrdinalIgnoreCase) != true) return false;
            if (!TryOpenCommentLink(text, out _, out _, out string error)) ShowNotification(new GUIContent(error));
            return true;
        }

        private void AddCommentTextLinks(VisualElement row, string text)
        {
            int count = 0;
            foreach (Match match in Regex.Matches(text, @"whimtex://comment/(?:asset|session)/[0-9a-fA-F]{32}/[^\s<>""\[\]]+"))
            {
                if (++count > 8) break;
                string link = match.Value.TrimEnd('.', ',', ';', ')');
                if (!ParseCommentLink(link, out _, out _, out _)) continue;
                var button = new Button(() => TryPasteCommentLink(link)) { text = "Open linked comment", tooltip = link, name = "commentTextLink" };
                button.AddToClassList("whimtex-comment-text-link"); row.Add(button);
            }
        }
    }
}
