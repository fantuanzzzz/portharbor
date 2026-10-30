# 测试入口与迁移清单

更新：2026-10-30。T6 将纯逻辑检查接入 NUnit 4 / VSTest，保留原 JSON 检查器和独立 Windows、协议、UI、ConPTY 入口。

## 标准测试

在仓库根目录的 PowerShell 执行（需要 .NET 8 SDK；首次恢复需要 NuGet 网络）：

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.build-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.packages'
dotnet restore tests/PortHarbor.UnitTests/PortHarbor.UnitTests.csproj --configfile NuGet.Config --locked-mode
dotnet test tests/PortHarbor.UnitTests/PortHarbor.UnitTests.csproj -c Release --no-restore --logger 'trx;LogFileName=core.trx' --results-directory artifacts/standard --collect 'XPlat Code Coverage' --settings tests/coverage.runsettings
```

`--list-tests` 可列出独立测试名称，`--filter 'Name~TelnetCodec'` 可只运行一组。失败报告保留原断言名称，可以直接定位规则；单组失败不阻止其他组运行。标准测试不启动 UI、外部进程或网络，不引用 Windows 项目。

| 原检查位置 | 标准测试名 | 范围 |
| --- | --- | --- |
| Application.Checks 顶层服务检查 | ApplicationServices | 保存、删除、凭据/信任端口与失败传播 |
| Shared/SessionValidationChecks | SessionValidation | 七协议字段校验与 inactive 字段 |
| Shared/SessionLibraryChecks | SessionLibrary | 筛选、排序、搜索、快照 |
| Shared/SessionActivityChecks | SessionActivity | 收藏、最近与删除关联 |
| Shared/ConfigurationTransferChecks | ConfigurationTransfer | 导入导出、合并、敏感字段排除 |
| Shared/WorkspacePlacementChecks | WorkspacePlacement | 工作区约束与恢复 |
| Shared/DiagnosticMessageChecks | DiagnosticMessages | 消息目录、错误/阶段回退 |
| Shared/RemoteDesktopChecks | RemoteDesktopConfiguration | RDP 配置生成与转义 |
| Shared/PlatformCapabilityChecks | PlatformCapabilities | 能力描述与禁用原因 |
| Shared/TerminalSessionChecks | TerminalLifecycle | 状态、输入顺序、UTF-8、取消、释放 |
| Shared/TransferQueueChecks | TransferQueue | 串行、失败暂停、取消与显式重试 |
| Checks/Program.Terminal → Shared/TerminalModelChecks | TerminalStateMachine | ANSI、缓冲、宽字符、畸形输入 |
| Checks/Program.Telnet → Shared/TelnetModelChecks | TelnetCodec | 分片、协商、IAC、NVT |
| Shared/FileTransferPolicyChecks | FileTransferPolicy | 超时、暂存、提交、恢复及取消 |
| Checks/DiagnosticBundleChecks → Shared/DiagnosticExportChecks | DiagnosticBundle | 预览/导出一致、限量、构建身份与敏感字段排除 |

迁移采用同一份 Shared 实现，由标准测试调用真实断言；没有包装整个旧控制台进程作为一项测试。原应用检查和核心检查仍调用相同逻辑，便于比较历史结果。**15 个测试组不等于 15 条断言，也不能与旧检查计数相加充当新增覆盖。**逐条拆分为更细的命名测试可继续改善定位，不阻碍本批标准框架落地。

## 保留的独立入口

| 入口 | 保留原因 / 结果 |
| --- | --- |
| Application.Checks | 跨平台兼容旧断言汇总；JSON |
| Checks `--unit` | JSON/INI/DPAPI、存储迁移、厂商/Windows 适配器、路径和新跨进程保护；JSON |
| Checks `--windows` | ConPTY、系统能力、真实 DPAPI、RDP 替身进程生命周期；JSON |
| Checks `--integration servers.json` | Python 回环 SSH/Telnet/FTP/SFTP 与中断/恢复；JSON |
| Checks `--ui` / `--ui-*` | 交互 Windows 桌面与 WinForms 控件；截图及 JSON |
| Checks `--ui-shell` | 真实 ConPTY 嵌入 UI；终端输出证据 |
| `build.ps1 -ExtendedBaseline` | 标准测试 + 旧分层检查 + 协议/UI/ConPTY + 历史目录包冒烟 |
| `build.ps1 -P10` | 单文件便携及隔离 AppId 安装/模拟升级/卸载；发行 JSON、manifest |
| `tests/shortcut-inspection.ps1` | Unicode Shell 快捷方式读取、文件身份、别名/错误/缺失目标及参数专项；JSON |

`TypedConfigurationChecks` 同时使用 `Store.Json` 和 INI 存储实现；凭据、格式/fixture、厂商异常分类、SSH 认证策略和平台生命周期属于存储/适配器回归，留在 Windows 检查器。Shared 的 MemoryRemoteFileSystem 和 ScriptedTerminalTransport 为测试替身，不是独立测试组。

## 覆盖率、格式与 CI

`tests/coverage.runsettings` 仅测量 **PortHarbor.Domain / PortHarbor.Application** 的行/分支覆盖率，Cobertura XML 位于结果目录的随机子目录。没有纳入 UI、Infrastructure、Windows 平台，也不代表服务器互通。原 JSON 的 passed 仍是断言计数；`engineering-baseline.json` 同时指向标准 TRX 和源码覆盖率，区分两种指标。本轮不设置无历史依据的百分比门槛。

`.editorconfig` 定义 C# 四空格等规则；`Directory.Build.props` 显式启用 .NET 8 分析器、Nullable 各项目既有设置、确定性构建和警告即错误。CA2200/CA2012 作为明确警告规则参与构建，不启用无限变化的 latest 分析级别。

```powershell
pwsh -File scripts/Check-CodeStyle.ps1
pwsh -File scripts/Check-CodeStyle.ps1 -Fix
```

格式门禁对相对 HEAD 的已改/新增 C# 文件执行 `dotnet format whitespace --verify-no-changes`；CI 对 PR base / push 前提交的变更文件执行。已提交后本地可传 `-BaseRevision HEAD^` 验证本次提交。需要先恢复所有项目（标准测试、Application.Checks、Checks 的恢复覆盖依赖图）；不批量重排未修改的历史代码。编译器及启用的分析器则检查整个项目。

CI 在 Linux/macOS 的 core matrix 仅恢复/构建/执行 net8.0 纯核心测试；Windows validate 上传标准 TRX/Cobertura 和旧 JSON/UI 证据。两类通过后独立 distribution job 获取验签的 Inno Setup 并运行 P10。工作流更新不等于 GitHub 已实际运行通过；首次推送后应查看 core 的两个平台、validate 和 distribution。没有将 WinForms/串口/RDP 放到非 Windows runner，也不表示产品支持非 Windows UI。真实环境项目及记录格式见[实机验收手册](REAL-ENVIRONMENT-ACCEPTANCE.md)。

发行快捷方式校验通过 `IShellLinkW` 读取 Unicode 目标，再用已打开文件的卷标识和文件 ID 判断是否确为安装的 EXE，不把路径字符串相等或文件内容相同当作同一个文件。仅读取，不解析启动或执行 `.lnk`。失败时控制台输出期望目标、实际目标、参数与文件身份比较结果；`shortcut-details.json` 与安装/升级/卸载测试日志作为 distribution-evidence 上传。原 GitHub #9/#10 的错误日志未记录实际目标，具体路径差异仍需修复后的 runner 证据确认。

专项的合成 `.lnk` 也用 `IShellLinkW` 和 `IPersistFile.Save` 创建，避免英文 runner 中 WScript.Shell 设置中文 TargetPath 抛出范围异常（#11 日志已定位）；样本只写入 ignored artifacts，不执行。此写入方法不用于安装器或应用，七项校验内容保持不变。
