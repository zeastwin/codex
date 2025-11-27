// ConfigView.xaml.cs  —— 合并版（包含 ConfigView + AppConfig + ConfigService）
using EW_Assistant.Services;
using EW_Assistant.Warnings;
using Newtonsoft.Json;   // 需安装包：Install-Package Newtonsoft.Json
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;

namespace EW_Assistant.Views
{
    public partial class ConfigView : UserControl
    {
        public EW_Assistant.Settings.AppConfig Config { get; private set; }

        public ConfigView()
        {
            InitializeComponent();
            // 读取配置；没有就生成默认并保存
            Config = EW_Assistant.Services.ConfigService.Load();
            DataContext = Config;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Config.ProductionLogPath)
                    || string.IsNullOrWhiteSpace(Config.AlarmLogPath)
                    || string.IsNullOrWhiteSpace(Config.IoMapCsvPath)
                    || string.IsNullOrWhiteSpace(Config.MCPServerIP)
                    || string.IsNullOrWhiteSpace(Config.URL)
                    || string.IsNullOrWhiteSpace(Config.AutoKey)
                    || string.IsNullOrWhiteSpace(Config.ChatKey)
                    || string.IsNullOrWhiteSpace(Config.DocumentKey)
                    || string.IsNullOrWhiteSpace(Config.EarlyWarningKey))
                {
                    MainWindow.PostProgramInfo(
                               $"内容不能为空。", "warn");
                    return;
                }

                EW_Assistant.Services.ConfigService.Save(Config);
                MainWindow.PostProgramInfo(
               $"配置已保存：{EW_Assistant.Services.ConfigService.FilePath}", "ok");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：\n" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 从磁盘重新加载 AppConfig.json
                var fresh = EW_Assistant.Services.ConfigService.Load();

                if (this.Config != null)
                {
                    this.Config.ProductionLogPath = fresh.ProductionLogPath;
                    this.Config.AlarmLogPath = fresh.AlarmLogPath;
                    this.Config.IoMapCsvPath = fresh.IoMapCsvPath;
                    this.Config.MCPServerIP = fresh.MCPServerIP;
                    this.Config.URL = fresh.URL;
                    this.Config.AutoKey = fresh.AutoKey;
                    this.Config.ChatKey = fresh.ChatKey;
                    this.Config.DocumentKey = fresh.DocumentKey;
                    this.Config.EarlyWarningKey = fresh.EarlyWarningKey;
                    this.Config.FlatFileLayout = fresh.FlatFileLayout;
                    this.Config.UseOkNgSplitTables = fresh.UseOkNgSplitTables;
                    this.Config.WarningOptions = fresh.WarningOptions;
                }
                else
                {
                    this.Config = fresh;
                    this.DataContext = this.Config;
                }

                // 走信息流
                MainWindow.PostProgramInfo(
                    $"已从磁盘重新读取配置：{EW_Assistant.Services.ConfigService.FilePath}", "info");
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo("重新读取失败：" + ex.Message, "error");
            }
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            var logRoot = Path.Combine("D:\\", "Data", "AiLog");
            try
            {
                if (!Directory.Exists(logRoot))
                    Directory.CreateDirectory(logRoot);

                using var proc = Process.Start(new ProcessStartInfo("explorer.exe", logRoot)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo($"打开日志目录失败：{ex.Message}", "error");
            }
        }
    }
}

// === 同文件内：配置模型 ===
namespace EW_Assistant.Settings
{
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Security.Policy;

    public class AppConfig : INotifyPropertyChanged
    {
        private string _productionLogPath = @"";
        public string ProductionLogPath
        {
            get => _productionLogPath;
            set { if (_productionLogPath != value) { _productionLogPath = value; OnPropertyChanged(); } }
        }

        private string _alarmLogPath = @"";
        public string AlarmLogPath
        {
            get => _alarmLogPath;
            set { if (_alarmLogPath != value) { _alarmLogPath = value; OnPropertyChanged(); } }
        }

        private string _ioMapCsvPath = @"";
        public string IoMapCsvPath
        {
            get => _ioMapCsvPath;
            set { if (_ioMapCsvPath != value) { _ioMapCsvPath = value; OnPropertyChanged(); } }
        }

        private string _mcpServerIP = "127.0.0.1:8081";
        public string MCPServerIP
        {
            get => _mcpServerIP;
            set { if (_mcpServerIP != value) { _mcpServerIP = value; OnPropertyChanged(); } }
        }
        private string _URL = "";
        public string URL
        {
            get => _URL;
            set { if (_URL != value) { _URL = value; OnPropertyChanged(); } }
        }

