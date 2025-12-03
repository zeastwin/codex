using System;
using System.Text;
using EW_Assistant.Domain.Reports;
using Newtonsoft.Json;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 报表 Prompt 构造集合，按报表类型生成 REPORT_TASK/REPORT_DATA_JSON。
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

    public static class DailyAlarmReportPromptBuilder
    {
        public static ReportPromptPayload BuildPayload(DailyAlarmData data)
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

        public static string BuildUserPrompt(DailyAlarmData data)
        {
            return BuildPayload(data).CombinedPrompt;
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
