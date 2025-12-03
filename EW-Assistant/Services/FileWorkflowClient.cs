using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EW_Assistant.Services
{
    /// <summary>根据 dify api.txt 封装的文件类 Workflow 客户端，负责上传文件并执行 Workflow。</summary>
    public sealed class FileWorkflowClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _workflowId;
        /// <summary>HTTP 超时时间（秒），默认 300；设为 0 或负数则表示无限制。</summary>
        public int TimeoutSeconds { get; set; } = 300;

        public FileWorkflowClient(FileWorkflowClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new ArgumentNullException(nameof(options.BaseUrl));
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                throw new ArgumentNullException(nameof(options.ApiKey));

            _baseUrl = options.BaseUrl.TrimEnd('/');
            _workflowId = options.WorkflowId != null ? options.WorkflowId.Trim() : null;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.ApiKey);

            if (TimeoutSeconds > 0)
                _httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            else
                _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        }


        /// <summary>
        /// 上传文件并运行 Workflow，返回解析后的结果；异常会写程序日志并转换为失败结果。
        /// </summary>
        public async Task<FileWorkflowResult> RunAsync(FileWorkflowRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.FilePath))
                throw new ArgumentNullException(nameof(request.FilePath));
            if (!File.Exists(request.FilePath))
                throw new FileNotFoundException("指定文件不存在。", request.FilePath);
            if (string.IsNullOrWhiteSpace(request.UserId))
                throw new ArgumentNullException(nameof(request.UserId));

            try
            {
                var fileId = await UploadFileAsync(request.FilePath, request.UserId, ct).ConfigureAwait(false);
                var payload = BuildPayloadJson(request, fileId);
                var url = string.IsNullOrWhiteSpace(_workflowId)
                    ? $"{_baseUrl}/workflows/run"
                    : $"{_baseUrl}/workflows/{_workflowId}/run";

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
                var respText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"调用 Workflow 失败，HTTP {(int)response.StatusCode}: {respText}");

                return ParseResult(respText, request.OutputFieldName);
            }
            catch (TaskCanceledException ex)
            {
                string reason = ct.IsCancellationRequested
                    ? "调用方主动取消了请求。"
                    : "HTTP 请求在超时时间内未完成，可能是 Workflow 处理时间过长。";

                MainWindow.PostProgramInfo("[FileWorkflow] 执行失败（超时/取消）：" + reason, "error");

                return new FileWorkflowResult
                {
                    Succeeded = false,
                    OutputText = null,
                    RawResponse = ex.ToString()
                };
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo($"[FileWorkflow] 执行失败：{ex.Message}", "error");
                return new FileWorkflowResult
                {
                    Succeeded = false,
                    OutputText = null,
                    RawResponse = ex.ToString()
                };
            }
        }

        /// <summary>上传本地文件到 Dify，返回文件 id，HTTP 非 201/200 视为失败。</summary>
        private async Task<string> UploadFileAsync(string filePath, string userId, CancellationToken ct)
        {
            using var content = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(filePath);
            var fileName = Path.GetFileName(filePath);

            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(filePath));
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(userId, Encoding.UTF8), "user");

            var url = $"{_baseUrl}/files/upload";
            using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Created && !response.IsSuccessStatusCode)
                throw new InvalidOperationException($"上传文件失败，HTTP {(int)response.StatusCode}: {body}");

            var json = JObject.Parse(body);
            var id = json.Value<string>("id");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("上传文件成功但未返回文件 id：" + body);

            return id;
        }

        private static string BuildPayloadJson(FileWorkflowRequest request, string uploadFileId)
        {
            var inputs = request.ExtraInputs != null
                ? JObject.FromObject(request.ExtraInputs)
                : new JObject();

            var fileNode = new JObject
            {
                ["transfer_method"] = "local_file",
                ["upload_file_id"] = uploadFileId,
                ["type"] = string.IsNullOrWhiteSpace(request.DocumentType) ? "document" : request.DocumentType
            };
            JToken fileValue = request.FileVariableAsArray ? new JArray(fileNode) : (JToken)fileNode;
            inputs[request.FileVariableName ?? "localfile"] = fileValue;

            if (!string.IsNullOrWhiteSpace(request.PromptVariableName))
                inputs[request.PromptVariableName] = request.Prompt ?? string.Empty;

            var payload = new JObject
            {
                ["inputs"] = inputs,
                ["response_mode"] = "blocking",
                ["user"] = request.UserId
            };

            return payload.ToString(Formatting.None);
        }

        private static FileWorkflowResult ParseResult(string respText, string outputFieldName)
        {
            var root = JObject.Parse(respText);
            var data = root["data"] as JObject ?? root;

            var status = data.Value<string>("status") ?? root.Value<string>("status");
            if (!string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Workflow 执行失败，status=" + status + "，error=" + (data["error"] ?? root["error"]));

            var outputs = data["outputs"] as JObject;
            if (outputs == null)
                throw new InvalidOperationException("Workflow 返回成功但未包含 outputs：" + respText);

            var text = outputFieldName != null ? outputs[outputFieldName]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(text))
                text = outputs.ToString(Formatting.None);

            return new FileWorkflowResult
            {
                Succeeded = true,
                RawResponse = respText,
                WorkflowRunId = data.Value<string>("workflow_run_id") ?? root.Value<string>("workflow_run_id"),
                TaskId = data.Value<string>("task_id") ?? root.Value<string>("task_id"),
                Outputs = outputs,
                OutputText = text
            };
        }

        private static string GuessMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            return ext switch
            {
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".markdown" => "text/markdown",
                ".json" => "application/json",
                ".csv" => "text/csv",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream"
            };
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    public sealed class FileWorkflowClientOptions
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string WorkflowId { get; set; }
    }

    public sealed class FileWorkflowRequest
    {
        public string FilePath { get; set; }
        public string UserId { get; set; }
        public string Prompt { get; set; }
        public IDictionary<string, object> ExtraInputs { get; set; }
        public string FileVariableName { get; set; } = "localfile";
        public string PromptVariableName { get; set; } = "prompt";
        public string OutputFieldName { get; set; } = "text";
        public string DocumentType { get; set; } = "document";
        /// <summary>某些 Workflow 将文件变量定义为“文件列表”。此类场景需将该值设为 true。</summary>
        public bool FileVariableAsArray { get; set; }
    }

    public sealed class FileWorkflowResult
    {
        public bool Succeeded { get; set; }
        public string OutputText { get; set; }
        public string WorkflowRunId { get; set; }
        public string TaskId { get; set; }
        public JObject Outputs { get; set; }
        public string RawResponse { get; set; }
    }

    /// <summary>MindmapService 使用 FileWorkflowClient 执行固定 Workflow。</summary>
    public sealed class MindmapService
    {
        private readonly FileWorkflowClientOptions _options;

        public MindmapService(string workflowId = null)
        {
            var cfg = ConfigService.Current ?? throw new InvalidOperationException("ConfigService 尚未初始化。");
            if (string.IsNullOrWhiteSpace(cfg.URL))
                throw new InvalidOperationException("URL 未配置，无法调用 Workflow。");
            if (string.IsNullOrWhiteSpace(cfg.DocumentKey))
                throw new InvalidOperationException("Key 未配置，无法调用 Workflow。");

            _options = new FileWorkflowClientOptions
            {
                BaseUrl = cfg.URL,
                ApiKey = cfg.DocumentKey,
                WorkflowId = workflowId
            };
        }

        public async Task<string> BuildMindmapJsonAsync(string filePath, string prompt, string userId, CancellationToken token, IDictionary<string, object> extraInputs = null)
        {
            using var client = new FileWorkflowClient(_options);
            var request = new FileWorkflowRequest
            {
                FilePath = filePath,
                Prompt = prompt,
                UserId = userId,
                FileVariableName = "localfile",
                PromptVariableName = "prompt",
                OutputFieldName = "text",
                ExtraInputs = extraInputs
            };

            var result = await client.RunAsync(request, token).ConfigureAwait(false);
            return result.OutputText;
        }
    }
}
