using System;

namespace StressTest.Models
{
    public sealed class StressConfig
    {
        public StressConfig(int durationSeconds, int deviceCount)
        {
            if (durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "压测时长必须大于 0。");
            if (deviceCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(deviceCount), "设备数必须大于 0。");

            DurationSeconds = durationSeconds;
            DeviceCount = deviceCount;
            var rampUp = Math.Max(10, Math.Min(60, (int)Math.Round(durationSeconds * 0.2)));
            RampUpSeconds = Math.Min(durationSeconds, rampUp);
            ThinkTimeBaseMs = 1000;
            ThinkTimeJitterMs = 1000;
        }

        public int DurationSeconds { get; }
        public int DeviceCount { get; }
        public int RampUpSeconds { get; }
        public int ThinkTimeBaseMs { get; }
        public int ThinkTimeJitterMs { get; }
    }
}
