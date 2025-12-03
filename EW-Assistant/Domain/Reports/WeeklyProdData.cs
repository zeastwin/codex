using System;
using System.Collections.Generic;

namespace EW_Assistant.Domain.Reports
{
    public class WeeklyProdData
    {
        public WeeklyProdData()
        {
            Days = new List<WeeklyProdDay>();
            Warnings = new List<string>();
            BestDays = new List<WeeklyProdDay>();
            WorstDays = new List<WeeklyProdDay>();
        }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public int Pass { get; set; }
        public int Fail { get; set; }
        public int Total { get; set; }
        public double Yield { get; set; }
        public double AvgYield { get; set; }
        public int MedianTotal { get; set; }
        public double Volatility { get; set; }

        public WeeklyProdDay LastDay { get; set; }
        public Delta LastDayDelta { get; set; }

        public IList<WeeklyProdDay> BestDays { get; set; }
        public IList<WeeklyProdDay> WorstDays { get; set; }
        public IList<WeeklyProdDay> Days { get; set; }
        public IList<string> Warnings { get; set; }
    }

    public class WeeklyProdDay
    {
        public DateTime Date { get; set; }
        public int Pass { get; set; }
        public int Fail { get; set; }
        public int Total { get; set; }
        public double Yield { get; set; }
        public string Note { get; set; }
    }

    public class Delta
    {
        public double TotalDelta { get; set; }
        public double YieldDelta { get; set; }
    }
}
