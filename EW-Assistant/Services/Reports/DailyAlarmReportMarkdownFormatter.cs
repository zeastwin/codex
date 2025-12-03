using System;
using System.Globalization;
using System.Linq;
using System.Text;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    public static class DailyAlarmReportMarkdownFormatter
    {
        public static string Render(DailyAlarmData data, string analysisMarkdown)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 报警日报（" + data.Date.ToString("yyyy-MM-dd") + "）");
            sb.AppendLine();
            AppendKpi(sb, data);
            AppendHourly(sb, data);

            if (!string.IsNullOrWhiteSpace(analysisMarkdown))
            {
                sb.AppendLine();
                sb.AppendLine(analysisMarkdown.Trim());
            }

            if (data.Warnings != null && data.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 注意事项");
                foreach (var w in data.Warnings) sb.AppendLine("- " + w);
            }

            return sb.ToString();
        }

        private static void AppendKpi(StringBuilder sb, DailyAlarmData data)
        {
            sb.AppendLine("## 概览指标");
            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("|:---|---:|");
            sb.AppendLine("| 报警条数 | " + data.DayAlarmCount.ToString("N0", CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| 报警总时长 | " + FormatSeconds(data.DayAlarmSeconds) + " |");
            sb.AppendLine("| 平均单次(秒) | " + data.AvgPerAlarmSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " |");
            sb.AppendLine("| 活跃小时 | " + data.ActiveHours + "/24 |");
            sb.AppendLine("| 峰值小时 | " + (data.PeakHour != null ? HourRange(data.PeakHour.Hour) + "（" + FormatSeconds(data.PeakHour.Seconds) + "）" : "无") + " |");
            sb.AppendLine();
        }

        private static void AppendHourly(StringBuilder sb, DailyAlarmData data)
        {
            sb.AppendLine("## 逐小时明细");
            sb.AppendLine("| 时段 | 条数 | 时长 | 平均(s) | Top代码 | Top内容 | 标签 |");
            sb.AppendLine("|:---|---:|---:|---:|:---|:---|:---|");

            foreach (var h in data.Hours.OrderBy(x => x.Hour))
            {
                var tags = (h.Tags != null && h.Tags.Count > 0) ? string.Join(" / ", h.Tags) : string.Empty;
                sb.Append("| " + HourRange(h.Hour) + " | ");
                sb.Append(h.AlarmCount.ToString("N0", CultureInfo.InvariantCulture) + " | ");
                sb.Append(FormatSeconds(h.AlarmSeconds) + " | ");
                sb.Append(h.AvgSeconds().ToString("0.0", CultureInfo.InvariantCulture) + " | ");
                sb.Append((h.TopAlarmCode ?? "无") + " | ");
                sb.Append((string.IsNullOrWhiteSpace(h.TopAlarmContent) ? "无" : h.TopAlarmContent) + " | ");
                sb.Append(tags + " |");
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static string HourRange(int h)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:00–{1:00}:00", h, (h + 1) % 24);
        }

        private static string FormatSeconds(double seconds)
        {
            var mins = seconds / 60d;
            if (mins >= 60)
            {
                return (mins / 60d).ToString("0.0", CultureInfo.InvariantCulture) + "h";
            }
            return mins.ToString("0.0", CultureInfo.InvariantCulture) + "m";
        }
    }
}
