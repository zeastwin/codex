using System;
using System.Diagnostics;
using System.IO;

namespace EW_Assistant.Services
{
    /// <summary>
    /// 负责托管 McpServer.exe 的生命周期（随 UI 启停）
    /// </summary>
    public sealed class McpServerProcessHost : IDisposable
    {
        private static readonly Lazy<McpServerProcessHost> _lazy =
            new(() => new McpServerProcessHost());

        public static McpServerProcessHost Instance => _lazy.Value;

        private readonly string _exePath;
        private readonly object _syncRoot = new();
        private Process _process;

        private McpServerProcessHost()
        {
            _exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "McpServer", "McpServer.exe");
        }

        /// <summary>
        /// 若进程未启动则尝试拉起，返回当前是否处于运行状态
        /// </summary>
        public bool StartIfNeeded(Action<string, string> logger = null)
        {
            lock (_syncRoot)
            {
                if (_process != null && !_process.HasExited)
                {
                    logger?.Invoke("MCP Server 已在运行，无需重复启动。", "info");
                    return true;
                }

                if (TryAttachExistingProcess(logger))
                {
                    return true;
                }

                if (DetectForeignInstance(logger))
                {
                    // 已有其他目录的 MCP Server 在运行，避免再次启动导致端口冲突
                    return false;
                }

                if (!File.Exists(_exePath))
                {
                    logger?.Invoke($"未找到 MCP Server 可执行文件：{_exePath}", "warn");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _exePath,
                    WorkingDirectory = Path.GetDirectoryName(_exePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                try
                {
                    var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        logger?.Invoke("启动 MCP Server 失败（返回空进程）。", "error");
                        return false;
                    }

                    if (process.WaitForExit(500))
                    {
                        logger?.Invoke($"MCP Server 进程启动后立即退出（ExitCode={process.ExitCode}）。", "error");
                        process.Dispose();
                        return false;
                    }

                    WatchProcess(process, logger);
                    logger?.Invoke($"MCP Server 已启动（PID={process.Id}）。", "ok");
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"启动 MCP Server 失败：{ex.Message}", "error");
                    _process = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// 请求终止当前 MCP 进程
        /// </summary>
        public void Stop(Action<string, string> logger = null)
        {
            lock (_syncRoot)
            {
                if (_process == null)
                    return;

                try
                {
                    var proc = _process;
                    if (proc != null && !proc.HasExited)
                    {
                        try
                        {
                            // 先尝试温和退出，再在超时后强杀
                            proc.CloseMainWindow();
                        }
                        catch
                        {
                            // 忽略无窗口异常
                        }

                        if (!proc.WaitForExit(3000))
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                        }
                    }

                    logger?.Invoke("MCP Server 已停止。", "info");
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"停止 MCP Server 失败：{ex.Message}", "error");
                }
                finally
                {
                    _process?.Dispose();
                    _process = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private bool TryAttachExistingProcess(Action<string, string> logger)
        {
            try
            {
                var exeName = Path.GetFileNameWithoutExtension(_exePath);
                if (string.IsNullOrEmpty(exeName))
                    return false;

                var candidates = Process.GetProcessesByName(exeName);
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var candidatePath = candidate.MainModule?.FileName;
                        if (string.IsNullOrEmpty(candidatePath))
                            continue;

                        if (!string.Equals(Path.GetFullPath(candidatePath), Path.GetFullPath(_exePath), StringComparison.OrdinalIgnoreCase))
                            continue;

                        WatchProcess(candidate, logger);
                        logger?.Invoke($"检测到已运行的 MCP Server（PID={candidate.Id}），复用现有实例。", "info");
                        return true;
                    }
                    catch (Exception inner)
                    {
                        logger?.Invoke($"检测现有 MCP Server 失败：{inner.Message}", "warn");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"列举 MCP Server 进程失败：{ex.Message}", "warn");
            }

            return false;
        }

        private bool DetectForeignInstance(Action<string, string> logger)
        {
            try
            {
                var exeName = Path.GetFileNameWithoutExtension(_exePath);
                if (string.IsNullOrEmpty(exeName))
                    return false;

                var expectedPath = Path.GetFullPath(_exePath);
                var foreignFound = false;

                foreach (var candidate in Process.GetProcessesByName(exeName))
                {
                    try
                    {
                        var candidatePath = candidate.MainModule?.FileName;
                        if (string.IsNullOrEmpty(candidatePath))
                            continue;

                        var fullCandidatePath = Path.GetFullPath(candidatePath);
                        if (string.Equals(fullCandidatePath, expectedPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        logger?.Invoke($"检测到其他目录运行的 MCP Server（PID={candidate.Id}, Path={fullCandidatePath}），请先关闭该实例后再启动 UI 托管。", "warn");
                        foreignFound = true;
                    }
                    catch (Exception inner)
                    {
                        logger?.Invoke($"检测外部 MCP Server 失败：{inner.Message}", "warn");
                    }
                }

                return foreignFound;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"列举 MCP Server 进程失败：{ex.Message}", "warn");
                return false;
            }
        }

        private void WatchProcess(Process process, Action<string, string> logger)
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, __) =>
            {
                logger?.Invoke("MCP Server 进程已退出。", "warn");
                lock (_syncRoot)
                {
                    _process?.Dispose();
                    _process = null;
                }
            };

            _process = process;
        }
    }
}
