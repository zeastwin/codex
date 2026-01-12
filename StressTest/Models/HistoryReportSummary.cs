using System;

namespace StressTest.Models
{
    public sealed class HistoryReportSummary
    {
        public string FolderName { get; set; }
        public string FolderPath { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int DurationSeconds { get; set; }
        public int DeviceCount { get; set; }
        public int RampUpSeconds { get; set; }
        public int ThinkTimeBaseMs { get; set; }
        public int ThinkTimeJitterMs { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessRequests { get; set; }
        public int FailedRequests { get; set; }
        public int CanceledRequests { get; set; }
        public double AvgLatencyMs { get; set; }
        public double P95LatencyMs { get; set; }
        public double AvgTtfbMs { get; set; }
        public double P95TtfbMs { get; set; }
        public double AvgRps { get; set; }
        public double ErrorRate { get; set; }

        public string DisplayName
        {
            get
            {
                if (StartTime.HasValue)
                    return StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                return FolderName ?? "未知时间";
            }
        }

        public string DetailText
        {
            get
            {
                var duration = DurationSeconds > 0 ? DurationSeconds + "s" : "-";
                var device = DeviceCount > 0 ? DeviceCount + " 台" : "-";
                var rps = AvgRps > 0 ? AvgRps.ToString("0.0") : "0.0";
                return $"时长 {duration} · 设备 {device} · 平均RPS {rps}";
            }
        }
    }
}
