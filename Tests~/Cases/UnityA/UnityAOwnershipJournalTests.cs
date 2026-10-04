// Independent protocol regression for durable ownership; creates only in-memory test windows.
public static class UnityAOwnershipJournalTests
{
    public static string Run() => WhimTex.Tests.TestContext.Run("BCL owned-window journal round trip", context =>
    {
        string oldV2 = UnityEditor.SessionState.GetString("WhimTex.Tests.UnityA.OwnedWindows.v2", "");
        WhimTex.Tests.UnityA.UnityAScope.TestJournalWire(context);
        WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope =>
        {
            var window = scope.OwnWindow(UnityEngine.ScriptableObject.CreateInstance<WhimTex.Tests.UnityA.UnityAHeaderWindow>());
            string identity = WhimTex.Tests.UnityA.UnityAScope.AssertJournalOwnership(context, window);
            window.name = "Renamed owned protocol fixture";
            context.Equal(identity, WhimTex.Tests.UnityA.UnityAScope.AssertJournalOwnership(context, window), "Renaming does not change exact native identity");

            // Our own unjournaled stand-in verifies protection without touching a user window.
            var unjournaled = scope.OwnObject(UnityEngine.ScriptableObject.CreateInstance<WhimTex.Tests.UnityA.UnityAHeaderWindow>());
            unjournaled.name = scope.Tag + "-owned-UnityAHeaderWindow-9999";
            bool rejected = false;
            try { WhimTex.Tests.UnityA.UnityAScope.CloseOwned(unjournaled); }
            catch (System.InvalidOperationException) { rejected = true; }
            context.True(rejected, "Matching type and GUID-looking name are insufficient without exact native creation proof");
            context.True(unjournaled != null, "Unjournaled stand-in remains untouched by CloseOwned");

            WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window);
            context.True(window == null, "Never-shown renamed owned window destroyed through supported API");
            WhimTex.Tests.UnityA.UnityAScope.AssertJournalRetired(context, identity);
            // Scope disposal revisits the captured destroyed window safely (double-cleanup regression).
        });
        context.Equal(oldV2, UnityEditor.SessionState.GetString("WhimTex.Tests.UnityA.OwnedWindows.v2", ""), "Prior empty/malformed v2 journal is untouched");
    });
}
