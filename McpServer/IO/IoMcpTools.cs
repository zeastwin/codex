using System;
using EW_Assistant.Io;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using McpServer;

namespace EW_Assistant.McpTools
{
    [McpServerToolType]
    public static class IoMcpTools
    {
        // === 配置：读回位含义（true 表示“开”）===
        private const bool CHECK_TRUE_MEANS_OPEN = true;

        private static string Err(string where, string msg) =>
            JsonConvert.SerializeObject(new { type = "error", where, message = msg });

        private static readonly object s_ioLogLock = new object();

        /// <summary>IO 相关的本地日志，便于排查 LLM 调用失败原因。</summary>
        private static void LogIoTrace(object detail)
        {
            try
            {
                var dir = @"D:\Data\AiLog\McpTools";
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch
                {
                    var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory ?? ".";
                    dir = Path.Combine(baseDir, "AiLog", "McpTools");
                    Directory.CreateDirectory(dir);
                }

                var path = Path.Combine(dir, $"io-command-{DateTime.Now:yyyy-MM-dd}.log");
                var json = JsonConvert.SerializeObject(detail, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {json}{Environment.NewLine}";

                lock (s_ioLogLock)
                {
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败忽略，避免影响主流程
            }
        }

        private static string TrimForLog(string? text, int max = 2000)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (text.Length <= max) return text;
            return text.Substring(0, max) + $"...（后续截断 {text.Length - max} 字符）";
        }

        // 仅用于选择 open/close；不再支持 toggle
        // 返回：ok=true 时 intentOrReason 一定为 "open"/"close"
        //       ok=false 时 intentOrReason 为错误原因说明
        private static (bool ok, string intentOrReason) NormalizeIntent(string raw)
        {
            var s = (raw ?? string.Empty).Trim();

            if (s.Length == 0)
            {
                // 原来这里是默认 open，现在改成直接报错，强制调用方给出明确意图
                return (false, "未提供操作意图，请明确指定：open / close（或中文：打开 / 关闭）。");
            }

            var lower = s.ToLowerInvariant();

            // ===== 英文别名 =====
            if (lower == "open" ||
                lower == "on" ||
                lower == "start" ||
                lower == "enable" ||
                lower == "1" ||
                lower == "true")
            {
                return (true, "open");
            }

            if (lower == "close" ||
                lower == "off" ||
                lower == "stop" ||
                lower == "disable" ||
                lower == "0" ||
                lower == "false")
            {
                return (true, "close");
            }

            // ===== 中文 / 混合短语：看是否包含典型关键词 =====
            // 开 → 打开、开启、上电、通电、吸合、合上、伸出、升起、抬起
            if (Regex.IsMatch(s, "打开|开启|上电|通电|吸合|合上|伸出|升起|抬起"))
            {
                return (true, "open");
            }

            // 关 → 关闭、关掉、关上、停机、停止、下电、断电、断开、释放、回缩、缩回、落下、放下、复位
            if (Regex.IsMatch(s, "关闭|关掉|关上|停机|停止|下电|断电|断开|释放|回缩|缩回|落下|放下|复位"))
            {
                return (true, "close");
            }

            // ===== 明确拒绝 toggle 语义 =====
            if (lower == "toggle" || Regex.IsMatch(s, "切换|翻转|取反"))
            {
                return (false, "当前逻辑不再支持 toggle/切换，请明确指定：打开(open) 或 关闭(close)。");
            }

            // ===== 兜底：无法判断 =====
            return (false, "无法识别是打开还是关闭，请明确指定：open / close（或中文：打开 / 关闭）。");
        }

        [McpServerTool, Description(
     "控制现场设备 IO 点位（例如气缸、电磁阀、继电器）的【打开 / 关闭】动作。\n" +
     "\n" +
     "【硬约束 1 —— ioName 必须是完整名称】\n" +
     " - 参数 ioName 必须与 IO 映射表中的 Name 完全一致，不能擅自改写、缩写或只保留后缀。\n" +
     " - 例如：用户说「NG抽屉解锁气缸A 打开」，则 ioName 必须是 \"NG抽屉解锁气缸A\"，\n" +
     "   绝不能只传 \"解锁气缸A\" 或 \"气缸A\"。\n" +
     " - 如果不确定映射表里的精确名称，应该向用户追问确认，而不是猜测或截断。\n" +
     "\n" +
     "【硬约束 2 —— op 只能是打开 / 关闭】\n" +
     " - 参数 op 只能表示两种意图：打开(open) 或 关闭(close)。\n" +
     " - 允许的取值示例：\"open\"、\"close\"，或中文 \"打开\"、\"关闭\"。\n" +
     " - 禁止使用：toggle/切换/翻转 等模糊意图。\n" +
     "\n" +
     "【调用时机】\n" +
     " - 当用户说「打开/关闭 某某气缸/电磁阀/IO」时，应优先调用此工具，而不是清除报警或复位机台。\n" +
     "\n" +
     "【示例】\n" +
     " - 打开 A未测分料气缸 → IoCommand(ioName=\"A未测分料气缸\", op=\"open\")\n" +
     " - 关闭 NG抽屉解锁气缸A → IoCommand(ioName=\"NG抽屉解锁气缸A\", op=\"close\")\n" +
     "\n" +
     "注意：本工具只做 IO 动作，不清除报警、不做机台复位。"
 )]
        public static async Task<string> IoCommand(
     [Description(
        "目标 IO 名称（严格匹配）。\n" +
        " - 必须与 IO 映射表中的 Name 完全一致。\n" +
        " - 如果用户说的是「NG抽屉解锁气缸A」，就原样使用 \"NG抽屉解锁气缸A\" 作为 ioName。\n" +
        " - 不要只保留尾部（例如不要只传 \"气缸A\"），也不要自行增删前缀/后缀。"
    )]
    string ioName = null,

     [Description(
        "操作意图，只允许表示两种状态：打开 或 关闭。\n" +
        " - open：打开、开启、上电、通电、吸合、推出、升起等。\n" +
        " - close：关闭、关掉、停止、断电、释放、回缩、落下、复位等。\n" +
        "推荐：\n" +
        " - 直接把用户原话中的「打开 / 关闭」填入本参数，或翻译为 open / close。\n" +
        "禁止：\n" +
        " - 不要默认使用 \"open\"；\n" +
        " - 不要使用 toggle/切换/翻转 等模糊词。"
    )]
    string op = null)
        {
            LogIoTrace(new { stage = "开始", ioName, op, mapCount = IoMapRepository.Count });

            if (IoMapRepository.Count == 0)
            {
                var err = Err("IoCommand", "IO 映射未加载，请先调用 LoadIoMap。");
                LogIoTrace(new { stage = "校验失败", reason = "IO 映射未加载", ioName, op });
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "IO 映射未加载");
                return err;
            }

            if (string.IsNullOrWhiteSpace(ioName))
            {
                var err = Err("IoCommand", "请提供 ioName。");
                LogIoTrace(new { stage = "校验失败", reason = "缺少 ioName", ioName, op });
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "缺少 ioName");
                return err;
            }

