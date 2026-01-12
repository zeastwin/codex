using StressTest.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace StressTest.Core
{
    public sealed class WorkflowSseClient
    {
        private sealed class SseEvent
        {
            public string Event { get; set; }
            public string Status { get; set; }
            public string WorkflowRunId { get; set; }
            public string TaskId { get; set; }
        }

        private readonly HttpClient _http;
        public WorkflowSseClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<RequestResult> RunAsync(
            string url,
            string apiKey,
            string prompt,
            string machineCode,
            string user,
            CancellationToken ct)
        {
            var result = new RequestResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var body = new Dictionary<string, object>
                {
                    ["inputs"] = new Dictionary<string, object>
                    {
                        ["ErrorCode"] = "0",
                        ["ErrorDesc"] = prompt ?? string.Empty,
                        ["machineCode"] = machineCode ?? string.Empty
                    },
                    ["response_mode"] = "streaming",
                    ["user"] = user ?? "stress"
                };

                var serializer = new JavaScriptSerializer();
                var json = serializer.Serialize(body);

                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                               .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            var errText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            result.StatusCode = (int)resp.StatusCode;
                            result.ErrorMessage = string.IsNullOrWhiteSpace(errText)
                                ? "HTTP " + (int)resp.StatusCode
                                : errText;
                            return result;
                        }

                        using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var evtBuf = new StringBuilder();
                            var finished = false;
                            var succeeded = false;

                            while (!reader.EndOfStream)
                            {
                                ct.ThrowIfCancellationRequested();

                                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null)
                                    break;

                                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!result.TtfbMs.HasValue)
                                        result.TtfbMs = sw.Elapsed.TotalMilliseconds;

                                    evtBuf.Append(line.Substring(5).TrimStart());
                                }
                                else if (string.IsNullOrWhiteSpace(line))
                                {
                                    if (evtBuf.Length == 0)
                                        continue;

                                    var evt = ParseEvent(evtBuf.ToString());
                                    evtBuf.Clear();

                                    if (evt == null)
                                        continue;

                                    if (!string.IsNullOrWhiteSpace(evt.WorkflowRunId))
                                        result.WorkflowRunId = evt.WorkflowRunId;
                                    if (!string.IsNullOrWhiteSpace(evt.TaskId))
                                        result.TaskId = evt.TaskId;

                                    if (string.Equals(evt.Event, "workflow_finished", StringComparison.OrdinalIgnoreCase))
                                    {
                                        finished = true;
                                        succeeded = string.Equals(evt.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
                                        break;
                                    }
                                }
                            }

                            result.Success = finished && succeeded;
                            if (!finished)
                                result.ErrorMessage = "SSE 流提前结束";
                            else if (!succeeded)
                                result.ErrorMessage = "Workflow 未成功结束";
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.Canceled = true;
                result.ErrorMessage = "请求被取消";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                sw.Stop();
                result.DurationMs = sw.Elapsed.TotalMilliseconds;
            }

            return result;
        }

        private SseEvent ParseEvent(string json)
        {
            Dictionary<string, object> root;
            try
            {
                var serializer = new JavaScriptSerializer();
                root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }

            if (root == null)
                return null;

            var evt = new SseEvent
            {
                Event = ReadString(root, "event"),
                WorkflowRunId = ReadString(root, "workflow_run_id"),
                TaskId = ReadString(root, "task_id")
            };

            if (root.TryGetValue("data", out var dataObj))
            {
                if (dataObj is Dictionary<string, object> data)
                    evt.Status = ReadString(data, "status");
            }

            return evt;
        }

        private static string ReadString(Dictionary<string, object> dict, string key)
        {
            foreach (var pair in dict)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value?.ToString();
            }

            return null;
        }
    }
}
