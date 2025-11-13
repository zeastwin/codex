using EW_Assistant.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EW_Assistant
{
    public partial class MainWindow : Window
    {
        // 单例用于静态调用
        public static MainWindow Instance { get; private set; }

        // 绑定到 ListBox 的数据源
        public ObservableCollection<ProgramInfoItem> InfoItems { get; } = new();

        // 底部状态文本
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; Dispatcher.Invoke(() => { DataContext = this; }); }
        }
        private string _statusText = "就绪";

        private readonly Dictionary<string, Func<UIElement>> _routes = new()
        {
            ["总览"] = () => new DashboardView() ,
            ["AI助手"] = () => new AIAssistantView(),
            ["产能看板"] = () => new ProductionBoardView(),
            ["报警看板"] = () => new AlarmView(),
            ["机台控制"] = () => new MachineControl(),
            ["设置"] = () => new ConfigView(),
        };
        // 缓存已创建的页面，避免反复 new
        private readonly Dictionary<string, UIElement> _viewCache = new();
        private CancellationTokenSource _serverCts;
        private readonly Services.McpServerProcessHost _mcpHost = Services.McpServerProcessHost.Instance;
        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = this;

            // 启动 HTTP 服务（常驻）
            _serverCts = new CancellationTokenSource();
            var prefix = "http://127.0.0.1:8091/";
            _ = Net.WorkHttpServer.Instance.StartAsync(prefix, _serverCts.Token);
            _mcpHost.StartIfNeeded(LogMcpMessage);

            // 应用退出统一停止
            Application.Current.Exit += (_, __) =>
            {
                try { _serverCts.Cancel(); } catch { }
                Net.WorkHttpServer.Instance.Stop();
                _mcpHost.Stop(LogMcpMessage);
            };

            // ✅ 预创建 AI 助手页面：不加到 RightHost，但放入缓存
            if (!_viewCache.ContainsKey("AI助手"))
            {
                var ai = new EW_Assistant.Views.AIAssistantView();  // 这一步会设置 GlobalInstance
                _viewCache["AI助手"] = ai;
            }

            NavigateByContent("总览");
        }

        private void NavItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                var key = rb.Content?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(key))
                    NavigateByContent(key);
            }
        }

        private void NavigateByContent(string label)
        {
            if (!_viewCache.TryGetValue(label, out var view))
            {
                if (_routes.TryGetValue(label, out var factory))
                    view = factory();
                else
                    view = new TextBlock { Text = $"未实现视图：{label}", Margin = new Thickness(24), FontSize = 18 };

                _viewCache[label] = view;
            }
            RightHost.Children.Clear();
            RightHost.Children.Add(view);
        }

        // ===== 面向全程序开放的静态接口 =====
        /// <summary>
        /// 从任意地方/线程抛信息到信息卡。
        /// level: "info" | "ok" | "warn" | "error"
        /// </summary>
        public static void PostProgramInfo(string message, string level = "info")
        {
            var w = Instance;
            if (w == null) return;

            w.Dispatcher.Invoke(() => w.AppendInfo(message, level));
        }

        private void LogMcpMessage(string message, string level)
        {
            PostProgramInfo(message ?? string.Empty, level ?? "info");
        }

        // ===== 本地实现 =====
        private void AppendInfo(string text, string level)
        {
            InfoItems.Add(new ProgramInfoItem
            {
                Time = DateTime.Now,
                Text = text,
                Level = (level ?? "info").ToLowerInvariant()
            });

            // 自动滚动到底
            if (InfoItems.Count > 0)
                InfoList.ScrollIntoView(InfoItems.Last());

            // 控制上限，避免越积越多
            const int MaxItems = 200;
            while (InfoItems.Count > MaxItems)
                InfoItems.RemoveAt(0);
        }

        private void BtnClearInfo_Click(object sender, RoutedEventArgs e)
        {
            InfoItems.Clear();
            StatusText = "已清空";
        }
    }
    public class ProgramInfoItem
    {
        public DateTime Time { get; set; }
        public string Text { get; set; } = "";
        /// <summary> "info" | "ok" | "warn" | "error" </summary>
        public string Level { get; set; } = "info";
    }
    public class DeviceBrief
    {
        public string Name { get; set; }            // Handler#001
        public string Model { get; set; }           // WS-3000A
        public string Serial { get; set; }          // WS24-002-1156
        public DateTime? InstallDate { get; set; }  // 2025-09-15
        public double? RuntimeHours { get; set; }   // 运行小时
        public DateTime? LastMaintenance { get; set; }
        public DateTime? NextMaintenance { get; set; }
        public string Owner { get; set; }           // 张工
        public string OwnerPhone { get; set; }      // 13800138000
        public string WarrantyStatus { get; set; }  // 在保 / 过保
        public string Supplier { get; set; }        // 供应商
    }
}
