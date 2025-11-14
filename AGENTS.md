# AGENTS.md — EW-Assistant (WPF) × McpServer (.NET 8)
## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。

---

## 1. 系统概览
- `EW-Assistant` 是 .NET Framework 4.8.1 的 WPF Shell，`MainWindow` 缓存多视图、维护底部 `ProgramInfo` 日志，并在启动时拉起 `WorkHttpServer`（8091）和 `McpServerProcessHost`（托管 `McpServer.exe`）。
- `McpServer` 是 .NET 8 的本地 MCP 工具网关，使用 `ModelContextProtocol.Server` 对外暴露 CSV / 报警 / IO / 设备命令工具；输出统一 JSON，供 Dify Workflow/Chat 工具链消费。
- 全部设备写操作最终都命中 `http://127.0.0.1:8081` 的内部 API；`MachineControl`（人工 UI）与 MCP 的 `Tool.*`/`IoMcpTools.*` 复用相同 JSON 请求体。
- 本地数据来源均来自用户自建文件夹：生产 CSV (`ConfigService.Current.CsvRootPath`)、报警 CSV (`ConfigService.Current.AlarmLogPath`)、配置文件 `D:\AppConfig.json` / `D:\MCPAppConfig.json`、聊天/自动化日志 `D:\Data\AiLog`。

```mermaid
flowchart LR
subgraph UI[WPF UI Shell]
  Dash[DashboardView]
  Board[ProductionBoardView]
  Assistant[AIAssistantView]
  Alarm[AlarmView]
  Machine[MachineControl]
end
Assistant -- SSE/HTTP --> Dify[Dify Chat & Workflow]
Dify -- MCP Tool JSON --> MCP[McpServer (.NET 8)]
MCP -- 只读/受控写 --> Data[(产能 CSV / 报警 CSV / IO 映射)]
MCP -- HTTP 桥 --> Equip[设备 API @8081]
Machine -- Confirmed POST --> Equip
WorkHook[WorkHttpServer @8091] -- Trigger JSON --> DifyWorkflowClient --> Assistant
MainWindow -- McpServerProcessHost --> MCP
```

---

## 2. 目录结构（按现有代码）
```
EW-Assistant/
├─ Assets/EW.ico
├─ Component/
│  ├─ UiCoalescer.cs              # UI 节流/合流（Dispatcher 安全更新）
│  └─ WorkHttpServer.cs           # HttpListener @8091 → DifyWorkflowClient
├─ Services/
│  ├─ DifyChatAdapter.cs          # Dify Chat SSE 适配 + 日志
│  ├─ DifyWorkflowStreaming.cs    # 静态 DifyWorkflowClient（串行闸门）
│  └─ McpServerProcessHost.cs     # 附着/拉起 McpServer.exe
├─ Views/
│  ├─ AIAssistantView.xaml(.cs)   # 对话、Markdown、Quick Prompt、global sink
│  ├─ DashboardView.xaml(.cs)     # SkiaSharp 仪表盘（含内部 AlarmLogCache/Compute）
│  ├─ ProductionBoardView.xaml(.cs)# 电视墙版产能大屏
│  ├─ AlarmView.xaml(.cs)         # 报警可视化，独立数据管线
│  ├─ MachineControl.xaml(.cs)    # 本地设备操作（二次确认）
│  └─ ConfigView.xaml(.cs)        # 可视化编辑 `D:\AppConfig.json`（同文件定义 Settings.AppConfig + ConfigService）
├─ MainWindow.xaml(.cs)           # Shell、导航、全局日志
└─ EW-Assistant.csproj / App.config / App.xaml(.cs)

McpServer/
├─ Base.cs / Program.cs           # 读取 `D:\MCPAppConfig.json`、注册工具、系统托盘
├─ csv_production_tools.cs        # ProdCsvTools（CSV 仓库 + 工具导出）
├─ AlarmTools.cs                  # AlarmCsvRepository + ProdAlarmTools + AlarmCsvTools
├─ MCPTools.cs                    # Tool.*（Start/Pause/Reset/...）
├─ IO/
│  ├─ IoMapRepository.cs          # Excel → IO Name/Index/Address 映射
│  └─ IoMcpTools.cs               # IoCommand + 读回判断
├─ Controllers/                   # 预留 MVC 端点（当前为空）
├─ Tray/                          # 托盘图标/资源
├─ appsettings*.json / McpServer.http
└─ McpServer.csproj / McpServer.sln
```

