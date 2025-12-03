using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace EW_Assistant.ViewModels
{
    /// <summary>
    /// 报表中心视图模型，提供示例数据与占位 Markdown。
    /// </summary>
    public class ReportsCenterViewModel : ViewModelBase
    {
        private string _currentReportType;
        private string _previewMarkdown;
        private string _selectedReport;

        public ObservableCollection<string> ReportItems { get; private set; }

        /// <summary>当前选中的报表类型。</summary>
        public string CurrentReportType
        {
            get { return _currentReportType; }
            set { SetProperty(ref _currentReportType, value); }
        }

        /// <summary>报表列表中选中的示例项。</summary>
        public string SelectedReport
        {
            get { return _selectedReport; }
            set
            {
                if (SetProperty(ref _selectedReport, value))
                {
                    UpdatePreviewMarkdown(value, CurrentReportType);
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
            ReportItems = new ObservableCollection<string>();
            SwitchReportType("当日产能报表");
        }

        /// <summary>
        /// 切换报表类型并填充假数据。
        /// </summary>
        public void SwitchReportType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "当日产能报表";
            }

            CurrentReportType = type;

            var samples = BuildSamples(type);

            ReportItems.Clear();
            foreach (var item in samples)
            {
                ReportItems.Add(item);
            }

            if (ReportItems.Count > 0)
            {
                SelectedReport = ReportItems[0];
            }
            else
            {
                SelectedReport = string.Empty;
                UpdatePreviewMarkdown(string.Empty, type);
            }
        }

        private List<string> BuildSamples(string type)
        {
            var list = new List<string>();

            if (type == "当日产能报表")
            {
                list.Add("2025-12-03 当日产能报表（示例）");
                list.Add("2025-12-02 当日产能报表（示例）");
                list.Add("2025-12-01 当日产能报表（示例）");
            }
            else if (type == "产能周报")
            {
                list.Add("2025-48周 产能周报（示例）");
                list.Add("2025-47周 产能周报（示例）");
                list.Add("2025-46周 产能周报（示例）");
            }
            else if (type == "当日报警报表")
            {
                list.Add("2025-12-03 当日报警报表（示例）");
                list.Add("2025-12-02 当日报警报表（示例）");
                list.Add("2025-12-01 当日报警报表（示例）");
            }
            else if (type == "报警周报")
            {
                list.Add("2025-48周 报警周报（示例）");
                list.Add("2025-47周 报警周报（示例）");
                list.Add("2025-46周 报警周报（示例）");
            }
            else
            {
                list.Add("示例报表 A");
                list.Add("示例报表 B");
            }

            return list;
        }

        private void UpdatePreviewMarkdown(string selected, string type)
        {
            var title = !string.IsNullOrWhiteSpace(selected) ? selected : type + "（示例）";

            var sb = new StringBuilder();
            sb.AppendLine("# " + title);
            sb.AppendLine();
            sb.AppendLine("- 当前仅展示示例内容，后续会替换为真实报表数据。");
            sb.AppendLine("- 报表类型：" + type);
            sb.AppendLine("- 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine("## 关键指标（示例）");
            sb.AppendLine("- 产能：--");
            sb.AppendLine("- 良率：--");
            sb.AppendLine("- 报警次数：--");
            sb.AppendLine("- 备注：等待数据接入。");

            PreviewMarkdown = sb.ToString();
        }
    }
}
