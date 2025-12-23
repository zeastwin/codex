using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EW_Assistant.Services
{
    /// <summary>
    /// AI 异常分析历史记录本地存储。
    /// </summary>
    public sealed class AiAnalysisHistoryStore
    {
        private const int MaxHistoryCount = 200;
        private const int RetentionDays = 7;
        private static readonly Lazy<AiAnalysisHistoryStore> InstanceLazy =
            new Lazy<AiAnalysisHistoryStore>(() => new AiAnalysisHistoryStore());

        private readonly object _syncRoot = new object();
        private readonly string _filePath;
        private List<AiAnalysisRecord> _records = new List<AiAnalysisRecord>();
        private bool _loaded;

        private AiAnalysisHistoryStore()
        {
            _filePath = ResolveFilePath();
        }

        public static AiAnalysisHistoryStore Instance => InstanceLazy.Value;

        public string FilePath => _filePath;

        public void Initialize()
        {
            EnsureLoaded();
        }

        public IReadOnlyList<AiAnalysisRecord> GetSnapshot()
        {
            lock (_syncRoot)
            {
                EnsureLoaded();
                return new List<AiAnalysisRecord>(_records);
            }
        }

        public void Append(AiAnalysisRecord record)
        {
            if (record == null)
                return;

            lock (_syncRoot)
            {
                EnsureLoaded();
                _records.Insert(0, record);
                TrimOld(DateTime.Now);
                TrimMax();
                SaveToDisk();
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded)
                return;

            _records = LoadFromDisk();
            _loaded = true;
        }

        private List<AiAnalysisRecord> LoadFromDisk()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath, new UTF8Encoding(false));
                    var list = JsonConvert.DeserializeObject<List<AiAnalysisRecord>>(json);
                    if (list != null)
                    {
                        list.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                        TrimOldInternal(list, DateTime.Now);
                        TrimMaxInternal(list);
                        SaveToDisk(list);
                        return list;
                    }
                }
            }
            catch
            {
                // 读取失败时回退为空
            }

            return new List<AiAnalysisRecord>();
        }

        private void SaveToDisk()
        {
            SaveToDisk(_records);
        }

        private void SaveToDisk(List<AiAnalysisRecord> records)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(records, Formatting.Indented);
                File.WriteAllText(_filePath, json, new UTF8Encoding(false));
            }
            catch
            {
                // 记录失败不影响主流程
            }
        }

        private void TrimOld(DateTime now)
        {
            TrimOldInternal(_records, now);
        }

        private static void TrimOldInternal(List<AiAnalysisRecord> records, DateTime now)
        {
            var cutoff = now.AddDays(-RetentionDays);
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i] == null || records[i].Timestamp < cutoff)
                    records.RemoveAt(i);
            }
        }

        private void TrimMax()
        {
            TrimMaxInternal(_records);
        }

        private static void TrimMaxInternal(List<AiAnalysisRecord> records)
        {
            if (records.Count <= MaxHistoryCount)
                return;

            records.RemoveRange(MaxHistoryCount, records.Count - MaxHistoryCount);
        }

        private static string ResolveFilePath()
        {
            var baseDir = Directory.Exists("D:\\")
                ? Path.Combine("D:\\", "DataAI")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataAI");
            return Path.Combine(baseDir, "performance_ai_history.json");
        }
    }
}
