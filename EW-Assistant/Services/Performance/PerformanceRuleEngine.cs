using System;
using System.Collections.Generic;
using System.Linq;

namespace EW_Assistant.Services
{
    public sealed class PerformanceRuleEngine
    {
        private const int HistorySeconds = 60;
        private const float HighCpuThreshold = 80f;
        private static readonly TimeSpan HighCpuDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Top1Duration = TimeSpan.FromSeconds(20);

        private readonly object _syncRoot = new object();
        private readonly Queue<CpuSnapshot> _history = new Queue<CpuSnapshot>();
        private readonly List<PerformanceEvent> _events = new List<PerformanceEvent>();
        private readonly HashSet<string> _whitelist;

        private DateTime? _highCpuStart;
        private bool _highCpuRaised;

        private int? _top1Pid;
        private DateTime _top1Start;
        private bool _top1Raised;

        private HashSet<int> _nonWhitelistTop3 = new HashSet<int>();

        public PerformanceRuleEngine(IEnumerable<string> whitelist = null)
        {
            _whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Idle",
                "System"
            };

            if (whitelist != null)
            {
                foreach (var name in whitelist)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        _whitelist.Add(name.Trim());
                }
            }
        }

        /// <summary>可配置的白名单进程名集合（不包含 .exe）。</summary>
        public ISet<string> Whitelist => _whitelist;

        /// <summary>返回当前事件列表快照。</summary>
        public IReadOnlyList<PerformanceEvent> GetEventsSnapshot()
        {
            lock (_syncRoot)
            {
                return _events.ToList();
            }
        }

        /// <summary>处理一次采样并返回新增事件。</summary>
        public IReadOnlyList<PerformanceEvent> ProcessSnapshot(CpuSnapshot snapshot)
        {
            if (snapshot == null)
                return Array.Empty<PerformanceEvent>();

            var newEvents = new List<PerformanceEvent>();
            lock (_syncRoot)
            {
                AddSnapshot(snapshot);
                EvaluateHighCpu(snapshot, newEvents);
                EvaluateTop1(snapshot, newEvents);
                EvaluateNonWhitelist(snapshot, newEvents);

                if (newEvents.Count > 0)
                    _events.AddRange(newEvents);
            }

            return newEvents;
        }

        private void AddSnapshot(CpuSnapshot snapshot)
        {
            _history.Enqueue(snapshot);
            var cutoff = snapshot.Timestamp.AddSeconds(-HistorySeconds);
            while (_history.Count > 0 && _history.Peek().Timestamp < cutoff)
                _history.Dequeue();
        }

        private void EvaluateHighCpu(CpuSnapshot snapshot, List<PerformanceEvent> newEvents)
        {
            if (snapshot.TotalCpuUsage > HighCpuThreshold)
            {
                if (!_highCpuStart.HasValue)
                {
                    _highCpuStart = snapshot.Timestamp;
                    _highCpuRaised = false;
                }

                if (!_highCpuRaised && snapshot.Timestamp - _highCpuStart.Value >= HighCpuDuration)
                {
                    newEvents.Add(new PerformanceEvent
                    {
                        EventType = "CPU_TOTAL_HIGH",
                        StartTime = _highCpuStart.Value,
                        RelatedProcess = string.Empty,
                        Description = string.Format("CPU 总占用率连续 30 秒超过 80%，当前 {0:F1}%。", snapshot.TotalCpuUsage)
                    });
                    _highCpuRaised = true;
                }
            }
            else
            {
                _highCpuStart = null;
                _highCpuRaised = false;
            }
        }

        private void EvaluateTop1(CpuSnapshot snapshot, List<PerformanceEvent> newEvents)
        {
            var top1 = snapshot.TopProcesses != null && snapshot.TopProcesses.Count > 0
                ? snapshot.TopProcesses[0]
                : null;

            if (top1 == null)
            {
                ResetTop1();
                return;
            }

            if (!_top1Pid.HasValue || _top1Pid.Value != top1.Pid)
            {
                _top1Pid = top1.Pid;
                _top1Start = snapshot.Timestamp;
                _top1Raised = false;
            }

            if (!_top1Raised && snapshot.Timestamp - _top1Start >= Top1Duration)
            {
                var label = BuildProcessLabel(top1.Name, top1.Pid);
                newEvents.Add(new PerformanceEvent
                {
                    EventType = "TOP1_STREAK",
                    StartTime = _top1Start,
                    RelatedProcess = label,
                    Description = string.Format("进程 {0} 连续 20 秒保持 CPU Top 1。", label)
                });
                _top1Raised = true;
            }
        }

        private void EvaluateNonWhitelist(CpuSnapshot snapshot, List<PerformanceEvent> newEvents)
        {
            var current = new HashSet<int>();
            if (snapshot.TopProcesses == null)
            {
                _nonWhitelistTop3 = current;
                return;
            }

            foreach (var proc in snapshot.TopProcesses.Take(3))
            {
                if (proc == null)
                    continue;

                var name = proc.Name ?? string.Empty;
                if (_whitelist.Contains(name))
                    continue;

                current.Add(proc.Pid);
                if (!_nonWhitelistTop3.Contains(proc.Pid))
                {
                    var label = BuildProcessLabel(name, proc.Pid);
                    newEvents.Add(new PerformanceEvent
                    {
                        EventType = "NON_WHITELIST_TOP3",
                        StartTime = snapshot.Timestamp,
                        RelatedProcess = label,
                        Description = string.Format("非白名单进程进入 CPU Top 3：{0}。", label)
                    });
                }
            }

            _nonWhitelistTop3 = current;
        }

        private void ResetTop1()
        {
            _top1Pid = null;
            _top1Raised = false;
            _top1Start = DateTime.MinValue;
        }

        private static string BuildProcessLabel(string name, int pid)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "PID=" + pid;
            return name + " (PID=" + pid + ")";
        }
    }
}
