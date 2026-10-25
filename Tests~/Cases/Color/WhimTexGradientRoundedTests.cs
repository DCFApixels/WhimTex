using WhimTex.Tests;

// Retained entry points identify retired checks explicitly; they must never report a pass.
public static class WhimTexGradientRoundedTests
{
    private static string Retired() => TestContext.Result("skipped", 0,
        "Retired by F30: Rounded domain-remap and its captured curves are no longer the gradient contract. No replacement assertions or captures were created.").ToJson();

    public static string Run() => Retired();
    public static string Capture(string runId) => Retired();
    public static string CleanupCapture(string runId) => Retired();
}
