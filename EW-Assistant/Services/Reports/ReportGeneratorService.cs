using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 报表生成服务：负责调用 LLM 获取 Markdown，并保存到本地仓库。
    /// </summary>
    public class ReportGeneratorService
    {
        private readonly ReportStorageService _storage;
        private readonly LlmReportClient _llm;

        public ReportGeneratorService(ReportStorageService storage = null, LlmReportClient llm = null)
        {
            _storage = storage ?? new ReportStorageService();
            _llm = llm ?? new LlmReportClient();
        }

        public async Task<ReportInfo> GenerateDailyProdAsync(DateTime date, CancellationToken token = default(CancellationToken))
        {
            return await GenerateDailyAsync(ReportType.DailyProd, date, token, ReportPromptBuilder.BuildDailyProdPrompt(date)).ConfigureAwait(false);
        }

        public async Task<ReportInfo> GenerateDailyAlarmAsync(DateTime date, CancellationToken token = default(CancellationToken))
        {
            return await GenerateDailyAsync(ReportType.DailyAlarm, date, token, ReportPromptBuilder.BuildDailyAlarmPrompt(date)).ConfigureAwait(false);
        }

        public async Task<ReportInfo> GenerateWeeklyProdAsync(DateTime endDate, CancellationToken token = default(CancellationToken))
        {
            var start = endDate.Date.AddDays(-6);
            return await GenerateWeeklyAsync(ReportType.WeeklyProd, start, endDate.Date, token, ReportPromptBuilder.BuildWeeklyProdPrompt(endDate)).ConfigureAwait(false);
        }

        public async Task<ReportInfo> GenerateWeeklyAlarmAsync(DateTime endDate, CancellationToken token = default(CancellationToken))
        {
            var start = endDate.Date.AddDays(-6);
            return await GenerateWeeklyAsync(ReportType.WeeklyAlarm, start, endDate.Date, token, ReportPromptBuilder.BuildWeeklyAlarmPrompt(endDate)).ConfigureAwait(false);
        }

        private async Task<ReportInfo> GenerateDailyAsync(ReportType type, DateTime date, CancellationToken token, string prompt)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var md = await _llm.GenerateMarkdownAsync(prompt, token).ConfigureAwait(false);
                var path = _storage.SaveReportContent(type, date.Date, md);
                return _storage.GetReportInfoByPath(type, path) ?? BuildFallbackInfo(type, date.Date, null, path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ReportGenerationException("生成日报失败：" + ex.Message, ex);
            }
        }

        private async Task<ReportInfo> GenerateWeeklyAsync(ReportType type, DateTime startDate, DateTime endDate, CancellationToken token, string prompt)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var md = await _llm.GenerateMarkdownAsync(prompt, token).ConfigureAwait(false);
                var path = _storage.SaveReportContent(type, startDate.Date, endDate.Date, md);
                return _storage.GetReportInfoByPath(type, path) ?? BuildFallbackInfo(type, startDate.Date, endDate.Date, path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ReportGenerationException("生成周报失败：" + ex.Message, ex);
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
    }

    /// <summary>
    /// 报表生成异常，便于上层捕获后提示用户。
    /// </summary>
    public class ReportGenerationException : Exception
    {
        public ReportGenerationException(string message, Exception inner = null) : base(message, inner) { }
    }
}
