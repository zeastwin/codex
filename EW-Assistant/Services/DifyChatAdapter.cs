using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EW_Assistant.Services
{
    /// <summary>
    /// 适配 Dify 工作流编排【对话型应用】API（SSE流式）
    /// 负责：发送消息（流式）、处理 SSE 事件、会话续聊、停止响应、文件上传。
    /// </summary>
    public sealed class DifyChatAdapter
    {
        private readonly HttpClient _http; // 复用外部HttpClient

        /// <summary>上次对话的 conversation_id（用于续聊）</summary>
        public string ConversationId { get; private set; }

        /// <summary>最近一次流的 task_id（用于 /stop）</summary>
        public string LastTaskId { get; private set; }

        public DifyChatAdapter(HttpClient http, string baseUrl, string apiKey, string user, string conversationId = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            ConversationId = conversationId;
        }

        /// <summary>
        /// 发送一条消息并以 SSE 流式读取结果。
        /// </summary>
        public async Task SendStreamingAsync(
    string query,
    CancellationToken ct,
    Action<string> onToken,              // 分片文本
    Action<string> onReplaceAll = null,  // 审查触发时替换整个回复
    Action onComplete = null,            // 收到 message_end
    IDictionary<string, object> inputs = null,
    string workflowId = null,
    bool? autoGenerateName = null,
    IEnumerable<object> files = null,    // 见：BuildFileObjects(...) 用法
    Action<string, JObject> onEvent = null
)
        {
            // === 追加：日志累积与包裹回调 ===
            var answerSb = new StringBuilder();
            var startAt = DateTime.Now;
            var hasCompleted = false;

            // 包裹 token 回调：既推 UI，又累计文本
            void TokenSink(string t)
            {
                if (!string.IsNullOrEmpty(t)) answerSb.Append(t);
                onToken?.Invoke(t);
            }

            // 包裹 replace 回调：替换 UI 的同时，重置累计文本
            void ReplaceSink(string full)
            {
                answerSb.Clear();
                if (full != null) answerSb.Append(full);
                onReplaceAll?.Invoke(full);
            }

            // 包裹 complete 回调：写一次完整 Q/A 日志
            void CompleteSink()
            {
                if (!hasCompleted)
                {
                    hasCompleted = true;
                    var elapsed = (DateTime.Now - startAt).TotalSeconds;
                    // ConversationId / LastTaskId 建议是你类上的字段，HandleSseChunk 已在事件里赋值
                    WriteLog(
                        $"[CHAT] completed" + Environment.NewLine +
                        $"Q: {query}" + Environment.NewLine +
                        $"A: {answerSb}" + Environment.NewLine +
                        $"conv={ConversationId ?? ""} task={LastTaskId ?? ""} elapsed={elapsed:F2}s"
                    );
                }
                onComplete?.Invoke();
            }

            // === 原有发起请求逻辑 ===
            var payload = new Dictionary<string, object>
            {
                ["inputs"] = inputs ?? new Dictionary<string, object>(),
                ["query"] = query ?? string.Empty,
                ["response_mode"] = "streaming",
                ["conversation_id"] = ConversationId ?? string.Empty,
                ["user"] = "abc-123"
            };
            if (!string.IsNullOrWhiteSpace(workflowId)) payload["workflow_id"] = workflowId;
            if (autoGenerateName.HasValue) payload["auto_generate_name"] = autoGenerateName.Value;
            if (files != null) payload["files"] = files;

            using var req = new HttpRequestMessage(HttpMethod.Post, ConfigService.Current.URL+ "/chat-messages");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigService.Current.ChatKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var cancelReg = ct.Register(() =>
            {
                try { stream.Dispose(); } catch { }
                try { resp.Dispose(); } catch { }
            });

            using var reader = new StreamReader(stream, Encoding.UTF8);

            // 先把“问”记一笔（方便排查，即使后面失败也能看到问题）
            WriteLog(
                $"[CHAT] start" + Environment.NewLine +
                $"Q: {query}" + Environment.NewLine +
                $"conv(pre)={ConversationId ?? ""}"
            );

            var buffer = new StringBuilder();
            try
            {
                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null) break;

                    // 空行 => 一个 SSE 块结束
                    if (line.Length == 0)
                    {
                        if (buffer.Length > 0)
                        {
                            var json = buffer.ToString();
                            buffer.Clear();
                            // 用包裹后的回调
                            HandleSseChunk(json, TokenSink, ReplaceSink, CompleteSink, onEvent);
                        }
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var part = line.Substring(5).TrimStart();
                        buffer.Append(part);
                    }
                }

                // 末尾残留
                if (buffer.Length > 0)
                {
                    HandleSseChunk(buffer.ToString(), TokenSink, ReplaceSink, CompleteSink, onEvent);
                    buffer.Clear();
                }
            }
            catch (Exception ex)
            {
                // 异常时也把当前已累积答案写入日志（不覆盖正常 complete 的日志）
                if (!hasCompleted)
                {
                    WriteLog(
                        $"[CHAT] exception: {ex.Message}" + Environment.NewLine +
                        $"Q: {query}" + Environment.NewLine +
                        $"A(partial): {answerSb}" + Environment.NewLine +
                        $"conv={ConversationId ?? ""} task={LastTaskId ?? ""}"
                    );
                }
                throw;
            }
            finally
            {
                // 有些极端情况下未触发 message_end，这里兜底一次
                if (!hasCompleted)
                {
                    var elapsed = (DateTime.Now - startAt).TotalSeconds;
                    WriteLog(
                        $"[CHAT] finalize(no message_end)" + Environment.NewLine +
                        $"Q: {query}" + Environment.NewLine +
                        $"A(partial): {answerSb}" + Environment.NewLine +
                        $"conv={ConversationId ?? ""} task={LastTaskId ?? ""} elapsed={elapsed:F2}s"
                    );
                }
            }
        }
        private static readonly object s_logLock = new object();
        public static void WriteLog(string str)
        {
            try
            {
                Directory.CreateDirectory(@"D:\Data\AiLog\Chat");
                var path = Path.Combine(@"D:\Data\AiLog\Chat", DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]     {str}"
                            .Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

                lock (s_logLock)
                {
                    using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                    {
                        if (fs.Length == 0)
                        {
                            var bom = Encoding.UTF8.GetPreamble(); // EF BB BF
                            if (bom.Length > 0) fs.Write(bom, 0, bom.Length);
                        }

                        fs.Seek(0, SeekOrigin.End);
                        using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                        {
                            sw.WriteLine(line);
                            sw.Flush();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WriteLog failed: {ex.Message}");
            }
        }
        private void HandleSseChunk(string jsonLine,
            Action<string> onToken,
            Action<string> onReplaceAll,
            Action onComplete,
           Action<string, JObject> onEvent)
        {
            // Dify 的 SSE 每块就是一条 JSON
            if (string.IsNullOrWhiteSpace(jsonLine)) return;

            JObject obj;
            try { obj = JObject.Parse(jsonLine); }
            catch
            {
                // 非 JSON（偶发脏行/心跳），忽略
                return;
            }

            var evt = (string)obj["event"]; // e.g. "message" / "message_end" / "message_replace" / "error" / ping / workflow/node...
            onEvent?.Invoke(evt, obj);  // ⬅️ 抛给上层做“进度”可视化
            switch (evt)
            {
                case "message":
                    LastTaskId = (string)(obj["task_id"] ?? LastTaskId);
                    ConversationId = (string)(obj["conversation_id"] ?? ConversationId);
                    var delta = (string)obj["answer"];
                    if (!string.IsNullOrEmpty(delta))
                        onToken?.Invoke(delta);
                    break;

                case "message_replace":
                    LastTaskId = (string)(obj["task_id"] ?? LastTaskId);
                    ConversationId = (string)(obj["conversation_id"] ?? ConversationId);
                    var replaced = (string)obj["answer"];
                    if (replaced != null)
                        onReplaceAll?.Invoke(replaced);
                    break;

                case "message_file":
                    // Dify: 图片文件事件（来自 assistant）
                    // 你可以按需把图片以 Markdown 嵌入（MdXaml 支持）
                    var url = (string)obj["url"];
                    if (!string.IsNullOrEmpty(url))
                        onToken?.Invoke($"\n\n![]({url})\n\n");
                    break;

                case "message_end":
                    LastTaskId = (string)(obj["task_id"] ?? LastTaskId);
                    ConversationId = (string)(obj["conversation_id"] ?? ConversationId);
                    onComplete?.Invoke();
                    break;

                case "tts_message":
                case "tts_message_end":
                case "workflow_started":
                case "workflow_finished":
                case "node_started":
                case "node_finished":
                case "ping":
                    // 可按需调试/忽略
                    break;

                case "error":
                    var code = (string)obj["code"];
                    var msg = (string)obj["message"];
                    onToken?.Invoke($"\n\n> ⚠️ **Dify错误** {code}: {msg}\n\n");
                    onComplete?.Invoke();
                    break;

                default:
                    // 未知事件，忽略
                    break;
            }
        }

        /// <summary>
        /// 停止当前流（仅流式模式有效）。要求 body 传 user，且 task_id 为最近一次消息的 task。
        /// </summary>
        public async Task<bool> TryStopAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(LastTaskId)) return false;

            var url = $"{ConfigService.Current.URL + "/chat-messages"}/{LastTaskId}/stop";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigService.Current.ChatKey);
            req.Content = new StringContent(JsonConvert.SerializeObject(new { user = "abc-123" }), Encoding.UTF8, "application/json");

            try
            {
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
