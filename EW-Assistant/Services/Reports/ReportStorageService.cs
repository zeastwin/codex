using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EW_Assistant.Domain.Reports;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 报表存储与索引服务，负责扫描本地文件、读取正文与导出。
    /// </summary>
    public class ReportStorageService
    {
        private const string ReportsRoot = @"D:\DataAI\Reports";

        private static readonly Dictionary<ReportType, string> s_typeDisplayNames = new Dictionary<ReportType, string>
        {
            { ReportType.DailyProd, "当日产能报表" },
            { ReportType.WeeklyProd, "产能周报" },
            { ReportType.DailyAlarm, "当日报警报表" },
            { ReportType.WeeklyAlarm, "报警周报" }
        };

        public ReportStorageService()
        {
            EnsureDirectories();
        }

        /// <summary>根目录，当前固定为 D:\DataAI\Reports。</summary>
        public string RootDirectory
        {
            get { return ReportsRoot; }
        }

        /// <summary>
        /// 判断指定日期的日报文件是否已存在。
        /// </summary>
        public bool DailyReportExists(ReportType type, DateTime date)
        {
            if (type != ReportType.DailyProd && type != ReportType.DailyAlarm)
            {
                return false;
            }

            var path = BuildReportFilePath(type, date.Date, null);
            return File.Exists(path);
        }

        /// <summary>
        /// 判断指定结束日期的周报文件是否已存在（区间=结束日向前 6 天）。
        /// </summary>
        public bool WeeklyReportExists(ReportType type, DateTime endDate)
        {
            if (type != ReportType.WeeklyProd && type != ReportType.WeeklyAlarm)
            {
                return false;
            }

            var start = endDate.Date.AddDays(-6);
            var path = BuildReportFilePath(type, start, endDate.Date);
            return File.Exists(path);
        }

        /// <summary>
        /// 扫描全部报表类型的索引列表。
        /// </summary>
        public IList<ReportInfo> GetAllReports()
        {
            var result = new List<ReportInfo>();
            foreach (ReportType type in Enum.GetValues(typeof(ReportType)))
            {
                result.AddRange(GetReportsByType(type));
            }

            return SortReports(result).ToList();
        }

        /// <summary>
        /// 按报表类型扫描索引列表，仅读取元数据。
        /// </summary>
        public IList<ReportInfo> GetReportsByType(ReportType type)
        {
            var list = new List<ReportInfo>();
            try
            {
                var dir = EnsureTypeDirectory(type);
                var files = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var info = ParseReportInfo(type, file);
                    if (info != null)
                    {
                        list.Add(info);
                    }
                }
            }
            catch
            {
                // 扫描失败时返回已收集的数据，避免影响 UI。
            }

            return SortReports(list).ToList();
        }

        /// <summary>
        /// 读取指定报表的 Markdown 正文。
        /// </summary>
        public string ReadReportContent(ReportInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.FilePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(info.FilePath, Encoding.UTF8);
        }

        /// <summary>
        /// 保存日报 Markdown 内容，并返回写入的文件路径。
        /// </summary>
        public string SaveReportContent(ReportType type, DateTime date, string markdown)
        {
            if (type != ReportType.DailyProd && type != ReportType.DailyAlarm)
            {
                throw new ArgumentException("日报类型不匹配，请使用周报保存重载。", "type");
            }

            var path = BuildReportFilePath(type, date, null);
            EnsureDirectoryForPath(path);
            File.WriteAllText(path, markdown ?? string.Empty, Encoding.UTF8);
            return path;
        }

        /// <summary>
        /// 保存周报 Markdown 内容，并返回写入的文件路径。
        /// </summary>
        public string SaveReportContent(ReportType type, DateTime startDate, DateTime endDate, string markdown)
        {
            if (type != ReportType.WeeklyProd && type != ReportType.WeeklyAlarm)
            {
                throw new ArgumentException("周报类型不匹配，请使用日报保存重载。", "type");
            }

            if (endDate < startDate)
            {
                var tmp = startDate;
                startDate = endDate;
                endDate = tmp;
            }

            var path = BuildReportFilePath(type, startDate, endDate);
            EnsureDirectoryForPath(path);
            File.WriteAllText(path, markdown ?? string.Empty, Encoding.UTF8);
            return path;
        }

        /// <summary>
        /// 导出报表文件到用户指定的位置。
        /// </summary>
        public void ExportReportFile(ReportInfo info, string targetPath)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.FilePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            File.Copy(info.FilePath, targetPath, true);
        }

        /// <summary>返回报表类型的中文展示名。</summary>
        public string GetTypeDisplayName(ReportType type)
        {
            string name;
            if (s_typeDisplayNames.TryGetValue(type, out name))
            {
                return name;
            }

            return type.ToString();
        }

        /// <summary>
        /// 通过文件路径重新解析报表信息，便于生成后立即刷新索引。
        /// </summary>
        public ReportInfo GetReportInfoByPath(ReportType type, string filePath)
        {
            try
            {
                return ParseReportInfo(type, filePath);
            }
            catch
            {
                return null;
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(ReportsRoot);
            foreach (ReportType type in Enum.GetValues(typeof(ReportType)))
            {
                EnsureTypeDirectory(type);
            }
        }

        private string EnsureTypeDirectory(ReportType type)
        {
            var dir = Path.Combine(ReportsRoot, type.ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static IEnumerable<ReportInfo> SortReports(IEnumerable<ReportInfo> reports)
        {
            return reports.OrderByDescending(r => r.Date ?? r.EndDate ?? r.StartDate ?? r.GeneratedAt)
                          .ThenByDescending(r => r.GeneratedAt);
        }

        private ReportInfo ParseReportInfo(ReportType type, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var fileName = Path.GetFileName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;

            DateTime? date = null;
            DateTime? startDate = null;
            DateTime? endDate = null;

            var suffix = TrimTypePrefix(type, nameWithoutExt);
            if (type == ReportType.DailyProd || type == ReportType.DailyAlarm)
            {
                DateTime parsed;
                if (DateTime.TryParseExact(suffix, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    date = parsed;
                }
            }
            else
            {
                var parts = suffix.Split(new[] { '~' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    DateTime start;
                    if (DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                    {
                        startDate = start;
                    }

                    DateTime end;
                    if (DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                    {
                        endDate = end;
                    }
                }
            }

            var size = GetFileSize(filePath);
            var info = new ReportInfo
            {
                Type = type,
                Id = !string.IsNullOrWhiteSpace(nameWithoutExt) ? nameWithoutExt : Guid.NewGuid().ToString("N"),
                TypeDisplayName = GetTypeDisplayName(type),
                Title = BuildTitle(type, date, startDate, endDate, nameWithoutExt),
                DateLabel = BuildDateLabel(type, date, startDate, endDate),
                Date = date,
                StartDate = startDate,
                EndDate = endDate,
                FilePath = filePath,
                FileName = fileName,
                GeneratedAt = File.GetLastWriteTime(filePath),
                IsToday = date.HasValue && date.Value.Date == DateTime.Now.Date,
                FileSize = size,
                FileSizeText = FormatFileSize(size)
            };

            return info;
        }

        private static string TrimTypePrefix(ReportType type, string nameWithoutExt)
        {
            if (string.IsNullOrEmpty(nameWithoutExt))
            {
                return string.Empty;
            }

            var prefix = type.ToString();
            if (nameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return nameWithoutExt.Substring(prefix.Length).TrimStart('_');
            }

            return nameWithoutExt;
        }

        private string BuildTitle(ReportType type, DateTime? date, DateTime? startDate, DateTime? endDate, string fallbackName)
        {
            var typeName = GetTypeDisplayName(type);
            if (type == ReportType.DailyProd || type == ReportType.DailyAlarm)
            {
                if (date.HasValue)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}（{1:yyyy-MM-dd}）", typeName, date.Value);
                }
            }
            else
            {
                if (startDate.HasValue && endDate.HasValue)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}（{1:yyyy-MM-dd}~{2:yyyy-MM-dd}）", typeName, startDate.Value, endDate.Value);
                }
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}（{1}）", typeName, fallbackName);
        }

        private string BuildDateLabel(ReportType type, DateTime? date, DateTime? startDate, DateTime? endDate)
        {
            if (type == ReportType.DailyProd || type == ReportType.DailyAlarm)
            {
                if (date.HasValue)
                {
                    return date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                return "未解析日期";
            }

            if (startDate.HasValue && endDate.HasValue)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} ~ {1:yyyy-MM-dd}", startDate.Value, endDate.Value);
            }

            return "未解析日期区间";
        }

        private string BuildReportFilePath(ReportType type, DateTime startDate, DateTime? endDate)
        {
            var dir = EnsureTypeDirectory(type);
            string fileName;
            if (type == ReportType.DailyProd || type == ReportType.DailyAlarm)
            {
                fileName = string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyy-MM-dd}.md", type, startDate);
            }
            else
            {
                var end = endDate ?? startDate;
                fileName = string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyy-MM-dd}~{2:yyyy-MM-dd}.md", type, startDate, end);
            }

            return Path.Combine(dir, fileName);
        }

        private void EnsureDirectoryForPath(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static long GetFileSize(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                return fi.Exists ? fi.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatFileSize(long size)
        {
            const double OneK = 1024d;
            const double OneM = OneK * 1024d;
            const double OneG = OneM * 1024d;

            if (size >= OneG)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} GB", size / OneG);
            }

            if (size >= OneM)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} MB", size / OneM);
            }

            if (size >= OneK)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} KB", size / OneK);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} B", size);
        }
    }
}
