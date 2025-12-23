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
        private readonly PerformanceMonitorService _monitorService;
        private readonly AiPerformanceAnalysisService _aiService;
        private readonly AiAnalysisContextBuilder _contextBuilder = new AiAnalysisContextBuilder();
        private readonly AiAnalysisHistoryStore _historyStore = AiAnalysisHistoryStore.Instance;
        private float _totalCpuUsage;
        private string _aiAnalysisResult = string.Empty;
        private bool _isAiAnalyzing;
        private AiAnalysisRecord _latestAiRecord;
        private MemorySnapshot _memorySnapshot;
        private string _cpuAlertMessage = string.Empty;
        private bool _isCpuAlertActive;
        private string _diskAlertMessage = string.Empty;
        private bool _isDiskAlertActive;
        private bool _isSubscribed;
        private bool _historyLoaded;

        public PerformanceMonitorViewModel(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            TopProcesses = new ObservableCollection<ProcessSnapshot>();
            Events = new ObservableCollection<PerformanceEvent>();
            AiHistory = new ObservableCollection<AiAnalysisRecord>();
            DiskSnapshots = new ObservableCollection<DiskUsageSnapshot>();
            _memorySnapshot = new MemorySnapshot();
            _aiService = new AiPerformanceAnalysisService();
            _monitorService = PerformanceMonitorService.Instance;
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

        public MemorySnapshot CurrentMemory
        {
            get => _memorySnapshot;
            private set => SetProperty(ref _memorySnapshot, value);
        }

        public string CpuAlertMessage
        {
            get => _cpuAlertMessage;
            private set => SetProperty(ref _cpuAlertMessage, value);
        }

        public bool IsCpuAlertActive
        {
            get => _isCpuAlertActive;
            private set => SetProperty(ref _isCpuAlertActive, value);
        }

        public async Task TriggerTestCpuAlertAndAnalyzeAsync()
        {
            var testSnapshot = _monitorService.TriggerTestCpuAlert();
            ApplyCpuAlertState();

            if (IsAiAnalyzing)
                return;

            var eventsSnapshot = _monitorService.GetEventsSnapshot();
            var averageCpu = _monitorService.AverageCpuUsage5Min;
            var context = _contextBuilder.Build(testSnapshot, averageCpu, eventsSnapshot);
            await AnalyzeAsync(context);
        }

        public string DiskAlertMessage
        {
            get => _diskAlertMessage;
            private set => SetProperty(ref _diskAlertMessage, value);
        }

        public bool IsDiskAlertActive
        {
            get => _isDiskAlertActive;
            private set => SetProperty(ref _isDiskAlertActive, value);
        }

        public void Start()
        {
            _monitorService.Start();
            Subscribe();
            LoadAiHistory();
            RefreshFromService();
        }

        public void Stop()
        {
            Unsubscribe();
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

            TopProcesses.Clear();
            foreach (var item in snapshot.TopProcesses)
            {
                TopProcesses.Add(item);
            }

            ApplyCpuAlertState();
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
                _dispatcher.BeginInvoke(new Action(() => UpdateDiskSnapshotsAndAlert(e.Snapshots)));
                return;
            }

            UpdateDiskSnapshotsAndAlert(e.Snapshots);
        }

        private void UpdateDiskSnapshots(IReadOnlyList<DiskUsageSnapshot> snapshots)
        {
            DiskSnapshots.Clear();
            for (int i = 0; i < snapshots.Count; i++)
            {
                DiskSnapshots.Add(snapshots[i]);
            }
        }

        private void UpdateDiskSnapshotsAndAlert(IReadOnlyList<DiskUsageSnapshot> snapshots)
        {
            UpdateDiskSnapshots(snapshots);
            ApplyDiskAlertState();
        }

        private void OnPerformanceEventsRaised(object sender, PerformanceEventsEventArgs e)
        {
            if (e == null || e.Events == null)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => OnPerformanceEventsRaised(sender, e)));
                return;
            }

            foreach (var evt in e.Events)
            {
                Events.Add(evt);
            }
        }

        private void Subscribe()
        {
            if (_isSubscribed)
                return;

            _monitorService.CpuSnapshotUpdated += OnSnapshotUpdated;
            _monitorService.MemorySnapshotUpdated += OnMemorySnapshotUpdated;
            _monitorService.DiskSnapshotUpdated += OnDiskSnapshotUpdated;
            _monitorService.PerformanceEventsRaised += OnPerformanceEventsRaised;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed)
                return;

            _monitorService.CpuSnapshotUpdated -= OnSnapshotUpdated;
            _monitorService.MemorySnapshotUpdated -= OnMemorySnapshotUpdated;
            _monitorService.DiskSnapshotUpdated -= OnDiskSnapshotUpdated;
            _monitorService.PerformanceEventsRaised -= OnPerformanceEventsRaised;
            _isSubscribed = false;
        }

        private void RefreshFromService()
        {
            var snapshot = _monitorService.LastCpuSnapshot;
            if (snapshot != null)
                ApplySnapshot(snapshot);

            var memory = _monitorService.LastMemorySnapshot;
            if (memory != null)
                UpdateMemorySnapshot(memory);

            var disks = _monitorService.LastDiskSnapshots;
            if (disks != null)
                UpdateDiskSnapshots(disks);
            ApplyDiskAlertState();

            ApplyCpuAlertState();

            var eventsSnapshot = _monitorService.GetEventsSnapshot();
            if (eventsSnapshot != null && eventsSnapshot.Count > 0)
            {
                Events.Clear();
                foreach (var evt in eventsSnapshot)
                {
                    Events.Add(evt);
                }
            }
        }

        private void ApplyDiskAlertState()
        {
            DiskAlertMessage = _monitorService.DiskAlertMessage ?? string.Empty;
            IsDiskAlertActive = _monitorService.IsDiskAlertActive;
        }

        private void ApplyCpuAlertState()
        {
            CpuAlertMessage = _monitorService.CpuAlertMessage ?? string.Empty;
            IsCpuAlertActive = _monitorService.IsCpuAlertActive;
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

            _historyStore.Append(record);
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

        private void LoadAiHistory()
        {
            if (_historyLoaded)
                return;

            var snapshot = _historyStore.GetSnapshot();
            if (snapshot.Count > 0)
            {
                AiHistory.Clear();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    AiHistory.Add(snapshot[i]);
                }

                LatestAiRecord = AiHistory[0];
                AiAnalysisResult = LatestAiRecord.Content;
            }

            _historyLoaded = true;
        }

        public void Dispose()
        {
            Unsubscribe();
        }
    }
}
