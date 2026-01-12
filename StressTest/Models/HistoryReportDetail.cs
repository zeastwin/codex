using System.Collections.Generic;

namespace StressTest.Models
{
    public sealed class HistoryReportDetail
    {
        public HistoryReportSummary Summary { get; set; }
        public IReadOnlyList<TimeSeriesPoint> Series { get; set; }
        public IReadOnlyList<ErrorItem> Errors { get; set; }
    }
}
