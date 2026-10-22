# PortHarbor Development Workbook

This workbook records every development session. Each entry covers what changed, why it changed, files affected, verification, remaining problems, and follow-up work.

## 2026-10-22 - Connection diagnostics baseline

### What was developed

- Added a structured connection diagnostics contract in `src/PortHarbor/Diagnostics.cs`.
- Added stable connection states, operation stages, error codes, retryability, and a per-connection `OperationId`.
- Connected `TerminalConnection` to diagnostic events for connection, authentication, host-key verification, channel creation, read/write, cancellation, failure, and close paths.
- Added a lifecycle check that verifies a platform failure produces a stable diagnostic and reaches the closed state.
- Confirmed the project path and write permissions after the workspace migration.

### Files changed

- `src/PortHarbor/Diagnostics.cs`
- `src/PortHarbor/TerminalConnection.cs`
- `tests/PortHarbor.Checks/Program.cs`
- `README.md`, `build.ps1`, `src/PortHarbor/PortHarbor.csproj`, and `.github/` contain the earlier engineering baseline changes.

### Verification

- .NET SDK `8.0.426` is installed and available.
- Locked restore completed with `NuGet.Config`.
- Release build completed with 0 warnings and 0 errors.
- Unit checks passed: 30 checks.
- Windows checks passed: 3 checks, including the diagnostic lifecycle assertion.
- `git diff --check` passed.
- Results were written to `artifacts/unit-results.json` and `artifacts/windows-results.json`.

### Known problems

- Existing source files contain legacy encoding artifacts in some Chinese user-facing strings; this should be handled as a separate encoding cleanup to avoid mixing behavior changes with text migration.
- File transfer operations do not yet publish the same diagnostic contract as terminal connections.
- UI subscription to the new diagnostic events is not implemented yet.

### Next step

1. Add the diagnostic contract to `FileConnection`.
2. Add a small UI-safe diagnostic presenter that shows stage and error code without exposing secrets or terminal content.
3. Treat encoding cleanup as a separate change and add focused checks before changing user-facing strings.

## Workbook rule

Every future development session must append an entry with: date, development scope, files changed, verification performed, remaining problems, and next step. Failed checks and environment limitations must be recorded instead of being omitted.

## 2026-10-22 - File transfer diagnostics, safe presentation, and encoding audit

### What was developed

- Added the structured diagnostics lifecycle to `FileConnection`, including operation ID, connection state, connection stage, error code, retryability, and cleanup transitions.
- Added `DiagnosticPresenter` for UI-safe status and error text. It shows a short operation identifier and stable error category without exposing host names, paths, credentials, or raw exception text.
- Updated `FileView` to subscribe to diagnostics, show stage progress in the status bar, and use the safe presenter for failures.
- Added unit checks for file-connection failure/close diagnostics and endpoint redaction in the presenter.
- Audited the reported Chinese encoding issue. The source files are valid UTF-8 when read as bytes; the mojibake was produced by the PowerShell output encoding. No source re-encoding was applied because it would risk changing valid text.

### Files changed

- `src/PortHarbor/FileConnection.cs`
- `src/PortHarbor/DiagnosticPresenter.cs`
- `src/PortHarbor/FileView.cs`
- `tests/PortHarbor.Checks/Program.cs`
- `docs/DEVELOPMENT-LOG.md`

### Verification

- Release build passed with 0 warnings and 0 errors.
- Unit checks passed: 32 checks.
- Windows checks passed: 3 checks.
- New checks confirm file connection diagnostics include failed and closed lifecycle events.
- New checks confirm the UI presenter does not expose the endpoint value.

### Remaining problems

- File operation diagnostics currently use generic English internal messages; the UI presents localized stable categories, but the diagnostic catalog still needs a localization policy.
- `FileConnection.Exists` is still a direct protocol call and does not emit a diagnostic when it fails.
- The PowerShell console encoding should be configured separately if Chinese source text needs to be inspected directly in that shell.

### Next step

1. Extract protocol-specific file operations behind a shared file transfer interface.
2. Add cancellation-aware async methods so UI work does not rely on synchronous network calls inside `Task.Run`.
3. Add a localization resource for diagnostic categories and user-facing status text.

## 2026-10-22 - 文件传输接口与 FTP/SFTP 适配器拆分

### 本次开发内容与修改原因

