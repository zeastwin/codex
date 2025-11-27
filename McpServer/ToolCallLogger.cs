using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace McpServer
{
    /// <summary>
    /// MCP 工具调用日志落盘，便于排查调用与返回。
    /// </summary>
    internal static class ToolCallLogger
    {
        private static readonly object _lock = new object();
        private const string LogRoot = @"D:\\Data\\AiLog\\McpTools";

        public static void Log(string toolName, object args, string result, string error = null)
        {
            try
            {
                var dir = LogRoot;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var path = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {toolName}");
                if (args != null)
                    sb.AppendLine("args: " + SafeSerialize(args));
                if (!string.IsNullOrWhiteSpace(error))
                    sb.AppendLine("error: " + error);
                else
                    sb.AppendLine("result: " + result);
                sb.AppendLine(new string('-', 48));

                var txt = sb.ToString();
                lock (_lock)
                {
                    File.AppendAllText(path, txt, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败忽略，避免影响主流程
            }
        }

        private static string SafeSerialize(object o)
        {
            try { return JsonConvert.SerializeObject(o); }
            catch { return o?.ToString() ?? string.Empty; }
        }
    }
}
