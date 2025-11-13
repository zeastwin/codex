# AGENTS.md — EW-Assistant (WPF) × McpServer (.NET 8)
## 语言风格

- 所有代码注释、文档说明、提交说明（commit message）默认使用**简体中文**。
- 代码中的标识符、API 名称保持英文。
- 给开发者的解释、分析、总结，也请用简体中文输出。

> 本文说明仓库里的 Agent 设计、工具白名单、调用契约、安全边界与运行调试要点。  
> **特别提醒**：`EW-Assistant/Services/AlarmLogCache.cs` 与 `AlarmLogCompute.cs` 只服务 `DashboardView`（周/月统计 & Top 卡片），完全不参与 `AlarmView` 的绘制或数据链路。

---

## 0. 仓库结构（按实际代码）

```
<repo-root>/
├─ EW-Assistant/                     # .NET Framework 4.8.1 WPF UI Shell
│  ├─ Component/
│  │  ├─ UiCoalescer.cs              # UI 合流/节流 + Dispatcher 安全更新
│  │  └─ WorkHttpServer.cs           # HttpListener（8091）→ DifyWorkflowClient 触发入口
│  ├─ Services/
│  │  ├─ AlarmLogCache.cs            # Dashboard 专用报警日志缓存（逐日原文）
│  │  ├─ AlarmLogCompute.cs          # Dashboard 专用聚合/Top 计算
│  │  └─ DifyWorkflowStreaming.cs    # Dify Workflow SSE 适配、串行执行闸
│  ├─ Views/
│  │  ├─ AIAssistantView.xaml(.cs)   # 对话+Markdown（工具入口 & Quick Prompt）
│  │  ├─ DashboardView.xaml(.cs)     # 日/周/月产量、报警卡片（SkiaSharp 渲染）
│  │  ├─ AlarmView.xaml(.cs)         # 报警可视化（独立数据管线）
│  │  ├─ MachineControl.xaml(.cs)    # 机台手动控制（调用本地 8081 API）
│  │  └─ ConfigView.xaml(.cs)        # `D:\AppConfig.json` 可视化编辑
│  ├─ DifyChatAdapter.cs             # Chat API + SSE 适配 & `D:\Data\AiLog\Chat`
│  └─ MainWindow.xaml(.cs)           # Shell、路由、WorkHttpServer 生命周期
└─ McpServer/                        # .NET 8 MCP 工具网关
   ├─ csv_production_tools.cs        # `[McpServerToolType] ProdCsvTools`（产能只读）
   ├─ AlarmTools.cs                  # ProdAlarmTools + AlarmCsvTools（报警分析）
   ├─ IO/
   │  ├─ IoMapRepository.cs          # 读取 Excel → IO Name/Address/CheckIndex 映射
   │  └─ IoMcpTools.cs               # `IoCommand`（含读回、JSON 封装）
   ├─ MCPTools.cs                    # `[McpServerToolType] Tool.*`（Start/Pause/...）
   ├─ Base.cs / Program.cs           # `D:\MCPAppConfig.json`、HostBuilder、路由
   ├─ appsettings*.json              # ASP.NET Core 默认配置
   └─ McpServer.sln / McpServer.csproj
```

---

## 1. 架构与职责

```mermaid
flowchart LR
subgraph UI[WPF UI]
  Dash[DashboardView]
  Assistant[AIAssistantView]
  Alarm[AlarmView]
  Machine[MachineControl]
end
Assistant -- SSE/HTTP --> Dify[Dify Workflow / Agents]
Dify -- Tool JSON --> MCP[McpServer (.NET 8, MCP)]
MCP -- 只读/受控写 --> Data[(CSV 产能 / 报警日志 / IO 映射)]
MCP -- HTTP 桥 --> Equip[本地设备 API @8081]
Machine -- Confirmed POST --> Equip
WorkHook[WorkHttpServer @8091] -- Trigger JSON --> DifyWorkflowClient --> Assistant
```

