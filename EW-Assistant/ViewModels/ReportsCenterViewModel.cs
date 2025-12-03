using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EW_Assistant.Domain.Reports;
using EW_Assistant.Services.Reports;

namespace EW_Assistant.ViewModels
{
    /// <summary>
    /// 报表中心视图模型，负责绑定本地报表索引与按需加载正文。
    /// </summary>
    public class ReportsCenterViewModel : ViewModelBase
    {
        private readonly ReportStorageService _storage;
        private readonly ReportGeneratorService _generator;
        private ReportType _currentReportType;
        private string _currentReportTypeName;
        private string _previewMarkdown;
        private ReportInfo _selectedReport;
        private bool _isBusy;
        private CancellationTokenSource _cts;

        public ObservableCollection<ReportInfo> Reports { get; private set; }

        /// <summary>当前选中的报表类型。</summary>
        public ReportType CurrentReportType
        {
            get { return _currentReportType; }
            set { SetProperty(ref _currentReportType, value); }
        }

        /// <summary>报表类型中文名称，供 UI 展示。</summary>
        public string CurrentReportTypeName
        {
            get { return _currentReportTypeName; }
            set { SetProperty(ref _currentReportTypeName, value); }
        }

        /// <summary>当前选中的报表项。</summary>
        public ReportInfo SelectedReport
        {
            get { return _selectedReport; }
            set
            {
                if (SetProperty(ref _selectedReport, value))
                {
                    LoadSelectedReportContent(value);
                }
            }
        }

        /// <summary>Markdown 预览内容。</summary>
        public string PreviewMarkdown
        {
            get { return _previewMarkdown; }
            set { SetProperty(ref _previewMarkdown, value); }
        }

        /// <summary>生成或切换时的忙碌状态。</summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            set { SetProperty(ref _isBusy, value); }
        }

        public ReportsCenterViewModel()
        {
            _storage = new ReportStorageService();
            _generator = new ReportGeneratorService(_storage, new LlmReportClient());
            Reports = new ObservableCollection<ReportInfo>();
            SwitchReportType(ReportType.DailyProd);
        }

        /// <summary>
        /// 切换报表类型并重新生成索引列表。
        /// </summary>
        public void SwitchReportType(ReportType type)
        {
            CurrentReportType = type;
            CurrentReportTypeName = _storage.GetTypeDisplayName(type);
            LoadReports(type);
        }

