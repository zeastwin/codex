using System;
using System.Text;
using EW_Assistant.Domain.Reports;
using Newtonsoft.Json;

namespace EW_Assistant.Services.Reports
{
    public static class WeeklyProdReportPromptBuilder
    {
        public static ReportPromptPayload BuildPayload(WeeklyProdData data)
        {
            var task = BuildTaskText(data).Trim();
            var json = JsonConvert.SerializeObject(data, Formatting.None);
            var sb = new StringBuilder();
            sb.AppendLine("【REPORT_TASK】");
            sb.AppendLine(task);
            sb.AppendLine();
            sb.AppendLine("【REPORT_DATA_JSON】");
            sb.AppendLine(json);

            return new ReportPromptPayload
            {
                ReportTask = task,
                ReportDataJson = json,
                CombinedPrompt = sb.ToString()
            };
        }

        public static string BuildUserPrompt(WeeklyProdData data)
        {
            return BuildPayload(data).CombinedPrompt;
        }

        private static string BuildTaskText(WeeklyProdData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("请基于本地 CSV 统计出的结构化数据，撰写 **{0:yyyy-MM-dd} ~ {1:yyyy-MM-dd}（上一自然周）** 的产能周报分析。", data.StartDate, data.EndDate));
            sb.AppendLine("数据字段与 GetWeeklyProductionSummary 类似，包含 summary/days/bestDays/worstDays/lastDayDelta/warnings。");
            sb.AppendLine("输出章节：");
            sb.AppendLine("1) ## 周度KPI：阐述 PASS/FAIL/Total/良率、均值、中位数、波动、LastDayDelta。");
            sb.AppendLine("2) ## 日度表现：逐日亮点/缺口，引用 days.Note 说明缺失。");
            sb.AppendLine("3) ## 重点洞察：结合 bestDays/worstDays/趋势给 4-6 条洞察。");
            sb.AppendLine("4) ## 风险与改进：给 2-3 条措施，明确验证指标。");
            sb.AppendLine("5) ## 异常/缺失：引用 warnings（无则写“无”）。");
            sb.AppendLine("要求：中文商务语气，依据 JSON，不要编造，条理清晰。");
            return sb.ToString();
        }
    }
}
