using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace McpServer
{
    public class Base
    {
        public class AppConfig
        {
            // 产能/报警/IO 路径均与 WPF AppConfig 统一
            public string AlarmLogPath { get; set; } = string.Empty;
            public string ProductionLogPath { get; set; } = string.Empty;
            public string IoMapCsvPath { get; set; } = @"D:\";
            public string MCPServerIP { get; set; } = "127.0.0.1:8081";

            public string URL { get; set; } = string.Empty;
            public string AutoKey { get; set; } = string.Empty;
            public string ChatKey { get; set; } = string.Empty;
            public string DocumentKey { get; set; } = string.Empty;
            public string EarlyWarningKey { get; set; } = string.Empty;
            public bool FlatFileLayout { get; set; }
            public bool UseOkNgSplitTables { get; set; }
        }

        private const string ConfigRoot = @"D:\";
        private const string ConfigFileName = "AppConfig.json";
        private static readonly object _cfgLock = new object();

        /// <summary>
        /// 读取配置；当文件不存在或损坏时，返回默认配置。
        /// </summary>
        public static AppConfig ReadAppConfig()
        {
            lock (_cfgLock)
            {
                // 配置固定放在 D:\AppConfig.json
                var cfgPath = Path.Combine(ConfigRoot, ConfigFileName);

                try
                {
                    if (File.Exists(cfgPath))
                    {
                        using (var r = new StreamReader(cfgPath, new UTF8Encoding(false)))
                        {
                            var json = r.ReadToEnd();
                            var cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                            FillMissingFields(cfg);
                            return cfg;
                        }
                    }
                }
                catch
                {
                    // 读取失败
                }

                var def = CreateDefault();
                FillMissingFields(def);
                return def;
            }
        }

        // —— 内部工具 —— //

        private static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                AlarmLogPath = @"D:\",
                ProductionLogPath = @"D:\",
                IoMapCsvPath = @"D:\",
                MCPServerIP = "127.0.0.1:8081"
            };
        }

        private static void FillMissingFields(AppConfig cfg)
        {
            if (cfg == null) return;

            if (string.IsNullOrWhiteSpace(cfg.ProductionLogPath))
                cfg.ProductionLogPath = @"D:\";
            if (string.IsNullOrWhiteSpace(cfg.AlarmLogPath))
                cfg.AlarmLogPath = @"D:\";

            if (string.IsNullOrWhiteSpace(cfg.IoMapCsvPath))
                cfg.IoMapCsvPath = @"D:\";
            if (string.IsNullOrWhiteSpace(cfg.MCPServerIP))
                cfg.MCPServerIP = "127.0.0.1:8081";
        }

    }
}
