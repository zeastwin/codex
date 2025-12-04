# AGENTS.md — EW-Assistant (WPF) × McpServer (.NET 8)

# 强制使用 UTF-8 编码读取文件，使用 UTF-8 编码进行解析和处理，确保文件编码一致性。

## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。
- WPF 侧目标框架 net4.8.1（csproj 中 LangVersion=10），保持 .NET Framework 兼容，不要引入 .NET 5+/C# 11+ 特性；如需调整 LangVersion/TargetFramework 先确认。

## 仓库总览
- `EW-Assistant`：WPF 客户端（.NET Framework 4.8.1，依赖 SkiaSharp/AvalonEdit/LiveChartsCore/MdXaml 等），主窗口导航 7 个视图：总览 Dashboard、AI 助手、AI 文档、产能看板、报警看板、机台控制、设置。应用启动时托管本地 HTTP 监听（`WorkHttpServer`，默认 `http://127.0.0.1:8091/`）并拉起 `McpServer.exe`。
- `McpServer`：.NET 8 Windows 可执行 + 托盘程序，内置 Model Context Protocol 工具（产能/报警/IO/机器控制）。默认监听 `ReadAppConfig().MCPServerIP`（初始 `127.0.0.1:8081`），托盘菜单支持重启/退出。
- 运行日志：程序信息/AI 对话分别写入 `D:\Data\AiLog\UI` 与 `D:\Data\AiLog\Chat`（UTF-8，无 BOM）；保持 D 盘可写或确保兜底目录可用。

## 运行与配置
- WPF 配置文件：`D:\AppConfig.json`，字段 `ProductionLogPath`、`AlarmLogPath`、`IoMapCsvPath`、`MCPServerIP`、`URL`、`AutoKey`、`ChatKey`、`DocumentKey`、`EarlyWarningKey`、`flatFileLayout`。ConfigView 读写此文件，变更通过 `ConfigService.ConfigChanged` 广播。
- MCP 配置文件：`D:\AppConfig.json`（`Base.ReadAppConfig`/`SaveAppConfig` 维护），字段 `ProductionLogPath`（产能 CSV 路径）、`AlarmLogPath`（报警 CSV 路径）、`IoMapCsvPath`（IO 映射 Excel）、`MCPServerIP`。无法写入 D 盘时自动回落到运行目录。
- 构建顺序：先编译 `McpServer`（自带 `CopyMcpToAssistantBin` 目标会复制到 `EW-Assistant/bin/Debug/McpServer/`），再启动 WPF；WPF `McpServerProcessHost` 会复用/拉起同目录的可执行。
- 本地 HTTP：`WorkHttpServer` 接受 POST（`errorCode/prompt/machineCode/workflowId/onlyMajorNodes`），调用 `DifyWorkflowClient` 后台流式推送至 `AIAssistantView`，忙碌时 429。

## 数据输入与约定
- 产能 CSV：默认命名 `小时产量yyyyMMdd.csv`，位于 `AppConfig.ProductionLogPath`。解析时自动探测分隔符（`,`/`;`/Tab`），PASS/FAIL 列通过别名（pass/良品/良率pass，fail/不良/报废/抛料/ng）识别；A 列常为小时。`CsvProductionRepository`/`ProdCsvTools` 与 Dashboard/ProductionBoard 共用此格式。
- 报警 CSV：默认放在 `AppConfig.AlarmLogPath`，编码优先 `GB2312`，失败回退 `GB18030`/UTF-8；字段需包含报警代码/内容/类别/开始时间/结束时间/时长（秒）。文件名如含 `yyyyMMdd` 会优先匹配当天，否则取最近修改时间。
- IO 映射：`IoMapRepository.LoadFromXlsx` 读取 `IoMapCsvPath` 指定的 Excel（默认首个工作表，表头行=1），仅使用第 1 列和第 9 列两组：组1 关=30000/开=30001/读回起始=30100，组2 关=30002/开=30003/读回起始=30140；每 16 个 IO 读回地址递增 1。
- 日志落盘：`MainWindow.PostProgramInfo` 写 UI 信息流，同时写 `D:\Data\AiLog\UI\yyyy-MM-dd.log`；`DifyChatAdapter` 写聊天日志到 `D:\Data\AiLog\Chat\yyyy-MM-dd.txt`。

## 核心模块速览
- **AIAssistantView**：基于 `DifyChatAdapter` 流式对话（`Config.URL/chat-messages` + `ChatKey`），支持 Token 合并/整段替换，SSE 事件驱动进度卡片；提供产能/报警日报与周报的快速提示按钮，`HistoryLimit` 默认 10；Stop 按钮会尝试 `/chat-messages/{task}/stop`。
- **DocumentAiView**：利用 `DocumentMindMapParser`/`DocumentChecklistParser` + `MindmapService` 调用文件型 Workflow（`DocumentKey`），将 JSON 转为思维导图/Checklist，可拖拽/选择文件、迁移/缩放、导出、节点高亮。
- **DashboardView**：读取近 7 天产能 CSV + 当天 24 小时数据，Skia 绘制周趋势/小时堆叠/良率环形图；可自动刷新（默认 10s）。报警侧优先走缓存（`AlarmLogCompute`），否则扫描当天/最近 CSV 统计小时次数与类别时长占比。
- **ProductionBoardView**：面向大屏的产能看板（周趋势 + 当日 24 小时柱状 + Top3/Bottom3/低良率标记），支持日期切换与自动刷新，CSV 解析与 Dashboard 保持一致。
- **AlarmView**：展示报警数据的小时分布、类别占比、占用时序；依赖 `AppConfig.AlarmLogPath` 与 AlarmTools 解析逻辑。
- **MachineControl**：对接 `http://127.0.0.1:8081` 通用命令入口（Start/Pause/Reset/ClearAlarms/VisionCalibrate），统一确认弹框，结果写入信息流。
- **ConfigView**：配置视图，读取/保存 `AppConfig.json`，校验必填；`ConfigService` 为全局单例，其他模块订阅变更。