- **WPF Shell (`MainWindow`)**：缓存视图、统一的 `PostProgramInfo` 日志面板，并在启动时常驻 `WorkHttpServer`（默认 `http://127.0.0.1:8091/`）。
- **AIAssistantView**：SSE + Markdown 渲染、`GlobalInstance` 提供给 `WorkHttpServer`，并包含若干 Quick Prompt（例如「今日日报」会明确列出要调用的 MCP 工具和格式要求）。
- **DashboardView**：依赖 `ConfigService.Current.CsvRootPath` 与 `AlarmLogCache/Compute`，做本地渲染，不会走 MCP。
- **MachineControl**：仍然使用 `http://127.0.0.1:8081` 的内部 HTTP API，按钮必须二次确认，结果写入 `MainWindow` 日志；Agent 控制则通过 MCP 的 `Tool.*`/`IoMcpTools.*` 间接访问同一接口。
- **DifyWorkflowClient + WorkHttpServer**：用 `SemaphoreSlim` 限制任意时刻仅一条自动化 Workflow 在跑；`WorkHttpServer` 把 8091 端口上的 JSON POST 转交给 `DifyWorkflowClient.TryStartAutoAnalysisNow`（忙则 429）。
- **McpServer (.NET 8)**：通过 `ModelContextProtocol.Server` 暴露工具；`Program.cs` 启动时读取 `D:\MCPAppConfig.json`，设置 `CsvProductionRepository.ProductionLogPath`、`AlarmCsvTools.WarmLogPath`、`IoMapRepository.LoadFromXlsx(...)` 等，然后 `app.Run(appCfg.MCPServerIP)`。

---

## 2. WPF UI 模块与数据来源

- **AIAssistantView**  
  - 维护消息列表（Markdown ↔ 流式文本），Quick Prompt（如 `QuickReportBtn`）直接把「只允许调用哪些工具、按何格式输出」写进提示词。  
  - 通过 `DifyChatAdapter` 连接 `ConfigService.Current.ChatURL/ChatKey`（SSE），并把完整对话日志落盘 `D:\Data\AiLog\Chat\yyyy-MM-dd.txt`。  
  - 通过 `DifyWorkflowStreaming`/`DifyWorkflowClient` 调用 Auto Workflow（`ConfigService.Current.AutoURL/AutoKey`），执行过程写入 `D:\Data\AiLog\Auto\yyyy-MM-dd.txt`。

- **DashboardView**  
  - `FilePrefix` 默认为 `"小时产量"`，文件名形如 `小时产量20251112.csv`。  
  - 加载后立即 `AlarmLogCache.LoadRecent(ConfigService.Current.AlarmLogPath, days: 7)`，并用 `AlarmLogCompute` 做每日卡片/周 Top。**仍与 AlarmView 解耦**。

- **AlarmView**  
  - 拥有独立的数据拉取/渲染逻辑（可直接展示大文件、字体 fallback，默认优先 `Microsoft YaHei UI`）。不依赖 `AlarmLogCache`。

- **MachineControl**  
  - `SendCommand(action, actionName, args)` 直接对 `http://127.0.0.1:8081` POST，且所有按钮都走 `ConfirmThenSendAsync`（弹框 + 执行日志）。  
  - 结果写回 `MainWindow.PostProgramInfo`，供人工审计；Agent 版本的同款操作由 MCP `Tool.*` 暴露。

- **ConfigView + ConfigService**  
  - 图形化编辑 `D:\AppConfig.json`。字段：`CsvRootPath`、`AlarmLogPath`、`ChatURL`、`ChatKey`、`AutoURL`、`AutoKey`。  
  - 保存/重载会触发 `ConfigService.ConfigChanged`，并在缺失必填项时提示。

---

## 3. Agent 角色设定（建议）