---

## 3. WPF 模块职责
### 3.1 MainWindow 与全局日志
- 通过 `_routes` 与 `_viewCache` 动态切换视图；`AI 助手` 视图在启动时预加载以确保 `AIAssistantView.GlobalInstance` 可用。
- `InfoItems` 记录最近 200 条系统事件，统一由 `MainWindow.PostProgramInfo` 写入，MachineControl、MCP 托管器、Workflow Hook、MCP 工具日志都汇入此面板。
- 启动时：`WorkHttpServer` 监听 8091；`McpServerProcessHost` 会优先 attach 已有进程，否则尝试从 `.
McpServer\McpServer.exe` 拉起；应用退出时统一停止。

### 3.2 AIAssistantView
- 包含聊天消息列表（Markdown ↔ streaming 文本）、输入框、工具按钮、文件上传（AvalonEdit 编辑区）。
- `GlobalInstance` 供 `WorkHttpServer`/Workflow 输出直接写入 UI；`DifyChatAdapter` 负责 SSE 解包、token/replace 回调、文件构建、日志落盘 `D:\Data\AiLog\Chat\yyyy-MM-dd.txt`。
- `QuickReportBtn`、`QuickAlarmReportBtn` 等按钮直接把工具白名单、调用参数、输出模板写入用户提示词，明确禁止 JSON/raw 输出。
- `RunAutoAnalysis` 通过 `DifyWorkflowClient`（`Services/DifyWorkflowStreaming.cs`）串流渲染节点，日志写入 `D:\Data\AiLog\Auto\yyyy-MM-dd.txt`。

### 3.3 DashboardView（SkiaSharp）
- 使用 `ConfigService.Current.CsvRootPath` 定位 `FilePrefix + yyyyMMdd.csv`（默认 `FilePrefix="小时产量"`）；自动加载近 7 天数据绘制周趋势、当日 24 小时柱状、良率环图。
- 文件缺失会被灰显并记录 `warnings`；动画基于 `CompositionTarget.Rendering` + `Stopwatch`。
- 文件底部内嵌 `AlarmLogCache` / `AlarmLogCompute`（负责缓存近 7 天报警原文与 Top 聚合），仅供 Dashboard 使用，外部不要直接引用。

### 3.4 ProductionBoardView
- 面向电视墙/产线大屏：SkiaSharp + `DispatcherTimer` 每 10 秒刷新当日 CSV，支持日期切换、Top/Bottleneck 列表、周趋势卡片。
- 内建动画（`_anim`）和低良率阈值（默认 95%），缺失 CSV 会在 UI 顶部提示；`ConfigService.ConfigChanged` 会触发全量重载。

### 3.5 AlarmView
- 独立读取 `ConfigService.Current.AlarmLogPath`（按 `yyyy-MM-dd.csv` 命名）的大文件，提供周热度、Top Category、饼图、原始列表；能处理缺失文件与 GB2312/UTF-8 混排。
- 10 秒定时刷新仅在查看当天数据时启用，并支持日期选择器/前后翻页；字体优先 `Microsoft YaHei UI`，内置 fallback 检测。

### 3.6 MachineControl
- 所有按钮通过 `ConfirmThenSendAsync` 弹出危险提示，再构造 `{action,target,params}` JSON POST 至 `http://127.0.0.1:8081`。
- 结果写入 `MainWindow.PostProgramInfo`，日志格式与 MCP `Tool.*` 完全一致，便于人工审计；超时、错误状态会带上 HTTP code 与原始文本。

### 3.7 ConfigView 与 ConfigService
- 通过 UI 编辑 `D:\AppConfig.json`（字段：`CsvRootPath`、`AlarmLogPath`、`ChatURL`、`ChatKey`、`AutoURL`、`AutoKey`），按下保存时会校验必填项并触发 `ConfigService.ConfigChanged`。
- `ConfigService` 与 `Settings.AppConfig` 定义在同一文件内，首次加载会写回默认模板；其 `Current` 实例被 Dashboard/ProductionBoard/AlarmView/Dify 模块订阅。

---

