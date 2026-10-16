using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class CommentAuthorTests
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Settings = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings", true);
    static object Call(string method, params object[] args) => Settings.GetMethod(method, Flags).Invoke(null, args);
    static string Read(string property) => Settings.GetProperty(property, Flags).GetValue(null).ToString();
    static void Ensure(Func<string> email, Func<string> organization = null)
        => Call("EnsureCommentAuthorName", organization ?? (() => null), email);
    static string OrganizationName(string primary, params string[] names)
        => (string)Call("ResolveCommentOrganizationName", primary, names);
    static bool SetName(string name) => (bool)Call("TrySetCommentAuthorName", name, null);
    static Color AuthorColor => (Color)Settings.GetProperty("CommentAuthorColor", Flags).GetValue(null);
    static void SetColor(Color value) => Settings.GetProperty("CommentAuthorColor", Flags).SetValue(null, value);
    static void Notify() => ((Action)Settings.GetField("Changed", Flags)?.GetValue(null))?.Invoke();

    public static string Run() => TestContext.Run("CommentAuthorTests.Run", t =>
    {
        string key = (string)Settings.GetField("CommentAuthorKey", Flags).GetRawConstantValue();
        bool existed = EditorPrefs.HasKey(key);
        string previous = EditorPrefs.GetString(key);
        string colorKey = (string)Settings.GetField("CommentAuthorColorKey", Flags).GetRawConstantValue();
        string profileKey = (string)Settings.GetField("CommentAuthorIdKey", Flags).GetRawConstantValue();
        bool colorExisted = EditorPrefs.HasKey(colorKey), profileExisted = EditorPrefs.HasKey(profileKey);
        string previousColor = EditorPrefs.GetString(colorKey), previousProfile = EditorPrefs.GetString(profileKey);
        EditorWindow previousFocus = EditorWindow.focusedWindow;
        EditorWindow window = null;
        WhimTexDocument document = null;
        try
        {
            string profileId = Guid.NewGuid().ToString("N");
            EditorPrefs.SetString(profileKey, profileId);
            EditorPrefs.DeleteKey(colorKey);
            t.Equal("ARTIST", OrganizationName("artist", "Other Studio", "ARTIST"), "Organization display name restores original uppercase");
            t.Equal("MyStudio", OrganizationName("mystudio", "MyStudio"), "Organization display name preserves mixed case");
            t.Equal("MyStudio", OrganizationName("  mystudio  ", null, "  MyStudio  "), "Organization names are trimmed without changing casing");
            t.Equal("artist", OrganizationName("artist", (string[])null), "Unavailable organization names retain the primary alias");
            t.Equal("artist", OrganizationName("artist", "Other Studio", "Artist Studio"), "Unrelated organization names cannot replace the primary alias");
            t.Equal("artist", OrganizationName("artist", "ARTIST", "Artist"), "Ambiguous casing retains the primary alias");
            t.Equal("artist", OrganizationName("artist", "Artist", "ARTIST"), "Ambiguous casing is independent of organization order");
            t.Equal("ARTIST", OrganizationName("artist", "ARTIST", "ARTIST"), "Identical duplicate names retain the same display casing");
            t.Equal(null, OrganizationName(null, "ARTIST"), "Missing primary organization cannot select an arbitrary name");
            t.Equal(null, OrganizationName("A\nB", "A\nB"), "Invalid primary organization cannot become a nickname");
            EditorPrefs.DeleteKey(key);
            t.Equal("User", Read("CommentAuthorName"), "Missing preference displays User");
            t.Equal("Unassigned", Read("CommentAuthorNameSource"), "Missing preference remains unassigned");
            Ensure(() => null);
            t.True(EditorPrefs.HasKey(key), "Unavailable account persists an unassigned marker");
            t.Equal("", Read("AssignedCommentAuthorName"), "User fallback is not saved as a name");
            string unassigned = EditorPrefs.GetString(key);
            Ensure(() => "invalid");
            t.Equal(unassigned, EditorPrefs.GetString(key), "Missing email separator stays unassigned");
            Ensure(() => "  email.artist@example.test  ");
            t.Equal("email.artist", Read("CommentAuthorName"), "Retry resolves and trims the email alias");
            t.Equal("UnityEmail", Read("CommentAuthorNameSource"), "Email source persists");
            t.True(!EditorPrefs.GetString(key).Contains("@"), "Full email is never persisted");
            Ensure(() => "other@example.test");
            t.Equal("email.artist", Read("CommentAuthorName"), "Saved automatic alias is not overwritten");
            string savedAutomatic = EditorPrefs.GetString(key);
            Call("ClearCommentAuthorName");
            EditorPrefs.SetString(key, savedAutomatic);
            Ensure(() => null);
            t.Equal("email.artist", Read("CommentAuthorName"), "Persisted name survives unavailable account after reload");
            Call("ClearCommentAuthorName");
            Ensure(() => throw new InvalidOperationException());
            t.Equal("User", Read("CommentAuthorName"), "Provider failure remains usable offline");
            t.Equal("Unassigned", Read("CommentAuthorNameSource"), "Failures allow the next attempt");
            Ensure(() => "@example.test");
            t.Equal("Unassigned", Read("CommentAuthorNameSource"), "Empty email prefix is not an author");
            Ensure(() => new string('x', 129) + "@example.test");
            t.Equal("Unassigned", Read("CommentAuthorNameSource"), "Oversized alias stays unassigned");
            EditorPrefs.SetString(key, "{\"name\":\"Given Family\",\"source\":1}");
            t.Equal("User", Read("CommentAuthorName"), "Previously detected full name is no longer used");
            Ensure(() => null);
            t.Equal("Unassigned", Read("CommentAuthorNameSource"), "Old full name stays unassigned while offline");
            Ensure(() => "artist.handle@example.test");
            t.Equal("artist.handle", Read("CommentAuthorName"), "Old automatic full name is replaced with an alias");

            Call("ClearCommentAuthorName");
            int emailReads = 0;
            Ensure(() => { emailReads++; return "email.artist@example.test"; }, () => OrganizationName("personal.artist", "Personal.Artist"));
            t.Equal("Personal.Artist", Read("CommentAuthorName"), "Personal organization takes priority and preserves display casing");
            t.Equal("UnityPrimaryOrganization", Read("CommentAuthorNameSource"), "Organization source persists");
            t.Equal(0, emailReads, "Successful organization lookup never reads email");
            string savedOrganization = EditorPrefs.GetString(key);
            Call("ClearCommentAuthorName");
            EditorPrefs.SetString(key, savedOrganization);
            int organizationReads = 0;
            Ensure(() => { emailReads++; return null; }, () => { organizationReads++; return null; });
            t.Equal("Personal.Artist", Read("CommentAuthorName"), "Persisted organization casing survives unavailable account after reload");
            t.Equal(0, organizationReads + emailReads, "Saved organization alias does not query the account again");
            foreach (string invalid in new[] { null, "", "  ", "A\nB", new string('x', 129) })
            {
                Call("ClearCommentAuthorName");
                Ensure(() => "fallback@example.test", () => invalid);
                t.Equal("fallback", Read("CommentAuthorName"), "Missing or invalid organization falls back to email");
                t.Equal("UnityEmail", Read("CommentAuthorNameSource"), "Organization fallback records the actual email source");
            }
            Call("ClearCommentAuthorName");
            Ensure(() => "fallback@example.test", () => throw new MissingMemberException());
            t.Equal("fallback", Read("CommentAuthorName"), "Changed internal organization API falls back to email");
            Call("ClearCommentAuthorName");
            Ensure(() => throw new InvalidOperationException(), () => throw new MissingMemberException());
            t.Equal("Unassigned", Read("CommentAuthorNameSource"), "Failure of both providers permits another attempt");
            t.Equal("User", Read("CommentAuthorName"), "Failure of both providers displays User");
            Ensure(() => throw new InvalidOperationException(), () => "recovered.organization");
            t.Equal("recovered.organization", Read("CommentAuthorName"), "Unassigned retries can recover through the organization");

            t.True(SetName("  Local Artist  "), "Manual name accepted");
            t.Equal("Local Artist", Read("CommentAuthorName"), "Manual name trimmed");
            t.Equal("Manual", Read("CommentAuthorNameSource"), "Manual source persists");
            int reads = 0;
            Ensure(() => { reads++; return "account@example.test"; }, () => { reads++; return "personal.artist"; });
            t.Equal(0, reads, "Manual name never queries account data");
            foreach (string invalid in new[] { "", "  ", "A\nB", new string('x', 129) })
            {
                t.True(!SetName(invalid), "Invalid manual name rejected");
                t.Equal("Local Artist", Read("CommentAuthorName"), "Invalid input keeps the existing author");
            }
            t.True(SetName("User"), "User can be a deliberately chosen name");
            t.Equal("Manual", Read("CommentAuthorNameSource"), "Explicit User differs from unassigned");
            Ensure(() => { reads++; return "account@example.test"; }, () => { reads++; return "personal.artist"; });
            t.Equal(0, reads, "Explicit User does not retry automatic detection");
            EditorPrefs.SetString(key, "{invalid");
            t.Equal("User", Read("CommentAuthorName"), "Malformed preference falls back safely");
            Ensure(() => "recovered@example.test");
            t.Equal("recovered", Read("CommentAuthorName"), "Malformed preference permits recovery");

            SetName("Settings Artist");
            var windowType = Settings.Assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettingsWindow", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            window.ShowUtility();
            windowType.GetMethod("CreateGUI", Flags).Invoke(window, null);
            var field = window.rootVisualElement.Q<TextField>("commentAuthorName");
            t.Equal("Nickname", field.label, "Settings explicitly requests a nickname");
            t.Equal("Settings Artist", field.value, "Settings shows persisted author");
            t.True(field.isDelayed, "Author commits after editing");
            field.value = "UI Artist";
            t.Equal("UI Artist", Read("CommentAuthorName"), "Settings field saves a manual author");
            t.True(window.rootVisualElement.Q<Label>("commentAuthorStatus").text.Contains("Custom"), "Settings reports manual source");
            Call("ClearCommentAuthorName");
            t.Equal("", field.value, "Unassigned settings does not claim User is an assigned name");
            t.True(window.rootVisualElement.Q<Label>("commentAuthorStatus").text.Contains("Using User"), "Unassigned settings explains the fallback");
            t.True(window.rootVisualElement.Q<Button>("detectCommentAuthor") != null, "Settings offers explicit automatic reset");
            Ensure(() => "fallback@example.test", () => OrganizationName("ui.organization", "UI.Organization"));
            t.Equal("UI.Organization", field.value, "Settings preserves the detected organization display casing");
            t.True(window.rootVisualElement.Q<Label>("commentAuthorStatus").text.Contains("personal Unity organization"), "Settings reports organization source");
            Call("ClearCommentAuthorName");
            Ensure(() => "fallback@example.test");
            t.True(window.rootVisualElement.Q<Label>("commentAuthorStatus").text.Contains("email"), "Settings reports email fallback source");

            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            var thread = document.AddComment(Vector2.zero, "Anonymous model comment");
            t.Equal("User", thread.messages[0].author, "Standalone model defaults to User without local-account access");
            SetName("Later Artist");
            t.Equal("User", thread.messages[0].author, "Changing settings never renames existing messages");
            t.Equal("Assistant", document.AddCommentMessage(thread.id, "Agent reply", "Assistant").author, "Explicit agent author stays independent");

            Color.RGBToHSV(WhimTexComment.DefaultColor, out _, out float defaultS, out float defaultV);
            foreach (string author in new[] { "Artist", "Another Artist", "Художник", "画师", "User" })
            {
                Color color = WhimTexComment.ColorFromAuthor(author);
                Color.RGBToHSV(color, out _, out float s, out float v);
                t.Near(defaultS, s, 1e-6, "Automatic colors retain the default blue saturation");
                t.Near(defaultV, v, 1e-6, "Automatic colors retain the default blue brightness");
                t.Equal(1f, color.a, "Automatic colors are opaque");
            }
            t.Equal(WhimTexComment.ColorFromAuthor("User"), WhimTexComment.ColorFromAuthor(null), "Unnamed author uses User's stable hue");
            Color.RGBToHSV(WhimTexComment.ColorFromAuthor("Artist"), out float artistHue, out _, out _);
            t.Near(0.26067620515823364, artistHue, 1e-6, "Name hue uses a fixed hash independent of process string hashing");
            t.True(WhimTexComment.ColorFromAuthor("Artist") != WhimTexComment.ColorFromAuthor("Another Artist"), "Different names produce different hues");
            SetName("Artist");
            t.Equal(WhimTexComment.ColorFromAuthor("Artist"), AuthorColor, "Unset user color follows the name hash");
            t.True(!(bool)Settings.GetProperty("HasCustomCommentAuthorColor", Flags).GetValue(null), "Absent color is automatic");
            var custom = (Color)new Color32(77, 149, 205, 255);
            SetColor(custom);
            t.Equal(custom, AuthorColor, "Custom color persists");
            SetName("Renamed Artist");
            t.Equal(custom, AuthorColor, "Manual user color survives a nickname change");
            t.Equal(profileId, Call("EnsureCommentAuthorId"), "Nickname changes retain color ownership");
            var automatic = document.AddComment(Vector2.zero, "Automatic", "Artist");
            document.SetCommentAuthorColor(automatic.id, WhimTexComment.ColorFromAuthor("Artist"), profileId);
            t.Equal(custom, Call("GetCommentColor", automatic), "Existing automatic comment follows its user after rename");
            t.True((bool)CallDocument(document, "RefreshCommentAuthorColors", profileId, custom), "Automatic color updates its portable stored snapshot");
            t.Equal(custom, automatic.color, "Updated author color is retained in document data");
            var manual = document.AddComment(Vector2.zero, "Manual", "Artist", WhimTexComment.DefaultColor);
            manual.colorAuthorId = profileId;
            var foreign = document.AddComment(Vector2.zero, "Foreign", "Another Artist");
            document.SetCommentAuthorColor(foreign.id, foreign.color, Guid.NewGuid().ToString("N"));
            Color foreignColor = foreign.color;
            SetColor(Color.green);
            CallDocument(document, "RefreshCommentAuthorColors", profileId, AuthorColor);
            t.Equal(Color.green, automatic.color, "User color changes update automatic comments");
            t.Equal(WhimTexComment.DefaultColor, manual.color, "Manual default-blue comments remain untouched");
            t.Equal(foreignColor, foreign.color, "Another profile's automatic comments remain untouched");
            t.True(document.SetCommentColor(automatic.id, automatic.color), "Choosing the same color manually still pins it");
            t.True(!automatic.useAuthorColor, "Manual assignment disables automatic color");
            SetColor(Color.red); CallDocument(document, "RefreshCommentAuthorColors", profileId, AuthorColor);
            t.Equal(Color.green, automatic.color, "Pinned color ignores subsequent user color changes");
            document.SetCommentAuthorColor(automatic.id, AuthorColor, profileId);
            t.Equal(Color.red, automatic.color, "Reset to author color resumes automatic appearance");
            Call("ResetCommentAuthorColor");
            t.Equal(WhimTexComment.ColorFromAuthor("Renamed Artist"), AuthorColor, "Reset user color returns to the current name hash");
            var colorField = window.rootVisualElement.Q<WhimTexColorField>("commentAuthorColor");
            colorField.value = custom;
            t.Equal(custom, AuthorColor, "User settings color field writes the preference");
            var reset = window.rootVisualElement.Q<Button>("resetCommentAuthorColor");
            t.True(reset.enabledSelf, "Custom user color enables Reset");
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = reset; reset.SendEvent(e); }
            t.Equal(WhimTexComment.ColorFromAuthor("Renamed Artist"), colorField.value, "Reset updates the displayed automatic color");
            t.True(!reset.enabledSelf, "Automatic user color disables Reset");
            var ownMessage = manual.messages[0];
            Call("AssignCommentMessageAuthor", ownMessage);
            SetColor(custom);
            CallDocument(document, "RefreshCommentAuthorColors", profileId, AuthorColor);
            t.Equal(custom, Call("GetCommentMessageAuthorColor", manual, ownMessage), "Manual thread color does not override its author's color");
            t.Equal(custom, ownMessage.authorColor, "Message snapshot follows the local profile inside a manual thread");
            SetName("Another nickname");
            t.Equal(custom, Call("GetCommentMessageAuthorColor", manual, ownMessage), "Message identity survives nickname changes");
            var foreignMessage = foreign.messages[0];
            foreignMessage.authorId = Guid.NewGuid().ToString("N"); foreignMessage.authorColor = Color.yellow;
            t.Equal(Color.yellow, Call("GetCommentMessageAuthorColor", foreign, foreignMessage), "Another profile retains its portable custom author color");
            foreignMessage.author = Read("CommentAuthorName");
            t.Equal(Color.yellow, Call("GetCommentMessageAuthorColor", foreign, foreignMessage), "A matching nickname cannot replace another profile's color");
            foreignMessage.authorId = null; foreign.colorAuthorId = null; foreignMessage.authorColor = Color.clear; foreignMessage.author = "Older author";
            t.Equal(WhimTexComment.ColorFromAuthor("Older author"), Call("GetCommentMessageAuthorColor", foreign, foreignMessage), "Older messages without color metadata fall back to the author's hue");
        }
        finally
        {
            if (window != null) window.Close();
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (document != null) UnityEngine.Object.DestroyImmediate(document);
            if (existed) EditorPrefs.SetString(key, previous); else EditorPrefs.DeleteKey(key);
            if (colorExisted) EditorPrefs.SetString(colorKey, previousColor); else EditorPrefs.DeleteKey(colorKey);
            if (profileExisted) EditorPrefs.SetString(profileKey, previousProfile); else EditorPrefs.DeleteKey(profileKey);
            Notify();
            if (previousFocus != null && EditorWindow.focusedWindow != previousFocus) previousFocus.Focus();
        }
    });

    static object CallDocument(WhimTexDocument document, string method, params object[] args)
        => typeof(WhimTexDocument).GetMethod(method, Flags).Invoke(document, args);
}