| Agent | UI/入口 | 允许的工具 / 接口 | 输出 & 限制 |
| --- | --- | --- | --- |
| **Orchestrator** | `AIAssistantView`（默认对话）或 `WorkHttpServer` webhook | 不直接触碰设备；只拆解任务、把子任务派发给下述 Domain Agent；必要时可调用纯文档解析/检索工具 | Markdown 规划/状态同步；禁止绕过白名单 |
| **Production‑Analyst** | `AIAssistantView`、`DashboardView` Quick Prompt | `ProdCsvTools.GetProductionSummary / GetHourlyStats / QuickQueryWindow / GetSummaryLastHours / GetProductionRangeSummary` | 生成 Markdown 表格 +结论+建议，并引用缺失文件/警告 |
| **Alarm‑Analyst** | 同上 | `ProdAlarmTools.GetHourlyProdWithAlarms / GetAlarmImpactSummary / GetTopAlarmsDuringLowYield / AnalyzeProdAndAlarmsNL`；`AlarmCsvTools.GetAlarmRangeWindowSummary / QueryAlarms` | 输出 TopN、低良率小时列表、相关性系数、引用样本 |
| **IO‑Controller** | `MachineControl`（人工）/ `AIAssistantView`（Agent） | `IoMcpTools.IoCommand`（必须读回）；`Tool.StartMachine / PauseMachine / ResetMachine / ClearMachineAlarms / VisionCalibrateMachine / QuickInspectionMachine` | 串行执行；每一步都返回结构化 JSON，并附人类摘要；失败立即停止 |
| **Knowledge‑Ingestion（可选）** | `AIAssistantView` | 只读文件解析/FAQ 生成，不访问 MCP | 输出 FAQ/摘要卡，用于知识同步 |

---

## 4. 工具白名单 & 契约（依据实际实现）

### 4.1 产能 CSV —— `ProdCsvTools`（`csv_production_tools.cs`）

- **底层仓库**：`CsvProductionRepository`  
  - `ProductionLogPath` 与 `DailyFilePattern = "小时产量yyyyMMdd'.csv'"` 来自 `D:\MCPAppConfig.json`。  
  - 自动检测分隔符（逗号/分号/TAB），并识别 `PASS/良品/良率pass`、`FAIL/不良/NG` 等表头别名。  
  - 带内存缓存（基于文件 mtime），避免重复 IO。

- **工具列表**  
  - `GetProductionSummary(date?)` → `type = "prod.summary"`，返回 `{date, pass, fail, total, yield}`。  
  - `GetHourlyStats(date?, startHour?, endHour?)` → `type = "prod.hours"`，包含 `hours = [{hour, pass, fail, total, yield}]` 与区间小计。  
  - `QuickQueryWindow(date?, window)` → 语法糖，调用 `GetHourlyStats` 并限制时间段（`10-14` 或 `10:00-14:00`）。  
  - `GetSummaryLastHours(lastHours=4, until?)` → `type = "prod.last_hours"`，跨天按小时滚动窗口，附 `warnings`（缺 CSV）。  
  - `GetProductionRangeSummary(startDate, endDate)` → `type = "prod.range.summary"`，输出逐日 `days` 与 `warnings`，自带 Markdown 摘要（供 UI 直接展示）。

### 4.2 产能 × 报警联表 —— `ProdAlarmTools`（`AlarmTools.cs`）

- 依赖 `CsvProductionRepository` + `AlarmCsvRepository`（均使用配置路径）。缺任一文件会记录 `warnings`，但尽量返回其他信息。
- 主要方法：
  - `GetHourlyProdWithAlarms(date, startHour?, endHour?)` → `type = "prod.hourly.with.alarms"`，每小时提供 `pass/fail/total/yield`、报警次数/秒数以及 Top1 报警代码。  
  - `GetAlarmImpactSummary(startDate, endDate, window?, lowYieldThreshold=95)` → `type = "prod.alarm.impact"`：返回报警秒数与产量/良率的 Pearson 相关系数、逐小时负载、低良率小时 `rows`、Top 报警代码、`byDay` 聚合以及 `warnings`。  
  - `GetTopAlarmsDuringLowYield(startDate, endDate, threshold=95, window?)` → `type = "prod.low_yield.top_alarms"`，筛出低良率小时，汇总 `rows` + 报警 Top。  
  - `AnalyzeProdAndAlarmsNL(text, lowYieldThreshold?)` → 解析自然语言日期/时段，再调用 `GetAlarmImpactSummary`。

