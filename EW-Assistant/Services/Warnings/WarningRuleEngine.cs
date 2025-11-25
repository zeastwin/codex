using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 基于本地 CSV 的简单预警规则引擎。
    /// </summary>
    public class WarningRuleEngine
    {
        private const double YieldThreshold = 0.97d;
        private const int YieldMinSamples = 30;
        private const double ThroughputPlanRatio = 0.9d;
        private const int DefaultHourlyPlan = 100;
        private const int AlarmCountThreshold = 5;
        private const double AlarmDowntimeMinutesThreshold = 10d;

        private readonly ProductionCsvReader _prodReader;
        private readonly AlarmCsvReader _alarmReader;

        public WarningRuleEngine(ProductionCsvReader prodReader, AlarmCsvReader alarmReader)
        {
            _prodReader = prodReader ?? throw new ArgumentNullException(nameof(prodReader));
            _alarmReader = alarmReader ?? throw new ArgumentNullException(nameof(alarmReader));
        }

        public IList<WarningItem> BuildWarnings(DateTime now)
        {
            var bucket = new Dictionary<string, WarningItem>();
            var windowStart = now.AddHours(-24);

            var productions = _prodReader.GetLast24HoursProduction(now) ?? new List<ProductionHourRecord>();
            foreach (var rec in productions)
            {
                if (rec.Hour < windowStart || rec.Hour >= now) continue;

                ApplyYieldRule(bucket, rec);
                ApplyThroughputRule(bucket, rec);
            }

            var alarms = _alarmReader.GetLast24HoursAlarms(now) ?? new List<AlarmHourStat>();
            foreach (var stat in alarms)
            {
                if (stat.Hour < windowStart || stat.Hour >= now) continue;
                ApplyAlarmRule(bucket, stat);
            }

            return bucket.Values
                .OrderBy(w => w.StartTime)
                .ThenBy(w => w.RuleId)
                .ToList();
        }

        private void ApplyYieldRule(IDictionary<string, WarningItem> bucket, ProductionHourRecord rec)
        {
            if (rec.Total < YieldMinSamples) return;
            var yield = rec.Yield;
            if (yield >= YieldThreshold) return;

            var start = rec.Hour;
            var end = start.AddHours(1);
            var summary = $"{start:HH}:00-{end:HH}:00 良率 {ToPercent(yield)}，低于阈值 {ToPercent(YieldThreshold)}。";

            var item = new WarningItem
            {
                Key = FormatKey("LOW_YIELD_ABS", start),
                RuleId = "LOW_YIELD_ABS",
                RuleName = "低良率绝对阈值 < 97%",
                Level = "Critical",
                Type = "Yield",
                StartTime = start,
                EndTime = end,
                FirstDetected = start,
                LastDetected = start,
                MetricName = "良率",
                CurrentValue = yield,
                ThresholdValue = YieldThreshold,
                Status = "Active",
                Summary = summary
            };

            Upsert(bucket, item, (oldVal, newVal) => Math.Min(oldVal, newVal));
        }

        private void ApplyThroughputRule(IDictionary<string, WarningItem> bucket, ProductionHourRecord rec)
        {
            var plan = rec.PlannedOutput ?? DefaultHourlyPlan;
            if (plan <= 0) return;

            var threshold = plan * ThroughputPlanRatio;
            var actual = rec.Total;
            if (actual >= threshold) return;

            var start = rec.Hour;
            var end = start.AddHours(1);
            var summary = $"{start:HH}:00-{end:HH}:00 产量 {actual}，低于计划 {plan} 的 90%（阈值 {threshold:F0}）。";

            var item = new WarningItem
            {
                Key = FormatKey("LOW_THROUGHPUT_PLAN", start),
                RuleId = "LOW_THROUGHPUT_PLAN",
                RuleName = "产量低于计划 90%",
                Level = "Warning",
                Type = "Throughput",
                StartTime = start,
                EndTime = end,
                FirstDetected = start,
                LastDetected = start,
                MetricName = "产量",
                CurrentValue = actual,
                BaselineValue = plan,
                ThresholdValue = threshold,
                Status = "Active",
                Summary = summary
            };

            Upsert(bucket, item, (oldVal, newVal) => Math.Min(oldVal, newVal));
        }

        private void ApplyAlarmRule(IDictionary<string, WarningItem> bucket, AlarmHourStat stat)
        {
            var hitCount = stat.Count >= AlarmCountThreshold;
            var hitDowntime = stat.DowntimeMinutes >= AlarmDowntimeMinutesThreshold;
            if (!hitCount && !hitDowntime) return;

            var start = stat.Hour;
            var end = start.AddHours(1);
            var ruleId = $"ALARM_FREQUENT_{(string.IsNullOrWhiteSpace(stat.Code) ? "UNKNOWN" : stat.Code)}";
            var metricName = hitDowntime && stat.DowntimeMinutes >= stat.Count ? "停机分钟" : "报警次数";
            var currentValue = hitDowntime ? stat.DowntimeMinutes : stat.Count;
            var threshold = hitDowntime ? AlarmDowntimeMinutesThreshold : AlarmCountThreshold;

            var summary = $"{start:HH}:00-{end:HH}:00 报警 {stat.Code ?? "UNKNOWN"} 次数 {stat.Count}，停机 {stat.DowntimeMinutes:F1} 分钟。";

            var item = new WarningItem
            {
                Key = FormatKey(ruleId, start),
                RuleId = ruleId,
                RuleName = "报警高频或停机",
                Level = "Warning",
                Type = "Alarm",
                StartTime = start,
                EndTime = end,
                FirstDetected = start,
                LastDetected = start,
                MetricName = metricName,
                CurrentValue = currentValue,
                ThresholdValue = threshold,
                Status = "Active",
                Summary = summary
            };

            Upsert(bucket, item, (oldVal, newVal) => Math.Max(oldVal, newVal));
        }

        private static void Upsert(IDictionary<string, WarningItem> bucket, WarningItem incoming, Func<double, double, double> pickCurrent)
        {
            if (!bucket.TryGetValue(incoming.Key, out var existing))
            {
                bucket[incoming.Key] = incoming;
                return;
            }

            existing.FirstDetected = incoming.FirstDetected < existing.FirstDetected ? incoming.FirstDetected : existing.FirstDetected;
            existing.LastDetected = incoming.LastDetected > existing.LastDetected ? incoming.LastDetected : existing.LastDetected;

            var newValue = pickCurrent(existing.CurrentValue, incoming.CurrentValue);
            if (Math.Abs(newValue - existing.CurrentValue) > double.Epsilon)
            {
                existing.CurrentValue = newValue;
                existing.Summary = incoming.Summary;
            }

            if (incoming.ThresholdValue.HasValue)
            {
                existing.ThresholdValue = incoming.ThresholdValue;
            }
            if (incoming.BaselineValue.HasValue)
            {
                existing.BaselineValue = incoming.BaselineValue;
            }
        }

        private static string FormatKey(string ruleId, DateTime startTime)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:yyyyMMddHH}", ruleId, startTime);
        }

        private static string ToPercent(double value)
        {
            return $"{value * 100:F1}%";
        }
    }
}
