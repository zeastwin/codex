# StressTest 使用说明

## 功能概述
StressTest 用于模拟多台 EW-Assistant 并发调用 Dify Workflow（SSE）接口，评估响应耗时、吞吐与错误率。

## 参数含义
- 压测时长（秒）：本次压测持续时间，达到时长后自动停止。
- 模拟设备数：并发虚拟设备数量，每台设备单通道串行请求。
- 升压：在前 X 秒内线性提升到目标并发数。
- 思考时间：每次请求完成后等待 1s + 0~1s 抖动。

## 指标说明
- RPS：每秒请求数。
- P95 耗时：响应耗时的 95 分位。
- TTFB：首包延迟。
- 错误率：失败/取消请求占比。

## 配置与依赖
- 读取配置：`D:\AppConfig.json` 的 `URL` 与 `AutoKey`。
- 接口地址：`{URL}/workflows/run`（SSE）。

## 报告导出
- 报告目录：`D:\DataAI\StressTest`（不可用时落到程序目录 `Reports`）。
- 生成文件：`summary.json`、`timeseries.csv`、`errors.csv`、`charts.png`。

## 注意事项
- 请确保 Dify 服务可达且 Key 有效。
- 压测期间避免同时用同一 Key 执行其他高负载任务。
