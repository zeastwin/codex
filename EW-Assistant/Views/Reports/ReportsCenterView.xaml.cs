using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using EW_Assistant.Domain.Reports;
using EW_Assistant.ViewModels;

namespace EW_Assistant.Views.Reports
{
    /// <summary>
    /// 报表中心视图，暂时展示占位数据与示例 Markdown。
    /// </summary>
    public partial class ReportsCenterView : UserControl
    {
        private readonly ReportsCenterViewModel _viewModel;

        public ReportsCenterView()
        {
            InitializeComponent();
            _viewModel = new ReportsCenterViewModel();
            DataContext = _viewModel;
        }

        private void DailyProdButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType(ReportType.DailyProd);
        }

        private void WeeklyProdButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType(ReportType.WeeklyProd);
        }

        private void DailyAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType(ReportType.DailyAlarm);
        }

        private void WeeklyAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType(ReportType.WeeklyAlarm);
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var info = _viewModel.SelectedReport;
            if (info == null || string.IsNullOrWhiteSpace(info.FilePath))
            {
                MessageBox.Show("请选择有效的报表文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!File.Exists(info.FilePath))
            {
                MessageBox.Show("报表文件不存在，可能已被移动或删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var args = "/select,\"" + info.FilePath + "\"";
                Process.Start("explorer.exe", args);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开资源管理器失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var info = _viewModel.SelectedReport;
            if (info == null)
            {
                MessageBox.Show("请选择要导出的报表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
                FileName = "客户版_" + (info.FileName ?? "report.md"),
                Title = "导出 Markdown"
            };

            var result = dialog.ShowDialog();
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            string error;
            if (_viewModel.TryExportReport(info, dialog.FileName, out error))
            {
                MessageBox.Show("导出成功。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("导出失败：" + error, "提示", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
