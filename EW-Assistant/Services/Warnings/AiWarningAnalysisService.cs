using EW_Assistant.Services;
using EW_Assistant.Warnings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace EW_Assistant.Services.Warnings
{
    /// <summary>
    /// 预警文本的 AI 分析服务（调用 LLM 返回 Markdown）。
    /// </summary>
    public class AiWarningAnalysisService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public AiWarningAnalysisService(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
            var cfg = ConfigService.Current;
            _baseUrl = cfg != null && !string.IsNullOrWhiteSpace(cfg.URL) ? cfg.URL.TrimEnd('/') : string.Empty;
            if (cfg != null)
            {
               _apiKey = cfg.EarlyWarningKey;
            }
            else
            {
                _apiKey = string.Empty;
            }
        }

        /// <summary>
        /// 调用 LLM 对单条预警进行分析，返回 Markdown 文本。
        /// </summary>
        public async Task<string> AnalyzeAsync(WarningItem warning)
        {
            if (warning == null) return string.Empty;

            var prompt = BuildPrompt(warning);
            if (string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_apiKey))
            {
                return "未配置 AI 接口或密钥，无法生成分析。";
            }

            try
            {
                var inputs = new Dictionary<string, object>
                {
                    { "warning_key", warning.Key ?? string.Empty },
                    { "rule_id", warning.RuleId ?? string.Empty },
                    { "rule_name", warning.RuleName ?? string.Empty },
                    { "level", warning.Level ?? string.Empty },
                    { "type", warning.Type ?? string.Empty },
                    { "time_range", string.Format("{0:yyyy-MM-dd HH:mm} ~ {1:yyyy-MM-dd HH:mm}", warning.StartTime, warning.EndTime) },
                    { "metric_name", warning.MetricName ?? string.Empty },
                    { "current_value", warning.CurrentValue },
                    { "baseline_value", warning.BaselineValue },
                    { "threshold_value", warning.ThresholdValue },
                    { "summary", warning.Summary ?? string.Empty },
                    { "prompt", prompt }
                };

                var payload = new Dictionary<string, object>
                {
                    { "inputs", inputs },
                    { "response_mode", "blocking" },
                    { "user", "warning-analyzer" }
                };

                var url = _baseUrl + "/workflows/run";
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var json = JsonConvert.SerializeObject(payload);
                    AppendLog("Request", url, json);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var resp = await _http.SendAsync(req).ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AppendLog("Response", url, string.Format("Status={0}, Body={1}", resp.StatusCode, body));
                        if (!resp.IsSuccessStatusCode)
                        {
                            return "调用 AI 接口失败：" + resp.StatusCode;
                        }

                        var answer = TryExtractAnswer(body);
                        if (!string.IsNullOrWhiteSpace(answer)) return answer;
                        return string.IsNullOrWhiteSpace(body) ? string.Empty : body;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Exception", _baseUrl + "/workflows/run", ex.Message);
                return "调用 AI 分析出错：" + ex.Message;
            }
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

        private static string BuildPrompt(WarningItem item)
        {
            var sb = new StringBuilder();
            sb.AppendLine("预警信息：");
            sb.AppendLine(string.Format("RuleId: {0}", item.RuleId));
            sb.AppendLine(string.Format("RuleName: {0}", item.RuleName));
            sb.AppendLine(string.Format("Level: {0}", item.Level));
            sb.AppendLine(string.Format("Type: {0}", item.Type));
            sb.AppendLine(string.Format("时间范围: {0:yyyy-MM-dd HH:mm} ~ {1:yyyy-MM-dd HH:mm}", item.StartTime, item.EndTime));
            if (!string.IsNullOrEmpty(item.MetricName))
                sb.AppendLine(string.Format("指标: {0}", item.MetricName));
            sb.AppendLine(string.Format("当前值: {0}", item.CurrentValue));
            if (item.BaselineValue.HasValue)
                sb.AppendLine(string.Format("基线值: {0}", item.BaselineValue.Value));
            if (item.ThresholdValue.HasValue)
                sb.AppendLine(string.Format("阈值: {0}", item.ThresholdValue.Value));
            if (!string.IsNullOrEmpty(item.Summary))
            {
                sb.AppendLine(string.Format("Summary: {0}", item.Summary));
            }

            return sb.ToString();
        }

        private static void AppendLog(string stage, string url, string content)
        {
            try
            {
                var dir = Path.Combine("D:\\", "Data", "AiLog", "WarningAI");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var path = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var line = string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}{4}{5}",
                    DateTime.Now,
                    stage,
                    url ?? string.Empty,
                    Environment.NewLine,
                    content ?? string.Empty,
                    Environment.NewLine);

                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
            catch
            {
                // 记录失败不影响主流程
            }
        }
    }
}
