using EW_Assistant.ViewModels;
using EW_Assistant.Warnings;
using System;
using System.IO;
using System.Windows.Controls;
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

            // 后台生成缺失的 AI 分析结果
            var _ = ViewModel.AnalyzeMissingWarningsAsync();

            StartAlarmMonitor();
        }

        private void StartAlarmMonitor()
        {
            _lastAlarmWriteTime = GetLatestAlarmWriteTime();
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(10);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var latest = GetLatestAlarmWriteTime();
                if (latest > _lastAlarmWriteTime)
                {
                    _lastAlarmWriteTime = latest;
                    ViewModel.LoadWarningsFromCsv();
                    var _ = ViewModel.AnalyzeMissingWarningsAsync();
                }
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo("预警数据刷新失败：" + ex.Message, "warn");
            }
        }

        private DateTime GetLatestAlarmWriteTime()
        {
            try
            {
                var root = LocalDataConfig.AlarmCsvRoot;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    return _lastAlarmWriteTime;

                var max = DateTime.MinValue;
                var files = Directory.GetFiles(root, "*.csv");
                foreach (var path in files)
                {
                    var t = File.GetLastWriteTime(path);
                    if (t > max) max = t;
                }
                return max;
            }
            catch
            {
                return _lastAlarmWriteTime;
            }
        }

        private DispatcherTimer _refreshTimer;
        private DateTime _lastAlarmWriteTime = DateTime.MinValue;
    }
}
