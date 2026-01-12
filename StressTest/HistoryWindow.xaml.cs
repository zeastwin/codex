using StressTest.Core;
using StressTest.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StressTest
{
    public partial class HistoryWindow : Window
    {
        private readonly ObservableCollection<HistoryReportSummary> _reports = new ObservableCollection<HistoryReportSummary>();
        private readonly ObservableCollection<ErrorItem> _errorRows = new ObservableCollection<ErrorItem>();
        private TimeSeriesPoint[] _currentSeries = Array.Empty<TimeSeriesPoint>();
        private string _baseFolder;

        public HistoryWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ReportList.ItemsSource = _reports;
            ErrorGrid.ItemsSource = _errorRows;
            LoadReports();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadReports();
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = _baseFolder;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    MessageBox.Show("报告目录不存在。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开目录失败：" + ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ReportList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReportList.SelectedItem is HistoryReportSummary summary)
                LoadReport(summary.FolderPath);
            else
                ClearReportView();
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCharts(_currentSeries);
        }

        private void LoadReports()
        {
            _baseFolder = ReportExporter.GetReportBaseFolder();
            BaseFolderText.Text = string.IsNullOrWhiteSpace(_baseFolder) ? "-" : _baseFolder;

            _reports.Clear();
            var summaries = HistoryReportReader.LoadSummaries(_baseFolder);
            foreach (var item in summaries)
                _reports.Add(item);

            EmptyHintText.Visibility = _reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_reports.Count > 0)
                ReportList.SelectedIndex = 0;
            else
                ClearReportView();
        }

        private void LoadReport(string folder)
        {
            var detail = HistoryReportReader.LoadDetail(folder);
            if (detail == null)
            {
                ClearReportView();
                return;
            }

            UpdateSummary(detail.Summary);
            UpdateSeries(detail.Series);
            UpdateErrors(detail.Errors);
            UpdateCharts(_currentSeries);
        }

        private void UpdateSummary(HistoryReportSummary summary)
        {
            if (summary == null)
            {
                TotalRequestsText.Text = "-";
                AvgRpsText.Text = "-";
                P95LatencyText.Text = "-";
                ErrorRateText.Text = "-";
                StartTimeText.Text = "-";
                EndTimeText.Text = "-";
                DurationText.Text = "-";
                DeviceText.Text = "-";
                ResultText.Text = "-";
                AvgTtfbText.Text = "-";
                return;
            }

            TotalRequestsText.Text = summary.TotalRequests.ToString();
            AvgRpsText.Text = summary.AvgRps.ToString("0.0");
            P95LatencyText.Text = summary.P95LatencyMs.ToString("0.0");
            ErrorRateText.Text = summary.ErrorRate.ToString("0.0") + "%";

            StartTimeText.Text = summary.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            EndTimeText.Text = summary.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            DurationText.Text = summary.DurationSeconds > 0 ? summary.DurationSeconds + " s" : "-";
            DeviceText.Text = summary.DeviceCount > 0 ? summary.DeviceCount + " 台" : "-";
            ResultText.Text = $"{summary.SuccessRequests}/{summary.FailedRequests}/{summary.CanceledRequests}";
            AvgTtfbText.Text = summary.AvgTtfbMs > 0 ? summary.AvgTtfbMs.ToString("0.0") + " ms" : "0.0 ms";
        }

        private void UpdateSeries(System.Collections.Generic.IReadOnlyList<TimeSeriesPoint> series)
        {
            _currentSeries = series?.ToArray() ?? Array.Empty<TimeSeriesPoint>();
        }

        private void UpdateErrors(System.Collections.Generic.IReadOnlyList<ErrorItem> errors)
        {
            _errorRows.Clear();
            if (errors == null)
                return;

            foreach (var item in errors)
                _errorRows.Add(item);
        }

        private void UpdateCharts(TimeSeriesPoint[] series)
        {
            if (series == null || series.Length == 0)
            {
                ClearChart(RpsPolyline, RpsAxisX, RpsAxisY, RpsAxisMinText, RpsAxisMaxText, RpsAxisStartText, RpsAxisEndText, RpsStartMarker, RpsEndMarker, RpsStartValueText, RpsEndValueText);
                ClearChart(LatencyPolyline, LatencyAxisX, LatencyAxisY, LatencyAxisMinText, LatencyAxisMaxText, LatencyAxisStartText, LatencyAxisEndText, LatencyStartMarker, LatencyEndMarker, LatencyStartValueText, LatencyEndValueText);
                ClearChart(TtfbPolyline, TtfbAxisX, TtfbAxisY, TtfbAxisMinText, TtfbAxisMaxText, TtfbAxisStartText, TtfbAxisEndText, TtfbStartMarker, TtfbEndMarker, TtfbStartValueText, TtfbEndValueText);
                ClearChart(ErrorPolyline, ErrorAxisX, ErrorAxisY, ErrorAxisMinText, ErrorAxisMaxText, ErrorAxisStartText, ErrorAxisEndText, ErrorStartMarker, ErrorEndMarker, ErrorStartValueText, ErrorEndValueText);
                RpsChartValueText.Text = "最新 -";
                LatencyChartValueText.Text = "最新 -";
                TtfbChartValueText.Text = "最新 -";
                ErrorChartValueText.Text = "最新 -";
                return;
            }

            var rpsValues = series.Select(p => (double)p.Count).ToArray();
            var latencyValues = series.Select(p => p.P95LatencyMs).ToArray();
            var ttfbValues = series.Select(p => p.AvgTtfbMs).ToArray();
            var errorValues = series.Select(p => p.ErrorRate).ToArray();

            var startSecond = series.First().Second;
            var endSecond = series.Last().Second;

            DrawChart(RpsChartCanvas, RpsPolyline, RpsAxisX, RpsAxisY, RpsAxisMinText, RpsAxisMaxText, RpsAxisStartText, RpsAxisEndText, RpsStartMarker, RpsEndMarker, RpsStartValueText, RpsEndValueText, rpsValues, startSecond, endSecond, "0.0", string.Empty, false);
            DrawChart(LatencyChartCanvas, LatencyPolyline, LatencyAxisX, LatencyAxisY, LatencyAxisMinText, LatencyAxisMaxText, LatencyAxisStartText, LatencyAxisEndText, LatencyStartMarker, LatencyEndMarker, LatencyStartValueText, LatencyEndValueText, latencyValues, startSecond, endSecond, "0.0", "ms", false);
            DrawChart(TtfbChartCanvas, TtfbPolyline, TtfbAxisX, TtfbAxisY, TtfbAxisMinText, TtfbAxisMaxText, TtfbAxisStartText, TtfbAxisEndText, TtfbStartMarker, TtfbEndMarker, TtfbStartValueText, TtfbEndValueText, ttfbValues, startSecond, endSecond, "0.0", "ms", false);
            DrawChart(ErrorChartCanvas, ErrorPolyline, ErrorAxisX, ErrorAxisY, ErrorAxisMinText, ErrorAxisMaxText, ErrorAxisStartText, ErrorAxisEndText, ErrorStartMarker, ErrorEndMarker, ErrorStartValueText, ErrorEndValueText, errorValues, startSecond, endSecond, "0.0", string.Empty, true);

            var latest = series[series.Length - 1];
            RpsChartValueText.Text = $"最新 {latest.Count:0.0}";
            LatencyChartValueText.Text = $"最新 {latest.P95LatencyMs:0.0} ms";
            TtfbChartValueText.Text = $"最新 {latest.AvgTtfbMs:0.0} ms";
            ErrorChartValueText.Text = $"最新 {latest.ErrorRate:0.0}%";
        }

        private void ClearReportView()
        {
            UpdateSummary(null);
            UpdateSeries(Array.Empty<TimeSeriesPoint>());
            UpdateErrors(null);
            UpdateCharts(Array.Empty<TimeSeriesPoint>());
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
    }
}
