# a 批 · 门禁先立（2026-08-21）

- 新增 `Tools/Gates/PathAsciiChecker.cs` 与命令 `gate.pathascii`，接进 `gate.ps1`。
- 配置加两个键：`pathAsciiMode`（`warn` / `block`）与 `pathAsciiExemptPrefixes`（存量欠账名单）。
- 首次扫描结果：**存量 222 条**（含未跟踪文件；已跟踪的是 206 条）。
- **刻意先 warn**：一道新规矩默认不该把别人的构建弄红；但它照样把每一条列出来——
  看不见存量的规矩等于没有。
