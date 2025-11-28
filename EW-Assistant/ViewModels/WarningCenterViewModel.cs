using EW_Assistant.Services;
using EW_Assistant.Services.Warnings;
using EW_Assistant.Warnings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EW_Assistant.ViewModels
{
    /// <summary>
    /// 预警中心视图模型，负责加载并映射预警数据。
    /// </summary>
    public class WarningCenterViewModel : INotifyPropertyChanged
    { 
        private WarningItemViewModel _selected;
        private string _aiAnalysisText = "当前还未接入 AI 分析，下面仅展示预警基本信息。";
        private string _lastUpdatedText = string.Empty;
        private readonly Dictionary<string, WarningItem> _warningMap = new Dictionary<string, WarningItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WarningTicketRecord> _ticketMap = new Dictionary<string, WarningTicketRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly WarningAnalysisCache _analysisCache;
        private readonly AiWarningAnalysisService _aiService;
        private readonly IWarningTicketStore _ticketStore;
        private bool _isAnalyzingWarnings;
        private readonly List<WarningTicketRecord> _allTickets = new List<WarningTicketRecord>();
        private const int ResolveGraceMinutes = 120;
        private readonly int _ignoreMinutes;
        private string _filterStatus = "Pending";
        private readonly WarningRuleOptions _options;

        public ObservableCollection<WarningItemViewModel> Warnings { get; } = new ObservableCollection<WarningItemViewModel>();
        public WarningItemViewModel SelectedWarning
        {
            get => _selected;
            set
            {
                if (!Equals(_selected, value))
                {
                    _selected = value;
                    OnPropertyChanged();
                    UpdateAnalysisText();
                }
            }
        }

        public string AiAnalysisText
        {
            get { return _aiAnalysisText; }
            set
            {
                if (_aiAnalysisText != value)
                {
                    _aiAnalysisText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LastUpdatedText
        {
            get { return _lastUpdatedText; }
            set
            {
                if (_lastUpdatedText != value)
                {
                    _lastUpdatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FilterStatus
        {
            get { return _filterStatus; }
            set
            {
                if (_filterStatus != value)
                {
                    _filterStatus = value;
                    OnPropertyChanged();
                    ApplyFilterAndRender();
                }
            }
        }

        public WarningCenterViewModel()
        {
            _analysisCache = new WarningAnalysisCache(null);
            _analysisCache.Load();
            _aiService = new AiWarningAnalysisService();
            _ticketStore = new JsonWarningTicketStore(null);
            _options = WarningRuleOptions.Normalize(ConfigService.Current?.WarningOptions);
            _ignoreMinutes = _options.IgnoreMinutes > 0 ? _options.IgnoreMinutes : WarningRuleOptions.DefaultIgnoreMinutes;

            LoadWarningsFromCsv();
            ApplyCachedAnalysis();
            UpdateAnalysisText();
        }

        /// <summary>
        /// 从 CSV 读取最近 24 小时预警并映射到 UI。
        /// </summary>
        public void LoadWarningsFromCsv()
        {
            Warnings.Clear();
            _warningMap.Clear();
            _ticketMap.Clear();
            try
            {
                var prodReader = new ProductionCsvReader(LocalDataConfig.ProductionCsvRoot);
                var alarmReader = new AlarmCsvReader(LocalDataConfig.AlarmCsvRoot);
                var engine = new WarningRuleEngine(prodReader, alarmReader);

                var now = DateTime.Now;
                var items = engine.BuildWarnings(now) ?? new List<WarningItem>();

                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Key)) continue;
                    var fp = BuildFingerprint(item);
                    _warningMap[fp] = item;
                }

                MergeTickets(items, now);
                ApplyFilterAndRender();
            }
            catch (Exception ex)
            {
                SelectedWarning = null;
                AiAnalysisText = "加载预警数据时发生错误：" + ex.Message;
                LastUpdatedText = "上次更新：" + DateTime.Now.ToString("HH:mm:ss");
                return;
            }

            LastUpdatedText = "上次更新：" + DateTime.Now.ToString("HH:mm:ss");
            UpdateAnalysisText();
        }

        /// <summary>
        /// 异步生成缺失的 AI 分析，不阻塞 UI。
        /// </summary>
        public async Task AnalyzeMissingWarningsAsync()
        {
            if (_isAnalyzingWarnings) return;
            _isAnalyzingWarnings = true;

            try
            {
                foreach (var w in Warnings.ToList())
                {
                    if (w == null || w.HasAiMarkdown) continue;
                    WarningItem item;
                    if (!_warningMap.TryGetValue(w.Key ?? string.Empty, out item) || item == null)
                    {
                        var fallback = GetTicketByFingerprint(w.Key ?? string.Empty);
                        if (fallback != null)
                        {
                            item = BuildWarningFromTicket(fallback);
                        }
                    }
                    if (item == null) continue;

                    var md = await _aiService.AnalyzeAsync(item);
                    if (!string.IsNullOrWhiteSpace(md))
                    {
                        w.AiMarkdown = md;
                        w.HasAiMarkdown = true;

                        var record = new WarningAnalysisRecord
                        {
                            Key = item.Key,
                            RuleId = item.RuleId,
                            RuleName = item.RuleName,
                            Level = item.Level,
                            Type = item.Type,
                            StartTime = item.StartTime,
                            EndTime = item.EndTime,
                            AiMarkdown = md,
                            EngineVersion = string.Empty
                        };
                        _analysisCache.Upsert(record);

                        if (w == SelectedWarning)
                        {
                            UpdateAnalysisText();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AiAnalysisText = "AI 分析出错：" + ex.Message;
            }
            finally
            {
                _isAnalyzingWarnings = false;
            }
        }

        public void MarkProcessedSelected()
        {
            if (SelectedWarning == null) return;
            var ticket = GetTicketByFingerprint(SelectedWarning.Key);
            if (ticket == null) return;
            ticket.Status = "Processed";
            ticket.ProcessedAt = DateTime.Now;
            ticket.UpdatedAt = DateTime.Now;
            SaveTickets();
            ApplyFilterAndRender();
        }

        private void ApplyCachedAnalysis()
        {
            foreach (var w in Warnings)
            {
                if (w == null || string.IsNullOrWhiteSpace(w.Key)) continue;
                WarningAnalysisRecord record;
                if (_analysisCache != null && _analysisCache.TryGet(w.Key, out record) && record != null)
                {
                    if (!string.IsNullOrWhiteSpace(record.AiMarkdown))
                    {
                        w.AiMarkdown = record.AiMarkdown;
                        w.HasAiMarkdown = true;
                    }
                }
            }
        }

        private void UpdateAnalysisText()
        {
            if (Warnings.Count == 0)
            {
                AiAnalysisText = "最近 24 小时没有触发任何预警。";
                return;
            }

            if (SelectedWarning == null)
            {
                AiAnalysisText = "请选择一条预警";
                return;
            }

            if (SelectedWarning.HasAiMarkdown && !string.IsNullOrEmpty(SelectedWarning.AiMarkdown))
            {
                AiAnalysisText = SelectedWarning.AiMarkdown;
            }
            else
            {
                AiAnalysisText = "已检测到预警，但 AI 分析尚未生成，请稍后。";
            }
        }

        private static int LevelRank(string level)
        {
            switch ((level ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "critical": return 3;
                case "warning": return 2;
                case "info": return 1;
                default: return 0;
            }
        }

        private static string MapLevelDisplay(string level)
        {
            var lv = (level ?? string.Empty).Trim().ToLowerInvariant();
            switch (lv)
            {
                case "critical": return "严重";
                case "warning": return "警告";
                case "info": return "提示";
                default: return string.IsNullOrEmpty(level) ? "提示" : level;
            }
        }

        private static string MapTypeDisplay(string type)
        {
            var tp = (type ?? string.Empty).Trim().ToLowerInvariant();
            switch (tp)
            {
                case "yield": return "良率";
                case "throughput": return "产能";
                case "alarm": return "报警";
                case "combined": return "综合";
                default: return string.IsNullOrEmpty(type) ? "其他" : type;
            }
        }

        private string BuildFingerprint(WarningItem item)
        {
            var dimension = string.IsNullOrWhiteSpace(item.MetricName) ? "default" : item.MetricName.Trim();
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:yyyyMMddHH}|{3}",
                item.RuleId ?? "UNKNOWN",
                item.Type ?? "Other",
                item.StartTime,
                dimension);
        }

        private void MergeTickets(IList<WarningItem> items, DateTime now)
        {
            var existing = _ticketStore.LoadAll() ?? new List<WarningTicketRecord>();
            _ticketMap.Clear();
            foreach (var t in existing)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.Fingerprint)) continue;
                if (t.LastSeen == default(DateTime))
                {
                    t.LastSeen = t.CreatedAt == default(DateTime) ? DateTime.Now : t.CreatedAt;
                }
                if (t.LastSeen != default(DateTime))
                {
                    if ((now - t.LastSeen).TotalMinutes > ResolveGraceMinutes && !string.Equals(t.Status ?? string.Empty, "Resolved", StringComparison.OrdinalIgnoreCase))
                    {
                        t.Status = "Resolved";
                        t.UpdatedAt = now;
                    }
                }
                _ticketMap[t.Fingerprint] = t;
            }

            foreach (var item in items ?? new List<WarningItem>())
            {
                var fp = BuildFingerprint(item);
                WarningTicketRecord ticket;
                if (!_ticketMap.TryGetValue(fp, out ticket))
                {
                    ticket = new WarningTicketRecord
                    {
                        Fingerprint = fp,
                        RuleId = item.RuleId,
                        RuleName = item.RuleName,
                        Level = item.Level,
                        Type = item.Type,
                        StartTime = item.StartTime,
                        EndTime = item.EndTime,
                        Summary = item.Summary,
                        MetricName = item.MetricName,
                        CurrentValue = item.CurrentValue,
                        BaselineValue = item.BaselineValue,
                        ThresholdValue = item.ThresholdValue,
                        Status = "Active",
                        CreatedAt = now,
                        UpdatedAt = now,
                        FirstSeen = item.StartTime,
                        LastSeen = now,
                        OccurrenceCount = 1
                    };
                    _ticketMap[fp] = ticket;
                }
                else
                {
                    ticket.RuleId = item.RuleId;
                    ticket.RuleName = item.RuleName;
                    ticket.Level = item.Level;
                    ticket.Type = item.Type;
                    ticket.StartTime = item.StartTime;
                    ticket.EndTime = item.EndTime;
                    ticket.Summary = item.Summary;
                    ticket.MetricName = item.MetricName;
                    ticket.CurrentValue = item.CurrentValue;
                    ticket.BaselineValue = item.BaselineValue;
                    ticket.ThresholdValue = item.ThresholdValue;
                    ticket.LastSeen = now;
                    ticket.UpdatedAt = now;
                    ticket.OccurrenceCount = ticket.OccurrenceCount <= 0 ? 1 : ticket.OccurrenceCount + 1;

                    var status = (ticket.Status ?? string.Empty).Trim();
                    if (string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Processed", StringComparison.OrdinalIgnoreCase))
                    {
                        ticket.Status = "Active";
                    }
                    else if (string.Equals(status, "Ignored", StringComparison.OrdinalIgnoreCase)
                        && ticket.IgnoredUntil.HasValue && ticket.IgnoredUntil.Value < now)
                    {
                        ticket.Status = "Active";
                        ticket.IgnoredUntil = null;
                    }
                }
            }

            _allTickets.Clear();
            foreach (var kv in _ticketMap)
            {
                _allTickets.Add(kv.Value);
            }

            SaveTickets();
        }

        private void ApplyFilterAndRender()
        {
            Warnings.Clear();
            var filtered = FilterTickets(_allTickets, _filterStatus)
                .OrderByDescending(t => LevelRank(t.Level))
                .ThenByDescending(t => t.LastSeen)
                .ToList();

            foreach (var t in filtered)
            {
                var item = ConvertToViewModel(t);
                if (item != null)
                {
                    Warnings.Add(item);
                }
            }

            if (Warnings.Count > 0)
            {
                SelectedWarning = Warnings[0];
            }
            else
            {
                SelectedWarning = null;
            }

            ApplyCachedAnalysis();
            UpdateAnalysisText();
        }

        private static IEnumerable<WarningTicketRecord> FilterTickets(IEnumerable<WarningTicketRecord> tickets, string filter)
        {
            var list = new List<WarningTicketRecord>();
            if (tickets == null) return list;

            var f = (filter ?? string.Empty).Trim().ToLowerInvariant();
            foreach (var t in tickets)
            {
                if (t == null) continue;
                var status = (t.Status ?? string.Empty).Trim().ToLowerInvariant();
                if (status == "ignored" && t.IgnoredUntil.HasValue && t.IgnoredUntil.Value > DateTime.Now)
                {
                    // 忽略窗口未到期，直接跳过展示
                    continue;
                }
                if (f == "all")
                {
                    list.Add(t);
                    continue;
                }
                if (f == "pending")
                {
                    if (status == "active" || status == "acknowledged")
                        list.Add(t);
                    continue;
                }
                if (f == "active" && status == "active")
                {
                    list.Add(t);
                    continue;
                }
                if (f == "acknowledged" && status == "acknowledged")
                {
                    list.Add(t);
                    continue;
                }
                if (f == "ignored" && status == "ignored")
                {
                    list.Add(t);
                    continue;
                }
                if (f == "processed" && status == "processed")
                {
                    list.Add(t);
                    continue;
                }
                if (f == "resolved" && status == "resolved")
                {
                    list.Add(t);
                    continue;
                }
            }

            return list;
        }

        private WarningItemViewModel ConvertToViewModel(WarningTicketRecord ticket)
        {
            if (ticket == null) return null;
            var timeRange = ticket.StartTime.ToString("HH:mm") + " - " + ticket.EndTime.ToString("HH:mm");

            string title = ticket.RuleName;
            var isYieldMetric = string.Equals(ticket.Type ?? string.Empty, "yield", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(ticket.MetricName) && ticket.MetricName.Contains("良率"));
            Func<double, string> formatMetric = v => isYieldMetric ? (v * 100).ToString("F1") : v.ToString("F1");

            string metricText = string.Empty;
            if (ticket.ThresholdValue.HasValue)
            {
                metricText = string.Format("{0} 当前 {1}，阈值 {2}",
                    string.IsNullOrEmpty(ticket.MetricName) ? "指标" : ticket.MetricName,
                    formatMetric(ticket.CurrentValue),
                    formatMetric(ticket.ThresholdValue.Value));
            }
            else if (ticket.CurrentValue > 0)
            {
                metricText = (string.IsNullOrEmpty(ticket.MetricName) ? "指标" : ticket.MetricName)
                    + " 当前 " + formatMetric(ticket.CurrentValue);
            }

            var displayStatus = MapTicketStatusDisplay(ticket.Status);

            return new WarningItemViewModel
            {
                Key = ticket.Fingerprint,
                Level = ticket.Level ?? "Info",
                Type = ticket.Type ?? "Yield",
                LevelDisplay = MapLevelDisplay(ticket.Level),
                TypeDisplay = MapTypeDisplay(ticket.Type),
                TimeRange = timeRange,
                Title = string.IsNullOrEmpty(title) ? "预警" : title,
                Summary = ticket.Summary ?? string.Empty,
                RuleName = ticket.RuleName ?? string.Empty,
                RuleId = ticket.RuleId ?? string.Empty,
                MetricName = ticket.MetricName ?? string.Empty,
                MetricText = metricText,
                CurrentValue = formatMetric(ticket.CurrentValue),
                BaselineValue = ticket.BaselineValue.HasValue ? formatMetric(ticket.BaselineValue.Value) : string.Empty,
                ThresholdValue = ticket.ThresholdValue.HasValue ? formatMetric(ticket.ThresholdValue.Value) : string.Empty,
                Status = displayStatus,
                AiMarkdown = string.Empty,
                HasAiMarkdown = false,
                SortTime = ticket.LastSeen,
                LastDetected = ticket.LastSeen,
                OccurrenceCount = ticket.OccurrenceCount
            };
        }

        private static string MapTicketStatusDisplay(string status)
        {
            var st = (status ?? string.Empty).Trim().ToLowerInvariant();
            switch (st)
            {
                case "active": return "待处理";
                case "acknowledged": return "已确认";
                case "ignored": return "已忽略";
                case "processed": return "已处理";
                case "resolved": return "已恢复";
                default: return string.IsNullOrEmpty(status) ? "待处理" : status;
            }
        }

        private WarningTicketRecord GetTicketByFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) return null;
            WarningTicketRecord ticket;
            if (_ticketMap.TryGetValue(fingerprint, out ticket))
            {
                return ticket;
            }
            return null;
        }

        private void SaveTickets()
        {
            _ticketStore.SaveAll(_allTickets);
        }

        private WarningItem BuildWarningFromTicket(WarningTicketRecord t)
        {
            if (t == null) return null;
            return new WarningItem
            {
                Key = t.Fingerprint,
                RuleId = t.RuleId,
                RuleName = t.RuleName,
                Level = t.Level,
                Type = t.Type,
                StartTime = t.StartTime,
                EndTime = t.EndTime,
                FirstDetected = t.FirstSeen,
                LastDetected = t.LastSeen,
                MetricName = t.MetricName,
                CurrentValue = t.CurrentValue,
                BaselineValue = t.BaselineValue,
                ThresholdValue = t.ThresholdValue,
                Status = t.Status,
                Summary = t.Summary
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// UI 侧预警模型。
    /// </summary>
    public class WarningItemViewModel : INotifyPropertyChanged
    {
        private string _status;
        private string _aiMarkdown;
        private bool _hasAiMarkdown;
        private int _occurrenceCount;

        public string Key { get; set; }
        public string Level { get; set; }
        public string Type { get; set; }
        public string LevelDisplay { get; set; }
        public string TypeDisplay { get; set; }

        public string Title { get; set; }
        public string Summary { get; set; }
        public string RuleName { get; set; }
        public string RuleId { get; set; }

        public string TimeRange { get; set; }

        public string MetricName { get; set; }
        public string MetricText { get; set; }
        public string CurrentValue { get; set; }
        public string BaselineValue { get; set; }
        public string ThresholdValue { get; set; }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public string AiMarkdown
        {
            get { return _aiMarkdown; }
            set
            {
                if (_aiMarkdown != value)
                {
                    _aiMarkdown = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasAiMarkdown
        {
            get { return _hasAiMarkdown; }
            set
            {
                if (_hasAiMarkdown != value)
                {
                    _hasAiMarkdown = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime SortTime { get; set; }
        public DateTime LastDetected { get; set; }
        public int OccurrenceCount
        {
            get => _occurrenceCount;
            set
            {
                if (_occurrenceCount != value)
                {
                    _occurrenceCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
