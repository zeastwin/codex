using EW_Assistant.Io;
using McpServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.Text;
using static McpServer.Base;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var builder = WebApplication.CreateBuilder(args);

// 启动时读取配置
var appCfg = ReadAppConfig();
CsvProductionRepository.ProductionLogPath = appCfg.ProductionLogPath;
AlarmCsvTools.WarmLogPath = appCfg.WarmLogPath;

// —— 启动即加载（只读内存）——
var ioCsv = appCfg.IoMapCsvPath;
try
{
    IoMapRepository.LoadFromXlsx(ioCsv);
}
catch (Exception ex)
{
}


builder.Services
    .AddMcpServer()
    .WithHttpTransport()  
    .WithToolsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

app.MapMcp();

app.MapGet("/", () => "MCP Server (local bridge) running on localhost");

app.Run(appCfg.MCPServerIP); 
//app.Run("http://192.168.200.10:5001");
