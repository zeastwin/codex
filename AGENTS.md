# AGENTS.md — EW-Assistant (WPF) × McpServer (.NET 8)
## 语言约定
- 所有代码注释、技术文档、提交说明一律使用**简体中文**。
- 在 VS Code “Finished working / 工作日志” 面板中，描述自己执行的步骤时，优先使用简体中文，
  不要再使用英文步骤标题（如 “Planning Excel export method update”）。
- 代码中的标识符、API 名称、JSON 字段保持英文；用户可见字符串按 UI 设计需求呈现。
- 面向开发者的说明、分析、总结全部用简体中文输出。
- WPF 必须 net48、禁止 C# 8+
环境约束（请严格遵守）：
- OS: Windows 11
- 终端: PowerShell
- 前端: WPF (.NET Framework 4.8)，项目在 EW-Assistant.sln 中
  - 不要用 `dotnet build` 构建 WPF 项目，要用 `msbuild`
- 后端: MCP Server (.NET 8)，项目是 .\EW-Assistant-MCP\McpServer\McpServer.csproj
  - 可以用 `dotnet build` / `dotnet run`
- 所有路径使用 Windows 风格：C:\ 或 .\ 相对路径
  - 禁止输出 /home、~/ 之类 Linux 路径
---

## 1. 系统概览
- `EW-Assistant` 是 .NET Framework 4.8.1 的 WPF Shell，`MainWindow` 缓存多视图、维护底部 `ProgramInfo` 日志，并在启动时拉起 `WorkHttpServer`（8091）和 `McpServerProcessHost`（托管 `McpServer.exe`）。
- `McpServer` 是 .NET 8 的本地 MCP 工具网关，使用 `ModelContextProtocol.Server` 对外暴露 CSV / 报警 / IO / 设备命令工具；输出统一 JSON，供 Dify Workflow/Chat 工具链消费。
