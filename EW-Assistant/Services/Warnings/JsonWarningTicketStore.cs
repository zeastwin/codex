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
        private const int TicketRetentionDays = 7;
        private readonly string _filePath;
        private readonly object _lock = new object();

        public JsonWarningTicketStore(string filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var dir = Path.Combine("D:\\", "DataAI");
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
                        return RemoveExpired(records);
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
                    var pruned = RemoveExpired(tickets);
                    var json = JsonConvert.SerializeObject(pruned ?? new List<WarningTicketRecord>(), Formatting.Indented);
                    var temp = _filePath + ".tmp";
                    var bytes = new UTF8Encoding(false).GetBytes(json);
                    using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush(true);
                    }
                    File.Copy(temp, _filePath, true);
                    File.Delete(temp);
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

        private List<WarningTicketRecord> RemoveExpired(IList<WarningTicketRecord> source)
        {
            var list = source == null ? new List<WarningTicketRecord>() : new List<WarningTicketRecord>(source);
            var now = DateTime.Now;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                var lastSeen = t.LastSeen != default(DateTime)
                    ? t.LastSeen
                    : (t.UpdatedAt != default(DateTime) ? t.UpdatedAt : t.CreatedAt);

                if (lastSeen == default(DateTime))
                {
                    lastSeen = now;
                }

                if ((now - lastSeen).TotalDays > TicketRetentionDays)
                {
                    list.RemoveAt(i);
                }
            }

            return list;
        }
    }
}
