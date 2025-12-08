# AGENTS.md — EW-Assistant (WPF) × McpServer (.NET 8)

# 强制使用 UTF-8 编码读取文件，使用 UTF-8 编码进行解析和处理，确保文件编码一致性。

## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。
- WPF 侧目标框架 net4.8.1（csproj 中 LangVersion=10），保持 .NET Framework 兼容，不要引入 .NET 5+/C# 11+ 特性；如需调整 LangVersion/TargetFramework 先确认。

## 仓库总览
- `EW-Assistant`：WPF 客户端（.NET Framework 4.8.1，依赖 SkiaSharp/AvalonEdit/LiveChartsCore/MdXaml 等），主窗口导航 10 个视图：总览 Dashboard、AI 助手、AI 文档、产能看板、报警看板、报表中心、预警中心、机台控制、库存管理、设置。应用启动时托管本地 HTTP 监听（`WorkHttpServer`，默认 `http://127.0.0.1:8091/`）、拉起 `McpServer.exe`，并后台循环生成报表。
- `McpServer`：.NET 8 Windows 可执行 + 托盘程序，内置 Model Context Protocol 工具（产能/报警/IO/机器控制）。默认监听 `ReadAppConfig().MCPServerIP`（初始 `127.0.0.1:8081`），托盘菜单支持重启/退出。
- 后台任务：启动时预创建 AI 助手/预警中心视图；`ReportScheduler` 每 30 分钟补齐最近 3 天日报与上一自然周周报；`McpServerProcessHost` 拉起同目录 MCP 并在启动前清理其他同名进程。

## 运行与配置
- WPF 配置文件：`D:\AppConfig.json`，字段 `ProductionLogPath`、`AlarmLogPath`、`IoMapCsvPath`、`MCPServerIP`、`URL`、`AutoKey`、`ChatKey`、`DocumentKey`、`ReportKey`、`EarlyWarningKey`、`flatFileLayout`、`UseOkNgSplitTables`、`warningOptions`（预警阈值集合）。ConfigView 读写此文件，变更通过 `ConfigService.ConfigChanged` 广播。
- MCP 配置文件：`D:\AppConfig.json`（`Base.ReadAppConfig` 维护），字段 `ProductionLogPath`、`AlarmLogPath`、`IoMapCsvPath`、`MCPServerIP`、`FlatFileLayout`、`UseOkNgSplitTables`。缺字段填默认值。
- 构建顺序：先编译 `McpServer`（`CopyMcpToAssistantBin` 会复制到 `EW-Assistant/bin/Debug/McpServer/`），再启动 WPF；WPF `McpServerProcessHost` 会复用/拉起同目录的可执行。
- 本地 HTTP：`WorkHttpServer` 接受 POST（`errorCode/prompt/machineCode/workflowId/onlyMajorNodes`），调用 `DifyWorkflowClient` 后台流式推送至 `AIAssistantView`，忙碌时直接 429；单通道闸门，最多同时处理 1 个自动分析。

## 日志与本地数据
- 程序信息写 `D:\Data\AiLog\UI\yyyy-MM-dd.log`；聊天写 `D:\Data\AiLog\Chat\yyyy-MM-dd.txt`；自动分析追加到 `D:\Data\AiLog\Auto\yyyy-MM-dd.txt`；文档 AI 结果落 `D:\Data\AiLog\DocumentAI\yyyy-MM-dd.txt`；报警/预警 AI 调用日志写 `D:\Data\AiLog\WarningAI\yyyy-MM-dd.log`。
- MCP 工具调用（含 IO）写 `D:\Data\AiLog\McpTools\yyyy-MM-dd.txt`，IO 读写记录缺 D 盘时回落运行目录。
- 报表/库存/预警本地数据存放 `D:\DataAI`：库存 `inventory_*.json`，报表 `Reports` 目录，预警工单/缓存 `warning_tickets.json`、`WarningAnalysisCache.json`。

