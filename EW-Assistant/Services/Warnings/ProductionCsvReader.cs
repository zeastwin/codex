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
    /// 读取最近 24 小时的产能 / 良率数据并按小时聚合。
    /// </summary>
    public class ProductionCsvReader
    {
        private readonly string _root;

        public ProductionCsvReader(string root = null)
        {
            _root = string.IsNullOrWhiteSpace(root) ? LocalDataConfig.ProductionCsvRoot : root;
        }

        public IList<ProductionHourRecord> GetLast24HoursProduction(DateTime now)
        {
            var windowStart = now.AddHours(-24);
            var result = new Dictionary<DateTime, ProductionHourRecord>();

            foreach (var file in EnumerateRecentFiles(windowStart))
            {
                var dateFromFile = GuessDateFromFile(file);
                foreach (var row in ReadRows(file))
                {
                    if (!row.Hour.HasValue) continue;

                    var hour = row.Hour.Value;
                    if (hour.Year < 1900)
                    {
                        hour = new DateTime(dateFromFile.Year, dateFromFile.Month, dateFromFile.Day, hour.Hour, 0, 0, hour.Kind);
                    }

                    if (hour < windowStart || hour >= now) continue;

                    if (!result.TryGetValue(hour, out var agg))
                    {
                        agg = new ProductionHourRecord
                        {
                            Hour = hour,
                            PlannedOutput = row.PlannedOutput
                        };
                        result.Add(hour, agg);
                    }

                    agg.Pass += row.Pass;
                    agg.Fail += row.Fail;
                    if (row.PlannedOutput.HasValue)
                    {
                        agg.PlannedOutput = row.PlannedOutput;
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

        private IEnumerable<ProductionRow> ReadRows(string path)
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

                var hourIndex = FindIndex(headers, "hour", "小时", "时间", "时段");
                var passIndex = FindIndex(headers, "pass", "良品", "良率pass", "ok");
                var failIndex = FindIndex(headers, "fail", "不良", "报废", "抛料", "ng");
                var planIndex = FindIndex(headers, "plan", "计划", "目标");

                if (hourIndex < 0 && headers.Length > 0) hourIndex = 0;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var cells = line.Split(new[] { delimiter }, StringSplitOptions.None);
                    if (cells.Length == 0) continue;

                    var row = new ProductionRow
                    {
                        Hour = ParseHour(GetCell(cells, hourIndex)),
                        Pass = ParseInt(GetCell(cells, passIndex)),
                        Fail = ParseInt(GetCell(cells, failIndex)),
                        PlannedOutput = ParseNullableInt(GetCell(cells, planIndex))
                    };

                    yield return row;
                }
            }
        }

        private static string GetCell(string[] cells, int index)
        {
            if (index < 0 || index >= cells.Length) return null;
            return cells[index];
        }

        private static int ParseInt(string value)
        {
            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            return 0;
        }

        private static int? ParseNullableInt(string value)
        {
            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }

        private static DateTime? ParseHour(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();

            if (TimeSpan.TryParse(text, out var ts))
            {
                return new DateTime(1, 1, 1, ts.Hours, 0, 0);
            }

            if (text.Length >= 5 && TimeSpan.TryParseExact(text, "hh\\:mm", CultureInfo.InvariantCulture, out ts))
            {
                return new DateTime(1, 1, 1, ts.Hours, 0, 0);
            }

            if (int.TryParse(text, out var hourOnly) && hourOnly >= 0 && hourOnly <= 23)
            {
                return new DateTime(1, 1, 1, hourOnly, 0, 0);
            }

            if (text.Length >= 2 && int.TryParse(text.Substring(0, 2), out hourOnly) && hourOnly >= 0 && hourOnly <= 23)
            {
                return new DateTime(1, 1, 1, hourOnly, 0, 0);
            }

            if (text.Length == 10 && DateTime.TryParseExact(text, "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return TruncateToHour(dt);
            }

            if (text.Length == 12 && DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return TruncateToHour(dt);
            }

            if (DateTime.TryParse(text, out dt))
            {
                return TruncateToHour(dt);
            }

            return null;
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

        private static DateTime TruncateToHour(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind);
        }

        private static DateTime GuessDateFromFile(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var match = Regex.Match(fileName ?? string.Empty, @"(20\d{6})");
            if (match.Success)
            {
                if (DateTime.TryParseExact(match.Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date;
                }
            }

            return File.GetLastWriteTime(path).Date;
        }

        private sealed class ProductionRow
        {
            public DateTime? Hour { get; set; }
            public int Pass { get; set; }
            public int Fail { get; set; }
            public int? PlannedOutput { get; set; }
        }
    }

    /// <summary>
    /// 每小时产能聚合结果。
    /// </summary>
    public class ProductionHourRecord
    {
        public DateTime Hour { get; set; }
        public int Pass { get; set; }
        public int Fail { get; set; }
        public int? PlannedOutput { get; set; }

        public int Total => Pass + Fail;
        public double Yield => Total <= 0 ? 0d : (double)Pass / Total;
    }
}
