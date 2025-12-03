using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
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
        private ReportType _currentReportType;
        private string _currentReportTypeName;
        private string _previewMarkdown;
        private ReportInfo _selectedReport;

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

        public ReportsCenterViewModel()
        {
            _storage = new ReportStorageService();
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

        private void LoadReports(ReportType type)
        {
            IList<ReportInfo> reports = null;
            Reports.Clear();
            SelectedReport = null;

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
            sb.AppendLine("# " + (title ?? CurrentReportTypeName));
            sb.AppendLine();
            sb.AppendLine("> " + message);
            sb.AppendLine();
            sb.AppendLine("- 报表类型：" + CurrentReportTypeName);
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
    }
}
