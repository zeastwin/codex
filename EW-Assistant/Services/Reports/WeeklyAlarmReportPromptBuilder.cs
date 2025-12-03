using System;
using System.Text;
using EW_Assistant.Domain.Reports;
using Newtonsoft.Json;

namespace EW_Assistant.Services.Reports
{
    public static class WeeklyAlarmReportPromptBuilder
    {
        public static ReportPromptPayload BuildPayload(WeeklyAlarmData data)
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

        public static string BuildUserPrompt(WeeklyAlarmData data)
        {
            return BuildPayload(data).CombinedPrompt;
        }

        private static string BuildTaskText(WeeklyAlarmData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("请基于本地报警 CSV 统计出的结构化数据，撰写 **{0:yyyy-MM-dd} ~ {1:yyyy-MM-dd}（上一自然周）** 的报警周报分析。", data.StartDate, data.EndDate));
            sb.AppendLine("数据包含周度 totals/byDay/top，字段含义与 AlarmCsvTools/ProdAlarmTools 的聚合相近。");
            sb.AppendLine("输出章节：");
            sb.AppendLine("1) ## 周度KPI：报警次数、总时长、平均单次、覆盖小时数、低良率小时（如有）。");
            sb.AppendLine("2) ## 日度趋势：按日期说明报警强度和代表性 Top 报警。");
            sb.AppendLine("3) ## Top 报警：结合 top 列表分析前三大报警的影响和可能原因。");
            sb.AppendLine("4) ## 样本与措施：给 3-5 条可执行措施或样本案例。");
            sb.AppendLine("5) ## 异常/缺失：引用 warnings（无则写“无”）。");
            sb.AppendLine("要求：中文商务语气，不要编造数据，条理清晰。");
            return sb.ToString();
        }
    }
}
