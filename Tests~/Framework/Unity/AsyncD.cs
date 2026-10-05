// Test-only async host: BCL state survives separately compiled Pipeline invocations.
// It does not survive domain reload; missing state is a failure, never a pass.
namespace WhimTex.Tests.UnityD
{
    public static class AsyncD
    {
        // Ambient only inside the original task. Poll/Cancel/Cleanup use the persisted
        // BCL bridge, never statics from a separately compiled Pipeline assembly.
        static readonly System.Threading.AsyncLocal<object[]> executing = new System.Threading.AsyncLocal<object[]>();
        static System.Collections.Generic.List<string> CleanupErrors(object[] state)
            => (System.Collections.Generic.List<string>)state[2];
        public static void CleanupOwned(params System.Action[] actions)
        {
            var errors = new System.Collections.Generic.List<System.Exception>();
            foreach (var action in actions)
                try { action(); } catch (System.Exception error) { errors.Add(error); }
            if (errors.Count == 0) return;
            var aggregate = new System.AggregateException("Owned resource cleanup/restoration failed", errors);
            if (executing.Value == null) throw aggregate;
            // Do not throw from finally and overwrite a primary assertion exception.
            CleanupErrors(executing.Value).Add(aggregate.ToString());
        }
        sealed class CleanupScope : System.IDisposable
        {
            System.IDisposable owned;
            public CleanupScope(System.IDisposable owned) { this.owned = owned; }
            public void Dispose()
            {
                var value = owned; owned = null;
                if (value != null) CleanupOwned(value.Dispose);
            }
        }
        public static System.IDisposable OwnCleanup(System.IDisposable owned) => new CleanupScope(owned);
        static string Key(string runId)
        {
            if (!System.Guid.TryParseExact(runId, "D", out _)) throw new System.ArgumentException("Expected per-run GUID");
            return "WhimTex.Tests.UnityD." + runId;
        }
        static object[] State(string runId) => System.AppDomain.CurrentDomain.GetData(Key(runId)) as object[];
        public static string Start(string runId, System.Func<WhimTex.Tests.TestContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> body)
        {
            if (State(runId) != null) return WhimTex.Tests.TestContext.Result("failed", 0, "Duplicate run", "Existing owned work was not replaced").ToJson();
            var stop = new System.Threading.CancellationTokenSource();
            // Only BCL types cross assembly boundaries; no cast to ephemeral test types.
            var state = new object[] { stop, null, new System.Collections.Generic.List<string>(), false };
            System.AppDomain.CurrentDomain.SetData(Key(runId), state);
            state[1] = Execute(body, stop.Token, state);
            return Poll(runId);
        }
        static async System.Threading.Tasks.Task<string> Execute(System.Func<WhimTex.Tests.TestContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> body, System.Threading.CancellationToken token, object[] state)
        {
            var context = new WhimTex.Tests.TestContext();
            MigrationD fixture = null;
            System.Exception primaryError = null;
            bool cancelled = false;
            executing.Value = state;
            try
            {
                fixture = new MigrationD();
                await body(context, token);
                token.ThrowIfCancellationRequested();
                if (context.Checks == 0) throw new System.InvalidOperationException("No assertions were executed.");
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            { cancelled = true; }
            catch (System.Exception error)
            { primaryError = error; }
            finally
            {
                if (fixture != null) CleanupOwned(fixture.Dispose);
                state[3] = true;
                executing.Value = null;
            }
            var failures = new System.Collections.Generic.List<string>();
            if (primaryError != null) failures.Add(primaryError.ToString());
            failures.AddRange(CleanupErrors(state));
            if (failures.Count > 0)
                return WhimTex.Tests.TestContext.Result("failed", context.Checks, "UI test failed", failures.ToArray()).ToJson();
            return WhimTex.Tests.TestContext.Result(cancelled ? "cancelled" : "passed", context.Checks,
                cancelled ? "Owned task stopped and finally completed" : "UI assertions and owned cleanup completed").ToJson();
        }
        public static string Poll(string runId)
        {
            var state = State(runId);
            if (state == null) return WhimTex.Tests.TestContext.Result("failed", 0, "Missing run state", "Domain reload or unknown GUID").ToJson();
            var task = (System.Threading.Tasks.Task<string>)state[1];
            return task.IsCompleted ? task.GetAwaiter().GetResult() : WhimTex.Tests.TestContext.Result("running", 0, "Owned UI work is running").ToJson();
        }
        public static async System.Threading.Tasks.Task<string> Cancel(string runId)
        {
            var state = State(runId);
            if (state == null) return WhimTex.Tests.TestContext.Result("failed", 0, "Missing run state", "Cannot prove work stopped").ToJson();
            System.Exception cancellationError = null;
            try { ((System.Threading.CancellationTokenSource)state[0]).Cancel(); }
            catch (System.Exception error) { cancellationError = error; }
            // Cancellation acknowledgement is emitted only AFTER the task's finally.
            string json = await (System.Threading.Tasks.Task<string>)state[1];
            if (cancellationError != null) CleanupErrors(state).Add(cancellationError.ToString());
            var result = UnityEngine.JsonUtility.FromJson<WhimTex.Tests.TestResult>(json);
            if (result.status == "failed") return json;
            if (cancellationError != null) return WhimTex.Tests.TestContext.Result("failed", result.checks,
                "Cancellation callback failed after owned task drained", cancellationError.ToString()).ToJson();
            return WhimTex.Tests.TestContext.Result("cancelled", result.checks, "Owned task is terminal and cleanup finished").ToJson();
        }
        public static async System.Threading.Tasks.Task<string> Cleanup(string runId)
        {
            var state = State(runId);
            if (state == null) return WhimTex.Tests.TestContext.Result("failed", 0, "Missing run state", "Cannot prove cleanup").ToJson();
            var task = (System.Threading.Tasks.Task<string>)state[1];
            try
            {
                if (!task.IsCompleted)
                    try { ((System.Threading.CancellationTokenSource)state[0]).Cancel(); }
                    catch (System.Exception error) { CleanupErrors(state).Add(error.ToString()); }
                try { await task; } // Primary verdict stays with Poll/the runner, not the cleanup phase.
                catch (System.Exception error)
                {
                    if (!(bool)state[3]) CleanupErrors(state).Add("Owned task did not finish cleanup: " + error);
                    // A terminal primary failure cannot turn proven cleanup into cleanup-failed.
                }
            }
            finally
            {
                try { ((System.Threading.CancellationTokenSource)state[0]).Dispose(); }
                catch (System.Exception error) { CleanupErrors(state).Add(error.ToString()); }
                try { System.AppDomain.CurrentDomain.SetData(Key(runId), null); }
                catch (System.Exception error) { CleanupErrors(state).Add(error.ToString()); }
            }
            if (!(bool)state[3]) CleanupErrors(state).Add("Owned task cleanup/restoration did not finish");
            if (CleanupErrors(state).Count > 0)
                return WhimTex.Tests.TestContext.Result("failed", 0, "Owned cleanup/restoration failed; bridge release attempted",
                    CleanupErrors(state).ToArray()).ToJson();
            return WhimTex.Tests.TestContext.Run("Owned async state removed after task completion", c =>
                c.True(task.IsCompleted && System.AppDomain.CurrentDomain.GetData(Key(runId)) == null,
                    "Task drained and GUID state erased; primary assertion verdict unchanged"));
        }
        public static async System.Threading.Tasks.Task Tick(UnityEngine.UIElements.VisualElement element, System.Threading.CancellationToken token)
        {
            var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var item = element.schedule.Execute(() => completion.TrySetResult(true)).StartingIn(50);
            using (token.Register(() => { try { item.Pause(); } finally { completion.TrySetCanceled(); } }))
                try { await completion.Task; }
                finally { CleanupOwned(item.Pause); }
        }
    }
}