## 4. 服务层与基础设施
- **WorkHttpServer**：`Component/WorkHttpServer.cs` 使用 `HttpListener` 长驻监听 8091；强制 POST JSON，自动兼容 `errorCode`/`ErrorCode` 等大小写；忙碌时返回 429 `{ok:false,busy:true}`。
- **DifyChatAdapter**：封装 Chat API + SSE，支持 token/replace 事件、`/files` 上传、`/stop` 调用；每次对话结束都会把 Q/A、task_id、conversation_id 写入 `D:\Data\AiLog\Chat\*.txt`。
- **DifyWorkflowClient**：`Services/DifyWorkflowStreaming.cs` 用 `SemaphoreSlim` 确保 Workflow 串行执行，`RunAutoAnalysisExclusiveAsync`/`TryStartAutoAnalysisNow` 会在忙碌时直接拒绝。
- **McpServerProcessHost**：负责守护 `McpServer.exe`，可附着已有进程、检测不同目录的“外部实例”、托管退出事件并将状态写入 `MainWindow` 日志。
- **UiCoalescer**：轻量级 Dispatcher 节流工具，用于 UI 线程防抖。

---

## 5. McpServer 组件
### 5.1 Program.cs & 托盘
- 启动时调用 `Base.ReadAppConfig()`，设置 `CsvProductionRepository.ProductionLogPath`、`AlarmCsvTools.WarmLogPath`，并使用 `IoMapRepository.LoadFromXlsx` 导入 IO 映射。
- `AddMcpServer().WithHttpTransport().WithToolsFromAssembly(...)` 自动扫描 `[McpServerTool]`；`app.MapMcp()` + 根路径 `"/"` 返回健康检查字符串。
- `McpTrayContext` 通过 Windows NotifyIcon 提供“重启/退出 MCP”菜单，选择重启会重新拉起当前进程。

### 5.2 Base.cs（配置）
- 固定读取/创建 `D:\MCPAppConfig.json`，字段：`ProductionLogPath`、`WarmLogPath`、`IoMapCsvPath`、`MCPServerIP`（应写成 `http://127.0.0.1:5005` 形式）；缺失会写入默认模板并确保目录存在。

### 5.3 csv_production_tools.cs
- `CsvProductionRepository` 带文件 mtime 缓存、自动分隔符检测、PASS/FAIL 表头别名；默认按 `“小时产量yyyyMMdd.csv”` 命名。
- 暴露 `ProdCsvTools.*`：`GetProductionSummary`、`GetHourlyStats`、`QuickQueryWindow`、`GetSummaryLastHours`、`GetProductionRangeSummary` 等。

### 5.4 AlarmTools.cs
- 合并 `AlarmCsvRepository`（缓存每日报警原文）、`ProdAlarmTools`（产能×报警联表）、`AlarmCsvTools`（报警专用工具）。
- 支持统计每小时报警次数/秒数、低良率时段 Top 报警、Pearson 相关、自然语言解析、报警区间 TopN 及原始明细过滤。

### 5.5 IO/
- `IoMapRepository` 借助 ClosedXML 读取 Excel：目前 `GROUPS` 仅包含第 1 列与第 9 列，各自起始地址分别是 `30000/30001` 与 `30003/30002`，读回基址 `30100/30140`，按 16 点分段。
- `IoMcpTools.IoCommand` 规范化 open/close 请求、写入 `IoWrite`，解析 16 位状态字，返回 `{type:"io.command", ok, status, expected, actual}` JSON。

### 5.6 MCPTools.cs
- `Tool.*` 系列（Start/Pause/Reset/Clear/VisionCalibrate/QuickInspection + 示例 `GetCurrentTime`/`OpenThisPC`）最终调用公共 `SendCommand`。
- `SendCommand` 统一 POST `http://127.0.0.1:8081`，支持自定义超时、读取 JSON Response 并把 `status=="ok"` 判定为成功，其余原样透传；`Tool.PD` 用于生成 params 字典，供 `IoMcpTools` 复用。

---

