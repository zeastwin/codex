using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EW_Assistant.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EW_Assistant.Services.Reports
{
    /// <summary>
    /// 面向报表生成的轻量 LLM 客户端，复用现有 Dify 接口调用方式。
    /// </summary>
    public class LlmReportClient
    {
        private readonly HttpClient _http;

        public LlmReportClient(HttpClient http = null)
        {
            _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>
        /// 调用 LLM 生成 Markdown 文本，必要时去除包裹的代码块。
        /// </summary>
        public async Task<string> GenerateMarkdownAsync(string prompt, CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentNullException("prompt");
            }

            var cfg = ConfigService.Current;
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.URL) || string.IsNullOrWhiteSpace(cfg.ReportKey))
            {
                throw new InvalidOperationException("LLM 地址或密钥未配置，请在设置中填写。");
            }

            var url = cfg.URL.TrimEnd('/') + "/chat-messages";
            var payload = new Dictionary<string, object>
            {
                ["inputs"] = new Dictionary<string, object>(),
                ["query"] = prompt,
                ["response_mode"] = "blocking",
                ["user"] = "report-bot"
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ReportKey);
                req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req, token).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("LLM 调用失败：HTTP " + (int)resp.StatusCode + "，" + text);
                    }

                    var answer = ExtractAnswer(text);
                    if (string.IsNullOrWhiteSpace(answer))
                    {
                        throw new InvalidOperationException("LLM 未返回有效内容。");
                    }

                    return NormalizeMarkdown(answer);
                }
            }
        }

        private static string ExtractAnswer(string respText)
        {
            if (string.IsNullOrWhiteSpace(respText)) return string.Empty;

            var root = JObject.Parse(respText);
            var answer = root.Value<string>("answer");

            if (string.IsNullOrWhiteSpace(answer))
            {
                var data = root["data"] as JObject;
                if (data != null)
                {
                    answer = data.Value<string>("answer");
                    if (string.IsNullOrWhiteSpace(answer))
                    {
                        var outputs = data["outputs"] as JObject;
                        if (outputs != null)
                        {
                            answer = outputs.Value<string>("text") ?? outputs.Value<string>("output");
                            if (string.IsNullOrWhiteSpace(answer))
                            {
                                answer = outputs.ToString(Formatting.None);
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(answer))
            {
                var outputs = root["outputs"] as JObject;
                if (outputs != null)
                {
                    answer = outputs.Value<string>("text") ?? outputs.Value<string>("output");
                    if (string.IsNullOrWhiteSpace(answer))
                    {
                        answer = outputs.ToString(Formatting.None);
                    }
                }
            }

            return answer;
        }

        private static string NormalizeMarkdown(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return string.Empty;

            var text = md.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                // 去除包裹的代码块标记
                var newlineIdx = text.IndexOf('\n');
                if (newlineIdx >= 0)
                {
                    text = text.Substring(newlineIdx + 1);
                }

                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                {
                    text = text.Substring(0, lastFence);
                }
            }

            return text.Trim();
        }
    }
}
