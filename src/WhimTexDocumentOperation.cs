using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexDocumentOperation : IDisposable
    {
        [ThreadStatic] private static WhimTexDocumentOperation current;
        private readonly WhimTexDocumentOperation previous;
        private readonly string title;
        private readonly Func<string, float, bool> cancel;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private string stage;
        private float fraction;
        private bool shown, committed;
        private long nextUpdate;

        internal WhimTexDocumentOperation(string title, Func<string, float, bool> cancel = null)
        {
            this.title = title;
            this.cancel = cancel;
            previous = current;
            current = this;
        }

        internal static void Report(string stage, float fraction)
        {
            if (current == null) return;
            current.stage = stage;
            current.fraction = fraction;
            current.Poll();
        }

        private void Poll()
        {
            if (committed) return;
            if (cancel != null)
            {
                if (cancel(stage, fraction)) throw new OperationCanceledException("WhimTex operation cancelled.");
                return;
            }
            if (clock.ElapsedMilliseconds < 400 || clock.ElapsedMilliseconds < nextUpdate) return;
            nextUpdate = clock.ElapsedMilliseconds + 80;
            shown = true;
            if (EditorUtility.DisplayCancelableProgressBar(title, stage, fraction))
                throw new OperationCanceledException("WhimTex operation cancelled.");
        }

        internal static void Run(Action work)
        {
            if (current == null) { work(); return; }
            var task = Task.Run(work);
            try
            {
                while (!task.IsCompleted)
                {
                    current.Poll();
                    // Wake as soon as work finishes; the timeout only bounds progress polling.
                    try { task.Wait(20); }
                    catch (AggregateException) { break; } // Unwrap the worker exception below.
                }
                task.GetAwaiter().GetResult();
            }
            catch
            {
                // Workers only own snapshots/bytes. Finish before callers dispose their buffers.
                try { task.GetAwaiter().GetResult(); } catch { }
                throw;
            }
        }

        internal static void Commit()
        {
            Report("Finishing file write", .94f);
            if (current == null) return;
            current.committed = true;
            if (current.shown) EditorUtility.DisplayProgressBar(current.title, "Importing saved texture", .96f);
        }

        public void Dispose()
        {
            current = previous;
            if (shown) EditorUtility.ClearProgressBar();
        }
    }
}
