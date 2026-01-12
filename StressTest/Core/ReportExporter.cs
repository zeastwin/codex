using StressTest.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StressTest.Core
{
    public static class ReportExporter
    {
        public static string GetReportBaseFolder()
        {
            return ResolveBaseFolder();
        }

        public static string PrepareReportFolder(DateTime startLocal, StressConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var folderName = $"{startLocal:yyyyMMdd_HHmmss}_时长{config.DurationSeconds}秒_设备{config.DeviceCount}台";
            var baseFolder = ResolveBaseFolder();
            var fullPath = Path.Combine(baseFolder, folderName);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public static void Export(
            string folder,
            StressConfig config,
            MetricsSnapshot snapshot,
            FrameworkElement chartElement)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("导出目录为空。", nameof(folder));
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            Directory.CreateDirectory(folder);

            WriteSummary(Path.Combine(folder, "summary.json"), config, snapshot);
            WriteTimeSeries(Path.Combine(folder, "timeseries.csv"), snapshot.Series ?? Array.Empty<TimeSeriesPoint>());
            WriteErrors(Path.Combine(folder, "errors.csv"), snapshot.ErrorTop);

            if (chartElement != null)
                SaveChartsPng(Path.Combine(folder, "charts.png"), chartElement);
        }

        private static string ResolveBaseFolder()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var reportsDir = Path.Combine(baseDir, "Reports");
            Directory.CreateDirectory(reportsDir);
            return reportsDir;
        }

        private static void WriteSummary(string path, StressConfig config, MetricsSnapshot snapshot)
        {
            var payload = new Dictionary<string, object>
            {
                ["startTime"] = snapshot.StartTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ["endTime"] = snapshot.EndTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ["durationSeconds"] = config.DurationSeconds,
                ["deviceCount"] = config.DeviceCount,
                ["rampUpSeconds"] = config.RampUpSeconds,
                ["thinkTimeBaseMs"] = config.ThinkTimeBaseMs,
                ["thinkTimeJitterMs"] = config.ThinkTimeJitterMs,
                ["totalRequests"] = snapshot.TotalRequests,
                ["successRequests"] = snapshot.SuccessRequests,
                ["failedRequests"] = snapshot.FailedRequests,
                ["canceledRequests"] = snapshot.CanceledRequests,
                ["avgLatencyMs"] = Math.Round(snapshot.AvgLatencyMs, 2),
                ["p95LatencyMs"] = Math.Round(snapshot.P95LatencyMs, 2),
                ["avgTtfbMs"] = Math.Round(snapshot.AvgTtfbMs, 2),
                ["p95TtfbMs"] = Math.Round(snapshot.P95TtfbMs, 2),
                ["avgRps"] = Math.Round(snapshot.AvgRps, 2),
                ["errorRate"] = Math.Round(snapshot.ErrorRate, 2),
                ["errorTop"] = snapshot.ErrorTop
            };

            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(payload);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static void WriteTimeSeries(string path, IReadOnlyList<TimeSeriesPoint> series)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Second,Count,Success,Fail,Canceled,AvgLatencyMs,P95LatencyMs,AvgTtfbMs,P95TtfbMs,ErrorRate");

            foreach (var p in series)
            {
                sb.AppendLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5:0.00},{6:0.00},{7:0.00},{8:0.00},{9:0.00}",
                    p.Second,
                    p.Count,
                    p.Success,
                    p.Fail,
                    p.Canceled,
                    p.AvgLatencyMs,
                    p.P95LatencyMs,
                    p.AvgTtfbMs,
                    p.P95TtfbMs,
                    p.ErrorRate));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void WriteErrors(string path, IReadOnlyDictionary<string, int> errors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Error,Count");

            if (errors != null)
            {
                foreach (var pair in errors.OrderByDescending(p => p.Value))
                {
                    var msg = pair.Key?.Replace("\r", " ").Replace("\n", " ") ?? string.Empty;
                    sb.AppendLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "\"{0}\",{1}",
                        msg.Replace("\"", "\"\""),
                        pair.Value));
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void SaveChartsPng(string path, FrameworkElement element)
        {
            if (element.ActualWidth < 2 || element.ActualHeight < 2)
                return;

            element.UpdateLayout();

            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                encoder.Save(fs);
            }
        }
    }
}
