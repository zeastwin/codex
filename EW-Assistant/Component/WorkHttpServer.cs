using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EW_Assistant.Net
{
    /// <summary> 常驻的 POST 监听服务：接收 work 请求并调用 DifyWorkflowClient（即刻返回是否受理） </summary>
    public sealed class WorkHttpServer
    {
        private HttpListener _listener;
        private CancellationToken _token;
        private string _prefix;

        // 单例
        public static WorkHttpServer Instance { get; } = new WorkHttpServer();
        private WorkHttpServer() { }

        public bool IsRunning => _listener != null && _listener.IsListening;

        /// <summary> 程序启动即调用，一直运行直到 App 退出 </summary>
        public async Task StartAsync(string prefix, CancellationToken token)
        {
            if (IsRunning) return;

            _prefix = prefix;
            _token = token;

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            try
            {
                _listener.Start();
                MainWindow.PostProgramInfo($"WorkHttpServer 已启动：{prefix}", "ok");
            }
            catch (HttpListenerException hlex)
            {
                MainWindow.PostProgramInfo($"监听失败：{hlex.Message}（可能需要 urlacl）", "error");
                throw;
            }

            _ = Task.Run(() => AcceptLoopAsync(), token);
        }

        public void Stop()
        {
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_token.IsCancellationRequested && IsRunning)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleContextAsync(ctx), _token);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    MainWindow.PostProgramInfo($"WorkHttpServer 异常：{ex.Message}", "warn");
                }
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = 405;
                await WriteJsonAsync(context, new { error = "method_not_allowed" });
                return;
            }

            string body = null;
            try
            {
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);

                var jo = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);

                // 允许两种命名：lowerCamel 或 大写开头
                string errCode = (string)(jo["errorCode"] ?? jo["ErrorCode"] ?? "0");
                string prompt = (string)(jo["prompt"] ?? jo["ErrorDesc"] ?? "");
                string machine = (string)(jo["machineCode"] ?? jo["MachineCode"] ?? "");
                string workflowId = (string)(jo["workflowId"] ?? jo["WorkflowId"] ?? null);
                bool onlyMajor = (bool?)(jo["onlyMajorNodes"] ?? true) ?? true;

                // 没有 UI 也要能跑：取全局实例作为结果接收器（可为空，则仅写日志）
                var sink = EW_Assistant.Views.AIAssistantView.GlobalInstance;

                // 关键改动：不等待 AI 结果，立刻触发，马上返回“是否受理”
                var accepted = DifyWorkflowClient.TryStartAutoAnalysisNow(
                    aiView: sink,
                    errorCode: errCode,
                    prompt: prompt,
                    machineCode: machine,
                    workflowId: workflowId,
                    onlyMajorNodes: onlyMajor
                );

                if (!accepted)
                {
                    context.Response.StatusCode = 429; // Too Many Requests / Busy
                    await WriteJsonAsync(context, new { ok = false, busy = true, msg = "busy" });
                    return;
                }

                // 立即确认已受理；答案只在接收端 UI 呈现
                context.Response.StatusCode = 200;
                await WriteJsonAsync(context, new { ok = true, busy = false, msg = "accepted" });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { ok = false, error = ex.Message, body });
            }
        }

        private static async Task WriteJsonAsync(HttpListenerContext ctx, object obj)
        {
            var bytes = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(obj));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            try
            {
                await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }
    }
}