## 6. Agent 角色与入口
| 角色 | 入口 | 允许工具 / 接口 | 输出要求 |
| --- | --- | --- | --- |
| **Orchestrator** | `AIAssistantView` 默认会话、`WorkHttpServer` Webhook | 不直接触碰设备，只拆解任务并派发给下述 Domain Agent，必要时可读取本地文档 | Markdown 规划/状态同步，遵守白名单 |
| **Production-Analyst** | AIAssistant Quick Prompt「今日产能日报」、Dashboard 查询 | `ProdCsvTools.GetProductionSummary / GetHourlyStats / QuickQueryWindow / GetSummaryLastHours / GetProductionRangeSummary` | 输出 Markdown 表格 + KPI 结论 + 改进建议，缺 CSV 必须列出 `warnings` |
| **Alarm-Analyst** | AIAssistant Quick Prompt「今日报警日报」、Alarm 分析会话 | `ProdAlarmTools.GetHourlyProdWithAlarms / GetAlarmImpactSummary / GetTopAlarmsDuringLowYield / AnalyzeProdAndAlarmsNL`、`AlarmCsvTools.GetAlarmRangeWindowSummary / QueryAlarms` | 需列出低良率小时、Top 报警、相关性，引用样本编号 |
| **IO-Controller** | AIAssistant（经人工批准）或 MachineControl（人工 UI） | `IoMcpTools.IoCommand`（必须读回） + `Tool.StartMachine / PauseMachine / ResetMachine / ClearMachineAlarms / VisionCalibrateMachine / QuickInspectionMachine` | 串行执行，逐步输出结构化 JSON + 人类摘要，失败立即终止 |
| **Knowledge-Ingestion** | AIAssistant 自由会话 | 只读文件解析（不调用 MCP 工具） | 输出 FAQ / 摘要卡片，供班组知识同步 |

---

## 7. 工具白名单与契约
### 7.1 产能 CSV —— `ProdCsvTools`
- `GetProductionSummary(date?)`：返回 `{type:"prod.summary", date, pass, fail, total, yield}`。
- `GetHourlyStats(date?, startHour?, endHour?)`：`{type:"prod.hours", hours:[], subtotal:{}}`，小时 0–23 一律补齐。
- `QuickQueryWindow(date?, window)`：语法糖 `HH-HH`/`HH:MM-HH:MM`，内部仍用 `GetHourlyStats`。
- `GetSummaryLastHours(lastHours=4, until?)`：跨天滚动窗口，附 `warnings`（缺文件、跨日）。
- `GetProductionRangeSummary(startDate, endDate)`：`days = [{date, pass, fail, total, yield}]`，额外附 `markdown` 片段方便 UI 直接展示。
- 所有函数在文件缺失时返回 `{type:"error", message:"未找到 CSV"}`；Agent 必须据实汇报。

### 7.2 产能 × 报警 —— `ProdAlarmTools`
- `GetHourlyProdWithAlarms`：合并单日 24 小时的产能 + 报警次数/秒数 + Top1 报警代码。
- `GetAlarmImpactSummary`：返回 `byHour`/`byDay` 聚合、Pearson 相关系数、低良率小时 `rows`、Top 报警列表及 `warnings`。
- `GetTopAlarmsDuringLowYield`：筛出 `yield<threshold` 的小时，统计 Top 报警代码与命中次数。
- `AnalyzeProdAndAlarmsNL`：解析自然语言中的日期/时间窗，再调用 `GetAlarmImpactSummary`。
- 缺任意 CSV 会通过 `warnings` 提醒，但仍尽量输出其他日期的数据。

### 7.3 报警 CSV —— `AlarmCsvTools`
- `AlarmCsvTools.WarmLogPath` 来自 `D:\MCPAppConfig.json`，文件按 `yyyy-MM-dd.csv` 命名。
- `GetAlarmRangeWindowSummary(startDate, endDate, window?, topN=10, sortBy="duration")`：输出 `{type:"alarm.range.summary", totals, byCategory, top}`，window 支持 `HH-HH` / `HH:MM-HH:MM`。
- `QueryAlarms(startDate, endDate, code?, keyword?, window?, take=50)`：返回 `{type:"alarm.query", rows:[], totalCount, durationSeconds}`，rows 包含开始时间、代码、内容、秒数、源文件名。
- 输入非法会直接返回 `{type:"error", message:"..."}`，Agent 必须阻断写操作。

### 7.4 IO 工具 —— `IoMcpTools` + `IoMapRepository`
- `IoMapRepository.LoadFromXlsx(ioCsv)` 详见 `MCPAppConfig.json`，每 16 点共用一组 `CheckAddress`，`CheckIndex` 范围 1..16（MSB）。
- `IoMcpTools.IoCommand(ioName, op)`：
  - `op` 仅允许 open/close（含中英 alias），拒绝 toggle/空值。
  - `Tool.SendCommand("IoWrite", ...)` 始终写 `on`，必要时可传 `checkAddress` 做读回；若 IO 表缺失、地址空、或命令超时，直接返回 `type:"error"` 或 `status:"timeout"`。
  - 正常完成时返回 `{type:"io.command", ok, status: ok|mismatch|readback_unavailable, expected, actual, address}`。

