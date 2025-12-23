using EW_Assistant.Views;
using EW_Assistant.Views.Inventory;
using EW_Assistant.Views.Reports;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Path = System.IO.Path;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using EW_Assistant.Services.Reports;

namespace EW_Assistant
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>全局唯一的主窗体实例，便于跨线程日志调用。</summary>
        public static MainWindow Instance { get; private set; }

        /// <summary>信息流列表的数据源，绑定到 UI。</summary>
        public ObservableCollection<ProgramInfoItem> InfoItems { get; } = new ObservableCollection<ProgramInfoItem>();

        /// <summary>底部状态栏文本。</summary>
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                SafeNotifyPropertyChanged(nameof(StatusText));
            }
        }
        private string _statusText = "就绪";

        /// <summary>当前导航标题。</summary>
        public string CurrentPageTitle
        {
            get => _currentPageTitle;
            set
            {
                if (string.Equals(_currentPageTitle, value, StringComparison.Ordinal))
                    return;
                _currentPageTitle = value ?? string.Empty;
                SafeNotifyPropertyChanged(nameof(CurrentPageTitle));
            }
        }
        private string _currentPageTitle = "总览";

        // 主导航映射：标签 -> 对应视图的工厂方法
        private readonly Dictionary<string, Func<UIElement>> _routes = new Dictionary<string, Func<UIElement>>()
        {
            ["总览"] = () => new DashboardView() ,
            ["AI助手"] = () => new AIAssistantView(),
            ["AI文档"] = () => new DocumentAiView(),
            ["产能看板"] = () => new ProductionBoardView(),
            ["报警看板"] = () => new AlarmView(),
            ["性能监控"] = () => new PerformanceMonitorView(),
            ["报表中心"] = () => new ReportsCenterView(),
            ["预警中心"] = () => new WarningCenterView(),
            ["机台控制"] = () => new MachineControl(),
            ["库存管理"] = () => new InventoryView(),
            ["设置"] = () => new ConfigView(),
        };
        // 缓存已创建的页面，避免反复 new
        private readonly Dictionary<string, UIElement> _viewCache = new Dictionary<string, UIElement>();
        private CancellationTokenSource _serverCts;
        private readonly Services.McpServerProcessHost _mcpHost = Services.McpServerProcessHost.Instance;
        private readonly ReportStorageService _reportStorage = new ReportStorageService();
        private readonly ReportGeneratorService _reportGenerator;
        private readonly ReportScheduler _reportScheduler;
        private readonly CancellationTokenSource _reportSchedulerCts = new CancellationTokenSource();
        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = this;
            _reportGenerator = new ReportGeneratorService(_reportStorage, new LlmWorkflowClient());
            _reportScheduler = new ReportScheduler(_reportStorage, _reportGenerator);

            // 启动常驻 HTTP 服务与 MCP 辅助进程
            _serverCts = new CancellationTokenSource();
            var prefix = "http://127.0.0.1:8091/";
            _ = Net.WorkHttpServer.Instance.StartAsync(prefix, _serverCts.Token);
            _mcpHost.StartIfNeeded(LogMcpMessage);

            // 应用退出统一停止
            Application.Current.Exit += (_, __) =>
            {
                try { _serverCts.Cancel(); } catch { }
                try { _reportSchedulerCts.Cancel(); } catch { }
                Net.WorkHttpServer.Instance.Stop();
                _mcpHost.Stop(LogMcpMessage);
            };

            // 预创建 AI 助手页以提前初始化全局实例，避免首次打开时延迟
            if (!_viewCache.ContainsKey("AI助手"))
            {
                var ai = new EW_Assistant.Views.AIAssistantView();  // 这一步会设置 GlobalInstance
                _viewCache["AI助手"] = ai;
            }
            // 预创建预警中心，使预警引擎启动时即加载，不必等待用户点击
            if (!_viewCache.ContainsKey("预警中心"))
            {
                var warning = new WarningCenterView();
                _viewCache["预警中心"] = warning;
            }

            NavigateByContent("总览");
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeReportsAsync();
            _ = StartReportSchedulerLoopAsync();
        }

        private async Task InitializeReportsAsync()
        {
            try
            {
                PostProgramInfo("正在初始化报表...", "info");
                await _reportScheduler.EnsureBasicReportsAsync();
                PostProgramInfo("报表初始化完成。", "ok");
            }
            catch (OperationCanceledException)
            {
                PostProgramInfo("报表初始化已取消。", "warn");
            }
            catch (Exception ex)
            {
                PostProgramInfo("报表初始化失败：" + ex.Message, "warn");
            }
        }

        private async Task StartReportSchedulerLoopAsync()
        {
            var token = _reportSchedulerCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _reportScheduler.EnsureBasicReportsAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PostProgramInfo("自动检查报表失败：" + ex.Message, "warn");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        // 修正无边框窗口最大化时的可用区域，保证尊重工作区（去掉任务栏）
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var source = (HwndSource)PresentationSource.FromVisual(this);
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = false; // 这里让 WPF 接着处理其他部分
            }

            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    RECT rcWorkArea = monitorInfo.rcWork;     // 工作区（去掉任务栏）
                    RECT rcMonitorArea = monitorInfo.rcMonitor; // 整个显示器

                    // 最大化时窗口左上角相对于显示器左上角的偏移
                    mmi.ptMaxPosition.X = rcWorkArea.Left - rcMonitorArea.Left;
                    mmi.ptMaxPosition.Y = rcWorkArea.Top - rcMonitorArea.Top;

                    // 最大化时窗口的宽高 = 工作区大小
                    mmi.ptMaxSize.X = rcWorkArea.Right - rcWorkArea.Left;
                    mmi.ptMaxSize.Y = rcWorkArea.Bottom - rcWorkArea.Top;

                    // 最大跟踪尺寸也同步一下，防止拖拉超过工作区
                    mmi.ptMaxTrackSize = mmi.ptMaxSize;
                }
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏：最大化/还原
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }
                else
                {
                    WindowState = WindowState.Maximized;
                }
            }
            else
            {
                // 拖动窗口
                try
                {
                    DragMove();
                }
                catch
                {
                    // 忽略偶发异常
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
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

            UpdateNavTitle(label);
        }

        private void UpdateNavTitle(string label)
        {
            CurrentPageTitle = label;

            if (NavTitlePanel == null || NavTitleTransform == null)
                return;

            NavTitlePanel.Opacity = 0;
            NavTitleTransform.Y = 10;

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slide = new DoubleAnimation
            {
                From = 10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            NavTitlePanel.BeginAnimation(UIElement.OpacityProperty, fade);
            NavTitleTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        // ===== 日志相关静态字段 =====
        /// <summary>程序信息日志根目录。</summary>
        private const string ProgramLogRoot = @"D:\Data\AiLog\UI\";

        /// <summary>写日志的锁，防止多线程同时写同一个文件。</summary>
        private static readonly object _programLogLock = new object();

        /// <summary>
        /// 安全写入一行程序信息日志，不抛异常。
        /// </summary>
        private static void SafeWriteProgramLog(string message, string level)
        {
            try
            {
                // 空值兜底，保证日志格式完整
                if (message == null) message = string.Empty;
                if (string.IsNullOrWhiteSpace(level)) level = "info";

                // 准备目录与按天切分的文件名
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
                // 日志失败不应影响主流程，这里仅落到调试输出
                Debug.WriteLine("[ProgramLog] 写日志失败：" + ex.Message);
            }
        }

        // ===== 面向全程序开放的静态接口 =====
        /// <summary>
        /// 跨线程安全地写入程序信息日志，同时投递到 UI 信息流。
        /// level 参数可选："info" | "ok" | "warn" | "error"
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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SafeNotifyPropertyChanged(string propertyName)
        {
            if (Dispatcher.CheckAccess())
            {
                OnPropertyChanged(propertyName);
            }
            else
            {
                Dispatcher.Invoke(() => OnPropertyChanged(propertyName));
            }
        }
    }
    public class ProgramInfoItem
    {
        public DateTime Time { get; set; }
        public string Text { get; set; } = "";
        /// <summary>等级："info" | "ok" | "warn" | "error"</summary>
        public string Level { get; set; } = "info";
    }
    public class DeviceBrief
    {
        /// <summary>设备名称（展示用）。</summary>
        public string Name { get; set; }

        /// <summary>设备型号或机种。</summary>
        public string Model { get; set; }

        /// <summary>序列号或资产编号。</summary>
        public string Serial { get; set; }

        /// <summary>安装日期。</summary>
        public DateTime? InstallDate { get; set; }

        /// <summary>累积运行小时数。</summary>
        public double? RuntimeHours { get; set; }

        public DateTime? LastMaintenance { get; set; }
        public DateTime? NextMaintenance { get; set; }

        /// <summary>当前责任人。</summary>
        public string Owner { get; set; }

        /// <summary>责任人联系方式。</summary>
        public string OwnerPhone { get; set; }

        /// <summary>质保状态描述。</summary>
        public string WarrantyStatus { get; set; }

        /// <summary>供应商信息。</summary>
        public string Supplier { get; set; }
    }
}
