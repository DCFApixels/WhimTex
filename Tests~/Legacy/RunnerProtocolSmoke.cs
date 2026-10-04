// Opt-in runner fixtures; no assets, scenes, windows or production test bridge.
using System;
using UnityEditor;

public static class RunnerProtocolSmoke
{
    const string Key = "WhimTex.RunnerProtocol.7a31b98595d945d0b7ecb812e5f579f1.";
    public static string Pass() => "PASS: runner protocol fixture";
    public static string NeedsArgument(int value) => "PASS: runner protocol fixture";
    public static object InnerFailure() => new { success = false, error = "Intentional inner failure" };
    public static string AssertionFailure() => throw new InvalidOperationException("Intentional assertion failure");
    public static string Skip() => "SKIP: intentional fixture";
    public static string Start()
    {
        SessionState.SetInt(Key + "polls", 0);
        SessionState.SetBool(Key + "failure", false);
        return "Started; call Result.";
    }
    public static string StartFailure()
    {
        Start(); SessionState.SetBool(Key + "failure", true);
        return "Started; call Result.";
    }
    public static string Result()
    {
        int polls = SessionState.GetInt(Key + "polls", -1);
        if (polls < 0) return "Not started";
        SessionState.SetInt(Key + "polls", polls + 1);
        if (polls == 0) return "Running";
        return SessionState.GetBool(Key + "failure", false)
            ? "FAILED: intentional async failure" : "Passed: runner async fixture";
    }
    public static string Cleanup()
    {
        SessionState.EraseInt(Key + "polls"); SessionState.EraseBool(Key + "failure");
        return "PASS: runner fixture cleanup";
    }
    public static string CheckCleanup() => SessionState.GetInt(Key + "polls", -1) == -1
        ? "PASS: runner fixture clean" : "FAILED: runner fixture leaked state";
}
