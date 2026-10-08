using WhimTex.Tests;
using UnityEngine;

public static class EntryContractTests
{
    static int calls;
    public static class Fixture
    {
        public static string Unique() { calls++; return null; }
        public static string Overloaded() { calls++; return null; }
        private static string Overloaded(int value) { calls++; return null; }
        public static string Required(string value) { calls++; return null; }
        public static string Optional(string value = "default") { calls++; return null; }
        private static string Hidden() { calls++; return null; }
        public static string Generic<T>() { calls++; return null; }
    }
    public static string Run() => TestContext.Run("Entry binding validation does not execute test bodies", context =>
    {
        calls = 0;
        string Status(string name, int arguments)
        {
            string result = EntryContract.Verify(typeof(Fixture).FullName + "." + name + "|" + arguments);
            var parsed = JsonUtility.FromJson<TestResult>(result);
            if (name == "Unique" && arguments == 0 && parsed.status != "passed")
                throw new System.InvalidOperationException(result);
            return parsed.status;
        }
        context.Equal("passed", Status("Unique", 0), "Unambiguous public entry");
        context.Equal("failed", Status("Overloaded", 0), "Private overload is also ambiguous in Pipeline");
        context.Equal("failed", Status("Missing", 0), "Unknown method");
        context.Equal("failed", Status("Unique", 1), "Extra argument rejected");
        context.Equal("failed", Status("Required", 0), "Missing required argument rejected");
        context.Equal("passed", Status("Required", 1), "Required argument supplied");
        context.Equal("passed", Status("Optional", 0), "Optional argument may be omitted");
        context.Equal("failed", Status("Hidden", 0), "Non-public entry rejected");
        context.Equal("failed", Status("Generic", 0), "Open generic entry rejected");
        context.Equal(0, calls, "No fixture method body was executed");
    });
}
