// Test-only support source, appended to a reviewed case by the runner. No production assembly.
namespace WhimTex.Tests
{
    [System.Serializable]
    public sealed class TestResult
    {
        public string status;
        public int checks;
        public string message;
        public string[] failures;
        public string ToJson() => UnityEngine.JsonUtility.ToJson(this);
    }

    public sealed class TestContext
    {
        public int Checks { get; private set; }
        public void True(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new System.InvalidOperationException(message);
        }
        public void Equal<T>(T expected, T actual, string message)
            => True(System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual),
                message + ": expected " + expected + ", got " + actual);
        public void Near(double expected, double actual, double tolerance, string message)
            => True(!double.IsNaN(expected) && !double.IsInfinity(expected) && !double.IsNaN(actual)
                && !double.IsInfinity(actual) && !double.IsInfinity(tolerance) && tolerance >= 0
                && System.Math.Abs(expected - actual) <= tolerance,
                message + ": expected " + expected + ", got " + actual + ", tolerance " + tolerance);
        public static string Run(string message, System.Action<TestContext> body)
        {
            var context = new TestContext();
            try
            {
                body(context); // The body's using/finally completes before a passed result is emitted.
                if (context.Checks == 0) throw new System.InvalidOperationException("No assertions were executed.");
                return Result("passed", context.Checks, message).ToJson();
            }
            catch (System.Exception error)
            {
                return Result("failed", context.Checks, message, error.ToString()).ToJson();
            }
        }
        public static TestResult Result(string status, int checks, string message, params string[] failures)
            => new TestResult { status = status, checks = checks, message = message, failures = failures };
    }
}
