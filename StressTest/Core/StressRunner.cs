using StressTest.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace StressTest.Core
{
    public sealed class StressRunner
    {
        // ErrorDesc 循环发送的示例文本
        private static readonly string[] ErrorDescriptions =
        {
            "未测流道A模组顶升工位1状态不符报警",
            "[A未测吸嘴2状态不符报警",
            "未测流道A模组顶升工位2状态不符报警",
            "A已测吸嘴1状态不符报警",
            "C31_A取料盘夹爪气缸动位报警",
            "B空盘仓满盘报警",
            "未测流道A模组阻挡工位1状态不符报警",
            "已测流道有料状态[1]状态不符报警"
        };

        private static long _errorDescIndex;

        private readonly StressConfig _config;
        private readonly AppConfigSnapshot _appConfig;
        private readonly MetricsAggregator _metrics;
        private readonly WorkflowSseClient _client;
        private readonly Action<string> _log;

        public StressRunner(
            StressConfig config,
            AppConfigSnapshot appConfig,
            MetricsAggregator metrics,
            WorkflowSseClient client,
            Action<string> log)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _log = log;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            ServicePointManager.DefaultConnectionLimit = Math.Max(100, _config.DeviceCount * 2);
            ServicePointManager.Expect100Continue = false;

            var startUtc = DateTime.UtcNow;
            var endUtc = startUtc.AddSeconds(_config.DurationSeconds);
            _metrics.Reset(startUtc);

            _log?.Invoke($"开始压测：时长 {_config.DurationSeconds}s，设备 {_config.DeviceCount} 台，升压 {_config.RampUpSeconds}s。");

            var tasks = new List<Task>(_config.DeviceCount);
            for (var i = 0; i < _config.DeviceCount; i++)
            {
                var deviceId = i + 1;
                var delayMs = (int)((long)_config.RampUpSeconds * 1000 * i / _config.DeviceCount);

                tasks.Add(Task.Run(async () =>
                {
                    if (delayMs > 0)
                        await Task.Delay(delayMs, ct).ConfigureAwait(false);

                    await RunDeviceLoop(deviceId, endUtc, ct).ConfigureAwait(false);
                }, ct));
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log?.Invoke("已触发停止，等待任务退出。");
            }
            finally
            {
                _metrics.MarkEnded(DateTime.UtcNow);
            }

            _log?.Invoke("压测结束。");
        }

        private async Task RunDeviceLoop(int deviceId, DateTime endUtc, CancellationToken ct)
        {
            var random = new Random(unchecked(deviceId * 397 + Environment.TickCount));
            var seq = 0;

            while (!ct.IsCancellationRequested && DateTime.UtcNow < endUtc)
            {
                var prompt = GetNextErrorDesc();
                var machineCode = $"MC-{deviceId:000}";
                var user = $"stress-{deviceId}-{seq++}";

                _metrics.MarkRequestStart();

                RequestResult result;
                try
                {
                    result = await _client.RunAsync(
                        _appConfig.WorkflowRunUrl,
                        _appConfig.AutoKey,
                        prompt,
                        machineCode,
                        user,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = new RequestResult { Canceled = true, ErrorMessage = "请求被取消" };
                }
                catch (Exception ex)
                {
                    result = new RequestResult { ErrorMessage = ex.Message };
                }

                _metrics.RecordResult(result, DateTime.UtcNow);

                if (ct.IsCancellationRequested || DateTime.UtcNow >= endUtc)
                    break;

                var delay = _config.ThinkTimeBaseMs + random.Next(_config.ThinkTimeJitterMs + 1);
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static string GetNextErrorDesc()
        {
            if (ErrorDescriptions.Length == 0)
                return string.Empty;

            var index = Interlocked.Increment(ref _errorDescIndex) - 1;
            var slot = (int)(index % ErrorDescriptions.Length);
            if (slot < 0)
                slot += ErrorDescriptions.Length;

            return ErrorDescriptions[slot];
        }
    }
}
