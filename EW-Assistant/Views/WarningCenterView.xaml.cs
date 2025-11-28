using EW_Assistant.ViewModels;
using EW_Assistant.Warnings;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace EW_Assistant.Views
{
    /// <summary>
    /// 预警中心视图。
    /// </summary>
    public partial class WarningCenterView : UserControl
    {
        public WarningCenterViewModel ViewModel { get; }

        public WarningCenterView()
        {
            ViewModel = new WarningCenterViewModel();
            InitializeComponent();
            DataContext = ViewModel;
            AiMarkdownViewer.PreviewMouseWheel += AiMarkdownViewer_PreviewMouseWheel;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateFilterButtons(ViewModel.FilterStatus);

            // 后台生成缺失的 AI 分析结果
            var _ = ViewModel.AnalyzeMissingWarningsAsync();

            StartAlarmMonitor();
        }

        private void StartAlarmMonitor()
        {
            _lastAlarmWriteTime = GetLatestAlarmWriteTime();
            _lastProdWriteTime = GetLatestProductionWriteTime();
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(10);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void WarningList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 选中切换时将 AI 分析滚动条复位到顶部
            Dispatcher.BeginInvoke(new Action(ScrollAiCardToTop), DispatcherPriority.Background);
        }

        private void ScrollAiCardToTop()
        {
            if (AiMarkdownViewer == null) return;
            AiMarkdownViewer.UpdateLayout();
            var scroller = FindChildScrollViewer(AiMarkdownViewer);
            if (scroller != null)
            {
                scroller.ScrollToVerticalOffset(0);
            }
        }

        private void AiMarkdownViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scroller = FindChildScrollViewer(AiMarkdownViewer);
            if (scroller == null) return;

            // 放大滚动距离，提升滚轮滚动速度
            var multiplier = 1.5;
            var target = scroller.VerticalOffset - e.Delta * multiplier;
            scroller.ScrollToVerticalOffset(target);
            e.Handled = true;
        }

        private static ScrollViewer FindChildScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                var result = FindChildScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var latestAlarm = GetLatestAlarmWriteTime();
                var latestProd = GetLatestProductionWriteTime();

                if (latestAlarm > _lastAlarmWriteTime || latestProd > _lastProdWriteTime)
                {
                    _lastAlarmWriteTime = latestAlarm > _lastAlarmWriteTime ? latestAlarm : _lastAlarmWriteTime;
                    _lastProdWriteTime = latestProd > _lastProdWriteTime ? latestProd : _lastProdWriteTime;
                    ViewModel.LoadWarningsFromCsv();
                    var _ = ViewModel.AnalyzeMissingWarningsAsync();
                }
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo("预警数据刷新失败：" + ex.Message, "warn");
            }
        }

        private void BtnProcessed_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.MarkProcessedSelected();
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not ToggleButton btn) return;
                var tag = btn.Tag as string;
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    ViewModel.FilterStatus = tag;
                    UpdateFilterButtons(tag);
                }
            }
            catch
            {
                // ignore UI errors
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.FilterStatus))
            {
                UpdateFilterButtons(ViewModel.FilterStatus);
            }
        }

        private void UpdateFilterButtons(string status)
        {
            var s = (status ?? string.Empty).Trim().ToLowerInvariant();
            BtnFilterPending.IsChecked = s == "pending" || string.IsNullOrEmpty(s);
            BtnFilterProcessed.IsChecked = s == "processed";
            BtnFilterResolved.IsChecked = s == "resolved";
        }

        private DateTime GetLatestAlarmWriteTime()
        {
            try
            {
                var root = LocalDataConfig.AlarmCsvRoot;
                var watchMode = LocalDataConfig.WatchMode;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    return _lastAlarmWriteTime;

                return watchMode
                    ? GetLatestWatchModeWriteTime(root)
                    : GetLatestFlatWriteTime(root);
            }
            catch
            {
                return _lastAlarmWriteTime;
            }
        }

        private DateTime GetLatestFlatWriteTime(string root)
        {
            var max = DateTime.MinValue;
            var files = Directory.GetFiles(root, "*.csv", SearchOption.TopDirectoryOnly);
            foreach (var path in files)
            {
                var t = File.GetLastWriteTime(path);
                if (t > max) max = t;
            }
            return max == DateTime.MinValue ? _lastAlarmWriteTime : max;
        }

        private DateTime GetLatestWatchModeWriteTime(string root)
        {
            var max = DateTime.MinValue;
            var dirs = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    continue;

                var files = Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly);
                foreach (var path in files)
                {
                    var t = File.GetLastWriteTime(path);
                    if (t > max) max = t;
                }
            }
            return max == DateTime.MinValue ? _lastAlarmWriteTime : max;
        }

        private DateTime GetLatestProductionWriteTime()
        {
            try
            {
                var root = LocalDataConfig.ProductionCsvRoot;
                var watchMode = LocalDataConfig.WatchMode;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    return _lastProdWriteTime;

                return watchMode
                    ? GetLatestWatchModeProdWriteTime(root)
                    : GetLatestFlatProdWriteTime(root);
            }
            catch
            {
                return _lastProdWriteTime;
            }
        }

        private DateTime GetLatestFlatProdWriteTime(string root)
        {
            var max = DateTime.MinValue;
            var files = Directory.GetFiles(root, "*.csv", SearchOption.TopDirectoryOnly);
            foreach (var path in files)
            {
                var t = File.GetLastWriteTime(path);
                if (t > max) max = t;
            }
            return max == DateTime.MinValue ? _lastProdWriteTime : max;
        }

        private DateTime GetLatestWatchModeProdWriteTime(string root)
        {
            var max = DateTime.MinValue;
            var dirs = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    continue;

                var files = Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly);
                foreach (var path in files)
                {
                    var t = File.GetLastWriteTime(path);
                    if (t > max) max = t;
                }
            }
            return max == DateTime.MinValue ? _lastProdWriteTime : max;
        }

        private DispatcherTimer _refreshTimer;
        private DateTime _lastAlarmWriteTime = DateTime.MinValue;
        private DateTime _lastProdWriteTime = DateTime.MinValue;
    }
}
