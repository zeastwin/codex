using EW_Assistant.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
using Path = System.IO.Path;

namespace EW_Assistant
{
    public partial class MainWindow : Window
    {
        // 单例用于静态调用
        public static MainWindow Instance { get; private set; }

        // 绑定到 ListBox 的数据源
        public ObservableCollection<ProgramInfoItem> InfoItems { get; } = new ObservableCollection<ProgramInfoItem>();

        // 底部状态文本
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; Dispatcher.Invoke(() => { DataContext = this; }); }
        }
        private string _statusText = "就绪";

        private readonly Dictionary<string, Func<UIElement>> _routes = new Dictionary<string, Func<UIElement>>()
        {
            ["总览"] = () => new DashboardView() ,
            ["AI助手"] = () => new AIAssistantView(),
            ["AI文档"] = () => new DocumentAiView(),
            ["产能看板"] = () => new ProductionBoardView(),
            ["报警看板"] = () => new AlarmView(),
            ["机台控制"] = () => new MachineControl(),
            ["设置"] = () => new ConfigView(),
        };
        // 缓存已创建的页面，避免反复 new
        private readonly Dictionary<string, UIElement> _viewCache = new Dictionary<string, UIElement>();
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

        // ===== 日志相关静态字段 =====
        /// <summary>程序信息日志根目录。</summary>
        private const string ProgramLogRoot = @"D:\Data\AiLog\UI\";   // 你可以改成自己习惯的路径

        /// <summary>写日志的锁，防止多线程同时写同一个文件。</summary>
        private static readonly object _programLogLock = new object();

        /// <summary>
        /// 安全写入一行程序信息日志，不抛异常。
        /// </summary>
        private static void SafeWriteProgramLog(string message, string level)
        {
            try
            {
                // 兜底
                if (message == null) message = string.Empty;
                if (string.IsNullOrWhiteSpace(level)) level = "info";

                // 准备目录 & 文件名（按天分文件）
                Directory.CreateDirectory(ProgramLogRoot);
                string fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".log";
                string filePath = Path.Combine(ProgramLogRoot, fileName);

                // 时间 + 级别 + 文本
                string line = string.Format(
                    "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}",
                    DateTime.Now,
                    level.ToUpperInvariant(),
                    message.Replace(Environment.NewLine, " ")  // 简单处理多行
                );

                lock (_programLogLock)
                {
                    File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // 日志失败绝对不能再影响主程序，这里只写 Debug 输出
                Debug.WriteLine("[ProgramLog] 写日志失败：" + ex.Message);
            }
        }

        // ===== 面向全程序开放的静态接口 =====
        /// <summary>
        /// 从任意地方/线程抛信息到信息卡 + 落地到日志文件。
        /// level: "info" | "ok" | "warn" | "error"
        /// </summary>
        public static void PostProgramInfo(string message, string level = "info")
        {
            // 1) 先写文件日志（与 UI 无关）
            SafeWriteProgramLog(message, level);

            // 2) 再投递到主窗口信息卡（原有逻辑）
            var w = Instance;
            if (w == null) return;

            // 应用退出时可能存在后台线程写日志，需吞掉 Dispatcher 已关闭带来的异常
            try
            {
                if (w.Dispatcher.HasShutdownStarted || w.Dispatcher.HasShutdownFinished)
                    return;

                if (w.Dispatcher.CheckAccess())
                    w.AppendInfo(message, level);
                else
                    w.Dispatcher.Invoke(() => w.AppendInfo(message, level));
            }
            catch (TaskCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"PostProgramInfo 调用失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PostProgramInfo 调用异常：{ex}");
            }
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