        /// <summary>人工触发：按当前选中项重新生成。</summary>
        public async Task RegenerateAsync(ReportInfo info)
        {
            if (info == null) return;
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                ReportInfo generated = null;
                if (info.Type == ReportType.DailyProd || info.Type == ReportType.DailyAlarm)
                {
                    var date = info.Date ?? DateTime.Today;
                    if (info.Type == ReportType.DailyProd)
                        generated = await _generator.GenerateDailyProdAsync(date, CancellationToken.None);
                    else
                        generated = await _generator.GenerateDailyAsync(info.Type, date, CancellationToken.None);
                }
                else if (info.Type == ReportType.WeeklyProd || info.Type == ReportType.WeeklyAlarm)
                {
                    var start = info.StartDate ?? DateTime.Today.AddDays(-6);
                    var end = info.EndDate ?? DateTime.Today;
                    generated = await _generator.GenerateWeeklyAsync(info.Type, start, end, CancellationToken.None);
                }

                LoadReports(info.Type);
                if (generated != null)
                {
                    var target = FindMatchingReport(generated);
                    if (target != null)
                    {
                        SelectedReport = target;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 触发生成报表并刷新列表。
        /// </summary>
        public async Task GenerateReportAsync(ReportType type)
        {
            // 生成过程中允许切换到其他类型查看已有报表，但不再启动新的生成。
            if (IsBusy)
            {
                if (type != CurrentReportType)
                {
                    SwitchReportType(type);
                }
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            IsBusy = true;
            CurrentReportType = type;
            CurrentReportTypeName = _storage.GetTypeDisplayName(type);

            ReportInfo generated = null;
            var today = DateTime.Today;
            var prevWeekStart = GetPreviousWeekStart(today);
            var prevWeekEnd = prevWeekStart.AddDays(6);

            try
            {
                var needGenerate = true;
                if (type == ReportType.DailyProd)
                {
                    needGenerate = !_storage.DailyReportExists(type, today);
                    if (needGenerate)
                        generated = await _generator.GenerateDailyProdAsync(today, cts.Token);
                }
                else if (type == ReportType.DailyAlarm)
                {
                    needGenerate = !_storage.DailyReportExists(type, today);
                    if (needGenerate)
                        generated = await _generator.GenerateDailyAlarmAsync(today, cts.Token);
                }
                else if (type == ReportType.WeeklyProd)
                {
                    needGenerate = !_storage.WeeklyReportExists(type, prevWeekEnd);
                    if (needGenerate)
                        generated = await _generator.GenerateWeeklyProdAsync(prevWeekEnd, cts.Token);
                }
                else if (type == ReportType.WeeklyAlarm)
                {
                    needGenerate = !_storage.WeeklyReportExists(type, prevWeekEnd);
                    if (needGenerate)
                        generated = await _generator.GenerateWeeklyAlarmAsync(prevWeekEnd, cts.Token);
                }

                LoadReports(type);
                if (generated != null)
                {
                    var target = FindMatchingReport(generated);
                    if (target != null)
                    {
                        SelectedReport = target;
                    }
                }
                else if (!needGenerate && Reports.Count > 0)
                {
                    // 已存在则选择最新一条
                    SelectedReport = Reports[0];
                }
            }
            catch (OperationCanceledException)
            {
                PreviewMarkdown = BuildPlaceholderMarkdown("生成已取消。");
            }
            catch (ReportGenerationException ex)
            {
                PreviewMarkdown = BuildPlaceholderMarkdown(ex.Message);
            }
            catch (Exception ex)
            {
                PreviewMarkdown = BuildPlaceholderMarkdown("生成报表失败：" + ex.Message);
            }
            finally
            {
                if (_cts == cts)
                {
                    _cts = null;
                    try { cts.Dispose(); } catch { }
                }
                IsBusy = false;
            }
        }

        private void LoadReports(ReportType type)
        {
            IList<ReportInfo> reports = null;
            Reports.Clear();
            SelectedReport = null;
            PreviewMarkdown = BuildPlaceholderMarkdown("正在加载报表列表，请稍候...");

            try
            {
                reports = _storage.GetReportsByType(type);
            }
            catch (Exception ex)
            {
                PreviewMarkdown = BuildPlaceholderMarkdown("扫描报表目录时出错：" + ex.Message);
                return;
            }

            if (reports != null)
            {
                foreach (var r in reports)
                {
                    Reports.Add(r);
                }
            }

            if (Reports.Count > 0)
            {
                SelectedReport = Reports[0];
            }
            else
            {
                SelectedReport = null;
                PreviewMarkdown = BuildPlaceholderMarkdown("当前类型暂无报表文件。");
            }
        }

        private void LoadSelectedReportContent(ReportInfo info)
        {
            if (info == null)
            {
                PreviewMarkdown = BuildPlaceholderMarkdown("请选择左侧报表以查看内容。");
                return;
            }

            try
            {
                var md = _storage.ReadReportContent(info);
                if (string.IsNullOrWhiteSpace(md))
                {
                    PreviewMarkdown = BuildPlaceholderMarkdown("该报表文件为空，等待后续生成内容。", info.Title);
                }
                else
                {
                    PreviewMarkdown = md;
                }
            }
            catch (Exception ex)
            {
                PreviewMarkdown = BuildPlaceholderMarkdown("读取报表失败：" + ex.Message, info.Title);
            }
        }

        private string BuildPlaceholderMarkdown(string message, string title = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + (title ?? CurrentReportTypeName ?? "报表预览"));
            sb.AppendLine();
            sb.AppendLine("> " + message);
            sb.AppendLine();
            sb.AppendLine("- 报表类型：" + (CurrentReportTypeName ?? "未选择"));
            sb.AppendLine("- 更新时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            return sb.ToString();
        }

        /// <summary>
        /// 导出报表文件，失败时返回错误信息。
        /// </summary>
        public bool TryExportReport(ReportInfo info, string targetPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (info == null)
            {
                errorMessage = "未选择报表。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                errorMessage = "未指定导出路径。";
                return false;
            }

            try
            {
                _storage.ExportReportFile(info, targetPath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private ReportInfo FindMatchingReport(ReportInfo generated)
        {
            if (generated == null || Reports == null || Reports.Count == 0)
            {
                return null;
            }

            return Reports.FirstOrDefault(r =>
                (!string.IsNullOrWhiteSpace(generated.Id) && string.Equals(r.Id, generated.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(generated.FilePath) && string.Equals(r.FilePath, generated.FilePath, StringComparison.OrdinalIgnoreCase)));
        }

        private DateTime GetPreviousWeekStart(DateTime referenceDate)
        {
            var day = referenceDate.Date;
            int diff = (int)day.DayOfWeek - (int)DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            var currentWeekStart = day.AddDays(-diff);
            return currentWeekStart.AddDays(-7);
        }
    }
}
