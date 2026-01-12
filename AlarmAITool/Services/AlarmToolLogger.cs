using System;
using System.IO;
using System.Text;

namespace AlarmAITool.Services
{
    internal static class AlarmToolLogger
    {
        private static readonly object s_lock = new object();
        private static readonly string LogFolder = @"D:\Data\AiLog\AlarmToolLog";

        public static void LogRequest(int row, string errorDesc)
        {
            WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 行{row} 请求: {Normalize(errorDesc)}");
        }

        public static void LogResponse(int row, string response)
        {
            WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 行{row} 响应: {Normalize(response)}");
        }

        public static void LogError(int row, string message)
        {
            WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 行{row} 错误: {Normalize(message)}");
        }

        private static void WriteLine(string line)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                var path = Path.Combine(LogFolder, DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
                lock (s_lock)
                {
                    using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                    fs.Seek(0, SeekOrigin.End);
                    using var writer = new StreamWriter(fs, new UTF8Encoding(false));
                    writer.WriteLine(line);
                }
            }
            catch
            {
            }
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", " ");
        }
    }
}
