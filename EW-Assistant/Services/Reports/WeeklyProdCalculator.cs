using System;
using System.Collections.Generic;
using System.Linq;
using EW_Assistant.Domain.Reports;
using EW_Assistant.Warnings;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 本地计算周产能（对齐 GetWeeklyProductionSummary 的字段）。
    /// </summary>
    public class WeeklyProdCalculator
    {
        private readonly ProductionCsvReader _reader;

        public WeeklyProdCalculator(ProductionCsvReader reader = null)
        {
            _reader = reader ?? new ProductionCsvReader();
        }

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
}
