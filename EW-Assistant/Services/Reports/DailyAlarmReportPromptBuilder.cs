using System;
using System.Text;
using EW_Assistant.Domain.Reports;
using Newtonsoft.Json;

namespace EW_Assistant.Services.Reports
{
    public static class DailyAlarmReportPromptBuilder
    {
        public static string BuildUserPrompt(DailyAlarmData data)
        {
            var task = BuildTaskText(data);
            var json = JsonConvert.SerializeObject(data, Formatting.None);
            var sb = new StringBuilder();
            sb.AppendLine("【REPORT_TASK】");
            sb.AppendLine(task.Trim());
            sb.AppendLine();
            sb.AppendLine("【REPORT_DATA_JSON】");
            sb.AppendLine(json);
            return sb.ToString();
        }

        private static string BuildTaskText(DailyAlarmData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("你是一名报警分析师，基于本地 CSV 统计出的结构化数据，撰写 **{0:yyyy-MM-dd} 的报警日报分析**。", data.Date));
            sb.AppendLine("数据字段说明：hours 包含每小时的产能与报警条数/时长/Top 报警；dayAlarmCount/dayAlarmSeconds/peakHour 等已预计算。");
            sb.AppendLine("输出章节：");
            sb.AppendLine("1) ## 概览结论：概括报警强度、峰值时段、对产能的影响。");
            sb.AppendLine("2) ## 逐小时观察：指出报警高发/低发/无报警时段的规律，可引用 hours.tags。");
            sb.AppendLine("3) ## 处理措施建议：给出 5-8 条可执行建议，关联高频/长时报警。");
            sb.AppendLine("4) ## 注意事项：引用 warnings 或补充数据缺失说明（无则写“无”）。");
            sb.AppendLine("要求：中文商务语气，结论基于 JSON，不要编造，避免接口/字段名，条目化表达。");
            return sb.ToString();
        }
    }
}
