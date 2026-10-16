using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Value 1 was an automatically detected full name; it must be resolved again as an alias.
    internal enum CommentAuthorSource { Unassigned = 0, UnityEmail = 2, Manual = 3, UnityPrimaryOrganization = 4 }

    internal static partial class WhimTexUserSettings
    {
        private const string CommentAuthorKey = "DCFApixels.WhimTex.CommentAuthor";
        private const string CommentAuthorColorKey = "DCFApixels.WhimTex.CommentAuthorColor";
        private const string CommentAuthorIdKey = "DCFApixels.WhimTex.CommentAuthorId";

        internal static string CommentAuthorId => EditorPrefs.GetString(CommentAuthorIdKey, string.Empty);
        internal static string EnsureCommentAuthorId()
        {
            string id = CommentAuthorId;
            if (!Guid.TryParseExact(id, "N", out _))
            {
                id = Guid.NewGuid().ToString("N");
                EditorPrefs.SetString(CommentAuthorIdKey, id);
            }
            return id;
        }

        internal static bool HasCustomCommentAuthorColor => Load(CommentAuthorColorKey).HasValue;
        internal static Color CommentAuthorColor
        {
            get => Load(CommentAuthorColorKey) ?? WhimTexComment.ColorFromAuthor(CommentAuthorName);
            set
            {
                Color? stored = Load(CommentAuthorColorKey);
                Save(CommentAuthorColorKey, ref stored, value);
            }
        }

        internal static void ResetCommentAuthorColor()
        {
            if (!EditorPrefs.HasKey(CommentAuthorColorKey)) return;
            EditorPrefs.DeleteKey(CommentAuthorColorKey);
            Changed?.Invoke();
        }

        internal static Color GetCommentColor(WhimTexComment comment)
            => comment.useAuthorColor && !string.IsNullOrEmpty(comment.colorAuthorId) && comment.colorAuthorId == CommentAuthorId
                ? CommentAuthorColor : comment.color;

        internal static void AssignCommentMessageAuthor(WhimTexCommentMessage message)
        {
            message.authorId = EnsureCommentAuthorId();
            message.authorColor = CommentAuthorColor;
        }

        internal static Color GetCommentMessageAuthorColor(WhimTexComment comment, WhimTexCommentMessage message)
        {
            string id = message.authorId;
            if (string.IsNullOrEmpty(id) && comment.messages[0] == message) id = comment.colorAuthorId;
            bool local = string.IsNullOrEmpty(id) ? message.author == CommentAuthorName : id == CommentAuthorId;
            return local ? CommentAuthorColor : message.authorColor.a == 1 ? message.authorColor : WhimTexComment.ColorFromAuthor(message.author);
        }

        [Serializable]
        private sealed class CommentAuthorPreference
        {
            public string name = string.Empty;
            public CommentAuthorSource source;
        }

        internal static string CommentAuthorName
        {
            get
            {
                var preference = ReadCommentAuthor();
                return preference.source == CommentAuthorSource.Unassigned ? "User" : preference.name;
            }
        }

        internal static string AssignedCommentAuthorName => ReadCommentAuthor().name;
        internal static CommentAuthorSource CommentAuthorNameSource => ReadCommentAuthor().source;

        private static CommentAuthorPreference ReadCommentAuthor()
        {
            try
            {
                string json = EditorPrefs.GetString(CommentAuthorKey, string.Empty);
                var preference = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CommentAuthorPreference>(json);
                if (preference != null &&
                    (preference.source == CommentAuthorSource.UnityPrimaryOrganization ||
                     preference.source == CommentAuthorSource.UnityEmail || preference.source == CommentAuthorSource.Manual) &&
                    IsValidCommentAuthorName(preference.name))
                    return preference;
            }
            catch (ArgumentException) { }
            return new CommentAuthorPreference();
        }

        private static bool IsValidCommentAuthorName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > WhimTexDocument.MaximumCommentAuthorLength) return false;
            foreach (char character in value) if (char.IsControl(character)) return false;
            return true;
        }

        private static void SaveCommentAuthor(string name, CommentAuthorSource source)
        {
            string json = JsonUtility.ToJson(new CommentAuthorPreference { name = name, source = source });
            if (EditorPrefs.GetString(CommentAuthorKey, string.Empty) == json) return;
            EditorPrefs.SetString(CommentAuthorKey, json);
            Changed?.Invoke();
        }

        internal static bool TrySetCommentAuthorName(string value, out string error)
        {
            value = (value ?? string.Empty).Trim();
            if (!IsValidCommentAuthorName(value))
            {
                error = "Enter a name of 1 to 128 characters without line breaks or control characters.";
                return false;
            }
            error = null;
            SaveCommentAuthor(value, CommentAuthorSource.Manual);
            return true;
        }

        internal static void ClearCommentAuthorName() => SaveCommentAuthor(string.Empty, CommentAuthorSource.Unassigned);

        internal static void UseUnityCommentAuthorName()
        {
            ClearCommentAuthorName();
            EnsureCommentAuthorName();
        }

        // Retry only while unassigned: on window opening and before a new UI post.
        // Providers also allow offline/failure checks without changing the signed-in Unity account.
        internal static void EnsureCommentAuthorName(Func<string> readPrimaryOrganization = null, Func<string> readEmail = null)
        {
            if (CommentAuthorNameSource != CommentAuthorSource.Unassigned) return;
            string name = ReadAccountName(readPrimaryOrganization ?? ReadUnityPrimaryOrganization);
            if (IsValidCommentAuthorName(name))
            {
                SaveCommentAuthor(name, CommentAuthorSource.UnityPrimaryOrganization);
                return;
            }
            string email = ReadAccountName(readEmail ?? (() => CloudProjectSettings.userName));
            int separator = email?.IndexOf('@') ?? -1;
            name = separator > 0 ? email.Substring(0, separator).Trim() : null;
            if (IsValidCommentAuthorName(name)) SaveCommentAuthor(name, CommentAuthorSource.UnityEmail);
            else ClearCommentAuthorName(); // Persist absence, never the display-only "User" fallback.
        }

        private static string ReadUnityPrimaryOrganization()
        {
            // UserInfo is internal and may change; ReadAccountName keeps this optional lookup fail-safe.
            // Read only the account's primary organization, never the current project's organization or full name.
            var connectType = typeof(Editor).Assembly.GetType("UnityEditor.Connect.UnityConnect");
            var instance = connectType?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var userInfo = instance == null ? null : connectType.GetProperty("userInfo", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            if (userInfo == null) return null;
            var userType = userInfo.GetType();
            if (!(userType.GetProperty("valid", BindingFlags.Public | BindingFlags.Instance)?.GetValue(userInfo) is bool valid) || !valid)
                return null;
            var primaryOrg = userType.GetProperty("primaryOrg", BindingFlags.Public | BindingFlags.Instance)?.GetValue(userInfo) as string;
            var organizationNames = userType.GetProperty("organizationNames", BindingFlags.Public | BindingFlags.Instance)?.GetValue(userInfo) as string[];
            return ResolveCommentOrganizationName(primaryOrg, organizationNames);
        }

        internal static string ResolveCommentOrganizationName(string primaryOrg, string[] organizationNames)
        {
            primaryOrg = primaryOrg?.Trim();
            if (!IsValidCommentAuthorName(primaryOrg)) return null;
            string displayName = null;
            if (organizationNames != null)
                foreach (string value in organizationNames)
                {
                    string name = value?.Trim();
                    if (!IsValidCommentAuthorName(name) || !string.Equals(primaryOrg, name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Restore casing only when all matching entries agree; names do not identify renamed organizations.
                    if (displayName != null && !string.Equals(displayName, name, StringComparison.Ordinal)) return primaryOrg;
                    displayName = name;
                }
            return displayName ?? primaryOrg;
        }

        private static string ReadAccountName(Func<string> read)
        {
            // Account data is optional; an unavailable or changed Editor API must not block comments.
            try { return read()?.Trim(); }
            catch (Exception) { return null; }
        }
    }
}
