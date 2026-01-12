using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AlarmAITool.Services
{
    internal sealed class DifyAutoClient
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public DifyAutoClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl?.Trim().TrimEnd('/');
            _apiKey = apiKey?.Trim();
        }

        public async Task<string> RunAsync(string errorDesc, string errorCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("URL/AutoKey 未配置。");

            var body = new Dictionary<string, object>
            {
                ["inputs"] = new Dictionary<string, object>
                {
                    ["ErrorCode"] = "ERC",
                    ["ErrorDesc"] = errorDesc ?? string.Empty,
                    ["machineCode"] = string.Empty
                },
                ["response_mode"] = "streaming",
                ["user"] = "abc-123"
            };

            var json = _serializer.Serialize(body);

            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/workflows/run");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {err}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var evtBuf = new StringBuilder();
            string finalText = null;

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                    break;

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    evtBuf.Append(line.Substring(5).TrimStart());
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line))
                    continue;

                if (evtBuf.Length == 0)
                    continue;

                var raw = evtBuf.ToString();
                evtBuf.Clear();

                if (!(_serializer.DeserializeObject(raw) is IDictionary<string, object> evt))
                    continue;

                var eventType = GetString(evt, "event");
                var data = GetDict(evt, "data");
                if (data == null)
                    continue;

                if (string.Equals(eventType, "node_finished", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(finalText))
                    {
                        var outputs = GetValue(data, "outputs");
                        var text = ExtractText(outputs, true);
                        if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 2)
                            finalText = text;
                    }
                    continue;
                }

                if (string.Equals(eventType, "workflow_finished", StringComparison.OrdinalIgnoreCase))
                {
                    var outputs = GetValue(data, "outputs");
                    finalText = ExtractText(outputs, false) ?? finalText;
                    break;
                }
            }

            return finalText ?? string.Empty;
        }

        private static string GetString(IDictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
                return value?.ToString();
            return null;
        }

        private static IDictionary<string, object> GetDict(IDictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
                return value as IDictionary<string, object>;
            return null;
        }

        private static object GetValue(IDictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
                return value;
            return null;
        }

        private static string ExtractText(object token, bool textOnly)
        {
            if (token == null)
                return null;

            if (token is string text)
                return text;

            if (token is bool || token is int || token is long || token is double || token is decimal)
                return textOnly ? null : Convert.ToString(token);

            if (token is IDictionary<string, object> obj)
            {
                foreach (var key in new[] { "text", "answer", "result", "output", "message" })
                {
                    if (obj.TryGetValue(key, out var value))
                    {
                        var inner = ExtractText(value, textOnly);
                        if (!string.IsNullOrWhiteSpace(inner))
                            return inner;
                    }
                }

                foreach (var pair in obj)
                {
                    if (pair.Value is string inner)
                        return inner;
                }

                return textOnly ? null : new JavaScriptSerializer().Serialize(obj);
            }

            if (token is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var inner = ExtractText(item, textOnly);
                    if (!string.IsNullOrWhiteSpace(inner))
                        return inner;
                }
                return null;
            }

            return textOnly ? null : token.ToString();
        }
    }
}
