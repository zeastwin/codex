using EW_Assistant.Warnings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace EW_Assistant.Services
{
    /// <summary>
    /// 最近 N 天产能 CSV 的集中读取与缓存，供 Dashboard/ProductionBoard 复用。
    /// </summary>
    public static class ProductionLogCache
    {
        public sealed class DayData
        {
            public DateTime Date { get; set; }
            public bool Missing { get; set; }
            public string? FilePath { get; set; }
            public int[] HourPass { get; } = new int[24];
            public int[] HourFail { get; } = new int[24];
            public int SumPass => HourPass.Sum();
            public int SumFail => HourFail.Sum();
            public int Total => SumPass + SumFail;
        }

        private static readonly object _lock = new();
        private static IReadOnlyDictionary<DateTime, DayData> _byDay = new Dictionary<DateTime, DayData>();
        private static string _cacheRoot = string.Empty;
        private static string _cachePrefix = string.Empty;
        private static int _cacheDays = 7;
        private static DateTime _lastLoad = DateTime.MinValue;

        /// <summary>
        /// 加载并缓存最近 N 天的产能 CSV；短时间内重复调用会复用缓存，force=true 时强制重读。
        /// </summary>
        public static void LoadRecent(string? root, string filePrefix, int days = 7, bool force = false)
        {
            if (days <= 0) days = 7;
            var dir = string.IsNullOrWhiteSpace(root) ? LocalDataConfig.ProductionCsvRoot : root;
            var prefix = string.IsNullOrWhiteSpace(filePrefix) ? "小时产量" : filePrefix;
            var now = DateTime.Now;

            lock (_lock)
            {
                if (!force &&
                    string.Equals(_cacheRoot, dir, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_cachePrefix, prefix, StringComparison.OrdinalIgnoreCase) &&
                    _cacheDays == days &&
                    (now - _lastLoad) < TimeSpan.FromSeconds(3))
                {
                    return;
                }
            }

            var map = new Dictionary<DateTime, DayData>(days);
            var watchMode = LocalDataConfig.WatchMode;

            for (int i = days - 1; i >= 0; i--)
            {
                var day = DateTime.Today.AddDays(-i).Date;
                var data = new DayData { Date = day, Missing = true };

                if (TrySeekCsv(dir, prefix, day, watchMode, out var file))
                {
                    try
                    {
                        if (TryLoadDay(file, data))
                        {
                            data.Missing = false;
                            data.FilePath = file;
                        }
                    }
                    catch
                    {
                        data.Missing = true;
                    }
                }

                map[day] = data;
            }

            lock (_lock)
            {
                _byDay = map;
                _cacheRoot = dir;
                _cachePrefix = prefix;
                _cacheDays = days;
                _lastLoad = now;
            }
        }

        /// <summary>取指定日期的缓存，不触发磁盘读取。</summary>
        public static DayData? GetDay(DateTime date)
        {
            lock (_lock)
            {
                return _byDay.TryGetValue(date.Date, out var v) ? v : null;
            }
        }

        /// <summary>按时间顺序返回最近 N 天的缓存快照。</summary>
        public static IReadOnlyList<DayData> SnapshotRecent(int days = 7)
        {
            if (days <= 0) days = 7;
            IReadOnlyDictionary<DateTime, DayData> snap;
            lock (_lock) snap = _byDay;

            var list = new List<DayData>(days);
            for (int i = days - 1; i >= 0; i--)
            {
                var d = DateTime.Today.AddDays(-i).Date;
                list.Add(snap.TryGetValue(d, out var v) ? v : new DayData { Date = d, Missing = true });
            }
            return list;
        }

        private static bool TrySeekCsv(string dir, string prefix, DateTime day, bool watchMode, out string? file)
        {
            file = null;
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) return false;

                if (watchMode)
                {
                    var dayDir = Path.Combine(dir, day.ToString("yyyy-MM-dd"));
                    if (Directory.Exists(dayDir))
                    {
                        var expect = Path.Combine(dayDir, $"{prefix}{day:yyyyMMdd}.csv");
                        if (File.Exists(expect)) { file = expect; return true; }
                        var fallback = Directory.GetFiles(dayDir, $"{prefix}{day:yyyyMMdd}*.csv").FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(fallback)) { file = fallback; return true; }
                    }
                }

                if (!Directory.Exists(dir)) return false;

                var name = Path.Combine(dir, $"{prefix}{day:yyyyMMdd}.csv");
                if (File.Exists(name)) { file = name; return true; }

                var alt = Directory.GetFiles(dir, $"{prefix}{day:yyyyMMdd}*.csv").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(alt)) { file = alt; return true; }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLoadDay(string file, DayData target)
        {
            Array.Clear(target.HourPass, 0, target.HourPass.Length);
            Array.Clear(target.HourFail, 0, target.HourFail.Length);

            Encoding enc;
            try { enc = Encoding.GetEncoding("GB2312"); }
            catch { enc = new UTF8Encoding(false); }

            var lines = ReadAllLinesShared(file, enc);
            if (lines.Count == 0) return false;

            var header = SmartSplit(lines[0]);
            int idxPass = FindIndex(header, "PASS", "良品", "良率PASS", "OK");
            int idxFail = FindIndex(header, "FAIL", "不良", "NG");
            int idxHour = FindIndex(header, "HOUR", "小时", "时段", "时刻");
            bool hasHour = idxHour >= 0;

            for (int i = 1; i < lines.Count; i++)
            {
                var row = SmartSplit(lines[i]);
                if (row.Length == 0) continue;

                int hour;
                if (hasHour)
                {
                    if (idxHour >= row.Length) continue;
                    hour = ExtractHour(row[idxHour]);
                }
                else
                {
                    hour = i - 1;
                }
                if (hour < 0 || hour > 23) continue;

                int pass = (idxPass >= 0 && idxPass < row.Length) ? ToInt(row[idxPass]) : 0;
                int fail = (idxFail >= 0 && idxFail < row.Length) ? ToInt(row[idxFail]) : 0;

                target.HourPass[hour] += Math.Max(0, pass);
                target.HourFail[hour] += Math.Max(0, fail);
            }

            return true;
        }

        private static List<string> ReadAllLinesShared(string path, Encoding enc)
        {
            var list = new List<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    list.Add(line);
            }
            return list;
        }

        private static string[] SmartSplit(string line)
        {
            if (line.Contains(",")) return line.Split(',');
            if (line.Contains("\t")) return line.Split('\t');
            if (line.Contains(";")) return line.Split(';');
            return new[] { line };
        }

        private static int FindIndex(string[] arr, params string[] keys)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var t = arr[i].Trim().ToUpperInvariant();
                foreach (var k in keys)
                    if (t == k.Trim().ToUpperInvariant()) return i;
            }
            return -1;
        }

        private static int ExtractHour(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && h >= 0 && h <= 23) return h;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt)) return dt.Hour;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt.Hour;
            var digits = new string(s.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length > 0 && int.TryParse(digits, out h) && h >= 0 && h <= 23) return h;
            return -1;
        }

        private static int ToInt(string s)
        {
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return 0;
        }
    }
}
