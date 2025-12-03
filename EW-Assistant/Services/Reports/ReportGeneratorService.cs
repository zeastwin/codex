using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EW_Assistant.Domain.Reports;
using EW_Assistant;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 报表生成服务：负责调用 LLM 获取 Markdown，并保存到本地仓库。
    /// </summary>
    public class ReportGeneratorService
    {
        private readonly ReportStorageService _storage;
        private readonly LlmWorkflowClient _llm;
        private readonly DailyProdCalculator _dailyProdCalculator;
        private readonly DailyAlarmCalculator _dailyAlarmCalculator;
        private readonly WeeklyProdCalculator _weeklyProdCalculator;
        private readonly WeeklyAlarmCalculator _weeklyAlarmCalculator;

        public ReportGeneratorService(ReportStorageService storage = null, LlmWorkflowClient llm = null)
        {
            _storage = storage ?? new ReportStorageService();
            _llm = llm ?? new LlmWorkflowClient();
            _dailyProdCalculator = new DailyProdCalculator();
            _dailyAlarmCalculator = new DailyAlarmCalculator();
            _weeklyProdCalculator = new WeeklyProdCalculator();
            _weeklyAlarmCalculator = new WeeklyAlarmCalculator();
        }

        public async Task<ReportInfo> GenerateDailyProdAsync(DateTime date, CancellationToken token = default(CancellationToken), bool force = false)
        {
            try
            {
                // 如果已存在，直接返回
                var existing = _storage.GetDailyReportInfo(ReportType.DailyProd, date.Date);
                if (!force && existing != null)
                {
                    return existing;
                }

                token.ThrowIfCancellationRequested();

                var data = _dailyProdCalculator.Calculate(date.Date);
                var prompt = DailyProdReportPromptBuilder.BuildPayload(data);
                var analysisMd = await _llm.GenerateMarkdownAsync(prompt.ReportTask, prompt.ReportDataJson, token).ConfigureAwait(false);
                var fullMd = DailyProdReportMarkdownFormatter.Render(data, analysisMd);

                var path = _storage.SaveReportContent(ReportType.DailyProd, date.Date, fullMd);
                var info = _storage.GetReportInfoByPath(ReportType.DailyProd, path) ?? BuildFallbackInfo(ReportType.DailyProd, date.Date, null, path);
                LogGeneration(ReportType.DailyProd, date.Date, null, path, true, null, fullMd);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogGeneration(ReportType.DailyProd, date.Date, null, null, false, ex.Message, null);
                throw new ReportGenerationException("生成日报失败：" + ex.Message, ex);
            }
        }

        public async Task<ReportInfo> GenerateDailyAlarmAsync(DateTime date, CancellationToken token = default(CancellationToken), bool force = false)
        {
            try
            {
                var existing = _storage.GetDailyReportInfo(ReportType.DailyAlarm, date.Date);
                if (!force && existing != null)
                {
                    return existing;
                }

                token.ThrowIfCancellationRequested();

                var data = _dailyAlarmCalculator.Calculate(date.Date);
                var prompt = DailyAlarmReportPromptBuilder.BuildPayload(data);
                var analysisMd = await _llm.GenerateMarkdownAsync(prompt.ReportTask, prompt.ReportDataJson, token).ConfigureAwait(false);
                var fullMd = DailyAlarmReportMarkdownFormatter.Render(data, analysisMd);

                var path = _storage.SaveReportContent(ReportType.DailyAlarm, date.Date, fullMd);
                var info = _storage.GetReportInfoByPath(ReportType.DailyAlarm, path) ?? BuildFallbackInfo(ReportType.DailyAlarm, date.Date, null, path);
                LogGeneration(ReportType.DailyAlarm, date.Date, null, path, true, null, fullMd);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogGeneration(ReportType.DailyAlarm, date.Date, null, null, false, ex.Message, null);
                throw new ReportGenerationException("生成报警日报失败：" + ex.Message, ex);
            }
        }

        public async Task<ReportInfo> GenerateWeeklyProdAsync(DateTime endDate, CancellationToken token = default(CancellationToken), bool force = false)
        {
            var start = endDate.Date.AddDays(-6);
            try
            {
                var existing = _storage.GetWeeklyReportInfo(ReportType.WeeklyProd, endDate.Date);
                if (!force && existing != null)
                {
                    return existing;
                }

                token.ThrowIfCancellationRequested();

                var data = _weeklyProdCalculator.Calculate(start, endDate.Date);
                var prompt = WeeklyProdReportPromptBuilder.BuildPayload(data);
                var analysisMd = await _llm.GenerateMarkdownAsync(prompt.ReportTask, prompt.ReportDataJson, token).ConfigureAwait(false);
                var fullMd = WeeklyProdReportMarkdownFormatter.Render(data, analysisMd);

                var path = _storage.SaveReportContent(ReportType.WeeklyProd, start, endDate.Date, fullMd);
                var info = _storage.GetReportInfoByPath(ReportType.WeeklyProd, path) ?? BuildFallbackInfo(ReportType.WeeklyProd, start, endDate.Date, path);
                LogGeneration(ReportType.WeeklyProd, start, endDate.Date, path, true, null, fullMd);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogGeneration(ReportType.WeeklyProd, start, endDate.Date, null, false, ex.Message, null);
                throw new ReportGenerationException("生成产能周报失败：" + ex.Message, ex);
            }
        }

        public async Task<ReportInfo> GenerateWeeklyAlarmAsync(DateTime endDate, CancellationToken token = default(CancellationToken), bool force = false)
        {
            var start = endDate.Date.AddDays(-6);
            try
            {
                var existing = _storage.GetWeeklyReportInfo(ReportType.WeeklyAlarm, endDate.Date);
                if (!force && existing != null)
                {
                    return existing;
                }

                token.ThrowIfCancellationRequested();

                var data = _weeklyAlarmCalculator.Calculate(start, endDate.Date);
                var prompt = WeeklyAlarmReportPromptBuilder.BuildPayload(data);
                var analysisMd = await _llm.GenerateMarkdownAsync(prompt.ReportTask, prompt.ReportDataJson, token).ConfigureAwait(false);
                var fullMd = WeeklyAlarmReportMarkdownFormatter.Render(data, analysisMd);

                var path = _storage.SaveReportContent(ReportType.WeeklyAlarm, start, endDate.Date, fullMd);
                var info = _storage.GetReportInfoByPath(ReportType.WeeklyAlarm, path) ?? BuildFallbackInfo(ReportType.WeeklyAlarm, start, endDate.Date, path);
                LogGeneration(ReportType.WeeklyAlarm, start, endDate.Date, path, true, null, fullMd);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogGeneration(ReportType.WeeklyAlarm, start, endDate.Date, null, false, ex.Message, null);
                throw new ReportGenerationException("生成报警周报失败：" + ex.Message, ex);
            }
        }

        private ReportInfo BuildFallbackInfo(ReportType type, DateTime start, DateTime? end, string path)
        {
            var display = _storage.GetTypeDisplayName(type);
            var title = display;
            var dateLabel = string.Empty;
            if (type == ReportType.DailyProd || type == ReportType.DailyAlarm)
            {
                title = string.Format("{0}（{1:yyyy-MM-dd}）", display, start);
                dateLabel = start.ToString("yyyy-MM-dd");
            }
            else if (end.HasValue)
            {
                title = string.Format("{0}（{1:yyyy-MM-dd}~{2:yyyy-MM-dd}）", display, start, end.Value);
                dateLabel = string.Format("{0:yyyy-MM-dd} ~ {1:yyyy-MM-dd}", start, end.Value);
            }

            long size = 0;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists) size = fi.Length;
            }
            catch { }

            return new ReportInfo
            {
                Type = type,
                TypeDisplayName = display,
                Id = Path.GetFileNameWithoutExtension(path),
                Title = title,
                DateLabel = dateLabel,
                Date = (type == ReportType.DailyProd || type == ReportType.DailyAlarm) ? (DateTime?)start : null,
                StartDate = (type == ReportType.WeeklyProd || type == ReportType.WeeklyAlarm) ? (DateTime?)start : null,
                EndDate = (type == ReportType.WeeklyProd || type == ReportType.WeeklyAlarm) ? end : null,
                FilePath = path,
                FileName = Path.GetFileName(path),
                GeneratedAt = DateTime.Now,
                FileSize = size,
                FileSizeText = FormatFileSizeForFallback(size),
                IsToday = (type == ReportType.DailyProd || type == ReportType.DailyAlarm) && start.Date == DateTime.Now.Date
            };
        }

        private static string FormatFileSizeForFallback(long size)
        {
            const double OneK = 1024d;
            const double OneM = OneK * 1024d;
            const double OneG = OneM * 1024d;

            if (size >= OneG) return string.Format("{0:0.##} GB", size / OneG);
            if (size >= OneM) return string.Format("{0:0.##} MB", size / OneM);
            if (size >= OneK) return string.Format("{0:0.##} KB", size / OneK);
            return size + " B";
        }

        /// <summary>记录生成日志，失败不抛出。</summary>
        private void LogGeneration(ReportType type, DateTime start, DateTime? end, string path, bool success, string message, string content)
        {
            try
            {
                var display = _storage.GetTypeDisplayName(type);
                var sb = new StringBuilder();
                sb.Append(success ? "[生成完成] " : "[生成失败] ");
                sb.Append(display);

                if (type == ReportType.DailyProd || type == ReportType.DailyAlarm)
                {
                    sb.Append(" ").Append(start.ToString("yyyy-MM-dd"));
                }
                else if (end.HasValue)
                {
                    sb.Append(" ").Append(start.ToString("yyyy-MM-dd")).Append("~").Append(end.Value.ToString("yyyy-MM-dd"));
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    sb.Append(" | 文件: ").Append(path);
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    sb.Append(" | 详情: ").Append(message);
                }

                // 写入程序日志（含文件落盘），避免影响主流程
                MainWindow.PostProgramInfo(sb.ToString(), success ? "info" : "warn");
                AppendLocalReportLog(sb.ToString(), content);
            }
            catch
            {
                // 绝不让日志异常影响生成流程
            }
        }

        private void AppendLocalReportLog(string line, string content)
        {
            try
            {
                var dir = Path.Combine(@"D:\Data", "AiLog", "Reports");
                Directory.CreateDirectory(dir);
                var fileName = "report-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
                var path = Path.Combine(dir, fileName);
                var sb = new StringBuilder();
                sb.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss}] ", DateTime.Now);
                sb.Append(line ?? string.Empty);
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine("==== 报表内容 ====");
                    sb.AppendLine(content.Trim());
                    sb.AppendLine("==== 结束 ====");
                }

                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.WriteLine(sb.ToString());
                    sw.Flush();
                }
            }
            catch
            {
                // 文件日志失败静默
            }
        }
    }

    /// <summary>
    /// 报表生成异常，便于上层捕获后提示用户。
    /// </summary>
    public class ReportGenerationException : Exception
    {
        public ReportGenerationException(string message, Exception inner = null) : base(message, inner) { }
    }
}