### 4.3 报警 CSV 专用 —— `AlarmCsvTools`（同文件）

- `AlarmCsvTools.WarmLogPath` 在 `Program.cs` 中赋值为 `appCfg.WarmLogPath`（默认 `D:\`，可在 `MCPAppConfig.json` 自定义至 `D:\Data\Alarms` 等）。
- 公开工具：
  - `GetAlarmRangeWindowSummary(startDate, endDate, window?, topN=10, sortBy="duration")`  
    - 验证输入格式（`yyyy-MM-dd` + `HH-HH` / `HH:MM-HH:MM`），读区间内所有 `yyyy-MM-dd.csv`，输出 `{type="alarm.range.summary", totals, byCategory, top}`。  
  - `QueryAlarms(startDate, endDate, code?, keyword?, window?, take=50)`  
    - 读取区间报警明细后按代码/关键字过滤，返回 `{type="alarm.query", rows:[...], totalCount, durationSeconds}`，供 Agent 展示引用样本。

### 4.4 IO 工具 —— `IoMcpTools` + `IoMapRepository`

- `IoMapRepository.LoadFromXlsx(ioCsv)` 在 `Program.cs` 启动时执行，Excel 列表需按照 `GROUPS` 中定义的列号（示例：第 1 列是名称，第 9 列是另一组），自动计算 `CloseAddress`/`OpenAddress` 与 `CheckAddress + CheckIndex`。  
- `IoMcpTools.IoCommand(ioName, op)`  
  - `op` 只接受 open/close（含中/英 alias）；拒绝 toggle。  
  - 通过 `Tool.SendCommand("IoWrite", ...)` 写入地址（始终写 `on`），返回 JSON：  
    ```json
    {
      "type": "io.command",
      "ok": true,
      "status": "ok | mismatch | readback_unavailable",
      "io": "Clamp#03",
      "intent": "open",
      "expected": true,
      "actual": true,
      "address": "30001"
    }
    ```  
  - 出现超时/异常会返回 `ok=false` 以及 `status="timeout"` 或 `message`。

### 4.5 设备命令 —— `Tool.*`（`MCPTools.cs`）

- `StartMachine` / `PauseMachine` / `ResetMachine` / `ClearMachineAlarms` / `VisionCalibrateMachine` / `QuickInspectionMachine`（以及示例 `GetCurrentTime`、`OpenThisPC`）。  
- 最终都调用 `SendCommand(action, actionName, args = Tool.PD(...), target?, timeoutMs=5000)`，请求体：
  ```json
  {
    "action": "StartMachine",
    "target": { "station": "T66-01", "device": "IO1" },
    "params": { "mode": "auto" }
  }
  ```
  - POST 至 `http://127.0.0.1:8081`，解析回复 JSON（若 `status=="ok"` 视为成功，否则把原文返回）。
  - 所有 Agent 写入都必须串行执行：上一条命令未拿到结果不得发送下一条。
- WPF `MachineControl` 与 `IoMcpTools` 共用同一设备 API，因此在改动 Base URL/接口时必须同步更新两个位置。

### 4.6 WorkHttpServer（本地 webhook）契约

- 监听地址：`http://127.0.0.1:8091/`（`MainWindow` 启动时拉起，可改 `prefix`）。需要事先执行 `netsh http add urlacl url=http://127.0.0.1:8091/ user=<域\用户>`。  
- 只接受 `POST`，JSON 体示例：
  ```json
  {
    "errorCode": "E203.1",
    "prompt": "上料后频繁卡料",
    "machineCode": "T66-01",
    "workflowId": "wf_123",
    "onlyMajorNodes": true
  }
  ```
  - 内部会抓取 `AIAssistantView.GlobalInstance` 作为输出承载；若 `_gate` 已被占用，则返回 `429 { ok:false, busy:true }`。  
  - 调用成功仅返回 `"accepted"`，全部内容输出都走 AIAssistant UI。

