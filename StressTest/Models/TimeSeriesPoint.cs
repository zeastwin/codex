namespace StressTest.Models
{
    public sealed class TimeSeriesPoint
    {
        public int Second { get; set; }
        public int Count { get; set; }
        public int Success { get; set; }
        public int Fail { get; set; }
        public int Canceled { get; set; }
        public double AvgLatencyMs { get; set; }
        public double P95LatencyMs { get; set; }
        public double AvgTtfbMs { get; set; }
        public double P95TtfbMs { get; set; }
        public double ErrorRate { get; set; }
    }
}