## 数据输入与约定
- 产能 CSV：默认命名 `小时产量yyyyMMdd.csv`，位于 `AppConfig.ProductionLogPath`。解析时自动探测分隔符（`,`/`;`/Tab`），PASS/FAIL 列通过别名（pass/良品/良率pass，fail/不良/报废/抛料/ng）识别；A 列常为小时。`UseOkNgSplitTables` 开启时改为解析 `{yyyy-MM-dd}-产品记录表.csv`（OK）与 `{yyyy-MM-dd}-抛料记录表.csv`（NG），优先日目录再查根目录，时间列容忍含日期/时间串，逐行累加到对应小时。
- 报警 CSV：默认放在 `AppConfig.AlarmLogPath`（`AlarmCsvTools.WarmLogPath` 默认为 `D:\Data\Alarms`），编码优先 `GB2312`，解析检测到乱码会退到 `GB18030`；字段需包含报警代码/内容/类别/开始时间/结束时间/时长（秒）。`flatFileLayout` 启用时按日期目录（`yyyy-MM-dd`）查找 `{yyyy-MM-dd}-报警记录表.csv`/`{yyyy-MM-dd}.csv`，否则从根目录扫描，优先匹配文件名日期，其次按修改时间。文件名解析失败会使用最后写入日期。解析时支持中英文/符号混排的时间和日期片段，缺失结束时间会用时长补齐，时长缺失则用 start/end 差值，保证秒数非负。
- IO 映射：`IoMapRepository.LoadFromXlsx` 读取 `IoMapCsvPath` 指定的 Excel（默认首个工作表，表头行=1），仅使用第 1 列和第 9 列两组：组1 关=30000/开=30001/读回起始=30100，组2 关=30003/开=30002/读回起始=30140；每 16 个 IO 读回地址递增 1。

## 产能解析实现要点
- `CsvProductionRepository` 按文件绝对路径+修改时间 UTC 缓存，当日单表模式读取 `DailyFilePattern`（默认 `小时产量yyyyMMdd'.csv'`），`PossibleDelimiters` 支持逗号/分号/Tab，首列兼容 “0/0:00” 等小时格式，忽略含“总数”的汇总行。
- `UseOkNgSplitTables` 模式下分别读取 OK/NG 表，按日目录优先，缺文件会跳过；时间列匹配“产出时间/抛料时间”等别名并容忍日期片段，超过 0-23 的行会被丢弃；小时桶累计后计算日汇总。
- PASS/FAIL 别名不区分大小写，缺列会直接报错；解析过程共享读锁，允许 CSV 被外部追加。

## 报警解析实现要点
- `AlarmCsvRepository` 对文件解析结果按绝对路径+修改时间 UTC 缓存；GetHourlyAggregation 会将跨小时区间拆分后求并集秒数，单小时上限 3600 秒，并按代码维护 Count/Duration，Count 跨小时累计。
- 文件解析会去除单双/中英文引号与空白，表头匹配容忍多种别名；日期解析优先使用文件名中的 `yyyyMMdd`，跨天报警会截断到当日 0-24 时，SourceFile 只保留文件名。
- `FlatFileLayout` 模式下先查当天目录；未命中则回落到全局扫描，允许以文件名或最后写入日期定位当天文件。

## 核心模块速览
- **AIAssistantView**：基于 `DifyChatAdapter` 流式对话（`Config.URL/chat-messages` + `ChatKey`），支持 Token 合并/整段替换，SSE 事件驱动进度卡片；提供产能/报警日报与周报的快速提示按钮，`HistoryLimit` 默认 10；Stop 按钮会尝试 `/chat-messages/{task}/stop`。
- **DocumentAiView**：利用 `DocumentMindMapParser`/`DocumentChecklistParser` + `MindmapService` 调用文件型 Workflow（`DocumentKey`），将 JSON 转为思维导图/Checklist，可拖拽/选择文件、迁移/缩放、导出、节点高亮。
- **DashboardView**：读取近 7 天产能 CSV + 当天 24 小时数据，Skia 绘制周趋势/小时堆叠/良率环形图；可自动刷新（默认 10s）。报警侧优先走缓存（`AlarmLogCompute`），否则扫描当天/最近 CSV 统计小时次数与类别时长占比。
- **ProductionBoardView**：面向大屏的产能看板（周趋势 + 当日 24 小时柱状 + Top3/Bottom3/低良率标记），支持日期切换与自动刷新，CSV 解析与 Dashboard 保持一致。
- **AlarmView**：展示报警数据的小时分布、类别占比、占用时序；依赖 `AppConfig.AlarmLogPath` 与 AlarmTools 解析逻辑。
- **ReportsCenterView**：集中浏览/生成/导出产能与报警日报/周报（Markdown），调用 `ReportGeneratorService` + LLM（`ReportKey`），支持重新生成与选中文件定位。
- **WarningCenterView**：预警规则引擎 + CSV 数据 + AI 分析：展示最近 24 小时预警，支持工单状态流转、AI 分析、筛选；预警数据与工单持久化在 `D:\DataAI`。
- **MachineControl**：对接 `http://127.0.0.1:8081` 通用命令入口（Start/Pause/Reset/ClearAlarms/VisionCalibrate），统一确认弹框，结果写入信息流。
- **InventoryView**：本地备件库存管理（文件仓储，路径 `D:\DataAI`），支持新增/出入库/调整与流水查询。
- **ConfigView**：配置视图，读取/保存 `AppConfig.json`，校验必填；`ConfigService` 为全局单例，其他模块订阅变更。

