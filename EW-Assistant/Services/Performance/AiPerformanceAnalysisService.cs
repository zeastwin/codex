using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EW_Assistant.Services
{
    /// <summary>
    /// AI 异常分析服务，仅返回文本结果，供 UI 展示与人工决策参考。
    /// </summary>
    public sealed class AiPerformanceAnalysisService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public AiPerformanceAnalysisService(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var cfg = ConfigService.Current;
            _baseUrl = cfg != null && !string.IsNullOrWhiteSpace(cfg.URL) ? cfg.URL.TrimEnd('/') : string.Empty;
            _apiKey = cfg != null ? cfg.PerformanceKey : string.Empty;
        }

        public async Task<string> AnalyzeAsync(AiAnalysisContext context, CancellationToken token = default(CancellationToken))
        {
            if (context == null) return string.Empty;

            if (string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_apiKey))
                return "未配置 AI 接口或密钥，无法生成分析。";

            var url = _baseUrl + "/workflows/run";
            var inputs = BuildInputs(context);
            var payload = new Dictionary<string, object>
            {
                ["inputs"] = inputs,
                ["response_mode"] = "blocking",
                ["user"] = "performance-analyzer"
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req, token).ConfigureAwait(false))
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        return "调用 AI 接口失败：" + resp.StatusCode;

                    var answer = TryExtractAnswer(body);
                    if (!string.IsNullOrWhiteSpace(answer))
                        return answer;

                    return string.IsNullOrWhiteSpace(body) ? string.Empty : body;
                }
            }
        }

        private static Dictionary<string, object> BuildInputs(AiAnalysisContext context)
        {
            var json = JsonConvert.SerializeObject(context);
            return new Dictionary<string, object>
            {
                ["context_json"] = json,
                ["timestamp"] = context.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ["current_cpu_usage"] = context.CurrentCpuUsage,
                ["average_cpu_usage_5min"] = context.AverageCpuUsage5Min,
                ["top_process_summary"] = context.TopProcessSummary ?? string.Empty,
                ["event_summary"] = context.EventSummary ?? string.Empty,
                ["historical_comparison"] = context.HistoricalComparison ?? string.Empty
            };
        }

        private static string TryExtractAnswer(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            try
            {
                var obj = JObject.Parse(body);
                var answer = (string)obj["answer"];
                if (!string.IsNullOrWhiteSpace(answer)) return answer;

                var outputText = (string)obj["output_text"];
                if (!string.IsNullOrWhiteSpace(outputText)) return outputText;

                var data = obj["data"] as JObject;
                if (data != null)
                {
                    var outputs = data["outputs"] as JObject;
                    if (outputs != null)
                    {
                        var result = (string)outputs["result"] ?? (string)outputs["text"] ?? (string)outputs["answer"];
                        if (!string.IsNullOrWhiteSpace(result)) return result;
                    }
                }
            }
            catch
            {
                // 解析失败直接返回空
            }
            return string.Empty;
        }
    }
}