---

## 5. 配置、构建与运行

1. **配置文件**  
   - `EW_Assistant.Services.ConfigService.FilePath = D:\AppConfig.json`  
     - 需要手动创建/编辑，至少填好 `CsvRootPath`（小时产量 CSV 目录）、`AlarmLogPath`（报警 `yyyy-MM-dd.csv`）、`ChatURL/ChatKey`（Dify Chat API）、`AutoURL/AutoKey`（Workflow API）。  
   - `McpServer.Base.ReadAppConfig()` 固定读取 `D:\MCPAppConfig.json`，字段：`ProductionLogPath`、`WarmLogPath`（报警 CSV 根目录）、`IoMapCsvPath`（Excel 映射）、`MCPServerIP`（例如 `"http://127.0.0.1:5005"`）。缺失会写入默认模板并保证文件夹存在。

2. **构建 & 拷贝**  
   - `McpServer` 目标框架 `net8.0`，编译后需把 `bin/<Config>/net8.0/**` 同步到 WPF 输出目录的 `McpServer/`。可在 `EW-Assistant.csproj` 加入：
     ```xml
     <Target Name="CopyMcpOnBuild" AfterTargets="Build">
       <ItemGroup>
         <McpFiles Include="$(SolutionDir)McpServer\bin\$(Configuration)\net8.0\**\*.*" />
       </ItemGroup>
       <MakeDir Directories="$(OutDir)McpServer" />
       <Copy SourceFiles="@(McpFiles)"
             DestinationFolder="$(OutDir)McpServer\%(RecursiveDir)"
             SkipUnchangedFiles="true" />
     </Target>
     ```

3. **运行顺序**  
   - 先启 `dotnet run --project McpServer/McpServer.csproj /property:Configuration=Release`（或发布自带服务）。  
   - 再开 `EW-Assistant.exe`，它会自动启动 8091 webhook 并允许手工操作。  
   - 若要在 UI 中一键启动 MCP，可在 `MainWindow`/`Configuration` 里增加检测逻辑（目前未实现）。

4. **本地设备 API**  
   - `MachineControl` 与 MCP `Tool.SendCommand` 均把命令发往 `http://127.0.0.1:8081`，并期望返回 `{status:"ok", message:"..."}`。更换端口/协议时必须同步修改 `MCPTools.cs` 与 `MachineControl.xaml.cs`。

---

## 6. 安全、审计与并发约束

- **串行执行**  
  - `DifyWorkflowClient` 使用 `_gate (SemaphoreSlim)`，保证任何时刻只有一条 Workflow 在跑；`WorkHttpServer` 忙时返回 429。  
  - `IoMcpTools.IoCommand` 在 `Tool.SendCommand("IoWrite")` 之后立即解析读回位，如无 readback 视为失败。

- **人工确认 & 审批**  
  - `MachineControl` 的所有按钮都调用 `ConfirmThenSendAsync(...)`，默认按钮文本/确认文案已经写明风险。  
  - Agent 写操作（`Tool.*` / `IoMcpTools`）必须在 UI 二次确认后才能真的执行。

- **日志与追溯**  
  - `DifyChatAdapter.WriteLog` → `D:\Data\AiLog\Chat\yyyy-MM-dd.txt`（记录每次 SSE 会话）。  
  - `DifyWorkflowStreaming` → `D:\Data\AiLog\Auto\yyyy-MM-dd.txt`。  
  - `MainWindow.PostProgramInfo` 在 UI 底部滚动区域保留最近 200 条事件，便于人工审查。

- **失败处理**  
  - MCP 工具若遇到缺 CSV / 报警文件 / IO 映射，统一返回 `{type:"error", where:"...", message:"..."}`，Agent 必须将错误转换成「只读解释 + 人工建议」，而不是继续写操作。

---

## 7. 健康检查与常见问题

