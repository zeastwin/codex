using System;
using System.Collections.Generic;
using System.Linq;
using EW_Assistant.Domain.Reports;
using EW_Assistant.Warnings;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 本地 CSV 计算当日报警数据（对齐 MCP 使用的 AlarmCsvReader + 产能合并）。
    /// </summary>
    public class DailyAlarmCalculator
    {
        private readonly AlarmCsvReader _alarmReader;
        private readonly ProductionCsvReader _prodReader;

        public DailyAlarmCalculator(AlarmCsvReader alarmReader = null, ProductionCsvReader prodReader = null)
        {
            _alarmReader = alarmReader ?? new AlarmCsvReader();
            _prodReader = prodReader ?? new ProductionCsvReader();
        }

        public DailyAlarmData Calculate(DateTime date)
        {
            var data = new DailyAlarmData { Date = date.Date };
            try
            {
                var start = date.Date;
                var end = start.AddDays(1);

                var prodRows = _prodReader.GetProductionRange(start, end) ?? new List<ProductionHourRecord>();
                var alarmRows = _alarmReader.GetAlarms(start, end) ?? new List<AlarmHourStat>();

                var hours = new List<DailyAlarmHourStat>();
                for (int h = 0; h < 24; h++)
                {
                    var at = start.AddHours(h);
                    var p = prodRows.FirstOrDefault(r => r.Hour.Hour == h);
                    var alarmHour = alarmRows.Where(a => a.Hour.Hour == h).ToList();

                    var pass = p != null ? p.Pass : 0;
                    var fail = p != null ? p.Fail : 0;
                    var total = pass + fail;
                    var yield = total > 0 ? (double)pass / total : 0d;

                    var alarmCount = alarmHour.Sum(a => a.Count);
                    var alarmSeconds = alarmHour.Sum(a => a.DowntimeMinutes) * 60d;
                    var top = alarmHour.OrderByDescending(a => a.DowntimeMinutes).FirstOrDefault();

                    hours.Add(new DailyAlarmHourStat
                    {
                        Hour = h,
                        Pass = pass,
                        Fail = fail,
                        Total = total,
                        Yield = yield,
                        AlarmCount = alarmCount,
                        AlarmSeconds = alarmSeconds,
                        TopAlarmCode = top != null ? top.Code : "无",
                        TopAlarmSeconds = top != null ? top.DowntimeMinutes * 60d : 0d,
                        TopAlarmContent = string.IsNullOrWhiteSpace(top?.Message) ? "无" : top.Message
                    });
                }

                data.Hours = hours;
                data.DayAlarmCount = hours.Sum(x => x.AlarmCount);
                data.DayAlarmSeconds = hours.Sum(x => x.AlarmSeconds);
                data.ActiveHours = hours.Count(h => h.AlarmSeconds > 0);
                data.AvgPerAlarmSeconds = data.DayAlarmCount > 0 ? data.DayAlarmSeconds / data.DayAlarmCount : 0d;

                var peak = hours.OrderByDescending(h => h.AlarmSeconds).FirstOrDefault(h => h.AlarmSeconds > 0);
                if (peak != null)
                {
                    data.PeakHour = new HourWindow { Hour = peak.Hour, Seconds = peak.AlarmSeconds };
                }

                ApplyTags(hours);

                if (prodRows.Count == 0)
                {
                    data.Warnings.Add("未读取到产能 CSV，产能列填 0。");
                }
                if (alarmRows.Count == 0)
                {
                    data.Warnings.Add("未读取到报警 CSV，报警列填 0。");
                }
            }
            catch (Exception ex)
            {
                data.Warnings.Add("计算报警数据时发生错误：" + ex.Message);
            }

            return data;
        }

        private static void ApplyTags(IList<DailyAlarmHourStat> hours)
        {
            var nonZero = hours.Where(h => h.AlarmSeconds > 0).OrderByDescending(h => h.AlarmSeconds).Take(3).Select(h => h.Hour).ToHashSet();
            foreach (var h in hours)
            {
                if (h.AlarmSeconds <= 0)
                {
                    h.Tags.Add("无报警");
                }
                else
                {
                    if (nonZero.Contains(h.Hour))
                    {
                        h.Tags.Add("高频");
                    }
                    if (h.AvgSeconds() >= 300) h.Tags.Add("长报警");
                    if (h.AvgSeconds() <= 30) h.Tags.Add("短报警");
                }
            }
        }
    }

    internal static class DailyAlarmStatExtensions
    {
        public static double AvgSeconds(this DailyAlarmHourStat h)
        {
            if (h == null || h.AlarmCount <= 0) return 0d;
            return h.AlarmSeconds / Math.Max(1, h.AlarmCount);
        }
    }
}
