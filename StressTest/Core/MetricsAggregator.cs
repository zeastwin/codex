using StressTest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StressTest.Core
{
    public sealed class MetricsAggregator
    {
        private sealed class Bucket
        {
            public int Count;
            public int Success;
            public int Fail;
            public int Canceled;
            public readonly List<double> Latencies = new List<double>();
            public readonly List<double> Ttfb = new List<double>();
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, Bucket> _buckets = new Dictionary<int, Bucket>();
        private readonly List<double> _allLatencies = new List<double>();
        private readonly List<double> _allTtfb = new List<double>();
        private readonly Dictionary<string, int> _errorTop = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _inflight;
        private int _ttfbCount;
        private double _latencySum;
        private double _ttfbSum;

        public DateTime StartTimeUtc { get; private set; }
        public DateTime? EndTimeUtc { get; private set; }
        public int TotalRequests { get; private set; }
        public int SuccessRequests { get; private set; }
        public int FailedRequests { get; private set; }
        public int CanceledRequests { get; private set; }

        public void Reset(DateTime startUtc)
        {
            lock (_lock)
            {
                _buckets.Clear();
                _allLatencies.Clear();
                _allTtfb.Clear();
                _errorTop.Clear();
                _latencySum = 0;
                _ttfbSum = 0;
                _ttfbCount = 0;
                TotalRequests = 0;
                SuccessRequests = 0;
                FailedRequests = 0;
                CanceledRequests = 0;
            }

            StartTimeUtc = startUtc;
            EndTimeUtc = null;
            Interlocked.Exchange(ref _inflight, 0);
        }

        public void MarkEnded(DateTime endUtc)
        {
            EndTimeUtc = endUtc;
        }

        public void MarkRequestStart()
        {
            Interlocked.Increment(ref _inflight);
        }

        public void RecordResult(RequestResult result, DateTime endUtc)
        {
            Interlocked.Decrement(ref _inflight);

            var elapsed = endUtc - StartTimeUtc;
            var sec = (int)Math.Floor(elapsed.TotalSeconds);
            if (sec < 0)
                sec = 0;

            lock (_lock)
            {
                if (!_buckets.TryGetValue(sec, out var bucket))
                {
                    bucket = new Bucket();
                    _buckets[sec] = bucket;
                }

                bucket.Count++;

                TotalRequests++;

                if (result.Canceled)
                {
                    bucket.Canceled++;
                    CanceledRequests++;
                }
                else if (result.Success)
                {
                    bucket.Success++;
                    SuccessRequests++;
                }
                else
                {
                    bucket.Fail++;
                    FailedRequests++;
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    {
                        if (_errorTop.TryGetValue(result.ErrorMessage, out var count))
                            _errorTop[result.ErrorMessage] = count + 1;
                        else
                            _errorTop[result.ErrorMessage] = 1;
                    }
                }

                if (result.DurationMs > 0)
                {
                    _latencySum += result.DurationMs;
                    _allLatencies.Add(result.DurationMs);
                    bucket.Latencies.Add(result.DurationMs);
                }

                if (result.TtfbMs.HasValue && result.TtfbMs.Value > 0)
                {
                    _ttfbSum += result.TtfbMs.Value;
                    _ttfbCount++;
                    _allTtfb.Add(result.TtfbMs.Value);
                    bucket.Ttfb.Add(result.TtfbMs.Value);
                }
            }
        }

        public MetricsSnapshot BuildSnapshot()
        {
            List<TimeSeriesPoint> series;
            Dictionary<string, int> errorTop;
            int inflight = Interlocked.CompareExchange(ref _inflight, 0, 0);
            double avgLatency;
            double avgTtfb;
            double p95Latency;
            double p95Ttfb;
            int totalRequests;
            int successRequests;
            int failedRequests;
            int canceledRequests;
            DateTime startUtc;
            DateTime? endUtc;

            lock (_lock)
            {
                totalRequests = TotalRequests;
                successRequests = SuccessRequests;
                failedRequests = FailedRequests;
                canceledRequests = CanceledRequests;
                startUtc = StartTimeUtc;
                endUtc = EndTimeUtc;

                avgLatency = totalRequests > 0 ? _latencySum / totalRequests : 0;
                avgTtfb = _ttfbCount > 0 ? _ttfbSum / _ttfbCount : 0;
                p95Latency = Percentile(_allLatencies, 0.95);
                p95Ttfb = Percentile(_allTtfb, 0.95);

                series = new List<TimeSeriesPoint>();
                foreach (var pair in _buckets.OrderBy(p => p.Key))
                {
                    var bucket = pair.Value;
                    var count = bucket.Count;
                    var avgMs = count > 0 ? bucket.Latencies.DefaultIfEmpty(0).Average() : 0;
                    var avgTtfbMs = bucket.Ttfb.Count > 0 ? bucket.Ttfb.Average() : 0;
                    var p95Ms = Percentile(bucket.Latencies, 0.95);
                    var p95TtfbMs = Percentile(bucket.Ttfb, 0.95);
                    var rate = (bucket.Success + bucket.Fail) > 0
                        ? (double)bucket.Fail / (bucket.Success + bucket.Fail) * 100
                        : 0;

                    series.Add(new TimeSeriesPoint
                    {
                        Second = pair.Key,
                        Count = count,
                        Success = bucket.Success,
                        Fail = bucket.Fail,
                        Canceled = bucket.Canceled,
                        AvgLatencyMs = avgMs,
                        P95LatencyMs = p95Ms,
                        AvgTtfbMs = avgTtfbMs,
                        P95TtfbMs = p95TtfbMs,
                        ErrorRate = rate
                    });
                }

                errorTop = _errorTop
                    .OrderByDescending(p => p.Value)
                    .Take(8)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            }

            var nowUtc = endUtc ?? DateTime.UtcNow;
            var elapsedSeconds = Math.Max(0.001, (nowUtc - startUtc).TotalSeconds);
            var currentSecond = (int)Math.Floor(elapsedSeconds);
            var currentBucket = series.FirstOrDefault(p => p.Second == currentSecond);
            var currentRps = currentBucket?.Count ?? 0;
            var errorRate = (successRequests + failedRequests) > 0
                ? (double)failedRequests / (successRequests + failedRequests) * 100
                : 0;

            return new MetricsSnapshot
            {
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                TotalRequests = totalRequests,
                SuccessRequests = successRequests,
                FailedRequests = failedRequests,
                CanceledRequests = canceledRequests,
                Inflight = inflight,
                AvgLatencyMs = avgLatency,
                P95LatencyMs = p95Latency,
                AvgTtfbMs = avgTtfb,
                P95TtfbMs = p95Ttfb,
                AvgRps = totalRequests / elapsedSeconds,
                CurrentRps = currentRps,
                ErrorRate = errorRate,
                Series = series,
                ErrorTop = errorTop
            };
        }

        private static double Percentile(IReadOnlyList<double> source, double percentile)
        {
            if (source == null || source.Count == 0)
                return 0;

            var data = source.ToList();
            data.Sort();
            var index = (int)Math.Ceiling(percentile * data.Count) - 1;
            if (index < 0) index = 0;
            if (index >= data.Count) index = data.Count - 1;
            return data[index];
        }
    }
}
