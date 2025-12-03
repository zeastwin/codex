using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 报表调度器：在后台自动补生成基础日报/周报。
    /// </summary>
    public class ReportScheduler
    {
        private readonly ReportStorageService _storage;
        private readonly ReportGeneratorService _generator;

        public ReportScheduler(ReportStorageService storage = null, ReportGeneratorService generator = null)
        {
            _storage = storage ?? new ReportStorageService();
            _generator = generator ?? new ReportGeneratorService(_storage, new LlmReportClient());
        }

        /// <summary>
        /// 确保当天的两份日报与最近 7 天的两份周报已生成，异常仅记录不抛出。
        /// </summary>
        public async Task EnsureBasicReportsAsync(CancellationToken token = default(CancellationToken))
        {
            var today = DateTime.Today;
            await EnsureDailyAsync(ReportType.DailyProd, today, token).ConfigureAwait(false);
            await EnsureDailyAsync(ReportType.DailyAlarm, today, token).ConfigureAwait(false);
            await EnsureWeeklyAsync(ReportType.WeeklyProd, today, token).ConfigureAwait(false);
            await EnsureWeeklyAsync(ReportType.WeeklyAlarm, today, token).ConfigureAwait(false);
        }

        private async Task EnsureDailyAsync(ReportType type, DateTime date, CancellationToken token)
        {
            if (_storage.DailyReportExists(type, date))
            {
                return;
            }

            try
            {
                if (type == ReportType.DailyProd)
                {
                    await _generator.GenerateDailyProdAsync(date, token).ConfigureAwait(false);
                }
                else if (type == ReportType.DailyAlarm)
                {
                    await _generator.GenerateDailyAlarmAsync(date, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("自动生成日报失败：" + ex.Message, "warn");
            }
        }

        private async Task EnsureWeeklyAsync(ReportType type, DateTime endDate, CancellationToken token)
        {
            if (_storage.WeeklyReportExists(type, endDate))
            {
                return;
            }

            try
            {
                if (type == ReportType.WeeklyProd)
                {
                    await _generator.GenerateWeeklyProdAsync(endDate, token).ConfigureAwait(false);
                }
                else if (type == ReportType.WeeklyAlarm)
                {
                    await _generator.GenerateWeeklyAlarmAsync(endDate, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("自动生成周报失败：" + ex.Message, "warn");
            }
        }

        private void Log(string message, string level)
        {
            try
            {
                MainWindow.PostProgramInfo(message, level);
            }
            catch
            {
                Debug.WriteLine("[ReportScheduler] " + message);
            }
        }
    }
}
