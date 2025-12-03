using System;
using System.Globalization;
using System.Linq;
using System.Text;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    public static class WeeklyAlarmReportMarkdownFormatter
    {
        public static string Render(WeeklyAlarmData data, string analysisMarkdown)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 报警周报（" + data.StartDate.ToString("yyyy-MM-dd") + " ~ " + data.EndDate.ToString("yyyy-MM-dd") + "）");
            sb.AppendLine();
            AppendKpi(sb, data);
            AppendDaily(sb, data);
            AppendTop(sb, data);

            if (!string.IsNullOrWhiteSpace(analysisMarkdown))
            {
                sb.AppendLine();
                sb.AppendLine(analysisMarkdown.Trim());
            }

            if (data.Warnings != null && data.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 异常/缺失");
                foreach (var w in data.Warnings) sb.AppendLine("- " + w);
            }

            return sb.ToString();
        }

        private static void AppendKpi(StringBuilder sb, WeeklyAlarmData data)
        {
            sb.AppendLine("## 周度KPI");
            sb.AppendLine("| 指标 | 数值 | 说明 |");
            sb.AppendLine("|---|---:|---|");
            sb.AppendLine("| 报警次数 | " + FormatInt(data.TotalCount) + " | 一周内记录到的报警总次数 |");
            sb.AppendLine("| 报警总时长 | " + FormatSeconds(data.TotalDurationSeconds) + " | 一周内报警累计时长 |");
            sb.AppendLine("| 平均单次时长 | " + FormatSeconds(data.TotalCount > 0 ? data.TotalDurationSeconds / data.TotalCount : 0d) + " | 每次报警平均持续时间 |");
            sb.AppendLine("| 覆盖小时数 | " + data.ActiveHours + "/168 | 本周有报警发生的小时数 |");
            sb.AppendLine();
        }

        private static void AppendDaily(StringBuilder sb, WeeklyAlarmData data)
        {
            sb.AppendLine("## 日度趋势");
            sb.AppendLine("| 日期 | 报警次数 | 报警时长 | Top 报警（概括） |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var d in data.ByDay.OrderBy(x => x.Date))
            {
                sb.Append("| " + d.Date.ToString("yyyy-MM-dd") + " | ");
                sb.Append(FormatInt(d.AlarmCount) + " | ");
                sb.Append(FormatSeconds(d.AlarmSeconds) + " | ");
                sb.Append((string.IsNullOrWhiteSpace(d.TopAlarm) ? "无" : d.TopAlarm) + " |");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private static void AppendTop(StringBuilder sb, WeeklyAlarmData data)
        {
            sb.AppendLine("## Top 报警");
            sb.AppendLine("| 代码 | 内容 | 次数 | 时长 |");
            sb.AppendLine("|---|---|---:|---:|");
            foreach (var t in data.Top)
            {
                sb.Append("| " + t.Code + " | " + (string.IsNullOrWhiteSpace(t.Content) ? "无" : t.Content) + " | ");
                sb.Append(FormatInt(t.Count) + " | " + FormatSeconds(t.DurationSeconds) + " |");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private static string FormatInt(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

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
