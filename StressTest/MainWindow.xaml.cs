using StressTest.Core;
using StressTest.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StressTest
{
    /// <summary>
    /// 主窗口逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<TimeSeriesPoint> _seriesRows = new ObservableCollection<TimeSeriesPoint>();
        private readonly ObservableCollection<string> _logLines = new ObservableCollection<string>();
        private readonly DispatcherTimer _uiTimer;
        private readonly HttpClient _httpClient;
        private ScrollViewer _timeSeriesScrollViewer;
        private bool _userScrollLocked;
        private bool _isAutoScrolling;

        private MetricsAggregator _metrics;
        private StressRunner _runner;
        private StressConfig _config;
        private AppConfigSnapshot _appConfig;
        private CancellationTokenSource _cts;
        private DateTime _startLocal;
        private bool _running;
        private TimeSeriesPoint[] _lastSeries = Array.Empty<TimeSeriesPoint>();
        private int _chartWindowSeconds = 60;

        public MainWindow()
        {
            InitializeComponent();

            _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uiTimer.Tick += (_, __) => RefreshUi();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TimeSeriesGrid.ItemsSource = _seriesRows;
            LoadAppConfig();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _ = StartAsync();
        }

        private async Task StartAsync()
        {
            if (_running)
                return;

            if (!TryParseInputs(out var durationSeconds, out var deviceCount))
                return;

            try
            {
                _config = new StressConfig(durationSeconds, deviceCount);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!LoadAppConfig())
            {
                MessageBox.Show("读取 AppConfig.json 失败，请检查路径与编码。", "配置读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_appConfig.Url) || string.IsNullOrWhiteSpace(_appConfig.AutoKey))
            {
                MessageBox.Show("AppConfig.json 中 URL 或 AutoKey 为空，无法启动压测。", "配置缺失", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _metrics = new MetricsAggregator();
            var client = new WorkflowSseClient(_httpClient);
            _runner = new StressRunner(_config, _appConfig, _metrics, client, AppendLog);

            _cts = new CancellationTokenSource();
            _running = true;
            _startLocal = DateTime.Now;
            _seriesRows.Clear();
            _logLines.Clear();
            _lastSeries = Array.Empty<TimeSeriesPoint>();
            _userScrollLocked = false;

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            DurationTextBox.IsEnabled = false;
            DeviceCountTextBox.IsEnabled = false;
            SetStatus("运行中", "StatusRunningBg");

            _uiTimer.Start();

            try
            {
                await _runner.RunAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                AppendLog("运行异常：" + ex.Message);
            }
            finally
            {
                _running = false;
                _uiTimer.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                DurationTextBox.IsEnabled = true;
                DeviceCountTextBox.IsEnabled = true;
                if (_cts.IsCancellationRequested)
                    SetStatus("已停止", "StatusStoppingBg");
                else
                    SetStatus("已完成", "StatusDoneBg");

                RefreshUi();
                ExportReport();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_running)
                return;

            SetStatus("停止中", "StatusStoppingBg");
            _cts?.Cancel();
        }

        private void OpenDocButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var docPath = ResolveDocPath();
                var window = string.IsNullOrWhiteSpace(docPath)
                    ? new DocWindow("说明文档不存在，请确认已生成并放在 Docs 目录。")
                    : DocWindow.FromFile(docPath);
                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开说明文档失败：" + ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new HistoryWindow
                {
                    Owner = this
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开历史分析失败：" + ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RangeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender == Last60Toggle)
            {
                Last60Toggle.IsChecked = true;
                AllToggle.IsChecked = false;
                _chartWindowSeconds = 60;
                RangeHintText.Text = "最近 60 秒";
            }
            else
            {
                AllToggle.IsChecked = true;
                Last60Toggle.IsChecked = false;
                _chartWindowSeconds = 0;
                RangeHintText.Text = "全量";
            }

            UpdateCharts(_lastSeries);
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCharts(_lastSeries);
        }

        private bool LoadAppConfig()
        {
            try
            {
                _appConfig = AppConfigReader.Load(AppConfigReader.DefaultPath);
                ConfigUrlText.Text = string.IsNullOrWhiteSpace(_appConfig.Url) ? "-" : _appConfig.Url.Trim();
                ConfigKeyText.Text = MaskKey(_appConfig.AutoKey);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("读取 AppConfig.json 失败：" + ex.Message);
                ConfigUrlText.Text = "-";
                ConfigKeyText.Text = "-";
                return false;
            }
        }

        private bool TryParseInputs(out int durationSeconds, out int deviceCount)
        {
            durationSeconds = 0;
            deviceCount = 0;

            if (!int.TryParse(DurationTextBox.Text, out durationSeconds) || durationSeconds <= 0)
            {
                MessageBox.Show("请输入有效的压测时长（秒）。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(DeviceCountTextBox.Text, out deviceCount) || deviceCount <= 0)
            {
                MessageBox.Show("请输入有效的设备数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void RefreshUi()
        {
            if (_metrics == null)
                return;

            var snapshot = _metrics.BuildSnapshot();
            UpdateSummary(snapshot);
            UpdateSeries(snapshot.Series);
            UpdateCharts(_lastSeries);
        }

        private void UpdateSummary(MetricsSnapshot snapshot)
        {
            TotalRequestsText.Text = snapshot.TotalRequests.ToString();
            SuccessRequestsText.Text = snapshot.SuccessRequests.ToString();
            FailedRequestsText.Text = snapshot.FailedRequests.ToString();
            CanceledRequestsText.Text = snapshot.CanceledRequests.ToString();
            InflightText.Text = snapshot.Inflight.ToString();
            CurrentRpsText.Text = snapshot.CurrentRps.ToString("0.0");
            AvgRpsText.Text = snapshot.AvgRps.ToString("0.0");
            AvgLatencyText.Text = snapshot.AvgLatencyMs.ToString("0.0");
            P95LatencyText.Text = snapshot.P95LatencyMs.ToString("0.0");
            AvgTtfbText.Text = snapshot.AvgTtfbMs.ToString("0.0");
            P95TtfbText.Text = snapshot.P95TtfbMs.ToString("0.0");
            ErrorRateText.Text = snapshot.ErrorRate.ToString("0.0") + "%";

            var elapsed = (snapshot.EndTimeUtc ?? DateTime.UtcNow) - snapshot.StartTimeUtc;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            var duration = _config != null ? TimeSpan.FromSeconds(_config.DurationSeconds) : TimeSpan.Zero;
            var remaining = duration - elapsed;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            ElapsedText.Text = "已运行 " + FormatTime(elapsed);
            RemainingText.Text = "剩余 " + FormatTime(remaining);
        }

        private void UpdateSeries(System.Collections.Generic.IReadOnlyList<TimeSeriesPoint> series)
        {
            _lastSeries = series?.ToArray() ?? Array.Empty<TimeSeriesPoint>();

            var count = _lastSeries.Length;
            for (var i = 0; i < count; i++)
            {
                if (i < _seriesRows.Count)
                    _seriesRows[i] = _lastSeries[i];
                else
                    _seriesRows.Add(_lastSeries[i]);
            }

            while (_seriesRows.Count > count)
                _seriesRows.RemoveAt(_seriesRows.Count - 1);

            if (!_userScrollLocked)
                ScrollToLatest();
        }

        private void UpdateCharts(TimeSeriesPoint[] series)
        {
            if (series == null || series.Length == 0)
            {
                ClearChart(RpsPolyline, RpsAxisX, RpsAxisY, RpsAxisMinText, RpsAxisMaxText, RpsAxisStartText, RpsAxisEndText, RpsStartMarker, RpsEndMarker, RpsStartValueText, RpsEndValueText);
                ClearChart(LatencyPolyline, LatencyAxisX, LatencyAxisY, LatencyAxisMinText, LatencyAxisMaxText, LatencyAxisStartText, LatencyAxisEndText, LatencyStartMarker, LatencyEndMarker, LatencyStartValueText, LatencyEndValueText);
                ClearChart(TtfbPolyline, TtfbAxisX, TtfbAxisY, TtfbAxisMinText, TtfbAxisMaxText, TtfbAxisStartText, TtfbAxisEndText, TtfbStartMarker, TtfbEndMarker, TtfbStartValueText, TtfbEndValueText);
                ClearChart(ErrorPolyline, ErrorAxisX, ErrorAxisY, ErrorAxisMinText, ErrorAxisMaxText, ErrorAxisStartText, ErrorAxisEndText, ErrorStartMarker, ErrorEndMarker, ErrorStartValueText, ErrorEndValueText);
                RpsValueText.Text = "0";
                LatencyValueText.Text = "0";
                TtfbValueText.Text = "0";
                ErrorValueText.Text = "0";
                RpsChartValueText.Text = "最新 -";
                LatencyChartValueText.Text = "最新 -";
                TtfbChartValueText.Text = "最新 -";
                ErrorChartValueText.Text = "最新 -";
                return;
            }

            var chartSeries = series;
            if (_chartWindowSeconds > 0 && series.Length > _chartWindowSeconds)
                chartSeries = series.Skip(series.Length - _chartWindowSeconds).ToArray();

            var rpsValues = chartSeries.Select(p => (double)p.Count).ToArray();
            var latencyValues = chartSeries.Select(p => p.P95LatencyMs).ToArray();
            var ttfbValues = chartSeries.Select(p => p.AvgTtfbMs).ToArray();
            var errorValues = chartSeries.Select(p => p.ErrorRate).ToArray();

            var startSecond = chartSeries.First().Second;
            var endSecond = chartSeries.Last().Second;

            DrawChart(RpsChartCanvas, RpsPolyline, RpsAxisX, RpsAxisY, RpsAxisMinText, RpsAxisMaxText, RpsAxisStartText, RpsAxisEndText, RpsStartMarker, RpsEndMarker, RpsStartValueText, RpsEndValueText, rpsValues, startSecond, endSecond, "0.0", string.Empty, false);
            DrawChart(LatencyChartCanvas, LatencyPolyline, LatencyAxisX, LatencyAxisY, LatencyAxisMinText, LatencyAxisMaxText, LatencyAxisStartText, LatencyAxisEndText, LatencyStartMarker, LatencyEndMarker, LatencyStartValueText, LatencyEndValueText, latencyValues, startSecond, endSecond, "0.0", "ms", false);
            DrawChart(TtfbChartCanvas, TtfbPolyline, TtfbAxisX, TtfbAxisY, TtfbAxisMinText, TtfbAxisMaxText, TtfbAxisStartText, TtfbAxisEndText, TtfbStartMarker, TtfbEndMarker, TtfbStartValueText, TtfbEndValueText, ttfbValues, startSecond, endSecond, "0.0", "ms", false);
            DrawChart(ErrorChartCanvas, ErrorPolyline, ErrorAxisX, ErrorAxisY, ErrorAxisMinText, ErrorAxisMaxText, ErrorAxisStartText, ErrorAxisEndText, ErrorStartMarker, ErrorEndMarker, ErrorStartValueText, ErrorEndValueText, errorValues, startSecond, endSecond, "0.0", string.Empty, true);

            var latest = series[series.Length - 1];
            RpsValueText.Text = latest.Count.ToString("0.0");
            LatencyValueText.Text = latest.P95LatencyMs.ToString("0.0");
            TtfbValueText.Text = latest.AvgTtfbMs.ToString("0.0");
            ErrorValueText.Text = latest.ErrorRate.ToString("0.0") + "%";
            RpsChartValueText.Text = $"最新 {latest.Count:0.0}";
            LatencyChartValueText.Text = $"最新 {latest.P95LatencyMs:0.0} ms";
            TtfbChartValueText.Text = $"最新 {latest.AvgTtfbMs:0.0} ms";
            ErrorChartValueText.Text = $"最新 {latest.ErrorRate:0.0}%";
        }

        private static void DrawChart(
            Canvas canvas,
            Polyline polyline,
            Line axisX,
            Line axisY,
            TextBlock axisMinText,
            TextBlock axisMaxText,
            TextBlock axisStartText,
            TextBlock axisEndText,
            Rectangle startMarker,
            Rectangle endMarker,
            TextBlock startValueText,
            TextBlock endValueText,
            double[] values,
            int startSecond,
            int endSecond,
            string valueFormat,
            string unit,
            bool percent)
        {
            if (canvas.ActualWidth < 2 || canvas.ActualHeight < 2 || values.Length < 2)
            {
                ClearChart(polyline, axisX, axisY, axisMinText, axisMaxText, axisStartText, axisEndText, startMarker, endMarker, startValueText, endValueText);
                return;
            }

            var min = values.Min();
            var max = values.Max();
            if (Math.Abs(max - min) < 0.001)
                max = min + 1;

            const double leftPadding = 34;
            const double rightPadding = 10;
            const double topPadding = 8;
            const double bottomPadding = 18;
            var width = canvas.ActualWidth - leftPadding - rightPadding;
            var height = canvas.ActualHeight - topPadding - bottomPadding;
            if (width <= 0 || height <= 0)
            {
                ClearChart(polyline, axisX, axisY, axisMinText, axisMaxText, axisStartText, axisEndText, startMarker, endMarker, startValueText, endValueText);
                return;
            }

            axisX.X1 = leftPadding;
            axisX.X2 = leftPadding + width;
            axisX.Y1 = topPadding + height;
            axisX.Y2 = axisX.Y1;
            axisY.X1 = leftPadding;
            axisY.X2 = leftPadding;
            axisY.Y1 = topPadding;
            axisY.Y2 = topPadding + height;

            SetVisibility(Visibility.Visible, axisX, axisY, axisMinText, axisMaxText, axisStartText, axisEndText, startMarker, endMarker, startValueText, endValueText);

            axisMaxText.Text = FormatValue(max, valueFormat, unit, percent, false);
            axisMinText.Text = FormatValue(min, valueFormat, unit, percent, false);
            axisStartText.Text = $"{startSecond}s";
            axisEndText.Text = $"{endSecond}s";

            Canvas.SetLeft(axisMaxText, 2);
            Canvas.SetTop(axisMaxText, Math.Max(0, topPadding - 6));
            Canvas.SetLeft(axisMinText, 2);
            Canvas.SetTop(axisMinText, Math.Max(0, topPadding + height - 10));
            Canvas.SetLeft(axisStartText, Math.Max(0, leftPadding - 6));
            Canvas.SetTop(axisStartText, topPadding + height + 2);
            Canvas.SetLeft(axisEndText, Math.Max(0, leftPadding + width - axisEndText.Width));
            Canvas.SetTop(axisEndText, topPadding + height + 2);

            var stepX = width / (values.Length - 1);
            var points = new PointCollection(values.Length);

            for (var i = 0; i < values.Length; i++)
            {
                var x = leftPadding + stepX * i;
                var ratio = (values[i] - min) / (max - min);
                var y = topPadding + (1 - ratio) * height;
                points.Add(new Point(x, y));
            }

            polyline.Points = points;

            var startPoint = points[0];
            var endPoint = points[points.Count - 1];

            SetMarkerPosition(canvas, startMarker, startPoint);
            SetMarkerPosition(canvas, endMarker, endPoint);

            startValueText.Text = FormatValue(values[0], valueFormat, unit, percent, true);
            endValueText.Text = FormatValue(values[values.Length - 1], valueFormat, unit, percent, true);

            SetValueLabelPosition(canvas, startValueText, startPoint, true);
            SetValueLabelPosition(canvas, endValueText, endPoint, false);
        }

        private static void ClearChart(Polyline polyline, params UIElement[] elements)
        {
            polyline.Points = new PointCollection();
            SetVisibility(Visibility.Collapsed, elements);
        }

        private static void SetVisibility(Visibility visibility, params UIElement[] elements)
        {
            foreach (var element in elements)
                element.Visibility = visibility;
        }

        private static void SetMarkerPosition(Canvas canvas, FrameworkElement marker, Point point)
        {
            var width = marker.Width;
            var height = marker.Height;
            SetElementPosition(canvas, marker, point.X - width / 2, point.Y - height / 2);
        }

        private static void SetValueLabelPosition(Canvas canvas, FrameworkElement label, Point point, bool alignLeft)
        {
            var width = label.Width > 0 ? label.Width : 60;
            var x = alignLeft ? point.X + 6 : point.X - width - 6;
            var y = point.Y - 18;
            SetElementPosition(canvas, label, x, y);
        }

        private static void SetElementPosition(Canvas canvas, FrameworkElement element, double x, double y)
        {
            var width = element.Width > 0 ? element.Width : element.ActualWidth;
            var height = element.Height > 0 ? element.Height : element.ActualHeight;
            var maxX = Math.Max(0, canvas.ActualWidth - width);
            var maxY = Math.Max(0, canvas.ActualHeight - height);
            Canvas.SetLeft(element, Math.Max(0, Math.Min(x, maxX)));
            Canvas.SetTop(element, Math.Max(0, Math.Min(y, maxY)));
        }

        private static string FormatValue(double value, string format, string unit, bool percent, bool includeUnit)
        {
            var text = value.ToString(format);
            if (percent)
                return text + "%";
            if (includeUnit && !string.IsNullOrWhiteSpace(unit))
                return text + " " + unit;
            return text;
        }

        private void ExportReport()
        {
            try
            {
                var snapshot = _metrics?.BuildSnapshot();
                if (snapshot == null || _config == null)
                    return;

                var folder = ReportExporter.PrepareReportFolder(_startLocal, _config);
                ReportExporter.Export(folder, _config, snapshot, ChartsPanel);
                AppendLog("报告已导出：" + folder);
            }
            catch (Exception ex)
            {
                AppendLog("导出失败：" + ex.Message);
            }
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                _logLines.Add(line);
                while (_logLines.Count > 200)
                    _logLines.RemoveAt(0);
            });
        }

        private static string MaskKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "-";

            if (key.Length <= 8)
                return new string('*', key.Length);

            return key.Substring(0, 4) + "..." + key.Substring(key.Length - 4);
        }

        private static string FormatTime(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return span.ToString("hh\\:mm\\:ss");

            return span.ToString("mm\\:ss");
        }

        private void SetStatus(string text, string bgResourceKey)
        {
            StatusText.Text = text;
            var brush = TryFindResource(bgResourceKey) as Brush;
            if (brush != null)
                StatusBadge.Background = brush;
        }

        private void TimeSeriesGrid_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _timeSeriesScrollViewer = FindScrollViewer(TimeSeriesGrid);
                if (_timeSeriesScrollViewer != null)
                    _timeSeriesScrollViewer.ScrollChanged += TimeSeriesScrollViewer_ScrollChanged;
            }), DispatcherPriority.Loaded);
        }

        private void TimeSeriesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isAutoScrolling)
                return;

            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                return;

            if (Math.Abs(e.VerticalChange) < 0.1)
                return;

            if (sender is ScrollViewer viewer)
                _userScrollLocked = !IsAtBottom(viewer);
        }

        private static bool IsAtBottom(ScrollViewer viewer)
        {
            if (viewer.ScrollableHeight <= 0)
                return true;

            return viewer.VerticalOffset >= viewer.ScrollableHeight - 1;
        }

        private void ScrollToLatest()
        {
            if (_seriesRows.Count == 0)
                return;

            var last = _seriesRows[_seriesRows.Count - 1];
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isAutoScrolling = true;
                try
                {
                    TimeSeriesGrid.UpdateLayout();
                    TimeSeriesGrid.ScrollIntoView(last);
                    TimeSeriesGrid.UpdateLayout();
                    TimeSeriesGrid.ScrollIntoView(last);
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isAutoScrolling = false;
                    }), DispatcherPriority.Background);
                }
            }), DispatcherPriority.Background);
        }

        private static ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            if (parent == null)
                return null;

            if (parent is ScrollViewer viewer)
                return viewer;

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var found = FindScrollViewer(child);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string ResolveDocPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var primary = IOPath.Combine(baseDir, "Docs", "StressTest说明.md");
            if (File.Exists(primary))
                return primary;

            var fallback = IOPath.Combine(baseDir, "StressTest说明.md");
            if (File.Exists(fallback))
                return fallback;

            var devPath = IOPath.GetFullPath(IOPath.Combine(baseDir, "..", "..", "Docs", "StressTest说明.md"));
            return File.Exists(devPath) ? devPath : null;
        }
    }
}
