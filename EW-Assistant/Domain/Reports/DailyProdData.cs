using System;
using System.Collections.Generic;

namespace EW_Assistant.Domain.Reports
{
    /// <summary>
    /// 当日产能报表的本地统计数据。
    /// </summary>
    public class DailyProdData
    {
        public DailyProdData()
        {
            PeakHours = new List<DailyProdHourStat>();
            ValleyHours = new List<DailyProdHourStat>();
            Downtimes = new List<DowntimeWindow>();
            Hours = new List<DailyProdHourStat>();
            Warnings = new List<string>();
        }

        public DateTime Date { get; set; }
        public string Machine { get; set; }

        public int DayPass { get; set; }
        public int DayFail { get; set; }
        public int DayTotal { get; set; }
        public double DayYield { get; set; }
        public int ActiveHours { get; set; }
        public double ActiveRate { get; set; }
        public double Cv { get; set; }

        public IList<DailyProdHourStat> PeakHours { get; set; }
        public IList<DailyProdHourStat> ValleyHours { get; set; }
        public IList<DowntimeWindow> Downtimes { get; set; }
        public IList<DailyProdHourStat> Hours { get; set; }
        public IList<string> Warnings { get; set; }
    }

    public class DailyProdHourStat
    {
        public int Hour { get; set; }
        public int Pass { get; set; }
        public int Fail { get; set; }
        public int Total { get; set; }
        public double Yield { get; set; }
        public IList<string> Tags { get; set; } = new List<string>();
    }

    public class DowntimeWindow
    {
        public int StartHour { get; set; }
        public int EndHour { get; set; }
        public int DurationHours { get; set; }
    }
}
