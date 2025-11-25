using EW_Assistant.Services;
using System;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 本地数据路径配置，优先读取 AppConfig。
    /// </summary>
    public static class LocalDataConfig
    {
        public static string ProductionCsvRoot
        {
            get
            {
                var cfg = ConfigService.Current;
                if (cfg != null && !string.IsNullOrWhiteSpace(cfg.CsvRootPath))
                    return cfg.CsvRootPath;
                return @"D:\Data\T66Data";
            }
        }

        public static string AlarmCsvRoot
        {
            get
            {
                var cfg = ConfigService.Current;
                if (cfg != null && !string.IsNullOrWhiteSpace(cfg.AlarmLogPath))
                    return cfg.AlarmLogPath;
                return @"D:\Data\T66Data";
            }
        }
    }
}
