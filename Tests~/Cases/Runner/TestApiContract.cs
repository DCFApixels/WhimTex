using System;
using UnityEngine;
using WhimTex.Tests;

public static class TestApiContractTests
{
    public static string Run() => TestContext.Run("Common C# assertion and result contract", context =>
    {
        TestResult Read(string json) => JsonUtility.FromJson<TestResult>(json);
        var passed = Read(TestContext.Run("Quoted \"message\"\nSecond line", inner => inner.Equal(2, 2, "equal")));
        context.Equal("passed", passed.status, "Passed status");
        context.Equal(1, passed.checks, "Passed checks");
        context.Equal("Quoted \"message\"\nSecond line", passed.message, "JSON preserves escaped text");
        context.Equal(0, passed.failures.Length, "Passed has no failures");
        var failed = Read(TestContext.Run("Failure", inner => inner.Equal(1, 2, "values")));
        context.Equal("failed", failed.status, "Assertion failure status");
        context.True(failed.failures[0].Contains("expected 1, got 2"), "Failure explains actual/expected");
        var cleanup = Read(TestContext.Run("Cleanup", inner => { try { inner.True(true, "body"); } finally { throw new Exception("cleanup failure"); } }));
        context.Equal("failed", cleanup.status, "Cleanup failure prevents pass");
        context.True(cleanup.failures[0].Contains("cleanup failure"), "Cleanup error retained");
        context.Equal("failed", Read(TestContext.Run("Empty", inner => { })).status, "Empty C# case fails");
        context.Equal("failed", Read(TestContext.Run("Infinite tolerance", inner => inner.Near(0, 1, double.PositiveInfinity, "near"))).status, "Non-finite tolerance fails");
        context.Equal("passed", Read(TestContext.Run("Tolerance", inner => inner.Near(1, 1.01, .02, "near"))).status, "Finite tolerance passes");
    });
}
