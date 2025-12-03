using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 单条预警的 AI 分析记录。
    /// </summary>
    public class WarningAnalysisRecord
    {
        public string Key { get; set; }               // 唯一键：RuleId + 时间段
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string Level { get; set; }
        public string Type { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public string AiMarkdown { get; set; }        // AI 分析的 Markdown 文本
        public string EngineVersion { get; set; }     // 规则引擎版本，便于缓存失效

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 负责本地 JSON 缓存的读写。
    /// </summary>
    public class WarningAnalysisCache
    {
        private readonly string _filePath;
        private readonly Dictionary<string, WarningAnalysisRecord> _records =
            new Dictionary<string, WarningAnalysisRecord>(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public WarningAnalysisCache(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var dataDir = Path.Combine(@"D:\", "DataAI");
                _filePath = Path.Combine(dataDir, "WarningAnalysisCache.json");
            }
            else
            {
                _filePath = filePath;
            }
        }

        /// <summary>
        /// 从 JSON 文件加载缓存，文件不存在时创建空集合。
        /// </summary>
        public void Load()
        {
            _records.Clear();

            try
            {
                EnsureDirectory();
                if (!File.Exists(_filePath))
                {
                    return;
                }

                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var items = JsonSerializer.Deserialize<List<WarningAnalysisRecord>>(json, s_jsonOptions);
                if (items == null) return;

                foreach (var item in items)
                {
                    if (item != null && !string.IsNullOrWhiteSpace(item.Key))
                    {
                        _records[item.Key] = item;
                    }
                }
            }
            catch
            {
                // 读取失败不抛出，避免影响 UI。
            }
        }

        /// <summary>
        /// 将当前缓存写回 JSON。写失败会静默，避免影响预警主流程。
        /// </summary>
        public void Save()
        {
            try
            {
                EnsureDirectory();
                var json = JsonSerializer.Serialize(_records.Values, s_jsonOptions);
                File.WriteAllText(_filePath, json, Encoding.UTF8);
            }
            catch
            {
                // 写入失败静默处理，避免干扰主流程。
            }
        }

        /// <summary>根据 key 读取缓存，未命中或 key 为空返回 false。</summary>
        public bool TryGet(string key, out WarningAnalysisRecord record)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                record = null;
                return false;
            }

            return _records.TryGetValue(key, out record);
        }

        /// <summary>
        /// 插入或更新缓存，并立即持久化。
        /// </summary>
        public void Upsert(WarningAnalysisRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Key))
            {
                return;
            }

            if (_records.TryGetValue(record.Key, out var existing))
            {
                if (record.CreatedAt == default(DateTime))
                {
                    record.CreatedAt = existing.CreatedAt;
                }
            }
            else
            {
                if (record.CreatedAt == default(DateTime))
                {
                    record.CreatedAt = DateTime.Now;
                }
            }

            record.UpdatedAt = DateTime.Now;
            _records[record.Key] = record;
            Save();
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
