using StressTest.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace StressTest.Core
{
    public static class HistoryReportReader
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

        public static IReadOnlyList<HistoryReportSummary> LoadSummaries(string baseFolder)
        {
            var list = new List<HistoryReportSummary>();
            if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
                return list;

            foreach (var folder in Directory.GetDirectories(baseFolder))
            {
                var summaryPath = Path.Combine(folder, "summary.json");
                if (!File.Exists(summaryPath))
                    continue;

                var summary = ReadSummary(summaryPath);
                if (summary == null)
                    continue;

                summary.FolderPath = folder;
                summary.FolderName = Path.GetFileName(folder);
                list.Add(summary);
            }

            return list.OrderByDescending(s => s.StartTime ?? DateTime.MinValue).ToList();
        }

        public static HistoryReportDetail LoadDetail(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            var summaryPath = Path.Combine(folder, "summary.json");
            if (!File.Exists(summaryPath))
                return null;

            var summary = ReadSummary(summaryPath);
            if (summary == null)
                return null;

            summary.FolderPath = folder;
            summary.FolderName = Path.GetFileName(folder);

            var seriesPath = Path.Combine(folder, "timeseries.csv");
            var errorsPath = Path.Combine(folder, "errors.csv");

            return new HistoryReportDetail
            {
                Summary = summary,
                Series = ReadSeries(seriesPath),
                Errors = ReadErrors(errorsPath)
            };
        }

        private static HistoryReportSummary ReadSummary(string path)
        {
            try
            {
                var json = File.ReadAllText(path, Utf8NoBom);
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                    return null;

                var summary = new HistoryReportSummary
                {
                    StartTime = ReadTime(root, "startTime"),
                    EndTime = ReadTime(root, "endTime"),
                    DurationSeconds = ReadInt(root, "durationSeconds"),
                    DeviceCount = ReadInt(root, "deviceCount"),
                    RampUpSeconds = ReadInt(root, "rampUpSeconds"),
                    ThinkTimeBaseMs = ReadInt(root, "thinkTimeBaseMs"),
                    ThinkTimeJitterMs = ReadInt(root, "thinkTimeJitterMs"),
                    TotalRequests = ReadInt(root, "totalRequests"),
                    SuccessRequests = ReadInt(root, "successRequests"),
                    FailedRequests = ReadInt(root, "failedRequests"),
                    CanceledRequests = ReadInt(root, "canceledRequests"),
                    AvgLatencyMs = ReadDouble(root, "avgLatencyMs"),
                    P95LatencyMs = ReadDouble(root, "p95LatencyMs"),
                    AvgTtfbMs = ReadDouble(root, "avgTtfbMs"),
                    P95TtfbMs = ReadDouble(root, "p95TtfbMs"),
                    AvgRps = ReadDouble(root, "avgRps"),
                    ErrorRate = ReadDouble(root, "errorRate")
                };

                return summary;
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<TimeSeriesPoint> ReadSeries(string path)
        {
            var list = new List<TimeSeriesPoint>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return list;

            var lines = File.ReadAllLines(path, Utf8NoBom);
            if (lines.Length <= 1)
                return list;

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 10)
                    continue;

                var point = new TimeSeriesPoint
                {
                    Second = ReadInt(parts[0]),
                    Count = ReadInt(parts[1]),
                    Success = ReadInt(parts[2]),
                    Fail = ReadInt(parts[3]),
                    Canceled = ReadInt(parts[4]),
                    AvgLatencyMs = ReadDouble(parts[5]),
                    P95LatencyMs = ReadDouble(parts[6]),
                    AvgTtfbMs = ReadDouble(parts[7]),
                    P95TtfbMs = ReadDouble(parts[8]),
                    ErrorRate = ReadDouble(parts[9])
                };

                list.Add(point);
            }

            return list;
        }

        private static IReadOnlyList<ErrorItem> ReadErrors(string path)
        {
            var list = new List<ErrorItem>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return list;

            var lines = File.ReadAllLines(path, Utf8NoBom);
            if (lines.Length <= 1)
                return list;

            for (var i = 1; i < lines.Length; i++)
            {
                var item = ParseErrorLine(lines[i]);
                if (item != null)
                    list.Add(item);
            }

            return list;
        }

        private static ErrorItem ParseErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var trimmed = line.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = trimmed.LastIndexOf("\",", StringComparison.Ordinal);
                if (end > 0)
                {
                    var message = trimmed.Substring(1, end - 1).Replace("\"\"", "\"");
                    var countText = trimmed.Substring(end + 2);
                    return new ErrorItem
                    {
                        Error = message,
                        Count = ReadInt(countText)
                    };
                }
            }

            var parts = trimmed.Split(',');
            if (parts.Length >= 2)
            {
                return new ErrorItem
                {
                    Error = parts[0],
                    Count = ReadInt(parts[1])
                };
            }

            return null;
        }

        private static DateTime? ReadTime(Dictionary<string, object> root, string key)
        {
            var text = ReadString(root, key);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time))
                return time;

            if (DateTime.TryParse(text, out time))
                return time;

            return null;
        }

        private static string ReadString(Dictionary<string, object> root, string key)
        {
            foreach (var pair in root)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value?.ToString();
            }

            return null;
        }

        private static int ReadInt(Dictionary<string, object> root, string key)
        {
            foreach (var pair in root)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ReadInt(pair.Value?.ToString());
            }

            return 0;
        }

        private static int ReadInt(string text)
        {
            if (int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return value;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return (int)Math.Round(d);

            return 0;
        }

        private static double ReadDouble(Dictionary<string, object> root, string key)
        {
            foreach (var pair in root)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ReadDouble(pair.Value?.ToString());
            }

            return 0;
        }

        private static double ReadDouble(string text)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return value;

            return 0;
        }
    }
}
