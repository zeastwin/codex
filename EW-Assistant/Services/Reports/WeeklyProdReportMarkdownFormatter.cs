using System;
using System.Globalization;
using System.Linq;
using System.Text;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    public static class WeeklyProdReportMarkdownFormatter
    {
        public static string Render(WeeklyProdData data, string analysisMarkdown)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 产能周报（" + data.StartDate.ToString("yyyy-MM-dd") + " ~ " + data.EndDate.ToString("yyyy-MM-dd") + "）");
            sb.AppendLine();
            AppendKpi(sb, data);
            AppendDays(sb, data);

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

        private static void AppendKpi(StringBuilder sb, WeeklyProdData data)
        {
            sb.AppendLine("## 周度KPI");
            sb.AppendLine("| 指标 | 数值 | 说明 |");
            sb.AppendLine("|---|---:|---|");
            sb.AppendLine("| PASS 总量 | " + FormatInt(data.Pass) + " | 一周内通过品数量总和 |");
            sb.AppendLine("| FAIL 总量 | " + FormatInt(data.Fail) + " | 一周内不良品数量总和 |");
            sb.AppendLine("| 总产量 | " + FormatInt(data.Total) + " | 一周内实际测试的总数量 |");
            sb.AppendLine("| 周整体良率 | " + FormatPercent(data.Yield) + " | 以一周总产量为基准的整体通过率 |");
            sb.AppendLine("| 周均良率 | " + FormatPercent(data.AvgYield) + " | 7 天良率平均 |");
            sb.AppendLine("| 中位产量 | " + FormatInt(data.MedianTotal) + " | 7 天日产量中位数 |");
            sb.AppendLine("| 产能波动（CV） | " + FormatPercent(data.Volatility) + " | 日产量的变异系数 |");
            if (data.LastDay != null && data.LastDayDelta != null)
            {
                sb.AppendLine("| 最后1天 vs 周均 | " +
                    FormatPercent(data.LastDayDelta.TotalDelta) + " / " +
                    FormatPercent(data.LastDayDelta.YieldDelta) + " | 产量/良率 相对周均的增减 |");
            }
            sb.AppendLine();
        }

        private static void AppendDays(StringBuilder sb, WeeklyProdData data)
        {
            sb.AppendLine("## 日度表现");
            sb.AppendLine("| 日期 | PASS | FAIL | 总量 | 良率 | 备注 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");
            foreach (var d in data.Days.OrderBy(x => x.Date))
            {
                sb.Append("| " + d.Date.ToString("yyyy-MM-dd") + " | ");
                sb.Append(FormatInt(d.Pass) + " | ");
                sb.Append(FormatInt(d.Fail) + " | ");
                sb.Append(FormatInt(d.Total) + " | ");
                sb.Append(FormatPercent(d.Yield) + " | ");
                sb.Append((string.IsNullOrWhiteSpace(d.Note) ? "正常" : d.Note) + " |");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private static string FormatInt(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
        private static string FormatPercent(double value) => (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }
}
