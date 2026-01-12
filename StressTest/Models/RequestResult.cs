namespace StressTest.Models
{
    public sealed class RequestResult
    {
        public bool Success { get; set; }
        public bool Canceled { get; set; }
        public int? StatusCode { get; set; }
        public string ErrorMessage { get; set; }
        public double DurationMs { get; set; }
        public double? TtfbMs { get; set; }
        public string WorkflowRunId { get; set; }
        public string TaskId { get; set; }
    }
}
