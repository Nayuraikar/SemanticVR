namespace SemanticVR.Perception
{
    /// <summary>
    /// Decides when a new inference may start: a minimum interval between starts, never while one is running.
    /// Between inferences the pipeline keeps serving the latest valid result.
    /// </summary>
    public sealed class InferenceScheduler
    {
        /// <summary>Minimum seconds between inference starts.</summary>
        public float IntervalSeconds = 0.5f;
        /// <summary>Main-thread milliseconds the backend may use per frame.</summary>
        public float StepBudgetMs = 4f;

        float lastStart = float.NegativeInfinity;

        /// <summary>True if an inference should start now.</summary>
        public bool ShouldStart(float now, bool backendBusy) => !backendBusy && now - lastStart >= IntervalSeconds;

        /// <summary>Records that an inference started at <paramref name="now"/>.</summary>
        public void MarkStarted(float now) => lastStart = now;

        /// <summary>Forces the next <see cref="ShouldStart"/> to return true.</summary>
        public void Reset() => lastStart = float.NegativeInfinity;
    }
}