## MCP 工具与数据接口
- `MCPTools`：通用命令工具（ClearMachineAlarms/ResetMachine/StartMachine/PauseMachine/VisionCalibrateMachine/QuickInspectionMachine），基地址 `http://127.0.0.1:8081`，JSON `{action,target,params}`。
- `ProdCsvTools`：产能相关工具 `GetProductionSummary`、`GetWeeklyProductionSummary`、`GetHourlyStats`、`QuickQueryWindow`、`GetSummaryLastHours`、`GetProductionRangeSummary` 等，依赖 `CsvProductionRepository`（`ProductionLogPath` + `DailyFilePattern`）。
- `ProdAlarmTools`：跨报警/产能的融合分析 `GetHourlyProdWithAlarms`、`GetAlarmImpactSummary`、`GetTopAlarmsDuringLowYield`、`AnalyzeProdAndAlarmsNL`，读取 `AlarmCsvRepository` + `CsvProductionRepository`。
- `AlarmCsvTools`：报警统计 `GetAlarmRangeWindowSummary`、`QueryAlarms`，支持时间窗口与 TopN，编码容错 GB2312/GB18030。
- `IoMcpTools.IoCommand`：基于映射 Excel 的 IO 写入工具，仅支持 open/close 意图（含中英文别名），写入地址 30002/30003 或 30000/30001，读回按 MSB 索引判断，返回 JSON 状态；启动时已尝试加载映射。
- 所有工具返回 JSON 字符串，错误格式 `{type:"error", where, message}`；调用前确保配置路径/文件存在。

## 开发注意事项
- 全程使用 UTF-8（无 BOM）读写文件；非 ASCII 文本一律使用简体中文。
- 本地依赖仅限 Windows（托盘/HttpListener/WinExe），避免引入跨平台不支持的 API。
- 兼容旧数据：解析 CSV/报警文件时已有较强容错，新增字段时保持向后兼容；不要更改现有文件命名/位置约定（D 盘配置/日志）除非同步更新读取逻辑。
- MCP 监听端口、WorkHttpServer urlacl 等需要管理员权限时提前处理；慎用破坏性命令（删除配置/覆盖日志）。


## 在 WSL 模式下工作（Linux 命令），但本项目的 WPF（EW-Assistant）必须用 Windows MSBuild 编译并在 Windows 上运行。

> 你正在 WSL 模式（Linux 命令环境）工作，但 EW-Assistant 是 Windows WPF 项目。
> 因此：**用 WSL 调 Windows 的 MSBuild.exe 编译，用 powershell.exe 启动 exe**。:contentReference[oaicite:4]{index=4}

### 0) 只验证 EW-Assistant（硬约束）
- 仅允许编译/运行：`EW-Assistant`
- 不对 `McpServer` 做构建/运行/发布/清理等操作（除非用户明确要求）

### 1) 允许使用的“唯一构建/运行方式”（白名单）
> 禁止使用 `cmd.exe /C start ...`（WSL + bash 引号极易炸）；统一用 `powershell.exe Start-Process`。:contentReference[oaicite:5]{index=5}

#### 1.1 构建（优先只构建 Assistant，避免牵连 McpServer）
**优先方案 A（推荐）：只构建 csproj**
```bash
'/mnt/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe' \
  'EW-Assistant/EW-Assistant.csproj' \
  /t:Restore,Build /m /v:m /nologo \
  '/p:Configuration=Debug' '/p:Platform=Any CPU'


