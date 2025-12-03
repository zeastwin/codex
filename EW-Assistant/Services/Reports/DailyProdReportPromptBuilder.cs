using System;
using System.Text;
using EW_Assistant.Domain.Reports;
using Newtonsoft.Json;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 构建当日产能报表的统一 Prompt：REPORT_TASK + REPORT_DATA_JSON。
    /// </summary>
    public static class DailyProdReportPromptBuilder
    {
        public static ReportPromptPayload BuildPayload(DailyProdData data)
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

        public static string BuildUserPrompt(DailyProdData data)
        {
            return BuildPayload(data).CombinedPrompt;
        }

        private static string BuildTaskText(DailyProdData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("你是一名工厂产能分析师，请基于本地 CSV 统计结果编写 **{0:yyyy-MM-dd} 的当日产能报表分析**。", data.Date));
            sb.AppendLine("数据来源是生产 CSV，本地已计算出逐小时产能、峰值/低谷、停机窗口等结构化字段。");
            sb.AppendLine("你需要输出以下章节（只做文字分析，不再调用工具）：");
            sb.AppendLine("1) ## 产能与良率诊断：结合峰值/低谷/停机窗口/低良率时段，给出 5-8 条业务可读的洞察与建议。");
            sb.AppendLine("2) ## 注意事项：列出数据缺失或异常（若无则写“无”）。");
            sb.AppendLine();
            sb.AppendLine("写作要求：");
            sb.AppendLine("- 使用中文商务语气，避免开发术语；");
            sb.AppendLine("- 结论必须基于提供的 JSON 数据，不要编造；");
            sb.AppendLine("- 重点关注波动、停机对稼动率/良率的影响，以及可执行的改进措施。");
            sb.AppendLine();
            sb.AppendLine("下方提供当日的 JSON 数据，请充分利用。");

            return sb.ToString();
        }
    }
}
