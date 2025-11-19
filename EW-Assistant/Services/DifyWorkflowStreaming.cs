// Net/DifyWorkflowClient.cs
using EW_Assistant.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace EW_Assistant.Net
{
    public static class DifyWorkflowClient
    {
        // ====== 并发闸门（一次只跑一个问答）======
        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static volatile bool _isBusy = false;
        private static string _currentTaskId = null;
        private static string _currentRunId = null;

        public static bool IsBusy => _isBusy;
        public static string CurrentTaskId => _currentTaskId;
        public static string CurrentRunId => _currentRunId;
        public sealed class RunHandle
        {
            public string WorkflowRunId { get; set; }
            public string TaskId { get; set; }  
            public string FinalText { get; set; }
            public bool Succeeded { get; set; }
        }
        /// <summary>
        /// 对外入口（单通道）：使用 ConfigService.Current.AutoURL / AutoKey 调用 workflow（streaming）。
        /// - 忙则直接提示并返回 null（不排队）
        /// - 只把“大节点”推到信息流；最终文本推到 AIAssistantView
        /// </summary>
        public static async Task<RunHandle> RunAutoAnalysisExclusiveAsync(
            EW_Assistant.Views.AIAssistantView aiView,
            string errorCode,
            string prompt,
            string machineCode,
            string workflowId = null,
            bool onlyMajorNodes = true,
            CancellationToken ct = default)
        {
            var cfg = EW_Assistant.Services.ConfigService.Current;
            if (string.IsNullOrWhiteSpace(cfg?.URL+ "/workflows/run") || string.IsNullOrWhiteSpace(cfg?.AutoKey))
            {
                Post("AutoURL / AutoKey 未配置。", "warn");
                return null;
            }

            // 尝试立即占用闸门（不等待）
            if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            {
              //  Post("当前有问答正在执行，请稍后再试。", "warn");
                return null;
            }

            _isBusy = true;
            try
            {
                var handle = await RunWorkflowStreamingAsync(
                    baseUrl: cfg.URL + "/workflows/run",
                    apiKey: cfg.AutoKey,
                    errorCode: errorCode ?? "0",
                    prompt: prompt ?? string.Empty,
                    machineCode: machineCode ?? string.Empty,
                    aiView: aiView,
                    workflowId: workflowId,
                    onlyMajorNodes: onlyMajorNodes,
                    ct: ct
                ).ConfigureAwait(false);

                return handle;
            }
            finally
            {
                _currentTaskId = null;
                _currentRunId = null;
                _isBusy = false;
                _gate.Release();
            }
        }

        /// <summary>
        /// 立刻返回：尝试启动一次后台 AI 自动分析（独占闸门）。<br/>
        /// 返回 true=已受理并在后台执行；false=繁忙或配置不完整。<br/>
        /// 答案不会返回给调用方，只会在 AIAssistantView 里流式呈现。
        /// </summary>
        public static bool TryStartAutoAnalysisNow(
            EW_Assistant.Views.AIAssistantView aiView,
            string errorCode,
            string prompt,
            string machineCode,
            string workflowId = null,
            bool onlyMajorNodes = true)
        {
            var cfg = EW_Assistant.Services.ConfigService.Current;
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.URL + "/workflows/run") || string.IsNullOrWhiteSpace(cfg.AutoKey))
            {
                Post("AutoURL / AutoKey 未配置。", "warn");
                return false;
            }

            // 无等待占闸门：一次只能跑一个
            if (!_gate.Wait(0))
            {
                return false; // 繁忙
            }

            _isBusy = true;

            // 后台跑（不阻塞调用方）
            Task.Run(async () =>
            {
                try
                {
                    await RunWorkflowStreamingAsync(
                        baseUrl: cfg.URL + "/workflows/run",
                        apiKey: cfg.AutoKey,
                        errorCode: string.IsNullOrWhiteSpace(errorCode) ? "0" : errorCode,
                        prompt: prompt ?? string.Empty,
                        machineCode: machineCode ?? string.Empty,
                        aiView: aiView,                 // 可为 null，内部已判空
                        workflowId: workflowId,
                        onlyMajorNodes: onlyMajorNodes,
                        ct: CancellationToken.None
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Post("后台执行异常：" + ex.Message, "warn");
                }
                finally
                {
                    _currentTaskId = null;
                    _currentRunId = null;
                    _isBusy = false;
                    try { _gate.Release(); } catch { }
                }
            });

            return true;
        }

        /// <summary>
        /// 流式运行（SSE）。只把“大节点”推到信息流；最终文本推到 AIAssistantView。
        /// </summary>
        public static async Task<RunHandle> RunWorkflowStreamingAsync(
            string baseUrl,
            string apiKey,
            string errorCode,
            string prompt,           // ErrorDesc
            string machineCode,
            EW_Assistant.Views.AIAssistantView aiView,
            string workflowId = null,             // 为空走 /workflows/run
            bool onlyMajorNodes = true,           // 只上报大节点
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var handle = new RunHandle();

            WriteLog(prompt);

            var body = new
            {
                inputs = new
                {
                    ErrorCode = errorCode,
                    ErrorDesc = prompt,
                    machineCode = machineCode
                },
                response_mode = "streaming",
                user = "abc-123"
            };
            var json = JsonConvert.SerializeObject(body);

            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var req = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Post($"❌ HTTP {(int)resp.StatusCode}: {err}", "error");
                handle.FinalText = $"HTTP {(int)resp.StatusCode}: {err}";
                return handle;
            }

            string runId = null;
            string taskId = null;
            string finalText = null;

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var evtBuf = new StringBuilder();

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;

                // 仅处理以 data: 开头的行，其他忽略
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    evtBuf.Append(line.Substring(5).TrimStart());
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    // 一个事件结束
                    if (evtBuf.Length == 0) continue;

                    var one = evtBuf.ToString();
                    evtBuf.Clear();

                    JObject evt;
                    try { evt = JObject.Parse(one); }
                    catch { continue; }

                    var type = (string)evt["event"];
                    var d = evt["data"] as JObject;

                    switch (type)
                    {
                        case "workflow_started":
                            {
                                runId = d?["id"]?.ToString();
                                handle.WorkflowRunId = evt.Value<string>("workflow_run_id") ?? runId;
                                taskId = evt.Value<string>("task_id");
                                handle.TaskId = taskId;

                                Post("▶️ 捕获到异常，进入AI自动分析流程", "error");
                                break;
                            }

                        case "node_started":
                            {
                                if (!ShouldReportNode(d, onlyMajorNodes)) break;

                                var title = d?["title"]?.ToString();
                                if (string.IsNullOrWhiteSpace(title)) title = "执行阶段";
                                break;
                            }

                        case "text_chunk":
                        case "text_delta":
                            // 你不需要中途显示文本，所以这里不处理
                            break;

                        case "node_finished":
                            {
                                // 仅在 LLM / 输出 节点上尝试抓取文本
                                var nodeType = d?["node_type"]?.ToString()?.ToLowerInvariant() ?? "";
                                var title = d?["title"]?.ToString() ?? "";

                                var isTextNode = nodeType is "llm" or "output"
                                                 || title.Contains("LLM")
                                                 || title.Contains("输出");

                                if (isTextNode && string.IsNullOrEmpty(finalText))
                                {
                                    var outText = ExtractText(d?["outputs"], textOnly: true);
                                    // 过滤掉过短/无意义内容（如 "0"）
                                    if (!string.IsNullOrWhiteSpace(outText) && outText.Trim().Length >= 2)
                                        finalText = outText;
                                }

                                // —— 下面是原有的大节点进度上报逻辑（保留不变）——
                                var status = d?["status"]?.ToString() ?? "succeeded";
                                var ok = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
                                var took = d?["elapsed_time"]?.ToString();
                                var phaseTitle = string.IsNullOrWhiteSpace(title) ? "执行阶段" : title;
                                var suffix = string.IsNullOrEmpty(took) ? "" : $" ({took}s)";
                                Post($"{(ok ? "☑" : "❌")} {phaseTitle}{suffix}", ok ? "ok" : "warn");
                            }
                            break;

                        case "workflow_finished":
                            {
                                var status = d?["status"]?.ToString() ?? "succeeded";
                                handle.Succeeded = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);

                                if (string.IsNullOrWhiteSpace(finalText))
                                {
                                    var outputs = d?["outputs"];
                                    // 先拿 outputs.text，再用宽松模式兜底（允许数字/JSON拼成字符串）
                                    finalText = outputs?["text"]?.ToString()
                                                ?? ExtractText(outputs, textOnly: false);
                                }

                                handle.FinalText = finalText ?? string.Empty;

                                if (aiView != null)
                                    Ui(() => aiView.AddBotMarkdown(string.IsNullOrWhiteSpace(handle.FinalText)
                                        ? "*（无文本输出）*" : handle.FinalText));

                                Post($"{(handle.Succeeded ? "✅" : "⏹")} 结束（{status}）", handle.Succeeded ? "ok" : "warn");
                                WriteLog(handle.FinalText);
                            }
                            break;

                        case "tts_message":
                        case "tts_message_end":
                        case "ping":
                        default:
                            // 忽略
                            break;
                    }
                }
            }

            return handle;
        }
        private static readonly object s_autoLogLock = new object();
        public static void WriteLog(string str)
        {
            var folderPath = @"D:\Data\AiLog\Auto";
            Directory.CreateDirectory(folderPath);
            var path = Path.Combine(folderPath, DateTime.Now.ToString("yyyy-MM-dd") + ".txt");

            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]     {str}"
                           .Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

                lock (s_autoLogLock)
                {
                    using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                    if (fs.Length == 0)
                    {
                        var bom = Encoding.UTF8.GetPreamble();
                        if (bom.Length > 0) fs.Write(bom, 0, bom.Length);
                    }

                    fs.Seek(0, SeekOrigin.End);
                    using var writer = new StreamWriter(fs, new UTF8Encoding(false)); // 追加时不再写 BOM
                    writer.WriteLine(line);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// （可选）停止当前流式任务：POST /workflows/tasks/:task_id/stop
        /// </summary>
        public static async Task<bool> StopCurrentAsyncAuto()
        {
            var taskId = _currentTaskId;
            if (string.IsNullOrEmpty(taskId)) return false;

            var url = $"{ConfigService.Current.URL + "/workflows/run".TrimEnd('/')}/workflows/tasks/{taskId}/stop";
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigService.Current.AutoKey);
            req.Content = new StringContent(JsonConvert.SerializeObject(new { user = "abc-123" }), Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, default).ConfigureAwait(false);
            var ok = resp.IsSuccessStatusCode;
            if (ok) Post("⏹ 已请求停止当前任务", "warn");
            return ok;
        }
        // 本地函数
        static bool ShouldReportNode(JObject data, bool onlyMajor)
        {
            if (data == null) return false;
            if (!onlyMajor) return true;

            var nodeType = data.Value<string>("node_type")?.ToLowerInvariant() ?? "";
            var title = data.Value<string>("title") ?? "";

            if (nodeType is "start" or "end" or "llm" or "if_else" or "agent" or "output")
                return true;

            if (title.Contains("LLM") ||
                title.Contains("条件") || title.Contains("Agent") ||
                title.Contains("开始") || title.Contains("结束") || title.Contains("输出"))
                return true;

            return false;
        }

        static string ExtractText(JToken token, bool textOnly = true)
        {
            if (token == null || token.Type == JTokenType.Null) return null;

            // 只要纯文本：数值/布尔一律忽略（避免 "0"）
            if (textOnly)
            {
                if (token.Type is JTokenType.Integer or JTokenType.Float or JTokenType.Boolean)
                    return null;
            }

            if (token.Type == JTokenType.String) return token.ToString();

            if (token is JObject obj)
            {
                // 常见键优先
                foreach (var k in new[] { "text", "answer", "result", "output", "message" })
                    if (obj.TryGetValue(k, out var v))
                    {
                        var s = ExtractText(v, textOnly);
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }

                // 任意字符串字段
                foreach (var p in obj.Properties())
                    if (p.Value.Type == JTokenType.String)
                        return p.Value.ToString();

                // 兜底：允许返回紧凑 JSON（仅当 textOnly=false）
                return textOnly ? null : obj.ToString(Formatting.None);
            }

            if (token is JArray arr)
            {
                foreach (var it in arr)
                {
                    var s = ExtractText(it, textOnly);
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
                return null;
            }

            // 允许在兜底阶段把数字也转成字符串
            return textOnly ? null : token.ToString();
        }


        static void Post(string text, string level) =>
            Ui(() => MainWindow.PostProgramInfo(text, level));

        static void Ui(Action action)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess()) action();
                else disp.Invoke(action);
            }
            catch { }
        }


    }
}