### 7.5 设备命令 —— `Tool.*`
- `StartMachine/PauseMachine/ResetMachine/ClearMachineAlarms/VisionCalibrateMachine/QuickInspectionMachine` 与 UI 相同：POST `http://127.0.0.1:8081`，等待 `{status:"ok"}`。
- `Tool.SendCommand` 默认 5000 ms 超时；如需其他动作（如 `IoWrite`）由上层指定 `actionName`。
- 所有 `Tool.*` 调用必须串行执行（Agent 完成一条命令后才能发下一条），并在日志中注明人类可读摘要。

---

## 8. Workflow / Webhook 契约
- `WorkHttpServer` 默认监听 `http://127.0.0.1:8091/`，必须提前执行 `netsh http add urlacl url=http://127.0.0.1:8091/ user=<DOMAIN\User>` 授权。
- 请求示例：
  ```json
  {
    "errorCode": "E203.1",
    "prompt": "上料后频繁卡料",
    "machineCode": "T66-01",
    "workflowId": "wf_123",
    "onlyMajorNodes": true
  }
  ```
- 成功返回 `{"ok":true,"busy":false,"msg":"accepted"}`，所有输出直接注入 `AIAssistantView.GlobalInstance`。
- 若 `_gate` 被占用，立即返回 `429`；调用方需稍后重试或等人工终止当前 Workflow。

---

## 9. 配置与目录
- **`D:\AppConfig.json`（WPF）**：`CsvRootPath`、`AlarmLogPath`、`ChatURL`、`ChatKey`、`AutoURL`、`AutoKey`。由 `ConfigService` 负责读写并广播 `ConfigChanged`，缺失时自动写入默认模板。
- **`D:\MCPAppConfig.json`（MCP）**：`ProductionLogPath`、`WarmLogPath`、`IoMapCsvPath`、`MCPServerIP`。必须提供可写目录，并确保 `MCPServerIP` 包含 `http://` 前缀；修改后需重启 MCP。
- **日志目录**：`D:\Data\AiLog\Chat\yyyy-MM-dd.txt`（聊天） / `D:\Data\AiLog\Auto\yyyy-MM-dd.txt`（Workflow）；`MainWindow` UI 中仅缓存最近 200 条。
- **McpServer 可执行文件**：`EW-Assistant` 输出目录下需存在 `McpServer\McpServer.exe`，供 `McpServerProcessHost` 自动启动/附着；请在发布后复制 net8.0 输出。

---

## 10. 构建与运行
1. **编译 MCP**  
   ```powershell
   dotnet publish McpServer/McpServer.csproj -c Release -o ..\EW-Assistant\bin\Release\net48\McpServer
   ```  
   或发布到任意文件夹再手动复制；确保 `McpServer.exe`、`Tray` 资源与 `appsettings*.json` 均在该目录。
2. **（可选）自动复制**：可在 `EW-Assistant.csproj` 添加 `CopyMcpOnBuild` Target，将 `McpServer\bin\<Config>\net8.0\**` 同步到 `$(OutDir)McpServer\%(RecursiveDir)`。
3. **编译 WPF**：使用 VS 或 `msbuild EW-Assistant.sln /p:Configuration=Release /m`，目标平台 AnyCPU（Debug 默认 x64）。
4. **运行顺序**：先启动 `McpServer.exe`（或依赖 `MainWindow` 自动拉起），再运行 `EW-Assistant.exe`；若 MCP 已在其他目录运行，`McpServerProcessHost` 会在日志中告警，需要人工确认是否关闭外部实例。

---

