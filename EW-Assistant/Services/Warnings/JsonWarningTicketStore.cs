using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 基于本地 JSON 文件的工单存储，路径默认为 %AppData%\EW-Assistant\warning_tickets.json。
    /// </summary>
    public class JsonWarningTicketStore : IWarningTicketStore
    {
        private readonly string _filePath;
        private readonly object _lock = new object();

        public JsonWarningTicketStore(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EW-Assistant");
                _filePath = Path.Combine(dir, "warning_tickets.json");
            }
            else
            {
                _filePath = filePath;
            }
        }

        public IList<WarningTicketRecord> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    EnsureDirectory();
                    if (!File.Exists(_filePath)) return new List<WarningTicketRecord>();

                    using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    {
                        var json = reader.ReadToEnd();
                        if (string.IsNullOrWhiteSpace(json)) return new List<WarningTicketRecord>();
                        var records = JsonConvert.DeserializeObject<List<WarningTicketRecord>>(json);
                        return records ?? new List<WarningTicketRecord>();
                    }
                }
                catch
                {
                    return new List<WarningTicketRecord>();
                }
            }
        }

        public void SaveAll(IList<WarningTicketRecord> tickets)
        {
            lock (_lock)
            {
                try
                {
                    EnsureDirectory();
                    var json = JsonConvert.SerializeObject(tickets ?? new List<WarningTicketRecord>(), Formatting.Indented);
                    File.WriteAllText(_filePath, json, new UTF8Encoding(false));
                }
                catch
                {
                    // 写入失败静默处理，避免影响主流程
                }
            }
        }

        private void EnsureDirectory()
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
