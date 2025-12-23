using System;

namespace EW_Assistant.Services
{
    public sealed class CpuTrendSnapshot
    {
        public float Avg1Min { get; set; }
        public float Avg5Min { get; set; }
        public float Avg15Min { get; set; }
        public float Max { get; set; }
        public float Min { get; set; }
        public float Fluctuation { get; set; }
        public string StabilityLevel { get; set; } = "Stable";
    }
}