- 新增协议无关的 `IFileTransfer` 和 `RemoteEntry`，由 `FileTransferFactory` 构造连接，`FileView` 不再持有具体的 `FileConnection` 类型。
- 为 `FileView` 增加接口注入构造入口，明确由视图负责释放连接，方便更换实现和检查界面。
- 新增内部 `IRemoteFileSystem`，将 FluentFTP 和 SSH.NET 调用分别迁入 `FtpRemoteFileSystem`、`SftpRemoteFileSystem`。
- `FileConnection` 专注连接状态、诊断、累计进度、取消检查、临时文件与上传提交，不再引用 FTP/SFTP 厂商库。
- 保留 FTP 覆盖和 SFTP POSIX 覆盖/旧文件暂存回退逻辑；统一两种协议的非空目录删除限制和重命名目标冲突规则。
- 修复 `Exists` 失败时缺少诊断的问题；新增未连接/已释放操作保护、重复连接和释放保护，以及预先取消时避免打开远程流的检查。
- 将连接失败单元检查改为内存替身，不再依赖外部 DNS；增加公共策略、清理失败、取消与真实协议回归检查。
- 在方案与 README 中登记实际边界，新增 `docs/FILE-TRANSFER.md`，说明协议扩展、资源所有权和目前的限制。

### 修改文件

- `src/PortHarbor/IFileTransfer.cs`
- `src/PortHarbor/FileConnection.cs`
- `src/PortHarbor/FileView.cs`
- `src/PortHarbor/RemoteFileSystems/IRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/FtpRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/SftpRemoteFileSystem.cs`
- `src/PortHarbor/Properties/AssemblyInfo.cs`（仅向检查程序集提供内部测试接缝）
- `tests/PortHarbor.Checks/FileTransferChecks.cs`
- `tests/PortHarbor.Checks/Program.cs`
- `tests/PortHarbor.Checks/UiShell.cs`
- `docs/FILE-TRANSFER.md`、`docs/TECHNICAL-PLAN.md`、`README.md`、本工作簿

### 验证结果与过程中的问题

- 最终 Release 构建：0 警告、0 错误。
- 核心检查：69 项通过，涵盖原有存储/终端检查以及本次文件传输公共策略。
- Windows 检查：3 项通过，覆盖 ConPTY 交互和失败生命周期。
- 本机协议检查：20 项通过，覆盖 Telnet、SSH 及真实 FTP/SFTP 的 Unicode 文件名、上传下载、覆盖、取消后目标保留、临时文件清理、重命名冲突、非空目录拒绝删除和空目录删除。
- UI 冒烟：4 项通过，覆盖主窗口、七种协议编辑器、通过注入接口加载 Unicode 文件列表及关闭视图释放连接。
- 终端 UI 冒烟通过，输出包含 `CONPTY_OK`。
- 验证报告：`artifacts/unit-results.json`、`artifacts/windows-results.json`、`artifacts/integration-results.json`、`artifacts/ui-results.json`、`artifacts/ui-shell-results.json`；截图与终端输出也保存在 `artifacts`。
- `git diff --check` 通过；Git 仅提示既有文件的 LF/CRLF 转换。
- 初次编译出现 2 个 CS0436 警告：测试程序集获取内部可见性后，两个程序集的 WinForms 自动生成 `ApplicationConfiguration` 类型冲突。测试改用显式 UI 初始化，最终构建无警告。
- 当前沙箱曾拒绝项目内源码写入；通过权限提升后完成保存。工作路径仍为 `D:/Ai-Workspace/Projects/portharbor`。
- 系统 Python 缺少协议测试依赖；在已忽略的 `artifacts/protocol-venv` 中创建隔离环境并安装 `tests/requirements.txt` 指定版本，未修改系统 Python。每次测试完成后均关闭本次启动的回环服务。

### 仍存在的问题与边界

- 本阶段完成同步接口及协议隔离；界面仍通过 `Task.Run` 调用同步网络操作。取消在数据块之间生效，阻塞读写或目录操作需等待协议超时。
- 断线可能导致远程 `.part` / `.bak` 清理失败；FTP 与不同 SFTP 服务器不能统一保证原子替换。
- 本机 SFTP fixture 支持 POSIX 覆盖；此次未额外模拟不支持该扩展时的回退失败或断线恢复，相关代码保留原有策略，仍需服务器兼容性验证。
- FTPS、私钥认证、多种实际服务器尚未完成本次验证。
- SFTP 认证和主机密钥检查仍复用 `TerminalConnection` 的 SSH 辅助函数，后续可提取为共享 SSH 基础设施。
- 诊断内部消息及界面文案尚未统一迁入本地化资源；没有新增传输队列、自动重连或断点续传。

### 下一步

1. 在现有接口边界上设计异步操作与结构化进度，贯穿取消令牌及超时设置。
2. FTP/SFTP 逐步使用底层库的异步能力，移除文件视图中的同步调用包装；补充取消响应和连接关闭竞态检查。
3. 再建立本地化资源和诊断消息目录，扩大服务器兼容性覆盖。
