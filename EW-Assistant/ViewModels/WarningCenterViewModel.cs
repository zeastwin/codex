using EW_Assistant.Services.Warnings;
using EW_Assistant.Warnings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private readonly WarningAnalysisCache _analysisCache;
        private readonly AiWarningAnalysisService _aiService;
        private bool _isAnalyzingWarnings;

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

        public WarningCenterViewModel()
        {
            _analysisCache = new WarningAnalysisCache(null);
            _analysisCache.Load();
            _aiService = new AiWarningAnalysisService();

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
                    if (!_warningMap.ContainsKey(item.Key))
                    {
                        _warningMap[item.Key] = item;
                    }

                    var vm = ConvertToViewModel(item);
                    if (vm != null)
                    {
                        Warnings.Add(vm);
                    }
                }

                if (Warnings.Count > 1)
                {
                    var ordered = Warnings
                        .OrderByDescending(w => LevelRank(w.Level))
                        .ThenByDescending(w => w.SortTime)
                        .ToList();

                    Warnings.Clear();
                    foreach (var w in ordered)
                        Warnings.Add(w);
                }

                if (Warnings.Count > 0)
                {
                    SelectedWarning = Warnings[0];
                }
                else
                {
                    SelectedWarning = null;
                }
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
                    if (!_warningMap.TryGetValue(w.Key ?? string.Empty, out item) || item == null) continue;

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

        private WarningItemViewModel ConvertToViewModel(WarningItem item)
        {
            if (item == null) return null;
            var timeRange = item.StartTime.ToString("HH:mm") + " - " + item.EndTime.ToString("HH:mm");

            string title = item.RuleName;
            if (!string.IsNullOrEmpty(item.MetricName) && item.CurrentValue > 0)
            {
                if (item.Type == "Yield")
                    title = string.Format("良率异常（{0:P1}）", item.CurrentValue);
                else if (item.Type == "Throughput")
                    title = "产能不足预警";
                else if (item.Type == "Alarm")
                    title = "报警频率异常";
            }

            string metricText = string.Empty;
            if (item.ThresholdValue.HasValue)
            {
                metricText = string.Format("{0} 当前 {1:F1}，阈值 {2:F1}",
                    string.IsNullOrEmpty(item.MetricName) ? "指标" : item.MetricName,
                    item.CurrentValue,
                    item.ThresholdValue.Value);
            }
            else if (item.CurrentValue > 0)
            {
                metricText = (string.IsNullOrEmpty(item.MetricName) ? "指标" : item.MetricName)
                    + " 当前 " + item.CurrentValue.ToString("F1");
            }

            return new WarningItemViewModel
            {
                Key = item.Key,
                Level = item.Level ?? "Info",
                Type = item.Type ?? "Yield",
                TimeRange = timeRange,
                Title = title,
                Summary = item.Summary ?? string.Empty,
                RuleName = item.RuleName ?? string.Empty,
                RuleId = item.RuleId ?? string.Empty,
                MetricName = item.MetricName ?? string.Empty,
                MetricText = metricText,
                CurrentValue = item.CurrentValue.ToString("F1"),
                BaselineValue = item.BaselineValue.HasValue ? item.BaselineValue.Value.ToString("F1") : string.Empty,
                ThresholdValue = item.ThresholdValue.HasValue ? item.ThresholdValue.Value.ToString("F1") : string.Empty,
                Status = string.IsNullOrEmpty(item.Status) ? "未处理" : item.Status,
                AiMarkdown = string.Empty,
                HasAiMarkdown = false,
                SortTime = item.LastDetected,
                LastDetected = item.LastDetected
            };
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

        public string Key { get; set; }
        public string Level { get; set; }
        public string Type { get; set; }

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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
