using EW_Assistant.Io;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Globalization;
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

        // 仅用于选择 open/close；不再支持 toggle
        private static (bool ok, string intentOrReason) NormalizeIntent(string? raw)
        {
            var s = (raw ?? "open").Trim().ToLowerInvariant();

            // 开
            if (s is "open" or "on" or "start" or "enable" or "1" or "true" ||
                Regex.IsMatch(s, "打开|开启|上电|通电|吸合|开(?!关)"))
                return (true, "open");

            // 关
            if (s is "close" or "off" or "stop" or "disable" or "0" or "false" ||
                Regex.IsMatch(s, "关闭|停止|断电|释放|下电|关(?!开)"))
                return (true, "close");

            if (s is "toggle" || Regex.IsMatch(s, "切换|翻转|取反"))
                return (false, "新逻辑不再支持 toggle，请明确：open / close");

            return (false, "无法识别开/关，请明确：open / close（或 打开/关闭）");
        }

        [McpServerTool, Description(
     "控制现场设备 IO 点位（如气缸、电磁阀、继电器）的打开/关闭。\n" +
     "当用户说『打开/关闭 某某气缸/电磁阀/IO』时，应优先调用此工具，而不是清除报警。\n" +
     "示例：\n" +
     " - 打开 A未测分料气缸 → IoCommand(ioName=\"A未测分料气缸\", op=\"open\")\n" +
     " - 关闭 A未测分料气缸 → IoCommand(ioName=\"A未测分料气缸\", op=\"close\")\n" +
     "注意：本工具只做 IO 动作，不清除报警或复位机台。"
 )]
        public static async Task<string> IoCommand(
     [Description("目标 IO 名称；与 IO 映射表中的 Name 完全一致，例如：\"A未测分料气缸\"")]
    string ioName = null,

     [Description(
        "操作意图：open=打开 / close=关闭。\n" +
        "支持自然语言别名：\"打开/开启/ON\" 归一为 open；\"关闭/关掉/OFF\" 归一为 close。"
    )]
    string op = "open")
        {
            if (IoMapRepository.Count == 0)
            {
                var err = Err("IoCommand", "IO 映射未加载，请先调用 LoadIoMap。");
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "IO 映射未加载");
                return err;
            }
            if (string.IsNullOrWhiteSpace(ioName))
            {
                var err = Err("IoCommand", "请提供 ioName。");
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "缺少 ioName");
                return err;
            }
            if (!IoMapRepository.TryGetEntry(ioName, out IoEntry entry) || entry == null)
            {
                var err = Err("IoCommand", $"未找到 IO 名称：{ioName}");
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "映射未找到");
                return err;
            }

            // 判定意图（仅用于选地址）
            var norm = NormalizeIntent(op);
            if (!norm.ok)
            {
                var err = Err("IoCommand", norm.intentOrReason);
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, norm.intentOrReason);
                return err;
            }
            var intent = norm.intentOrReason; // "open" / "close"

            // 选择目标地址（始终写 on）
            var targetAddress = intent == "open" ? entry.OpenAddress : entry.CloseAddress;
            if (string.IsNullOrWhiteSpace(targetAddress))
            {
                var err = Err("IoCommand", $"映射项缺少{(intent == "open" ? "OpenAddress" : "CloseAddress")}：{entry.Name}");
                ToolCallLogger.Log(nameof(IoCommand), new { ioName, op }, null, "映射缺少地址");
                return err;
            }

            // 组装参数；checkAddress 可为空（表示不做读回判定）
            var pd = Tool.PD(
                ("ioIndex", entry.Index),
                ("address", targetAddress)
            );

            // 读回地址：允许为空
            var checkAddress = entry.CheckAddress;

            // 特殊地址 30001 的读回地址重定向
            if (!string.IsNullOrWhiteSpace(checkAddress) && targetAddress == "30001")
            {
                checkAddress = checkAddress.Replace("3010", "3012");
            }

            // 最终有读回地址才传给下位机
            if (!string.IsNullOrWhiteSpace(checkAddress))
            {
                pd.Add("checkAddress", checkAddress);
            }

            // 下发
            var writeRaw = await Tool.SendCommand(
                action: "IoWrite",
                actionName: "IO写入",
                args: pd
            ).ConfigureAwait(false);

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
