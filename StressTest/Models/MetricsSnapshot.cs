using System;
using System.Collections.Generic;

namespace StressTest.Models
{
    public sealed class MetricsSnapshot
    {
        public DateTime StartTimeUtc { get; set; }
        public DateTime? EndTimeUtc { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessRequests { get; set; }
        public int FailedRequests { get; set; }
        public int CanceledRequests { get; set; }
        public int Inflight { get; set; }
        public double AvgLatencyMs { get; set; }
        public double P95LatencyMs { get; set; }
        public double AvgTtfbMs { get; set; }
        public double P95TtfbMs { get; set; }
        public double AvgRps { get; set; }
        public double CurrentRps { get; set; }
        public double ErrorRate { get; set; }
        public IReadOnlyList<TimeSeriesPoint> Series { get; set; }
        public IReadOnlyDictionary<string, int> ErrorTop { get; set; }
    }
}
