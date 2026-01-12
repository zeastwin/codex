namespace StressTest.Models
{
    public sealed class AppConfigSnapshot
    {
        public string Url { get; set; }
        public string AutoKey { get; set; }

        public string WorkflowRunUrl
            => string.IsNullOrWhiteSpace(Url) ? string.Empty : Url.TrimEnd('/') + "/workflows/run";
    }
}