| 项目 | 检查方式 | 常见问题 & 处理 |
| --- | --- | --- |
| MCP Host | 访问 `http://<MCPServerIP>/` 应返回 `"MCP Server (local bridge) running on localhost"`；或使用 MCP 客户端调用 `Tool.GetCurrentTime` | 若端口占用，`app.Run` 会抛异常；更新 `MCPAppConfig.json` → 重启 |
| 产能 CSV | 调用 `ProdCsvTools.GetProductionSummary` | `message: 未找到当天 CSV` → 检查 `ProductionLogPath`、文件名 `小时产量yyyyMMdd.csv` 是否齐全 |
| 报警 CSV | 调用 `ProdAlarmTools.GetHourlyProdWithAlarms` 或 `AlarmCsvTools.GetAlarmRangeWindowSummary` | 缺文件会写入 `warnings`；确认 `WarmLogPath` 指向 `yyyy-MM-dd.csv` 文件夹 |
| IO 映射 | `IoMcpTools.IoCommand(ioName="...")` | 如果返回 `IO 映射未加载` → 检查 `D:\MCPAppConfig.json` 的 `IoMapCsvPath` 和 Excel 格式；需重启 MCP 重新加载 |
| Workflow Hook | `curl -X POST http://127.0.0.1:8091/ -d '{"prompt":"..."}'` | 报 `busy` ⇒ UI 正在跑自动化；等 `_gate` 释放或调用 `RunAutoAnalysisExclusiveAsync` 主动终止 |
| SSE / Chat | 在 ConfigView 填好 `ChatURL/ChatKey`，点击 Quick Prompt 验证 | 若返回 401/403，检查 Dify Key；若 UI 无输出，查看 `D:\Data\AiLog\Chat` 是否写入 |

---

## 8. 扩展/新增 Agent 的步骤

1. **MCP 层**：在 `McpServer` 新增 `[McpServerTool]`（优先只读），并更新 `MCPAppConfig` / Excel 映射等依赖。  
2. **Dify 配置**：为新 Agent 定制 System Prompt & 工具白名单，只授予需用的 `ProdCsvTools.*`、`ProdAlarmTools.*` 等。  
3. **WPF 入口**：在 `AIAssistantView` 或其他视图增加按钮/模板，把 Agent 名称/白名单注入提示词。  
4. **安全钩子**：写操作需走 UI 确认 + 日志；必要时在 `MachineControl` 或 MCP 工具里补充双读回/回滚逻辑。  
5. **验证**：提交前附至少 1 组成功日志 + 1 组失败/越权日志（证明安全边界仍生效），并更新本文的白名单/契约章节。

---

## 9. 示例用户语句

- 「生成 **今天 0–24 点** 的产能日报，缺 CSV 要灰显并写 `warnings`，不要输出 JSON。」→ 触发 Quick Prompt，使用 `ProdCsvTools.GetHourlyStats` + `GetProductionSummary`。  
- 「最近 2 天 10:00–14:00 的产出与报警相关性？列出低于 93% 的小时及 Top 报警。」→ `ProdAlarmTools.GetAlarmImpactSummary`。  
- 「查询 11 月 5–9 日的报警 Top10（按 duration），以及代码 `E203.1` 的原始记录。」→ 组合 `AlarmCsvTools.GetAlarmRangeWindowSummary` + `QueryAlarms`。  
- 「清除报警 → 复位 → 启动，逐步执行并确认读回。」→ `IoMcpTools.IoCommand`（若涉及 IO）+ `Tool.ClearMachineAlarms/ResetMachine/StartMachine`，每步等成功再继续。

---

## 10. 维护提醒

- 任何新增/改名的 MCP 工具、配置字段、JSON 字段，都要同步更新 **§4 工具白名单** 与相关 Quick Prompt，确保 UI & Agent 描述一致。  
- PR 需附至少 1 组正向日志 + 1 组失败/拦截日志，验证写操作确实触发审计与回退。  
- 再次强调：`AlarmLogCache` / `AlarmLogCompute` 仅供 `DashboardView` 使用；`AlarmView` 走自身逻辑，若后续想复用请先抽象 `IAlarmReadOnlyStore` 并在 UI 层注入。