        private string _autoKey = "";
        public string AutoKey
        {
            get => _autoKey;
            set { if (_autoKey != value) { _autoKey = value; OnPropertyChanged(); } }
        }

        private string _chatKey = "";
        public string ChatKey
        {
            get => _chatKey;
            set { if (_chatKey != value) { _chatKey = value; OnPropertyChanged(); } }
        }

        private string _documentKey = "";
        public string DocumentKey
        {
            get => _documentKey;
            set { if (_documentKey != value) { _documentKey = value; OnPropertyChanged(); } }
        }
        private string _earlyWarningKey = "";
        public string EarlyWarningKey
        {
            get => _earlyWarningKey;
            set { if (_earlyWarningKey != value) { _earlyWarningKey = value; OnPropertyChanged(); } }
        }
        private bool _flatFileLayout;
        [JsonProperty("flatFileLayout")]
        public bool FlatFileLayout
        {
            get => _flatFileLayout;
            set { if (_flatFileLayout != value) { _flatFileLayout = value; OnPropertyChanged(); } }
        }

        private bool _useOkNgSplitTables;
        public bool UseOkNgSplitTables
        {
            get => _useOkNgSplitTables;
            set { if (_useOkNgSplitTables != value) { _useOkNgSplitTables = value; OnPropertyChanged(); } }
        }

        private WarningRuleOptions _warningOptions = WarningRuleOptions.CreateDefault();
        [JsonProperty("warningOptions")]
        public WarningRuleOptions WarningOptions
        {
            get => _warningOptions;
            set
            {
                var normalized = WarningRuleOptions.Normalize(value);
                if (!ReferenceEquals(_warningOptions, normalized))
                {
                    _warningOptions = normalized;
                    OnPropertyChanged();
                }
                else
                {
                    _warningOptions = normalized;
                }
            }
        }
        public static AppConfig CreateDefault() => new AppConfig();

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

// === 同文件内：配置读写服务 ===
namespace EW_Assistant.Services
{
    using EW_Assistant.Settings;
    using System.Collections.Generic;
    using System.Text;

    public static class ConfigService
    {
        public static string FilePath { get; } =
            Path.Combine("D:\\", "AppConfig.json");

        // 共享实例：全 app 共用这一个，便于 XAML 绑定自动刷新
        private static AppConfig _current;
        public static AppConfig Current => _current ??= Load();

        public static event EventHandler<AppConfig> ConfigChanged;

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath, new UTF8Encoding(false));
                    var cfg = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (cfg != null)
                    {
                        EnsureMcpFields(cfg);
                        _current = cfg;
                        ConfigChanged?.Invoke(null, _current);
                        return _current;
                    }
                }
            }
            catch { /* 忽略，走默认 */ }

            _current = AppConfig.CreateDefault();
            EnsureMcpFields(_current);
            Save(_current);                 // 首次落盘
            ConfigChanged?.Invoke(null, _current);
            return _current;
        }

        public static void Save(AppConfig cfg)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            EnsureMcpFields(cfg);

            var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
            File.WriteAllText(FilePath, json, new UTF8Encoding(false));

            if (_current == null)
            {
                _current = cfg;
            }
            else
            {
                _current.ProductionLogPath = cfg.ProductionLogPath;
                _current.AlarmLogPath = cfg.AlarmLogPath;
                _current.IoMapCsvPath = cfg.IoMapCsvPath;
                _current.MCPServerIP = cfg.MCPServerIP;
                _current.URL = cfg.URL;
                _current.AutoKey = cfg.AutoKey;
                _current.ChatKey = cfg.ChatKey;
                _current.DocumentKey = cfg.DocumentKey;
                _current.EarlyWarningKey = cfg.EarlyWarningKey;
                _current.FlatFileLayout = cfg.FlatFileLayout;
                _current.UseOkNgSplitTables = cfg.UseOkNgSplitTables;
                _current.WarningOptions = cfg.WarningOptions ?? WarningRuleOptions.CreateDefault();
            }

            ConfigChanged?.Invoke(null, _current);
        }


        private static void EnsureMcpFields(AppConfig cfg)
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
            cfg.WarningOptions = WarningRuleOptions.Normalize(cfg.WarningOptions);
        }

    }

}
