using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EW_Assistant.Domain.Reports;
using EW_Assistant.Warnings;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 本地 CSV 计算当日产能数据。
    /// </summary>
    public class DailyProdCalculator
    {
        private readonly ProductionCsvReader _reader;

        public DailyProdCalculator(ProductionCsvReader reader = null)
        {
            _reader = reader ?? new ProductionCsvReader();
        }

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
                for (int h = 0; h < 24; h++)
                {
                    var at = start.AddHours(h);
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

        private static void MarkPeaksAndValleys(IList<DailyProdHourStat> hours, DailyProdData data)
        {
            var nonZero = hours.Where(h => h.Total > 0).OrderByDescending(h => h.Total).ToList();
            var peak = nonZero.Take(3).ToList();
            data.PeakHours = peak;

            var valley = nonZero.OrderBy(h => h.Total).Take(3).ToList();
            data.ValleyHours = valley;
        }

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

            // 取最长 1 段并按开始小时升序
            return list.OrderByDescending(d => d.DurationHours).ThenBy(d => d.StartHour).ToList();
        }

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
}
