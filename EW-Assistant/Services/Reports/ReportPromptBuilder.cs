using System;
using System.Text;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 提供报表生成所需的 Prompt 模板，复用 AI Assistant 快捷按钮的逻辑。
    /// </summary>
    public static class ReportPromptBuilder
    {
        public static string BuildDailyProdPrompt(DateTime date)
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("生成 **今天（{0}）0–24 点** 的客户版 **产量/良率/稼动率** 日报。只输出 **Markdown**；禁止输出代码块、JSON、工具调用日志或多余说明。", "{DATE}"));
            sb.AppendLine();
            sb.AppendLine("【这是单独一台设备数据】");
            sb.AppendLine("【必须按此取数】");
            sb.AppendLine("1) 仅调用 MCP 工具：GetHourlyStats(date=\"{DATE}\", startHour=0, endHour=24) 获取逐小时数据（hour, pass, fail, total, yield）。");
            sb.AppendLine("2) 在工具返回的基础上自行计算并用于报告：");
            sb.AppendLine("   - day_pass = Σpass；day_fail = Σfail；day_total = day_pass + day_fail；");
            sb.AppendLine("   - day_yield = day_pass / day_total（百分比，保留2位）；");
            sb.AppendLine("   - active_hours = 统计 total > 0 的小时数；");
            sb.AppendLine("   - active_rate = active_hours / 24（百分比，保留2位，作为“近似稼动率”）；");
            sb.AppendLine("   - 峰值小时 = 非零 total 的 Top-1~3 小时（含产量数）；");
            sb.AppendLine("   - 低谷小时 = 非零 total 的 Bottom-1~3 小时（含产量数）；");
            sb.AppendLine("   - 最长连续停机窗口 = 将 total=0 的相邻小时合并，取持续时间最长的一段（格式示例：“03:00–07:00，4小时”）；");
            sb.AppendLine("   - 波动指数 CV = 标准差(total) / 均值(total)（百分比，保留2位）；");
            sb.AppendLine();
            sb.AppendLine("【排版模板（章节顺序固定、全部必须出现）】");
            sb.AppendLine("# 今日产能日报（{DATE}）");
            sb.AppendLine();
            sb.AppendLine("## 概览指标");
            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine("| PASS（良品） | {day_pass 千分位} |");
            sb.AppendLine("| FAIL（不良） | {day_fail 千分位} |");
            sb.AppendLine("| 总产量 | {day_total 千分位} |");
            sb.AppendLine("| **良率** | **{day_yield 两位百分比}** |");
            sb.AppendLine("| **活跃小时** | **{active_hours} 小时** |");
            sb.AppendLine("| **近似稼动率** | **{active_rate 两位百分比}** |");
            sb.AppendLine("| 峰值小时 | {例如 “10:00–11:00（1,024件）” 等 1–3 个} |");
            sb.AppendLine("| 最长停机窗口 | {如无则写“无”} |");
            sb.AppendLine("| 波动指数（CV） | {两位百分比} |");
            sb.AppendLine();
            sb.AppendLine("> 注：近似稼动率=有产出小时数/24，用于日常管理直观参考。");
            sb.AppendLine();
            sb.AppendLine("## 逐小时明细");
            sb.AppendLine("> 0–23 点必须全覆盖；若某小时工具未返回，按 PASS=0, FAIL=0, Total=0, Yield=0% 填充。");
            sb.AppendLine();
            sb.AppendLine("| 时段 | PASS | FAIL | 总量 | 良率(%) | 备注 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");
            sb.AppendLine("（逐行渲染以下行模板，共24行）逐小时表格“必须严格按 hours 数组逐行渲染”，不得跳过 0 产出小时、不得合并区间；表格行数必须等于 hours 的长度（正常为 24 行）。");
            sb.AppendLine("| {HH}:00–{HH+1}:00 | {pass 千分位} | {fail 千分位} | {total 千分位} | {yield 两位} | {标签} |");
            sb.AppendLine();
            sb.AppendLine("**标签判定（从强到弱，按需叠加，空格分隔）：**");
            sb.AppendLine("- total = 0 → `—停机/无产出`");
            sb.AppendLine("- 非零 total 的 Top-3 → `↑峰值`");
            sb.AppendLine("- 非零 total 的 Bottom-3 → `↓低谷`");
            sb.AppendLine("- yield < 85% → `★低良率`");
            sb.AppendLine("- yield ≥ 98% → `✓稳定`");
            sb.AppendLine();
            sb.AppendLine("**合计行（表末尾追加一行）：**");
            sb.AppendLine("| **合计** | **ΣPASS** | **ΣFAIL** | **ΣTotal** | **{day_yield 两位}%** | **活跃 {active_hours}/24** |");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## 产能与良率诊断");
            sb.AppendLine("- 基于峰值/低谷/停机窗口/低良率时段，给出 **5–8 条**业务可读的洞见与改善建议（例如：排班与交接、点检/快速换线、治具维护、程序/工艺参数优化、备料节拍、在制/WIP 管控等）。");
            sb.AppendLine("- 语气客观，避免开发术语。");
            sb.AppendLine();
            sb.AppendLine("## 注意事项");
            sb.AppendLine("- 如存在缺失 CSV、解析异常或跨天数据空洞，请逐条列出；若无则写“无”。");
            sb.AppendLine();
            sb.AppendLine("【格式与风格要求】");
            sb.AppendLine("- 所有数值使用 **千分位**；百分比 **保留2位**；时间统一 `HH:00`。");
            sb.AppendLine("- 中文商务表述，避免“一句话结论”、避免开发字段名。");
            sb.AppendLine("- **必须**渲染完整 24 行小时表与“合计行”；不可省略章节。");
            sb.AppendLine();
            sb.AppendLine("【失败兜底】");
            sb.AppendLine("- 如工具报错或无数据，仍按本模板输出各章节：表格填 0；并在“注意事项”中写明原因。");
            sb.AppendLine();
            sb.AppendLine("【可选增强】");
            sb.AppendLine("- 若存在报警工具（GetHourlyAlarms / QueryAlarms），追加一节：");
            sb.AppendLine("  ## 报警关联（概览）");
            sb.AppendLine("  - 按小时统计报警条数，标注与“停机/低谷”重合的时段，列出示例原因；若无该工具则跳过本节。");

            return sb.ToString().Replace("{DATE}", dateStr);
        }

        public static string BuildDailyAlarmPrompt(DateTime date)
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("生成 **今天（{0}）0–24 点** 的 **报警日报**。只输出 **Markdown**；禁止输出代码块、JSON、工具调用日志或多余说明。", "{DATE}"));
            sb.AppendLine();
            sb.AppendLine("【这是单独一台设备数据】");
            sb.AppendLine("【必须按此取数】");
            sb.AppendLine("1) 仅允许调用 1 次 MCP 工具（禁止调用其他任何工具）：");
            sb.AppendLine("   - GetHourlyProdWithAlarms(date=\"{DATE}\", startHour=0, endHour=24)");
            sb.AppendLine();
            sb.AppendLine("2) 你只能使用该工具返回的 items 字段：");
            sb.AppendLine("   - hour, pass, fail, total, yield, alarmCount, alarmDurationSec, topAlarmCode, topAlarmSeconds, topAlarmContent");
            sb.AppendLine();
            sb.AppendLine("3) 你需要自行计算：");
            sb.AppendLine("   - avg_s = alarmDurationSec / max(1, alarmCount)；");
            sb.AppendLine("   - day_count = Σ alarmCount；day_seconds = Σ alarmDurationSec；avg_per_alarm = day_seconds / max(1, day_count)；");
            sb.AppendLine("   - active_hours = alarmDurationSec > 0 的小时数；");
            sb.AppendLine("   - 峰值小时：alarmDurationSec 最大的小时（若全为0写“无”）；");
            sb.AppendLine();
            sb.AppendLine("【空值/缺省规则（必须遵守）】");
            sb.AppendLine("- 若 alarmCount=0 或 alarmDurationSec=0：Top代码=“无”，Top内容=“无”。");
            sb.AppendLine("- 若 topAlarmCode 为空：Top代码=“无”。");
            sb.AppendLine("- 若 topAlarmContent 为空：Top内容=“无”。（不要写“内容缺失”）");
            sb.AppendLine();
            sb.AppendLine("【排版模板（顺序固定、全部必须出现）】");
            sb.AppendLine("# 今日报警日报（{DATE}）");
            sb.AppendLine();
            sb.AppendLine("## 概览指标");
            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("|:---|---:|");
            sb.AppendLine("| 报警条数 | {day_count 千分位} |");
            sb.AppendLine("| 报警总时长 | {day_seconds 转 xhym 或 ms} |");
            sb.AppendLine("| 平均单次(秒) | {avg_per_alarm 一位} |");
            sb.AppendLine("| 活跃小时 | {active_hours}/24 |");
            sb.AppendLine("| 峰值小时 | {若存在：HH:00–HH+1:00（xhym/条数）；否则“无”} |");
            sb.AppendLine();

            sb.AppendLine("## 逐小时明细");
            sb.AppendLine("> 0–23 点必须全覆盖；缺数据的小时按 count=0, seconds=0, avg_s=0 填充。");
            sb.AppendLine();
            sb.AppendLine("| 时段 | 条数 | 时长 | 平均(s) | Top代码 | Top内容 | 备注 |");
            sb.AppendLine("|:---|---:|---:|---:|:---|:---|:---|");
            sb.AppendLine("（严格渲染 24 行；不得跳过 0 值小时，不得合并区间；必须 7 列，不得增减列）");
            sb.AppendLine("| {HH}:00–{HH+1}:00 | {count 千分位} | {seconds 时分秒} | {avg_s 一位} | {topCode} | {topContent} | {标签} |");
            sb.AppendLine();
            sb.AppendLine("**标签判定（从强到弱，可叠加）：**");
            sb.AppendLine("- count=0 或 seconds=0 → `—无报警`");
            sb.AppendLine("- 非零秒 Top-3 → `↑高频`");
            sb.AppendLine("- 非零秒 Bottom-3 → `↓低频`");
            sb.AppendLine("- avg_s ≥ 300 → `★长报警`");
            sb.AppendLine("- avg_s ≤ 30 → `✓短报警`");
            sb.AppendLine();
            sb.AppendLine("**合计行（表末尾追加）：**");
            sb.AppendLine("| **合计** | **Σcount** | **Σseconds** | **{avg_per_alarm 一位}** |  |  | **活跃 {active_hours}/24** |");
            sb.AppendLine();

            sb.AppendLine("## 今日 Top 报警（近似统计：按各小时 Top1 聚合）");
            sb.AppendLine("- 仅基于每小时 topAlarmCode/topAlarmSeconds 做近似聚合，输出 Top5（按 ΣtopAlarmSeconds 降序）：");
            sb.AppendLine("  - 代码 | 内容 | ΣtopAlarmSeconds(转时分秒) | 出现小时数");
            sb.AppendLine();

            sb.AppendLine("## 处理措施建议（5–8 条，务实可执行）");
            sb.AppendLine("- 每条按：**关联代码/内容** → **动作** → **验证指标**（例如报警总时长下降、某工位成功率提升）。");
            sb.AppendLine();

            sb.AppendLine("## 异常与注意事项");
            sb.AppendLine("- 若日报出现大量 Top内容=无：说明“报警文件无内容或内容为空，当前仅能展示代码”；否则写“无”。");
            sb.AppendLine();
            sb.AppendLine("【格式要求】");
            sb.AppendLine("- 数值千分位；百分比两位；时长用 `xhym` 或 `ms`；时间统一 `HH:00`。");
            sb.AppendLine("- 严禁编造：所有数值必须来自工具返回或由其严格计算；逐小时表 24 行不可缺。");

            return sb.ToString().Replace("{DATE}", dateStr);
        }

        public static string BuildWeeklyProdPrompt(DateTime endDate)
        {
            var end = endDate.Date;
            var start = end.AddDays(-6);
            var endStr = end.ToString("yyyy-MM-dd");
            var startStr = start.ToString("yyyy-MM-dd");
            var range = startStr + " ~ " + endStr;

            var sb = new StringBuilder();

            sb.AppendLine(string.Format("请输出 **{0}（最近7天）** 的产能周报，必须使用 Markdown，禁止 JSON、原始日志或随意发挥。", range));
            sb.AppendLine();
            sb.AppendLine("请按以下步骤执行：");
            sb.AppendLine(string.Format("1) 仅调用 MCP 工具 GetWeeklyProductionSummary(endDate=\"{0}\")，取得 `summary`（pass/fail/total/yield/avgYield/medianTotal/volatility/lastDay/lastDayDelta/bestDays/worstDays）、`days`（逐日明细）以及 `warnings`。", endStr));
            sb.AppendLine("2) 所有周度 KPI 必须直接引用 summary；日度表格数据来自 days，不得自行演算或猜测缺失字段。");
            sb.AppendLine("3) 若 warnings 不为空或某天缺 CSV，须在“异常/缺失”章节逐条说明，并在日度表中标注原因。");
            sb.AppendLine("4) 基于 bestDays / worstDays / lastDayDelta 给出有洞见的亮点、薄弱点与改进建议，尽量结合具体日期与数值。");
            sb.AppendLine();
            sb.AppendLine("输出模板（章节与表头不可删改，可补充文字说明）：");
            sb.AppendLine(string.Format("# 产能周报（{0}）", range));
            sb.AppendLine();
            sb.AppendLine("## 周度KPI");
            sb.AppendLine("| 指标 | 数值 | 说明 |");
            sb.AppendLine("|---|---:|---|");
            sb.AppendLine("| PASS 总量 | {summary.pass 千分位} | 一周内通过品数量总和 |");
            sb.AppendLine("| FAIL 总量 | {summary.fail 千分位} | 一周内不良品数量总和 |");
            sb.AppendLine("| 总产量 | {summary.total 千分位} | 一周内实际测试的总数量（PASS + FAIL） |");
            sb.AppendLine("| 周整体良率 | {summary.yield 百分比2位} | 以一周总产量为基准的整体通过率 |");
            sb.AppendLine("| 周均良率 | {summary.avgYield 百分比2位} | 7 天良率的平均水平，代表本周“常态”表现 |");
            sb.AppendLine("| 中位产量 | {summary.medianTotal 千分位} | 7 天日产量从小到大排序后的中间值，代表典型日产量 |");
            sb.AppendLine("| 产能波动（CV） | {summary.volatility 百分比2位} | 反映每天产量波动程度，数值越大波动越明显 |");
            sb.AppendLine("| 最后1天 vs 周均 | {summary.lastDayDelta.total 百分比1位}/{summary.lastDayDelta.yield 百分比1位} | 最后一天产量/良率相对本周平均的增减（正值=高于周均） |");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## 日度表现");
            sb.AppendLine("| 日期 | PASS | FAIL | 总量 | 良率 | 备注 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");
            sb.AppendLine("按 days 的顺序逐日渲染，备注列写明 warnings 对应的缺失、异常或特别说明；正常日期可写“正常”或留空。");
            sb.AppendLine();
            sb.AppendLine("## 重点洞察");
            sb.AppendLine("- 亮点：结合 summary.bestDays，说明哪些日期/班次产能和良率表现突出，以及可能原因。");
            sb.AppendLine("- 薄弱：结合 summary.worstDays，分析拖累周度指标的薄弱日期/工况，对周均的影响有多大。");
            sb.AppendLine("- 趋势：描述 7 天产能与良率走势、明显峰谷，以及 summary.lastDay / lastDayDelta 反映的最新状态。");
            sb.AppendLine();
            sb.AppendLine("## 风险与改进");
            sb.AppendLine("- 给出 2–3 条可执行措施，明确对应的验证指标（例如目标良率、目标日产量）和预计改善幅度。");
            sb.AppendLine();
            sb.AppendLine("## 异常/缺失");
            sb.AppendLine("- 若存在工具报错或缺 CSV，逐条列出日期与原因；否则写“无”。");
            sb.AppendLine();
            sb.AppendLine("> 所有指标必须来自 GetWeeklyProductionSummary 的结果，严禁输出 JSON 或凭空编造数据。");

            return sb.ToString();
        }

        public static string BuildWeeklyAlarmPrompt(DateTime endDate)
        {
            var endStr = endDate.ToString("yyyy-MM-dd");
            var startStr = endDate.AddDays(-6).ToString("yyyy-MM-dd");
            var range = startStr + " ~ " + endStr;
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("请输出 **{0}（最近7天）** 的报警周报，仅允许 Markdown 表达，禁止 JSON / 原始日志 / 代码块里的 JSON。", range));
            sb.AppendLine();
            sb.AppendLine("数据准备：");
            sb.AppendLine(string.Format("1) 调用 `ProdAlarmTools.GetAlarmImpactSummary(startDate=\"{0}\", endDate=\"{1}\", window=\"\")`，得到：", startStr, endStr));
            sb.AppendLine("   - `weeklyTotals.alarmSeconds`：周度去重后的报警总时长（秒，按小时并集 ≤24*3600*7，用于衡量设备真实挂报警时间）；");
            sb.AppendLine("   - `weeklyTotals.activeHours`：有报警秒数的小时数（覆盖小时数）；");
            sb.AppendLine("   - `weeklyTotals.lowYieldRowCount`：低良率小时条数（= lowYield.rows.Count）；");
            sb.AppendLine("   - `correlation.alarmSeconds_vs_yield`：报警秒数 vs 良率 的皮尔逊相关系数；");
            sb.AppendLine("   - `byDay`：每日聚合（`date / pass / fail / total / yield / alarmSeconds / alarmCount`）；");
            sb.AppendLine("   - `lowYield.rows`：低良率小时明细（含 `date / hour / total / yield / alarmSeconds / alarmCount / topAlarmCode / topAlarmSeconds`）；");
            sb.AppendLine("   - `lowYield.topAlarmCodes`：在低良率时段中累计时长靠前的报警代码。");
            sb.AppendLine();
            sb.AppendLine(string.Format("2) 调用 `AlarmCsvTools.GetAlarmRangeWindowSummary(startDate=\"{0}\", endDate=\"{1}\", window=\"\", topN=10, sortBy=\"duration\")`，得到：", startStr, endStr));
            sb.AppendLine("   - `totals.count`：报警记录条数（统计口径：CSV 明细行数）；");
            sb.AppendLine("   - `totals.durationSeconds`：报警记录累计时长（可能大于实际时间，用于类别占比和 Top 排序）；");
            sb.AppendLine("   - `byCategory`：各类别的次数 / 时长及占比；");
            sb.AppendLine("   - `top`：Top 报警代码列表（含 `code / content / count / duration`）。");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("3) 所有数值和结论必须基于上述工具返回的数据进行计算和归纳，不得凭空猜测。");
            sb.AppendLine();
            sb.AppendLine("输出模板：");
            sb.AppendLine(string.Format("# 报警周报（{0}）", range));
            sb.AppendLine();
            sb.AppendLine("## 周度KPI");
            sb.AppendLine("| 指标 | 数值 | 说明 |");
            sb.AppendLine("|---|---:|---|");
            sb.AppendLine("| 报警次数 | {alarm_record_count 千分位} | 一周内记录到的报警总次数 |");
            sb.AppendLine("| 报警总时长 | {impact_seconds 转 xhym} | 一周内设备处于报警状态的累计时间 |");
            sb.AppendLine("| 平均单次时长 | {avg_duration 秒1位} | 每次报警平均持续多长时间，建议写成“约 X 分钟/次” |");
            sb.AppendLine("| 覆盖小时数 | {active_hours}/168 | 本周有报警发生的小时数，占一周 168 小时的多少 |");
            sb.AppendLine("| 低良率小时 | {low_yield_hours 千分位} | 本周良率低于设定阈值（如 95%）的小时数 |");
            sb.AppendLine("| 报警-良率相关 | {pearson 百分比2位} | 报警时间与良率高低的关联程度（数值越接近 100% 关联越强） |");
            sb.AppendLine();
            sb.AppendLine("> 表格中的“说明”面向现场/管理人员，请用通俗中文描述，不要出现接口名或内部字段名。");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## 日度趋势");
            sb.AppendLine("| 日期 | 报警次数 | 报警时长 | 良率 | Top 报警（概括） |");
            sb.AppendLine("|---|---:|---:|---:|---|");
            sb.AppendLine("- 按日期升序遍历 `GetAlarmImpactSummary.byDay`，列出 `date / alarmCount / alarmSeconds / yield`。");
            sb.AppendLine("- 对每一天，可结合 `GetAlarmRangeWindowSummary.top` 和必要时的 `QueryAlarms`，用 1–2 句自然语言概括当日最典型或影响最大的报警现象（不强制精确到绝对 Top 代码）。");
            sb.AppendLine();
            sb.AppendLine("## 低良率与 Top 报警");
            sb.AppendLine("- 从 `lowYield.rows` 中筛选关键小时，按 `日期 + HH:00-HH+1:00 / yield / alarmSeconds / alarmCount / topAlarmCode` 展开，解释这些小时良率下降的主要原因。");
            sb.AppendLine("- 结合 `lowYield.topAlarmCodes` 与 `AlarmRangeWindowSummary.top`，分析前三大报警在：");
            sb.AppendLine("  - 低良率小时中的累计时长和次数；");
            sb.AppendLine("  - 对整体产能 / 良率的影响（例如：贡献了多少比例的低良率小时报警时长）。");
            sb.AppendLine();
            sb.AppendLine("## 样本与措施");
            sb.AppendLine("- 至少给出 2–3 条典型报警样本：包含开始时间、报警代码、报警内容、持续时间（换算成 min）、涉及工序 / 工位（如能识别）、以及对应的产能 / 良率影响。");
            sb.AppendLine("- 针对 Top 报警，总结本周已采取或计划采取的措施，例如：参数优化、软件修正、治具维护、培训等，并给出预计改善方向或关闭时间。");
            sb.AppendLine();
            sb.AppendLine("## 异常 / 缺失");
            sb.AppendLine("- 将 `GetAlarmImpactSummary.warnings` 中的内容逐条整理，例如：缺失某天产能 / 报警文件。");
            sb.AppendLine("- 若本周数据完整且无特别异常，请明确写出“无”。");
            sb.AppendLine();
            sb.AppendLine("> 严格使用 Markdown 输出整份周报，禁止输出任何 JSON 结构或原始 CSV 行。");

            return sb.ToString();
        }
    }
}
