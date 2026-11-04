using System;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private double previewTime, playbackStart, playbackOrigin, nextPlaybackTick;
        [NonSerialized] private bool isPlaying;
        [NonSerialized] private ulong timeRevision;
        [NonSerialized] private ShaderEvaluationContext evaluationContext;
        [NonSerialized] private bool previewEvaluation;
        [NonSerialized] private EffectRenderCache playbackDependencyCache;

        public double PreviewTime => previewTime;
        public bool IsPlaying => isPlaying;
        internal ulong TimeRevision => timeRevision;
        internal ShaderEvaluationContext EvaluationContext => evaluationContext;
        internal bool IsPreviewEvaluation => previewEvaluation;
        internal bool LayerPreviewUsesTime(Layer layer) => playbackDependencyCache?.DependsOnTime(this, layer) ?? true;

        public void SetPreviewTime(double seconds)
        {
            ValidatePreviewTime(seconds);
            SetTime(seconds);
            playbackOrigin = seconds;
            playbackStart = EditorApplication.timeSinceStartup;
            nextPlaybackTick = 0;
        }

        public void SetPlayback(bool playing)
        {
            if (isPlaying == playing) return;
            double now = EditorApplication.timeSinceStartup;
            if (!playing) AdvancePlayback(now);
            isPlaying = playing;
            playbackOrigin = previewTime;
            playbackStart = now;
            nextPlaybackTick = 0;
            timeRevision++;
        }

        internal void TickPlayback(double now)
        {
            if (!isPlaying || now < nextPlaybackTick) return;
            AdvancePlayback(now);
            nextPlaybackTick = now + 1d / 30d;
        }

        private void AdvancePlayback(double now) => SetTime(Math.Min(float.MaxValue,
            playbackOrigin + Math.Max(0, now - playbackStart)));

        private void SetTime(double seconds)
        {
            if (previewTime == seconds) return;
            previewTime = seconds;
            timeRevision++;
        }

        private static void ValidatePreviewTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Time must be finite, nonnegative and representable as a shader float.");
        }

        internal EvaluationScope EvaluateAtTime(double seconds)
        {
            ValidatePreviewTime(seconds);
            return new EvaluationScope(this, new ShaderEvaluationContext((float)seconds));
        }

        // One immutable input for the complete recursive render, including texture references.
        internal readonly struct EvaluationScope : IDisposable
        {
            private readonly WhimTexDocument document;
            private readonly ShaderEvaluationContext previous;
            private readonly bool previousPreview;
            internal EvaluationScope(WhimTexDocument document, ShaderEvaluationContext context)
            {
                this.document = document;
                previous = document.evaluationContext;
                previousPreview = document.previewEvaluation;
                document.evaluationContext = context;
                document.previewEvaluation = true;
            }
            public void Dispose()
            {
                document.evaluationContext = previous;
                document.previewEvaluation = previousPreview;
            }
        }
    }
}
