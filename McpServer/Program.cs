using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using EW_Assistant.Io;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using static McpServer.Base;

namespace McpServer
{
    internal static class Program
    {
        [STAThread]
        private static async Task Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var builder = WebApplication.CreateBuilder(args);

            var appCfg = ReadAppConfig();
            CsvProductionRepository.ProductionLogPath = appCfg.ProductionLogPath;
            CsvProductionRepository.UseOkNgSplitTables = appCfg.UseOkNgSplitTables;
            AlarmCsvTools.WarmLogPath = appCfg.AlarmLogPath;
            AlarmCsvRepository.FlatFileLayout = appCfg.FlatFileLayout;
            AlarmCsvTools.FlatFileLayout = appCfg.FlatFileLayout;

            var ioCsv = appCfg.IoMapCsvPath;
            try
            {
                IoMapRepository.LoadFromXlsx(ioCsv);
            }
            catch (Exception)
            {
            }

            builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly(typeof(Program).Assembly);

            var app = builder.Build();

            app.MapMcp();
            app.MapGet("/", () => "MCP Server (local bridge) running on localhost");

            var exitSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var runTask = app.RunAsync(appCfg.MCPServerIP);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var tray = new McpTrayContext(restart => exitSignal.TrySetResult(restart)))
            {
                Application.Run(tray);
            }

            if (!exitSignal.Task.IsCompleted)
            {
                exitSignal.TrySetResult(false);
            }

            var restartRequested = await exitSignal.Task.ConfigureAwait(false);

            try
            {
                await app.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止 MCP Server 失败：{ex.Message}");
            }

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MCP Server 运行异常：{ex.Message}");
            }

            if (restartRequested)
            {
                RestartCurrentProcess(args);
            }
        }

        private static void RestartCurrentProcess(string[] args)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                foreach (var arg in args ?? Array.Empty<string>())
                {
                    startInfo.ArgumentList.Add(arg);
                }

                Process.Start(startInfo);
            }
            catch
            {
                // 忽略重启失败，当前实例会正常 退出
            }
        }
    }

    internal sealed class McpTrayContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly Action<bool> _exitCallback;
        private bool _exitHandled;

        public McpTrayContext(Action<bool> exitCallback)
        {
            _exitCallback = exitCallback;

                        _menu = BuildMenu();
            _trayIcon = LoadTrayIcon();
            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "MCP Server",
                ContextMenuStrip = _menu,
                Visible = true
            };
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var restartItem = new ToolStripMenuItem("重启 MCP");
            restartItem.Click += (_, __) => RequestExit(true);

            var exitItem = new ToolStripMenuItem("退出 MCP");
            exitItem.Click += (_, __) => RequestExit(false);

            menu.Items.Add(restartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private void RequestExit(bool restart)
        {
            if (_exitHandled) return;
            _exitHandled = true;
            _notifyIcon.Visible = false;
            _exitCallback?.Invoke(restart);
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _menu.Dispose();
                _trayIcon?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? AppContext.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    var iconPath = Path.Combine(baseDir, "Tray", "gear.ico");
                    if (File.Exists(iconPath))
                        return new Icon(iconPath);
                }
            }
            catch
            {
                // 忽略异常，使用系统默认图标
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
