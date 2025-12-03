using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EW_Assistant.Domain.Reports;
using EW_Assistant.Warnings;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 本地 CSV 计算当日产能数据，补齐 24 小时缺口并标记峰值/低谷/停机区间。
    /// </summary>
    public class DailyProdCalculator
    {
        private readonly ProductionCsvReader _reader;

        public DailyProdCalculator(ProductionCsvReader reader = null)
        {
            _reader = reader ?? new ProductionCsvReader();
        }

        /// <summary>
        /// 汇总指定日期的小时级产能数据，并生成标签、峰谷、停机等辅助信息。
        /// </summary>
        public DailyProdData Calculate(DateTime date)
        {
            var data = new DailyProdData
            {
                Date = date.Date
            };

            try
            {
                var start = date.Date;
                var end = start.AddDays(1);
                var rows = _reader.GetProductionRange(start, end) ?? new List<ProductionHourRecord>();

                var hours = new List<DailyProdHourStat>();
                // 固定补齐 0-23 点，缺失小时填 0，避免后续图表出现断层
                for (int h = 0; h < 24; h++)
                {
                    var rec = rows.FirstOrDefault(r => r.Hour.Hour == h);
                    var pass = rec != null ? rec.Pass : 0;
                    var fail = rec != null ? rec.Fail : 0;
                    var total = pass + fail;
                    var yield = total > 0 ? (double)pass / total : 0d;
                    hours.Add(new DailyProdHourStat
                    {
                        Hour = h,
                        Pass = pass,
                        Fail = fail,
                        Total = total,
                        Yield = yield
                    });
                }

                data.Hours = hours;
                data.DayPass = hours.Sum(x => x.Pass);
                data.DayFail = hours.Sum(x => x.Fail);
                data.DayTotal = data.DayPass + data.DayFail;
                data.DayYield = data.DayTotal > 0 ? (double)data.DayPass / data.DayTotal : 0d;
                data.ActiveHours = hours.Count(x => x.Total > 0);
                data.ActiveRate = 24 > 0 ? (double)data.ActiveHours / 24d : 0d;
                data.Cv = CalculateCv(hours.Select(h => h.Total));

                MarkPeaksAndValleys(hours, data);
                data.Downtimes = DetectDowntimes(hours);
                ApplyTags(hours);

                if (rows.Count == 0)
                {
                    data.Warnings.Add("未读取到任何小时产能数据，可能缺少当日 CSV。");
                }
            }
            catch (Exception ex)
            {
                data.Warnings.Add("计算产能数据时发生错误：" + ex.Message);
            }

            return data;
        }

        /// <summary>
        /// 计算变异系数（标准差/均值），用于衡量产能波动。
        /// </summary>
        private static double CalculateCv(IEnumerable<int> values)
        {
            var arr = values?.ToList() ?? new List<int>();
            if (arr.Count == 0) return 0d;
            var mean = arr.Average();
            if (mean == 0) return 0d;
            var variance = arr.Average(v => Math.Pow(v - mean, 2));
            var stddev = Math.Sqrt(variance);
            return stddev / mean;
        }

        /// <summary>
        /// 选取非零小时的前 3 个峰值与低谷，供 UI 标记。
        /// </summary>
        private static void MarkPeaksAndValleys(IList<DailyProdHourStat> hours, DailyProdData data)
        {
            var nonZero = hours.Where(h => h.Total > 0).OrderByDescending(h => h.Total).ToList();
            data.PeakHours = nonZero.Take(3).ToList();
            data.ValleyHours = nonZero.OrderBy(h => h.Total).Take(3).ToList();
        }

        /// <summary>
        /// 检测连续无产出的时段，输出按时长降序的停机窗口。
        /// </summary>
        private static IList<DowntimeWindow> DetectDowntimes(IList<DailyProdHourStat> hours)
        {
            var list = new List<DowntimeWindow>();
            int start = -1;
            for (int i = 0; i < hours.Count; i++)
            {
                if (hours[i].Total <= 0)
                {
                    if (start < 0) start = i;
                }
                else
                {
                    if (start >= 0)
                    {
                        list.Add(new DowntimeWindow
                        {
                            StartHour = start,
                            EndHour = i,
                            DurationHours = i - start
                        });
                        start = -1;
                    }
                }
            }

            if (start >= 0)
            {
                list.Add(new DowntimeWindow
                {
                    StartHour = start,
                    EndHour = 24,
                    DurationHours = 24 - start
                });
            }

            return list.OrderByDescending(d => d.DurationHours).ThenBy(d => d.StartHour).ToList();
        }

        /// <summary>
        /// 为每个小时打上业务标签（停机/峰值/低谷/低良率/稳定）。
        /// </summary>
        private static void ApplyTags(IList<DailyProdHourStat> hours)
        {
            var peakHours = hours.Where(h => h.Total > 0).OrderByDescending(h => h.Total).Take(3).Select(h => h.Hour).ToHashSet();
            var valleyHours = hours.Where(h => h.Total > 0).OrderBy(h => h.Total).Take(3).Select(h => h.Hour).ToHashSet();

            foreach (var h in hours)
            {
                if (h.Total <= 0)
                {
                    h.Tags.Add("停机/无产出");
                }
                if (peakHours.Contains(h.Hour))
                {
                    h.Tags.Add("峰值");
                }
                if (valleyHours.Contains(h.Hour))
                {
                    h.Tags.Add("低谷");
                }
                if (h.Yield < 0.85 && h.Total > 0)
                {
                    h.Tags.Add("低良率");
                }
                if (h.Yield >= 0.98 && h.Total > 0)
                {
                    h.Tags.Add("稳定");
                }
            }
        }
    }

    /// <summary>
    /// 本地 CSV 计算当日报警数据，并与产能数据对齐，用于报表/看板展示。
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

        /// <summary>
        /// 汇总指定日期的报警与产能，补齐 24 小时空窗并计算均值、峰值等指标。
        /// </summary>
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

                ApplyAlarmTags(hours);

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

        /// <summary>
        /// 按报警时长、均值为小时记录打标签，便于快速辨识高频/长短报警。
        /// </summary>
        private static void ApplyAlarmTags(IList<DailyAlarmHourStat> hours)
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
        /// <summary>
        /// 计算当前小时的单次平均报警时长（秒），无报警时返回 0。
        /// </summary>
        public static double AvgSeconds(this DailyAlarmHourStat h)
        {
            if (h == null || h.AlarmCount <= 0) return 0d;
            return h.AlarmSeconds / Math.Max(1, h.AlarmCount);
        }
    }

    /// <summary>
    /// 本地计算周产能，输出与 MCP 工具一致的字段（总量、均值、波动、峰谷）。
    /// </summary>
    public class WeeklyProdCalculator
    {
        private readonly ProductionCsvReader _reader;

        public WeeklyProdCalculator(ProductionCsvReader reader = null)
        {
            _reader = reader ?? new ProductionCsvReader();
        }

        /// <summary>
        /// 汇总起止日期（含）之间的每日产能，计算均值/中位数/波动率等统计。
        /// </summary>
        public WeeklyProdData Calculate(DateTime start, DateTime end)
        {
            var data = new WeeklyProdData
            {
                StartDate = start.Date,
                EndDate = end.Date
            };

            var days = new List<WeeklyProdDay>();
            try
            {
                var day = start.Date;
                while (day <= end.Date)
                {
                    var rows = _reader.GetProductionRange(day, day.AddDays(1)) ?? new List<ProductionHourRecord>();
                    var pass = rows.Sum(r => r.Pass);
                    var fail = rows.Sum(r => r.Fail);
                    var total = pass + fail;
                    var yield = total > 0 ? (double)pass / total : 0d;
                    var warning = rows.Count == 0 ? "缺少当天产能数据" : string.Empty;
                    days.Add(new WeeklyProdDay
                    {
                        Date = day,
                        Pass = pass,
                        Fail = fail,
                        Total = total,
                        Yield = yield,
                        Note = warning
                    });
                    if (!string.IsNullOrWhiteSpace(warning))
                    {
                        data.Warnings.Add(day.ToString("yyyy-MM-dd") + " 缺少产能数据");
                    }
                    day = day.AddDays(1);
                }

                data.Days = days;
                data.Pass = days.Sum(d => d.Pass);
                data.Fail = days.Sum(d => d.Fail);
                data.Total = data.Pass + data.Fail;
                data.Yield = data.Total > 0 ? (double)data.Pass / data.Total : 0d;
                data.AvgYield = days.Count > 0 ? days.Average(d => d.Yield) : 0d;
                data.MedianTotal = ComputeMedian(days.Select(d => d.Total).ToList());
                data.Volatility = ComputeCv(days.Select(d => d.Total));
                data.LastDay = days.LastOrDefault();
                if (days.Count > 0 && data.LastDay != null)
                {
                    var avgTotal = days.Average(d => d.Total);
                    var avgYield = data.AvgYield;
                    data.LastDayDelta = new Delta
                    {
                        TotalDelta = avgTotal == 0 ? 0 : (data.LastDay.Total - avgTotal) / Math.Max(1, avgTotal),
                        YieldDelta = avgYield == 0 ? 0 : (data.LastDay.Yield - avgYield) / Math.Max(0.0001, avgYield)
                    };
                }

                data.BestDays = days.OrderByDescending(d => d.Total).Take(3).ToList();
                data.WorstDays = days.OrderBy(d => d.Total).Take(3).ToList();
            }
            catch (Exception ex)
            {
                data.Warnings.Add("计算周产能时出错：" + ex.Message);
            }

            return data;
        }

        private static int ComputeMedian(IList<int> list)
        {
            if (list == null || list.Count == 0) return 0;
            var arr = list.OrderBy(x => x).ToList();
            int mid = arr.Count / 2;
            if (arr.Count % 2 == 0)
            {
                return (int)Math.Round((arr[mid - 1] + arr[mid]) / 2.0);
            }
            return arr[mid];
        }

        private static double ComputeCv(IEnumerable<int> values)
        {
            var arr = values?.ToList() ?? new List<int>();
            if (arr.Count == 0) return 0d;
            var mean = arr.Average();
            if (mean == 0) return 0d;
            var variance = arr.Average(v => Math.Pow(v - mean, 2));
            var stddev = Math.Sqrt(variance);
            return stddev / mean;
        }
    }

    /// <summary>
    /// 本地计算报警周报数据，对齐 MCP 读取方式，输出 TopN 与每日汇总。
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
