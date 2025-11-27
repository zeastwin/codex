using System;
using System.Collections.Generic;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 预警工单记录，用于闭环状态跟踪。
    /// </summary>
    public class WarningTicketRecord
    {
        public string Fingerprint { get; set; }
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string Level { get; set; }
        public string Type { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public string Summary { get; set; }
        public string MetricName { get; set; }
        public double CurrentValue { get; set; }
        public double? BaselineValue { get; set; }
        public double? ThresholdValue { get; set; }

        public string Status { get; set; }               // Active / Acknowledged / Ignored / Processed / Resolved
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? IgnoredUntil { get; set; }
        public int OccurrenceCount { get; set; }
    }
}