## 报表中心
- 报表类型：DailyProd/DailyAlarm/WeeklyProd/WeeklyAlarm，Markdown 文件存放 `D:\DataAI\Reports\{Type}\`，按日期或区间命名。
- 数据来源：`ReportCalculators` 直接读取产能/报警 CSV（兼容分表/分目录模式），计算摘要/表格后拼装 Prompt（`ReportPromptBuilders`），通过 `LlmWorkflowClient`（`ReportKey`）获取分析 Markdown，再由 `ReportMarkdownFormatters` 合成最终正文。
- 生成策略：UI 手动触发或后台 `ReportScheduler` 自动补齐（跳过当日未结束的日报），每 30 分钟检查一次；支持“重新生成”覆盖已有文件。
- 预览/导出：`ReportStorageService` 扫描索引，按类型筛选并读取 Markdown；支持导出到任意路径、定位原始文件。

## 预警中心
- 数据源：`ProductionCsvReader`/`AlarmCsvReader` 读取 `AppConfig` 配置的 CSV 路径，支持 `UseOkNgSplitTables` 与 `flatFileLayout`；编码探测同报警解析规则。
- 规则引擎：`WarningRuleEngine` 组合良率/产能/报警/趋势规则，基线窗口默认 Lookback 14 天，支持 `warningOptions` 配置（良率阈值、趋势窗口、报警次数/时长阈值、SuppressHours、IgnoreMinutes、ResolveGraceMinutes 等）；跨小时压制和基线缓存 5 分钟。
- 工单与缓存：工单持久化 `D:\DataAI\warning_tickets.json`（保留 7 天），AI 分析缓存 `WarningAnalysisCache.json`；样例 `warning_tickets.sample.json` 可参考字段。
- AI 分析：`AiWarningAnalysisService` 调用 `Config.URL/workflows/run` + `EarlyWarningKey` 返回 Markdown，日志写 `D:\Data\AiLog\WarningAI`，结果缓存避免重复调用；支持过滤工单状态（Pending/InProgress/Resolved）。

## 库存管理
- `InventoryModule` 在应用启动时初始化文件仓储，数据目录固定 `D:\DataAI`。
- `FileInventoryRepository` 使用 `inventory_spareparts.json` / `inventory_transactions.json`，SemaphoreSlim 保护并发，记录入库/出库/调整流水（字段包含 QtyChange/AfterQty/Reason/Operator/RelatedDevice）。
- 未来可通过修改 `App.config` 的 `InventoryRepositoryMode` 切换实现（当前仅 File 模式，Api 模式未实现）。

## MCP 工具与数据接口
- `MCPTools`：通用命令工具（ClearMachineAlarms/ResetMachine/StartMachine/PauseMachine/VisionCalibrateMachine/QuickInspectionMachine），基地址 `http://127.0.0.1:8081`，JSON `{action,target,params}`。
- `ProdCsvTools`：产能相关工具 `GetProductionSummary`、`GetWeeklyProductionSummary`、`GetHourlyStats`、`QuickQueryWindow`、`GetSummaryLastHours`、`GetProductionRangeSummary` 等，依赖 `CsvProductionRepository`（`ProductionLogPath` + `DailyFilePattern`/`UseOkNgSplitTables`）。日期参数兼容 `yyyy-MM-dd/MM-dd/dd`，小时窗口钳制 0-24；周报输出波动率/Top3/Bottom3，跨天与最近 N 小时接口缺 CSV 会返回警告并保持行数齐全，最近小时按整点对齐。
- `ProdAlarmTools`：跨报警/产能的融合分析 `GetHourlyProdWithAlarms`、`GetAlarmImpactSummary`、`GetTopAlarmsDuringLowYield`、`AnalyzeProdAndAlarmsNL`，读取 `AlarmCsvRepository` + `CsvProductionRepository`；日期参数兼容 `yyyy-MM-dd/MM-dd/dd`（留空默认今天），小时窗口会钳制在 0-24 之间，Top 报警按去重后的 CodeDuration 选取并映射首条非空 Content。`GetAlarmImpactSummary` 会在缺产能时跳过该日并返回警告、缺报警仅记警告；按小时汇总的报警秒数已去重且不会钳位，周度返回 `weeklyTotals`（alarmSeconds/activeHours/lowYieldRowCount）与相关性系数。`GetTopAlarmsDuringLowYield` 仅在 Total>0 且良率低于阈值的小时聚合代码秒数/次数，并附带缺数据警告。`AnalyzeProdAndAlarmsNL` 仅支持“本月d1-d2”范围表达，命中后委托 `GetAlarmImpactSummary`。
- `AlarmCsvTools`：报警统计 `GetAlarmRangeWindowSummary`、`QueryAlarms`，支持时间窗口与 TopN，编码容错 GB2312/GB18030；时间窗口为闭区间，flatFileLayout 模式逐日解析当天目录；RangeSummary 按类别与代码聚合（sortBy=count/duration），Query 按 Start 倒序返回 start/end 字符串并支持 code/keyword 过滤。
- `IoMcpTools.IoCommand`：仅支持 open/close 意图（拒绝 toggle，支持中英文/数字别名），写入地址按映射：组1 open=30001/close=30000，组2 open=30002/close=30003；open 时读回地址会在 3010x 基础上替换成 3012x，按 MSB 位比对预期开关状态。调用前必须加载映射（缺名称/地址直接报错），请求与读回结果写入 `D:\Data\AiLog\McpTools`（不可写时回落运行目录）。
- 所有工具返回 JSON 字符串，错误格式 `{type:"error", where, message}`；调用前确保配置路径/文件存在。

## 开发注意事项
- 全程使用 UTF-8（无 BOM）读写文件；非 ASCII 文本一律使用简体中文。
- 本地依赖仅限 Windows（托盘/HttpListener/WinExe），避免引入跨平台不支持的 API。
- 兼容旧数据：解析 CSV/报警文件时已有较强容错，新增字段时保持向后兼容；不要更改现有文件命名/位置约定（D 盘配置/日志）除非同步更新读取逻辑。
- MCP 工具调用日志默认写入 `D:\Data\AiLog\McpTools\yyyy-MM-dd.txt`，失败会忽略但不会中断业务。
- MCP 监听端口、WorkHttpServer urlacl 等需要管理员权限时提前处理；慎用破坏性命令（删除配置/覆盖日志）。






