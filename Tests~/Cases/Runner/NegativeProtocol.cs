// Deliberately failing entry points for testing the live runner, not domain regressions.
using System;
using UnityEditor;
using WhimTex.Tests;

public static class NegativeProtocolTests
{
    static string Key(string runId)
    {
        Guid.ParseExact(runId, "D");
        return "WhimTex.Tests.NegativeProtocol." + runId;
    }
    public static string InnerFailure() => "{\"success\":false,\"error\":\"deliberate inner API failure\"}";
    public static string AssertionFailure() => TestContext.Run("Deliberate assertion", c => c.True(false, "Expected failure"));
    public static string ExecutionFailure() => throw new InvalidOperationException("Deliberate execution failure");
    public static string NeedsArgument(string required) => TestContext.Run("Required argument", c => c.True(required != null, "Argument required"));
    public static string Skip() => TestContext.Result("skipped", 0, "Deliberate unavailable fixture").ToJson();
    public static string StartFailure(string runId)
    {
        SessionState.SetBool(Key(runId), true);
        return TestContext.Result("running", 0, "Owned polling fixture started").ToJson();
    }
    public static string PollFailure(string runId) => TestContext.Run("Deliberate async failure", c =>
    {
        c.True(SessionState.GetBool(Key(runId), false), "Session exists");
        c.True(false, "Expected polling assertion failure");
    });
    public static string Cancel(string runId)
    {
        SessionState.EraseBool(Key(runId)); // No callbacks or tasks exist in this fixture.
        return TestContext.Result("cancelled", 0, "Polling fixture cancelled").ToJson();
    }
    public static string Cleanup(string runId) => TestContext.Run("Owned polling state cleaned", c =>
    {
        SessionState.EraseBool(Key(runId));
        c.True(!SessionState.GetBool(Key(runId), false), "Key erased");
    });
    public static string CheckCleanup(string runId) => TestContext.Run("No session remains", c =>
        c.True(!SessionState.GetBool(Key(runId), false), "Polling state did not survive cleanup"));
}
