using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using EW_Assistant.Domain.Reports;
using EW_Assistant.ViewModels;

namespace EW_Assistant.Views.Reports
{
    /// <summary>
    /// 报表中心视图，支持本地报表生成、预览与导出。
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
                FileName = info.FileName ?? "report.md",
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
                MainWindow.PostProgramInfo("报表已导出：" + dialog.FileName, "ok");
            }
            else
            {
                MainWindow.PostProgramInfo("导出失败：" + error, "error");
            }
        }

        private async void RegenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var info = _viewModel.SelectedReport;
            if (info == null)
            {
                return;
            }

            try
            {
                MainWindow.PostProgramInfo("开始重新生成报表：" + info.Title, "info");
                await _viewModel.RegenerateAsync(info);
                MainWindow.PostProgramInfo("报表重新生成完成：" + info.Title, "ok");
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo("重新生成失败：" + ex.Message, "error");
            }
        }

        private void ReportList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindScrollViewer(sender as DependencyObject);
            if (sv == null) return;

            double factor = 1.5;
            double delta = -e.Delta * factor;
            sv.ScrollToVerticalOffset(sv.VerticalOffset + delta);
            e.Handled = true;
        }

        private void PreviewScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer ?? FindScrollViewer(sender as DependencyObject);
            if (sv == null) return;

            double factor = 1.5;
            double delta = -e.Delta * factor;
            sv.ScrollToVerticalOffset(sv.VerticalOffset + delta);
            e.Handled = true;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject source)
        {
            if (source == null) return null;
            if (source is ScrollViewer sv) return sv;
            int count = VisualTreeHelper.GetChildrenCount(source);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(source, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
