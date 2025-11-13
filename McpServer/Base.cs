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
            // 默认都指向 D:\ 根目录
            public string ProductionLogPath { get; set; } = @"D:\";
            public string WarmLogPath { get; set; } = @"D:\";
            public string IoMapCsvPath { get; set; } = @"D:\";
            public string MCPServerIP { get; set; } = "127.0.0.1:8081";
        }

        private const string ConfigRoot = @"D:\";
        private const string ConfigFileName = "MCPAppConfig.json";
        private static readonly object _cfgLock = new object();

        /// <summary>
        /// 读取配置；当文件不存在或损坏时，在 D:\ 下生成 MCPAppConfig.json 并返回默认配置。
        /// </summary>
        public static AppConfig ReadAppConfig()
        {
            lock (_cfgLock)
            {
                // 配置固定放在 D:\MCPAppConfig.json
                var cfgDir = GetConfigDirectory();
                var cfgPath = Path.Combine(cfgDir, ConfigFileName);

                try
                {
                    if (!File.Exists(cfgPath))
                    {
                        var def = new AppConfig();
                        EnsureDirs(def);
                        WriteConfig(cfgPath, def);
                        return def;
                    }

                    using (var r = new StreamReader(cfgPath, new UTF8Encoding(false)))
                    {
                        var json = r.ReadToEnd();
                        var cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();

                        // 补齐可能缺失的字段
                        if (string.IsNullOrWhiteSpace(cfg.ProductionLogPath)) cfg.ProductionLogPath = @"D:\";
                        if (string.IsNullOrWhiteSpace(cfg.WarmLogPath)) cfg.WarmLogPath = @"D:\";
                        if (string.IsNullOrWhiteSpace(cfg.IoMapCsvPath)) cfg.IoMapCsvPath = @"D:\";
                        if (string.IsNullOrWhiteSpace(cfg.MCPServerIP)) cfg.MCPServerIP = "127.0.0.1:8081";

                        EnsureDirs(cfg);
                        return cfg;
                    }
                }
                catch
                {
                    // 读取失败：备份旧文件后写默认
                    try
                    {
                        if (File.Exists(cfgPath))
                        {
                            var bak = cfgPath + ".bad." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".json";
                            File.Copy(cfgPath, bak, true);
                        }
                    }
                    catch { /* 备份失败不影响主流程 */ }

                    var def = new AppConfig();
                    EnsureDirs(def);
                    try { WriteConfig(cfgPath, def); } catch { }
                    return def;
                }
            }
        }

        /// <summary>主动保存修改后的配置到 D:\MCPAppConfig.json。</summary>
        public static void SaveAppConfig(AppConfig cfg)
        {
            if (cfg == null) return;
            lock (_cfgLock)
            {
                var cfgDir = GetConfigDirectory();
                var cfgPath = Path.Combine(cfgDir, ConfigFileName);
                EnsureDirs(cfg);
                WriteConfig(cfgPath, cfg);
            }
        }

        // —— 内部工具 —— //

        private static string GetConfigDirectory()
        {
            // 固定放 D:\；若不可写则兜底到应用目录
            try
            {
                if (!Directory.Exists(ConfigRoot))
                    Directory.CreateDirectory(ConfigRoot);
                // 简单写权限探测
                var probe = Path.Combine(ConfigRoot, "~cfg_probe.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return ConfigRoot;
            }
            catch
            {
                string baseDir = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = AppDomain.CurrentDomain.BaseDirectory;
                try { if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir); } catch { }
                return baseDir;
            }
        }

        private static void WriteConfig(string path, AppConfig cfg)
        {
            var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
            using (var sw = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                sw.Write(json);
            }
        }

        /// <summary>确保关键目录存在（D:\ 通常已存在；这里对自定义值也做兜底）。</summary>
        private static void EnsureDirs(AppConfig cfg)
        {
            TryCreateDirectory(cfg.ProductionLogPath);
            TryCreateDirectory(cfg.WarmLogPath);
            TryCreateDirectory(cfg.IoMapCsvPath);
        }

        private static void TryCreateDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                // 若传入的是文件路径，可替换为：var dir = Path.GetDirectoryName(path);
                var dir = path;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { /* 忽略目录创建异常 */ }
        }
    }
}
