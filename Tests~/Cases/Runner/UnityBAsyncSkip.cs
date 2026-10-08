using System;
using System.Threading.Tasks;
using UnityEngine;
using WhimTex.Tests;

public static class UnityBAsyncSkipTests
{
    public static async Task<string> Run(string runId)
    {
        var context = new TestContext();
        bool owns = false;
        Exception primary = null;
        try
        {
            UnityBRun.Start(runId, "Owned skip protocol fixture", async () => {
                await UnityBRun.Delay(30);
                throw new UnityBSkipException("Deliberate missing prerequisite; not a failed assertion.");
            });
            owns = true;
            TestResult result;
            var deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                await Task.Delay(20);
                result = JsonUtility.FromJson<TestResult>(UnityBRun.Poll(runId));
            } while (result.status == "running" && DateTime.UtcNow < deadline);
            context.Equal("skipped", result.status, "Async prerequisite skip stays a SKIP");
            context.Equal(0, result.failures.Length, "Skip does not contain failure records");
            context.True(result.message.Contains("Deliberate missing prerequisite"), "Skip preserves its reason");
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (owns)
            {
                try
                {
                    var cleanup = JsonUtility.FromJson<TestResult>(UnityBRun.Cleanup(runId));
                    context.Equal("passed", cleanup.status, "Known completed skip releases its bridge");
                    var after = JsonUtility.FromJson<TestResult>(UnityBRun.Poll(runId));
                    context.Equal("failed", after.status, "A disposed GUID cannot be polled as a live owner");
                }
                catch (Exception error) { primary = primary == null ? error : new AggregateException(primary, error); }
            }
        }
        return TestContext.Result(primary == null ? "passed" : "failed", context.Checks,
            "Native asynchronous SKIP protocol and cleanup", primary == null ? Array.Empty<string>() : new[] { primary.ToString() }).ToJson();
    }
}