## 11. 安全 / 审计 / 并发
- `DifyWorkflowClient` 使用 `SemaphoreSlim` 串行化自动化工作流，防止多个 Workflow 同时操作 MCP 工具。
- `WorkHttpServer` 在忙碌时直接返回 429，避免外部系统重复触发；所有 webhook 请求与响应都会写入 `ProgramInfo`。
- `MachineControl`、`Tool.*`、`IoMcpTools.*` 统一通过 `MainWindow.PostProgramInfo` 记录命令、状态、异常；人机共用同一个 8081 API，确保审计线索齐全。
- 每个写操作都需要人工确认：UI 按钮有弹窗，Agent 必须在 System Prompt 中要求人工批准（Quick Prompt 已包含提醒）。
- `IoMcpTools` 在写入后立即解析 16 位读回，`status!="ok"` 时必须停止后续操作；若无读回能力则返回 `readback_unavailable`。
- 日志与配置均保存在本地磁盘，不得由 Agent 擅自上传；如遇缺失 CSV/报警文件，MCP 会返回 `type:"error"`，Agent 只能输出只读解释。

---

## 12. 健康检查与排障
| 项目 | 检查方式 | 常见问题 & 处置 |
| --- | --- | --- |
| MCP 服务 | 浏览 `http://<MCPServerIP>/` 或调用 `Tool.GetCurrentTime` | 端口占用/URL 缺少 `http://`：更新 `D:\MCPAppConfig.json` 并重启 |
| MCP 托盘 / Host | 查看系统托盘是否有 “MCP Server 运行” 图标；或查 `MainWindow` 日志出现 “MCP Server 已启动 PID=xxx” | 若提示“检测到其他目录的 MCP Server”，需先关闭外部实例 |
| WorkHttpServer | `curl -X POST http://127.0.0.1:8091/ -d '{"prompt":"test"}'` | 返回 429 ⇒ Workflow 正在运行；若提示 urlacl，需以管理员执行 `netsh http add urlacl ...` |
| 产能 CSV | `ProdCsvTools.GetProductionSummary` | `message: 未找到 CSV` ⇒ 检查 `CsvRootPath`、文件命名（无扩展、含 BOM） |
| 报警 CSV | `AlarmCsvTools.GetAlarmRangeWindowSummary` | `warnings` / `type:"error"` ⇒ 核对 `AlarmLogPath` 或文件编码（GB2312） |
| IO 映射 | `IoMcpTools.IoCommand(ioName="...")` | 返回 “IO 映射未加载” ⇒ 检查 `IoMapCsvPath`、Excel 列布局（当前只解析第 1/9 列），重启 MCP |
| Dify Chat/Workflow | 在 AIAssistant 中点击 Quick Prompt | 401/403 ⇒ 检查 `ChatKey/AutoKey`；UI 无输出 ⇒ 查看 `D:\Data\AiLog\Chat` 是否写入 |

---

## 13. 示例提示词
- 「生成 **今天 0–24 点** 的产能日报，缺 CSV 要写 `warnings`，只允许 Markdown。」→ 触发 Quick Prompt，调用 `GetHourlyStats` + `GetProductionSummary`。
- 「输出 **今天报警日报**，列出报警 Top、低良率时段、平均响应时间，禁止 JSON。」→ Quick Prompt 调用 `GetHourlyProdWithAlarms` + `GetAlarmImpactSummary`。
- 「最近 48 小时 10:00–14:00 的产能与报警相关性？列出 <93% 的小时及 Top 报警。」→ `ProdAlarmTools.GetAlarmImpactSummary`（window 参数）。
- 「查询 11 月 5–9 日报警 Top10（按 duration）并附代码 `E203.1` 样本。」→ `AlarmCsvTools.GetAlarmRangeWindowSummary` + `QueryAlarms`。
- 「清除报警 → 复位 → 启动，逐步执行并确认读回。」→ 先人工确认，再串行调用 `Tool.ClearMachineAlarms` / `Tool.ResetMachine` / `Tool.StartMachine`，必要时穿插 `IoMcpTools.IoCommand`。

---

## 14. 维护提醒
- 新增/改名 MCP 工具、配置字段、JSON schema、Quick Prompt 时务必同步更新本文档与 UI 描述，确保 Agent 白名单与 Dify 配置一致。
- `AlarmLogCache` / `AlarmLogCompute` 仅存在于 `DashboardView.xaml.cs`，不要直接在其他模块复用；如需共享请先抽象接口。
- `ProductionBoardView` 与 `ProdCsvTools` 使用同一 CSV 解析逻辑，产线 CSV 格式有改动时需要同时调整两个视图与 MCP 仓库。
- 发布版本前至少保留一组成功日志 + 一组失败/越权日志，验证写操作确实触发审计与回退；变更设备 API 地址时，请同步更新 `MCPTools.cs` 与 `Views/MachineControl.xaml.cs`。
