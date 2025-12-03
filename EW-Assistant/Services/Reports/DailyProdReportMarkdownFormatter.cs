using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 将 DailyProdData + LLM 分析 Markdown 合成为完整报表。
    /// </summary>
    public static class DailyProdReportMarkdownFormatter
    {
        public static string Render(DailyProdData data, string analysisMarkdown)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 今日产能日报（" + data.Date.ToString("yyyy-MM-dd") + "）");
            sb.AppendLine();

            AppendKpiTable(sb, data);
            AppendDowntimeAndPeaks(sb, data);
            AppendHourlyTable(sb, data);

            if (!string.IsNullOrWhiteSpace(analysisMarkdown))
            {
                sb.AppendLine();
                sb.AppendLine(analysisMarkdown.Trim());
            }

            if (data.Warnings != null && data.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 注意事项");
                foreach (var w in data.Warnings)
                {
                    sb.AppendLine("- " + w);
                }
            }

            return sb.ToString();
        }

        private static void AppendKpiTable(StringBuilder sb, DailyProdData data)
        {
            sb.AppendLine("## 概览指标");
            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine("| PASS（良品） | " + FormatInt(data.DayPass) + " |");
            sb.AppendLine("| FAIL（不良） | " + FormatInt(data.DayFail) + " |");
            sb.AppendLine("| 总产量 | " + FormatInt(data.DayTotal) + " |");
            sb.AppendLine("| **良率** | **" + FormatPercent(data.DayYield) + "** |");
            sb.AppendLine("| **活跃小时** | **" + data.ActiveHours + " 小时** |");
            sb.AppendLine("| **近似稼动率** | **" + FormatPercent(data.ActiveRate) + "** |");

            var peakText = BuildPeakValleyText(data.PeakHours);
            var valleyText = BuildPeakValleyText(data.ValleyHours);
            var downtimeText = BuildDowntimeText(data.Downtimes);

            sb.AppendLine("| 峰值小时 | " + (string.IsNullOrWhiteSpace(peakText) ? "无" : peakText) + " |");
            sb.AppendLine("| 低谷小时 | " + (string.IsNullOrWhiteSpace(valleyText) ? "无" : valleyText) + " |");
            sb.AppendLine("| 最长停机窗口 | " + (string.IsNullOrWhiteSpace(downtimeText) ? "无" : downtimeText) + " |");
            sb.AppendLine("| 波动指数（CV） | " + FormatPercent(data.Cv) + " |");
            sb.AppendLine();
        }

        private static void AppendDowntimeAndPeaks(StringBuilder sb, DailyProdData data)
        {
            if (data.Downtimes != null && data.Downtimes.Count > 0)
            {
                sb.AppendLine("> 停机窗口：" + BuildDowntimeText(data.Downtimes));
                sb.AppendLine();
            }
        }

        private static void AppendHourlyTable(StringBuilder sb, DailyProdData data)
        {
            sb.AppendLine("## 逐小时明细");
            sb.AppendLine("| 时段 | PASS | FAIL | 总量 | 良率(%) | 标签 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");

            foreach (var h in data.Hours.OrderBy(x => x.Hour))
            {
                var time = string.Format(CultureInfo.InvariantCulture, "{0:00}:00–{1:00}:00", h.Hour, (h.Hour + 1) % 24);
                var tags = (h.Tags != null && h.Tags.Count > 0) ? string.Join(" / ", h.Tags) : string.Empty;
                sb.Append("| " + time + " | ");
                sb.Append(FormatInt(h.Pass) + " | ");
                sb.Append(FormatInt(h.Fail) + " | ");
                sb.Append(FormatInt(h.Total) + " | ");
                sb.Append(FormatPercent(h.Yield) + " | ");
                sb.Append(tags + " |");
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static string BuildPeakValleyText(IList<DailyProdHourStat> list)
        {
            if (list == null || list.Count == 0) return string.Empty;
            return string.Join("；", list.Select(h =>
                string.Format(CultureInfo.InvariantCulture, "{0:00}:00（{1}件）", h.Hour, FormatInt(h.Total))));
        }

        private static string BuildDowntimeText(IList<DowntimeWindow> windows)
        {
            if (windows == null || windows.Count == 0) return string.Empty;
            var win = windows.OrderByDescending(w => w.DurationHours).ThenBy(w => w.StartHour).First();
            return string.Format(CultureInfo.InvariantCulture,
                "{0:00}:00–{1:00}:00，{2} 小时", win.StartHour, win.EndHour, win.DurationHours);
        }

        private static string FormatInt(int value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FormatPercent(double value)
        {
            return (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }
    }
}
