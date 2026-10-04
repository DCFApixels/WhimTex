using System;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

// Protocol fixture only: Poll advances state; no detached task or scheduled callback.
public static class AsyncProtocolTests
{
    static string Key(string runId)
    {
        if (!Guid.TryParseExact(runId, "D", out _)) throw new ArgumentException("Expected per-run GUID");
        return "WhimTex.Tests.AsyncProtocol." + runId + ".";
    }
    static string Reply(string status, int checks, string message, params string[] failures)
        => TestContext.Result(status, checks, message, failures).ToJson();
    public static string Start(string runId)
    {
        string key = Key(runId);
        if (SessionState.GetInt(key + "polls", -1) != -1) return Reply("failed", 0, "Already started", "Refusing to replace existing run state");
        SessionState.SetInt(key + "polls", 0);
        return Reply("running", 0, "Started");
    }
    public static string Poll(string runId)
    {
        string key = Key(runId);
        int polls = SessionState.GetInt(key + "polls", -1);
        if (polls < 0) return Reply("failed", 0, "Not started", "No state for this run");
        if (SessionState.GetBool(key + "cancelled", false)) return Reply("cancelled", 0, "Cancelled");
        SessionState.SetInt(key + "polls", polls + 1);
        return polls == 0 ? Reply("running", 0, "First poll")
            : TestContext.Run("Polling reached terminal state", context =>
                context.True(SessionState.GetInt(key + "polls", -1) >= 2, "Both polling steps executed"));
    }
    public static string Cancel(string runId)
    {
        SessionState.SetBool(Key(runId) + "cancelled", true);
        return Reply("cancelled", 0, "No detached work; fixture cancelled");
    }
    public static string Cleanup(string runId)
    {
        string key = Key(runId);
        SessionState.EraseInt(key + "polls"); SessionState.EraseBool(key + "cancelled");
        return TestContext.Run("Own run state erased", context =>
            context.True(SessionState.GetInt(key + "polls", -1) == -1 &&
                !SessionState.GetBool(key + "cancelled", false), "Both GUID state fields removed"));
    }
    public static string Lifecycle() => TestContext.Run("Async start/poll/cancel/cleanup lifecycle", context =>
    {
        string runId = Guid.NewGuid().ToString("D");
        string Status(string json) => JsonUtility.FromJson<TestResult>(json).status;
        try
        {
            context.Equal("running", Status(Start(runId)), "Start");
            context.Equal("failed", Status(Start(runId)), "Duplicate start rejected");
            context.Equal("running", Status(Poll(runId)), "First poll is pending");
            context.Equal("cancelled", Status(Cancel(runId)), "Cancel acknowledged");
            context.Equal("cancelled", Status(Poll(runId)), "Cancelled cannot turn into a pass");
            context.Equal("passed", Status(Cleanup(runId)), "Cleanup");
            context.Equal("failed", Status(Poll(runId)), "Cleanup removed this run's state");
            context.Equal("passed", Status(Cleanup(runId)), "Repeated own cleanup is harmless");
        }
        finally { Cleanup(runId); }
    });
}
