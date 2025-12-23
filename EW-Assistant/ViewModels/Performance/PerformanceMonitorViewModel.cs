using EW_Assistant.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace EW_Assistant.ViewModels
{
    public sealed class PerformanceMonitorViewModel : ViewModelBase, IDisposable
    {
        private const int MaxAiHistory = 200;
        private readonly Dispatcher _dispatcher;
        private readonly LocalPerformanceCollector _collector;
        private readonly MemoryUsageCollector _memoryCollector;
        private readonly DiskUsageCollector _diskCollector;
        private readonly PerformanceRuleEngine _ruleEngine;
        private readonly AiPerformanceAnalysisService _aiService;
        private readonly CpuTrendAnalyzer _trendAnalyzer;
        private float _totalCpuUsage;
        private string _aiAnalysisResult = string.Empty;
        private bool _isAiAnalyzing;
        private AiAnalysisRecord _latestAiRecord;
        private CpuTrendSnapshot _cpuTrend;
        private MemorySnapshot _memorySnapshot;

        public PerformanceMonitorViewModel(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            TopProcesses = new ObservableCollection<ProcessSnapshot>();
            Events = new ObservableCollection<PerformanceEvent>();
            AiHistory = new ObservableCollection<AiAnalysisRecord>();
            DiskSnapshots = new ObservableCollection<DiskUsageSnapshot>();
            _memorySnapshot = new MemorySnapshot();
            _cpuTrend = new CpuTrendSnapshot();
            _collector = new LocalPerformanceCollector(_dispatcher, TimeSpan.FromSeconds(1));
            _collector.SnapshotUpdated += OnSnapshotUpdated;
            _memoryCollector = new MemoryUsageCollector(TimeSpan.FromSeconds(1));
            _memoryCollector.SnapshotUpdated += OnMemorySnapshotUpdated;
            _diskCollector = new DiskUsageCollector(TimeSpan.FromSeconds(15));
            _diskCollector.SnapshotUpdated += OnDiskSnapshotUpdated;
            _ruleEngine = new PerformanceRuleEngine();
            _aiService = new AiPerformanceAnalysisService();
            _trendAnalyzer = new CpuTrendAnalyzer();
        }

        public float TotalCpuUsage
        {
            get => _totalCpuUsage;
            private set => SetProperty(ref _totalCpuUsage, value);
        }

        public ObservableCollection<ProcessSnapshot> TopProcesses { get; }
        public ObservableCollection<PerformanceEvent> Events { get; }
        public ObservableCollection<AiAnalysisRecord> AiHistory { get; }
        public ObservableCollection<DiskUsageSnapshot> DiskSnapshots { get; }

        public string AiAnalysisResult
        {
            get => _aiAnalysisResult;
            private set => SetProperty(ref _aiAnalysisResult, value);
        }

        public bool IsAiAnalyzing
        {
            get => _isAiAnalyzing;
            private set => SetProperty(ref _isAiAnalyzing, value);
        }

        public AiAnalysisRecord LatestAiRecord
        {
            get => _latestAiRecord;
            private set => SetProperty(ref _latestAiRecord, value);
        }

        public CpuTrendSnapshot CpuTrend
        {
            get => _cpuTrend;
            private set => SetProperty(ref _cpuTrend, value);
        }

        public MemorySnapshot CurrentMemory
        {
            get => _memorySnapshot;
            private set => SetProperty(ref _memorySnapshot, value);
        }

        public void Start()
        {
            _collector.Start();
            _memoryCollector.Start();
            _diskCollector.Start();
        }

        public void Stop()
        {
            _collector.Stop();
            _memoryCollector.Stop();
            _diskCollector.Stop();
        }

        private void OnSnapshotUpdated(object sender, CpuSnapshotEventArgs e)
        {
            if (e == null || e.Snapshot == null)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => ApplySnapshot(e.Snapshot)));
                return;
            }

            ApplySnapshot(e.Snapshot);
        }

        private void ApplySnapshot(CpuSnapshot snapshot)
        {
            TotalCpuUsage = snapshot.TotalCpuUsage;
            CpuTrend = _trendAnalyzer.Update(snapshot.TotalCpuUsage, snapshot.Timestamp);

            TopProcesses.Clear();
            foreach (var item in snapshot.TopProcesses)
            {
                TopProcesses.Add(item);
            }

            var newEvents = _ruleEngine.ProcessSnapshot(snapshot);
            foreach (var evt in newEvents)
            {
                Events.Add(evt);
            }
        }

        private void OnMemorySnapshotUpdated(object sender, MemorySnapshotEventArgs e)
        {
            if (e == null || e.Snapshot == null)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => UpdateMemorySnapshot(e.Snapshot)));
                return;
            }

            UpdateMemorySnapshot(e.Snapshot);
        }

        private void UpdateMemorySnapshot(MemorySnapshot snapshot)
        {
            CurrentMemory = snapshot;
        }

        private void OnDiskSnapshotUpdated(object sender, DiskUsageSnapshotEventArgs e)
        {
            if (e == null || e.Snapshots == null)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => UpdateDiskSnapshots(e.Snapshots)));
                return;
            }

            UpdateDiskSnapshots(e.Snapshots);
        }

        private void UpdateDiskSnapshots(IReadOnlyList<DiskUsageSnapshot> snapshots)
        {
            DiskSnapshots.Clear();
            for (int i = 0; i < snapshots.Count; i++)
            {
                DiskSnapshots.Add(snapshots[i]);
            }
        }

        public async Task AnalyzeAsync(AiAnalysisContext context)
        {
            if (context == null)
                return;

            SetAiAnalyzing(true);
            try
            {
                var result = await _aiService.AnalyzeAsync(context).ConfigureAwait(false);
                AppendAiRecord(result, DateTime.Now);
            }
            finally
            {
                SetAiAnalyzing(false);
            }
        }

        private void AppendAiRecord(string result, DateTime timestamp)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => AppendAiRecord(result, timestamp)));
                return;
            }

            var content = result ?? string.Empty;
            var severity = InferSeverity(content);
            var record = new AiAnalysisRecord
            {
                Timestamp = timestamp,
                Severity = severity,
                SeverityLabel = BuildSeverityLabel(severity),
                Content = content,
                Summary = BuildSummary(content)
            };

            LatestAiRecord = record;
            AiAnalysisResult = content;
            AiHistory.Insert(0, record);
            TrimAiHistory(DateTime.Now);
            if (AiHistory.Count > MaxAiHistory)
                AiHistory.RemoveAt(AiHistory.Count - 1);
        }

        private void SetAiAnalyzing(bool value)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => SetAiAnalyzing(value)));
                return;
            }

            IsAiAnalyzing = value;
        }

        private static string InferSeverity(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Info";

            var value = text.ToLowerInvariant();
            if (value.Contains("严重") || value.Contains("高风险") || value.Contains("高危") || value.Contains("critical") || value.Contains("紧急"))
                return "Critical";
            if (value.Contains("警告") || value.Contains("warning") || value.Contains("中风险") || value.Contains("异常"))
                return "Warning";
            if (value.Contains("提示") || value.Contains("低风险") || value.Contains("轻微"))
                return "Info";

            return "Info";
        }

        private static string BuildSummary(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "无分析内容";

            var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length <= 120)
                return normalized;

            return normalized.Substring(0, 120) + "...";
        }

        private static string BuildSeverityLabel(string severity)
        {
            switch (severity)
            {
                case "Critical":
                    return "严重";
                case "Warning":
                    return "警告";
                default:
                    return "提示";
            }
        }

        private void TrimAiHistory(DateTime now)
        {
            var cutoff = now.AddDays(-7);
            for (int i = AiHistory.Count - 1; i >= 0; i--)
            {
                if (AiHistory[i].Timestamp < cutoff)
                    AiHistory.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            _collector.SnapshotUpdated -= OnSnapshotUpdated;
            _collector.Dispose();
            _memoryCollector.SnapshotUpdated -= OnMemorySnapshotUpdated;
            _memoryCollector.Dispose();
            _diskCollector.SnapshotUpdated -= OnDiskSnapshotUpdated;
            _diskCollector.Dispose();
        }
    }
}
