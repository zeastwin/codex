using System;
using System.Collections.Generic;
using System.Linq;
using EW_Assistant.Domain.Reports;
using EW_Assistant.Warnings;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 本地计算报警周报数据，对齐 MCP 读取方式。
    /// </summary>
    public class WeeklyAlarmCalculator
    {
        private readonly AlarmCsvReader _alarmReader;

        public WeeklyAlarmCalculator(AlarmCsvReader alarmReader = null)
        {
            _alarmReader = alarmReader ?? new AlarmCsvReader();
        }

        public WeeklyAlarmData Calculate(DateTime start, DateTime end)
        {
            var data = new WeeklyAlarmData
            {
                StartDate = start.Date,
                EndDate = end.Date
            };

            try
            {
                var startAt = start.Date;
                var endAt = end.Date.AddDays(1);
                var rows = _alarmReader.GetAlarms(startAt, endAt) ?? new List<AlarmHourStat>();

                data.TotalCount = rows.Sum(r => r.Count);
                data.TotalDurationSeconds = rows.Sum(r => r.DowntimeMinutes) * 60d;
                data.ActiveHours = rows.Select(r => r.Hour).Distinct().Count();

                data.ByDay = rows.GroupBy(r => r.Hour.Date)
                    .Select(g => new WeeklyAlarmDay
                    {
                        Date = g.Key,
                        AlarmCount = g.Sum(x => x.Count),
                        AlarmSeconds = g.Sum(x => x.DowntimeMinutes) * 60d,
                        Yield = 0d,
                        TopAlarm = g.OrderByDescending(x => x.DowntimeMinutes).FirstOrDefault()?.Code
                    })
                    .OrderBy(d => d.Date)
                    .ToList();

                data.Top = rows.GroupBy(r => r.Code ?? "UNKNOWN")
                    .Select(g => new WeeklyAlarmTop
                    {
                        Code = string.IsNullOrWhiteSpace(g.Key) ? "UNKNOWN" : g.Key,
                        Content = g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Message))?.Message ?? "无",
                        Count = g.Sum(x => x.Count),
                        DurationSeconds = g.Sum(x => x.DowntimeMinutes) * 60d
                    })
                    .OrderByDescending(t => t.DurationSeconds)
                    .Take(10)
                    .ToList();
            }
            catch (Exception ex)
            {
                data.Warnings.Add("计算报警周报时出错：" + ex.Message);
            }

            return data;
        }
    }
}
