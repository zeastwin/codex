## 预警闭环与工单状态

本文档说明预警工单的指纹生成与状态流转规则，便于排查和扩展。

### 指纹与去重
- 指纹格式：`RuleId|Type|WindowStart|Dimension`，其中 WindowStart 按小时取整（yyyyMMddHH），Dimension 取指标名或 `default`。
- 同一指纹在同一刷新周期仅保留一条工单；忽略中的工单（IgnoredUntil 未到）不会重复展示。

### 状态定义
- `Active`：待处理，默认状态。
- `Acknowledged`：已确认/已响应，仍会展示。
- `Ignored`：已忽略/静默，`IgnoredUntil` 未到期时不展示。
- `Processed`：已处理完毕，后续新命中会自动转回 `Active`。
- `Resolved`：在 `ResolveGraceMinutes` 内未再次命中的自动恢复状态，可按需重开。

### 命令与自动流转
- 命令：确认（Active→Acknowledged）、忽略（→Ignored，带静默截止时间）、标记已处理（→Processed）、重开（→Active）。
- 自动恢复：若工单超过宽限期未再命中，状态置为 `Resolved`；若已恢复/已处理但再次命中，将转为 `Active` 并累加 OccurrenceCount。

### 存储
- 工单存储于 `%AppData%\EW-Assistant\warning_tickets.json`，读写容错、UTF-8，无需手工创建目录。*** End Patch```github.com/copilot" uṣiṣẹ manually."* ***!
