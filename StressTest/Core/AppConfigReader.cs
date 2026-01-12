using StressTest.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace StressTest.Core
{
    public static class AppConfigReader
    {
        public const string DefaultPath = @"D:\AppConfig.json";

        public static AppConfigSnapshot Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("配置路径为空。", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到配置文件。", path);

            var json = File.ReadAllText(path, Encoding.UTF8);
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
                throw new InvalidOperationException("配置解析失败，JSON 根对象为空。");

            var url = ReadString(root, "URL");
            var autoKey = ReadString(root, "AutoKey");

            return new AppConfigSnapshot
            {
                Url = url?.Trim(),
                AutoKey = autoKey?.Trim()
            };
        }

        private static string ReadString(Dictionary<string, object> root, string key)
        {
            foreach (var pair in root)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value?.ToString();
            }

            return null;
        }
    }
}
