using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 读取最近 24 小时的报警记录，并按小时 / 报警码聚合。
    /// </summary>
    public class AlarmCsvReader
    {
        private readonly string _root;

        public AlarmCsvReader(string root = null)
        {
            _root = string.IsNullOrWhiteSpace(root) ? LocalDataConfig.AlarmCsvRoot : root;
        }

        public IList<AlarmHourStat> GetLast24HoursAlarms(DateTime now)
        {
            var windowStart = now.AddHours(-24);
            var result = new Dictionary<string, AlarmHourStat>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in EnumerateRecentFiles(windowStart))
            {
                var fileDate = GuessDateFromFile(file);
                foreach (var row in ReadRows(file, fileDate))
                {
                    var occur = row.StartTime ?? row.EndTime;
                    if (!occur.HasValue) continue;
                    var at = occur.Value;

                    if (at < windowStart || at >= now) continue;

                    var hour = new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0, at.Kind);
                    var ruleKey = $"{(string.IsNullOrWhiteSpace(row.Code) ? "UNKNOWN" : row.Code)}|{hour:yyyyMMddHH}";

                    if (!result.TryGetValue(ruleKey, out var stat))
                    {
                        stat = new AlarmHourStat
                        {
                            Hour = hour,
                            Code = string.IsNullOrWhiteSpace(row.Code) ? "UNKNOWN" : row.Code,
                            Message = row.Message,
                            Category = row.Category
                        };
                        result.Add(ruleKey, stat);
                    }

                    stat.Count += 1;
                    stat.DowntimeMinutes += row.DowntimeMinutes ?? 0d;
                    if (string.IsNullOrWhiteSpace(stat.Message) && !string.IsNullOrWhiteSpace(row.Message))
                    {
                        stat.Message = row.Message;
                    }
                    if (string.IsNullOrWhiteSpace(stat.Category) && !string.IsNullOrWhiteSpace(row.Category))
                    {
                        stat.Category = row.Category;
                    }
                }
            }

            return result.Values.OrderBy(x => x.Hour).ToList();
        }

        private IEnumerable<string> EnumerateRecentFiles(DateTime windowStart)
        {
            if (!Directory.Exists(_root)) yield break;

            var dir = new DirectoryInfo(_root);
            foreach (var file in dir.EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly)
                                    .OrderByDescending(f => f.LastWriteTime))
            {
                if (file.LastWriteTime < windowStart.AddDays(-1)) continue;
                yield return file.FullName;
            }
        }

        private IEnumerable<AlarmRow> ReadRows(string path, DateTime fileDate)
        {
            if (!File.Exists(path)) yield break;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, new UTF8Encoding(false)))
            {
                string headerLine = null;
                while (headerLine == null && !reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line)) headerLine = line;
                }

                if (string.IsNullOrEmpty(headerLine)) yield break;

                var delimiter = DetectDelimiter(headerLine);
                var headers = headerLine.Split(new[] { delimiter }, StringSplitOptions.None)
                    .Select(NormalizeHeader)
                    .ToArray();

                var codeIndex = FindIndex(headers, "code", "报警代码", "alarmcode", "报警编号", "错误编码");
                var messageIndex = FindIndex(headers, "message", "content", "报警内容", "描述", "错误信息");
                var categoryIndex = FindIndex(headers, "category", "类别", "type", "错误类型");
                var startIndex = FindIndex(headers, "starttime", "开始时间", "start", "起始时间");
                var endIndex = FindIndex(headers, "endtime", "结束时间", "end");
                var durationIndex = FindIndex(headers, "duration", "时长", "持续", "durationsec", "时长秒", "持续时间", "维修耗时");

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var cells = line.Split(new[] { delimiter }, StringSplitOptions.None);
                    if (cells.Length == 0) continue;

                    var start = ParseDateTime(GetCell(cells, startIndex), fileDate);
                    var end = ParseDateTime(GetCell(cells, endIndex), fileDate);
                    var downtime = ParseNullableDouble(GetCell(cells, durationIndex));
                    if (downtime.HasValue)
                    {
                        downtime = downtime.Value / 60d; // CSV 默认秒，转为分钟
                    }
                    else if (start.HasValue && end.HasValue)
                    {
                        downtime = (end.Value - start.Value).TotalMinutes;
                    }

                    yield return new AlarmRow
                    {
                        Code = GetCell(cells, codeIndex),
                        Message = GetCell(cells, messageIndex),
                        Category = GetCell(cells, categoryIndex),
                        StartTime = start,
                        EndTime = end,
                        DowntimeMinutes = downtime
                    };
                }
            }
        }

        private static string GetCell(string[] cells, int index)
        {
            if (index < 0 || index >= cells.Length) return null;
            return cells[index];
        }

        private static string NormalizeHeader(string header)
        {
            if (header == null) return string.Empty;
            return header.Trim().Replace(" ", string.Empty).Replace("\t", string.Empty).ToLowerInvariant();
        }

        private static int FindIndex(string[] headers, params string[] names)
        {
            for (var i = 0; i < headers.Length; i++)
            {
                foreach (var n in names)
                {
                    if (string.Equals(headers[i], NormalizeHeader(n), StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        private static char DetectDelimiter(string line)
        {
            var candidates = new[] { ',', ';', '\t' };
            var best = candidates
                .Select(c => new { c, count = line.Count(ch => ch == c) })
                .OrderByDescending(x => x.count)
                .FirstOrDefault();

            return best == null || best.count == 0 ? ',' : best.c;
        }

        private static double? ParseNullableDouble(string value)
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (TimeSpan.TryParse(value, out var ts)) return ts.TotalSeconds;
            return null;
        }

        private static DateTime? ParseDateTime(string value, DateTime fallbackDate)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();

            if (TimeSpan.TryParse(text, out var ts))
            {
                return fallbackDate.Date + ts;
            }

            if (text.Length >= 5 && TimeSpan.TryParseExact(text, "hh\\:mm", CultureInfo.InvariantCulture, out ts))
            {
                return fallbackDate.Date + ts;
            }

            if (int.TryParse(text, out var hour) && hour >= 0 && hour <= 23)
            {
                return fallbackDate.Date.AddHours(hour);
            }

            var formats = new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH" };
            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(text, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    return dt;
                }
            }

            if (DateTime.TryParse(text, out var parsed)) return parsed;

            return null;
        }

        private static DateTime GuessDateFromFile(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var match = Regex.Match(fileName, @"(?<y>20\d{2})[-_/\.]?(?<m>\d{1,2})[-_/\.]?(?<d>\d{1,2})");
            if (match.Success
                && int.TryParse(match.Groups["y"].Value, out var y)
                && int.TryParse(match.Groups["m"].Value, out var m)
                && int.TryParse(match.Groups["d"].Value, out var d))
            {
                try { return new DateTime(y, m, d); } catch { }
            }

            return File.GetLastWriteTime(path).Date;
        }

        private sealed class AlarmRow
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public string Category { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public double? DowntimeMinutes { get; set; }
        }
    }

    /// <summary>
    /// 按小时聚合的报警信息。
    /// </summary>
    public class AlarmHourStat
    {
        public DateTime Hour { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Category { get; set; }
        public int Count { get; set; }
        public double DowntimeMinutes { get; set; }
    }
}