            if (!IoMapRepository.TryGetEntry(ioName, out IoEntry entry) || entry == null)
            {
                var err = Err("IoCommand", $"未找到 IO 名称：{ioName}");
                LogIoTrace(new { stage = "校验失败", reason = "映射未找到", ioName, op });
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "映射未找到");
                return err;
            }

            // 判定意图（仅用于选地址）
            var norm = NormalizeIntent(op);
            if (!norm.ok)
            {
                var err = Err("IoCommand", norm.intentOrReason);
                LogIoTrace(new { stage = "校验失败", reason = norm.intentOrReason, ioName = entry.Name, op });
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, norm.intentOrReason);
                return err;
            }
            var intent = norm.intentOrReason; // "open" / "close"

            // 下面保持你原来的逻辑不变……
            // 选择目标地址（始终写 on）
            var targetAddress = intent == "open" ? entry.OpenAddress : entry.CloseAddress;
            if (string.IsNullOrWhiteSpace(targetAddress))
            {
                var err = Err("IoCommand", $"映射项缺少{(intent == "open" ? "OpenAddress" : "CloseAddress")}：{entry.Name}");
                LogIoTrace(new { stage = "校验失败", reason = "映射缺少地址", ioName = entry.Name, intent });
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "映射缺少地址");
                return err;
            }

            var pd = Tool.PD(
                ("ioIndex", entry.Index),
                ("address", targetAddress)
            );

            var checkAddress = entry.CheckAddress;

            if (!string.IsNullOrWhiteSpace(checkAddress) && targetAddress == "30001")
            {
                checkAddress = checkAddress.Replace("3010", "3012");
            }

            LogIoTrace(new
            {
                stage = "准备发送",
                ioName = entry.Name,
                intent,
                targetAddress,
                checkAddress,
                ioIndex = entry.Index,
                checkIndex = entry.CheckIndex,
                rawOp = op
            });

            if (!string.IsNullOrWhiteSpace(checkAddress))
            {
                pd.Add("checkAddress", checkAddress);
            }

            // 下发
            Tool.CommandCallTrace trace = null;
            var writeRaw = await Tool.SendCommand(
                action: "IoWrite",
                actionName: "IO写入",
                args: pd,
                traceSink: t => trace = t
            ).ConfigureAwait(false);
            LogIoTrace(new
            {
                stage = "下发完成",
                ioName = entry.Name,
                intent,
                targetAddress,
                checkAddress,
                ioIndex = entry.Index,
                checkIndex = entry.CheckIndex,
                httpStatus = trace?.HttpStatus?.ToString(),
                elapsedMs = trace?.ElapsedMs,
                timeout = trace?.Timeout,
                exception = trace?.Exception,
                request = TrimForLog(trace?.RequestJson),
                response = TrimForLog(trace?.ResponseText),
                returned = TrimForLog(trace?.ReturnText ?? writeRaw)
            });

            // 超时/无响应
            if (writeRaw.Contains("超时", StringComparison.OrdinalIgnoreCase) ||
                writeRaw.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                var resTimeout = JsonConvert.SerializeObject(new
                {
                    type = "io.command",
                    ok = false,
                    status = "timeout",
                    io = entry.Name,
                    intent,
                    address = targetAddress,
                    reason = "request timeout/no response"
                });
                LogIoTrace(new
                {
                    stage = "超时/无响应",
                    ioName = entry.Name,
                    intent,
                    targetAddress,
                    checkAddress,
                    raw = TrimForLog(writeRaw),
                    elapsedMs = trace?.ElapsedMs
                });
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op, address = targetAddress }, resTimeout, "timeout/no response");
                return resTimeout;
            }

            // —— 读回期望：open=true / close=false（可通过常量翻转语义）——
            bool? expected = intent == "open"
                ? (CHECK_TRUE_MEANS_OPEN ? true : false)
                : (CHECK_TRUE_MEANS_OPEN ? false : true);

            // 解析服务端返回的状态字（16位）
            var parsed = ParseIoWriteResponse(writeRaw, entry.CheckIndex);
            int? statusWord = parsed.statusWord;
            bool? bitValue = parsed.bitValue; // true=位1, false=位0

            string verdict =
                (statusWord is null || bitValue is null) ? "readback_unavailable" :
                (bitValue == expected) ? "ok" : "mismatch";

            var res = JsonConvert.SerializeObject(
                new
                {
                    type = "io.command",
                    ok = verdict == "ok",
                    status = verdict,          // ok / mismatch / readback_unavailable
                    io = entry.Name,
                    intent,                    // open / close
                    expected,                  // true / false
                    actual = bitValue,         // true / false / null
                    address = targetAddress
                },
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            LogIoTrace(new
            {
                stage = "解析完成",
                ioName = entry.Name,
                intent,
                targetAddress,
                expected,
                statusWord,
                bits = parsed.bits16,
                actual = bitValue,
                verdict,
                raw = TrimForLog(writeRaw)
            });
            ToolCallLogger.Log(nameof(IoCommand), new { ioName, op, address = targetAddress }, res, verdict == "ok" ? null : $"status={verdict}");
            return res;
        }


        // ===== 解析 IoWrite 返回的 16位状态字（从 "status" 字段）=====
        private static (int? statusWord, string? bits16, bool? bitValue)
        ParseIoWriteResponse(string? raw, int checkIndex)
        {
            if (string.IsNullOrWhiteSpace(raw)) return (null, null, null);

            try
            {
                // 剥掉前缀，保留第一个 JSON 对象
                var json = TryExtractJsonObject(raw);
                if (json == null) return (null, null, null);

                var jo = JObject.Parse(json);
                var statusToken = jo["status"];
                if (statusToken == null) return (null, null, null);

                var statusStr = statusToken.Type == JTokenType.String
                    ? (string)statusToken
                    : statusToken.ToString();

                if (!int.TryParse(statusStr?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var word))
                    return (null, null, null);

                word &= 0xFFFF;
                var bits16 = Convert.ToString(word, 2).PadLeft(16, '0');

                // MSB 编号（1..16）
                bool bitValue = GetBit((ushort)(word & 0xFFFF), checkIndex, msbIndexing: true);

                return (word, bits16, bitValue);
            }
            catch
            {
                return (null, null, null);
            }
        }

        private static bool GetBit(ushort word, int checkIndex, bool msbIndexing)
        {
            if (checkIndex < 1 || checkIndex > 16) throw new ArgumentOutOfRangeException(nameof(checkIndex));
            int bit0 = msbIndexing ? (16 - checkIndex) : (checkIndex - 1);
            return ((word >> bit0) & 1) != 0;
        }

        // 提取第一个完整 JSON（考虑字符串转义）
        private static string? TryExtractJsonObject(string s)
        {
            int start = s.IndexOf('{');
            if (start < 0) return null;

            int depth = 0;
            bool inStr = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '"' && (i == start || s[i - 1] != '\\'))
                    inStr = !inStr;

                if (!inStr)
                {
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return s.Substring(start, i - start + 1);
                    }
                }
            }
            return null;
        }
    }
}
