// ConfigView.xaml.cs  —— 合并版（包含 ConfigView + AppConfig + ConfigService）
using EW_Assistant.Services;
using Newtonsoft.Json;   // 需安装包：Install-Package Newtonsoft.Json
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

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
                if (string.IsNullOrWhiteSpace(Config.CsvRootPath)
                     || string.IsNullOrWhiteSpace(Config.AlarmLogPath)
                     || string.IsNullOrWhiteSpace(Config.URL)
                     || string.IsNullOrWhiteSpace(Config.AutoKey)
                     || string.IsNullOrWhiteSpace(Config.ChatKey)
                     || string.IsNullOrWhiteSpace(Config.DocumentKey))
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
                    this.Config.CsvRootPath = fresh.CsvRootPath;
                    this.Config.AlarmLogPath = fresh.AlarmLogPath;
                    this.Config.URL = fresh.URL;
                    this.Config.AutoKey = fresh.AutoKey;
                    this.Config.ChatKey = fresh.ChatKey;
                    this.Config.DocumentKey = fresh.DocumentKey;
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
            // 后台逻辑里调用，不涉及 UI：
            var service = new MindmapService();

            string mindmapJson = await service.BuildMindmapJsonAsync(
                filePath: @"C:\Users\zeast\Desktop\3C定制化设备AI赋能生产运维项目立项书.docx",
                prompt: "请为该文件生成一棵完整的知识导图，突出整体结构和关键要点。",
                userId: "user-001",
                token: CancellationToken.None);
            MainWindow.PostProgramInfo(mindmapJson, "error");
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
        private string _csvRootPath = @"";
        public string CsvRootPath
        {
            get => _csvRootPath;
            set { if (_csvRootPath != value) { _csvRootPath = value; OnPropertyChanged(); } }
        }
        private string _alarmLogPath = @"";
        public string AlarmLogPath
        {
            get => _alarmLogPath;
            set { if (_alarmLogPath != value) { _alarmLogPath = value; OnPropertyChanged(); } }
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
                        _current = cfg;
                        ConfigChanged?.Invoke(null, _current);
                        return _current;
                    }
                }
            }
            catch { /* 忽略，走默认 */ }

            _current = AppConfig.CreateDefault();
            Save(_current);                 // 首次落盘
            ConfigChanged?.Invoke(null, _current);
            return _current;
        }
       
        public static void Save(AppConfig cfg)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
            File.WriteAllText(FilePath, json, new UTF8Encoding(false));

            // 保持 Current 引用，便于已有绑定不中断
            if (_current == null) _current = cfg;
            else _current.CsvRootPath = cfg.CsvRootPath;

            ConfigChanged?.Invoke(null, _current);
        }
    }

}
