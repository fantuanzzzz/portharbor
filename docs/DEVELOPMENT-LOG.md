# PortHarbor Development Workbook

This workbook records every development session. Each entry covers what changed, why it changed, files affected, verification, remaining problems, and follow-up work.

本文档保留历史开发与验证记录；各条目的“下一步”表示当时的安排。当前阶段状态、任务勾选及验收条件统一维护在[技术方案第 8 节](TECHNICAL-PLAN.md#8-分阶段实施)，文件传输当前契约见[文件传输约定](FILE-TRANSFER.md)。历史条目不随当前实现变化而回写。

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

每次开发结束还应同步更新技术方案第 8 节对应任务。阶段达到全部完成条件后，标记“已完成”，记录完成日期、对应提交及本日志的验收依据；没有验证依据的任务保持未勾选。

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

## 2026-10-23 - 文档分工与阶段进度维护

### 本次开发内容与修改原因

- 保留三份文档，明确技术方案负责路线图与阶段状态、开发日志负责历史记录、文件传输文档负责当前模块契约。
- 将阶段进度统一放在技术方案第 8 节，增加状态总览、任务勾选、完成日期/提交字段和验收依据。
- 根据当前源码、CI 工作流、构建脚本和历史日志标注已完成子项；将未完成或尚未验证的工作保留为未勾选。
- 修正技术方案中“暂无 CI”的过时描述，更新当前后续顺序；文件传输文档改为链接统一进度入口，保留完整技术约定。
- 约定每次开发结束同步更新任务清单；阶段全部达到完成条件后，记录完成日期、提交及验证日志。

### 修改文件

- `docs/TECHNICAL-PLAN.md`
- `docs/DEVELOPMENT-LOG.md`
- `docs/FILE-TRANSFER.md`

### 验证范围

- 对照源码、构建脚本与 CI 工作流核对任务状态，历史测试数量仅引用此前日志。
- 文档本地链接及阶段清单检查通过；`git diff --check` 最终通过。
- 初次差异检查发现新增行尾空格及文件末尾空行，已修正。
- 本次只修改文档，未重新构建或运行应用测试。

### 仍存在的问题与边界

- 阶段 0、1 尚未完成；阶段 3 仅同步文件传输边界拆分提前完成，不能视为整个阶段验收通过。
- 未联网核实远端 CI 运行结果，也未重新验证历史构建和测试记录。
- README 及项目版本标识、旧验证摘要的同步不在本次文档整理范围内。

### 下一步

- 按技术方案第 12 节推进文件传输异步、取消、超时及关闭竞态处理，并在开发结束后同步阶段清单。

## 2026-10-23 - 分类、标签关闭、主密码与预览打包

### 本次开发内容与修改原因

- 增加独立分类列表及工具栏/右键新建入口，支持保存空分类、选中分类后新建会话、编辑器选择分类和分类导入导出；目前为单层分类。
- 增加带 × 的标签控件，双击标题、×、中键、工具栏和 Ctrl+W 共用关闭确认；取消时保留标签及连接。存在会话标签时退出应用也需确认。
- 增加主密码库，使用 PBKDF2-HMAC-SHA256 派生密钥和 AES-256-GCM，对原 DPAPI 密文再加密；启动解锁、首次保存设置、修改密码、旧配置迁移及保存失败回滚均有实现。数据格式升级至 v2，仍能读取 v1；导出排除密码库。
- 默认只生成不含 .NET 环境的框架依赖包；`-IncludeRuntime` 可选生成自包含包。增加 `-OutputDirectory`，保留核心及 Windows 检查的 JSON 报告，避免报告随 staging 清理丢失。
- 更新 README、阶段清单及 ADR 0001。按用户要求，字体和安装/便携数据模式仅分析并提供建议，未改变实现。

### 修改文件

- `src/PortHarbor/MainForm.cs`、`ConnectionTabs.cs`、`SessionEditor.cs`
- `src/PortHarbor/Models.cs`、`PasswordVault.cs`、`Program.cs`
- `tests/PortHarbor.Checks/Program.cs`、`VaultChecks.cs`、`TabUiChecks.cs`
- `build.ps1`、`README.md`、`docs/TECHNICAL-PLAN.md`、`docs/adr/0001-master-password.md`、本日志

### 验证结果与过程中发现的问题

- Release 编译通过，0 警告、0 错误；核心检查 88 项通过，Windows 检查 3 项通过。
- UI 检查 11 项通过，包括空分类显示、取消关闭保留页面、确认关闭释放指定页面、原生消息触发双击/×/中键，以及既有文件视图检查。
- 密码库检查覆盖锁定访问拒绝、错误口令、修改后拒绝旧口令、重启锁定、凭据篡改与跨会话替换拒绝、v1 DPAPI 迁移、无密码导出及保存失败回滚。
- 默认发布成功生成框架依赖包；检查 ZIP 未包含 `coreclr.dll`、`clrjit.dll`、`System.Private.CoreLib.dll`，runtimeconfig 使用外部框架。预览产物位于 `artifacts/preview`。
- 初次编译被沙箱限制写入 .NET 用户初始化文件；使用项目内构建缓存并经权限提升后完成。DPAPI 检查在沙箱身份下失败，提升到当前 Windows 用户环境后通过。
- 初次 UI 冒烟通过，但新增原生鼠标消息检查发现 WinForms 标签没有可靠触发托管双击/点击事件；修正为原生消息处理后，三条鼠标关闭路径全部通过。
- 首次默认发布因旧应用正在占用 `artifacts/framework-win-x64` 而失败；未终止旧程序或连接，改用独立预览输出目录后成功。
- 读取本机字体清单确认缺少 Cascadia Mono；请求该字体实际回退为 Microsoft Sans Serif。已建议使用等宽字体回退并校准字符格，未修改 `TerminalView.cs`。
- 文档本地链接及 `git diff --check` 检查通过。本次未重跑网络协议服务、真实 SSH 登录及 `-IncludeRuntime` 自包含分支。

### 仍存在的问题与边界

- 迁移前配置与 `.bak` 仍按旧 DPAPI 或旧主密码保护，不会自动被新主密码重新加密；旧版应用不能读取 schema v2。切换新版前应关闭旧程序，避免同一配置目录并行写入。
- 忘记主密码没有找回功能；当前凭据继续绑定 Windows 用户，不支持把密码随便携目录直接跨机器使用；不包含自动锁定功能。
- 安装版、便携数据目录模式、字体修正及复杂终端兼容性仍未实施；总路线图阶段 0–4 的状态保持按各自完成条件判断。

### 下一步

- 等用户确定字体优化与安装/便携数据策略后另行实施；后续主线仍见技术方案第 12 节。
## 2026-10-23 - 等宽终端、嵌套分类、下拉菜单与双版本配置

### 已完成

- [x] 终端默认改为已安装的 Consolas，按 Cascadia Mono、Lucida Console、Courier New、系统等宽字体回退；测量多字符宽度，字体及 DPI 改变时重算网格。
- [x] 会话树增加独立绘制的文件夹、终端、文件传输和远程桌面图标；支持根分类、子分类及继续嵌套，完整路径保存在原 Group 字段；深层会话搜索保留祖先节点。
- [x] 顶部收拢为文件、会话、窗口、设置、帮助五个下拉菜单；保留会话库右键菜单与已有快捷键。
- [x] 安装版使用用户目录 settings.json；便携版通过随包标记、相邻 INI 或 --portable 选择，首次启动在程序旁创建 PortHarbor.ini，可用 --data-dir 指定测试目录。
- [x] INI 分节保存配置、分类、会话、密码库、凭据和主机密钥；字符串采用 JSON 转义，主机密钥名称编码；保留原子写入与备份。现有安装版配置不自动迁移到便携版。
- [x] 安装版密码库版本 1 保留 DPAPI + 主密码，便携版新密码库版本 2 使用主密码直接加密，不依赖 Windows 账户；更改主密码按当前模式重新加密，失败回滚。
- [x] 构建同时生成 installed-win-x64 和 portable-win-x64 两版目录包，IncludeRuntime 作为独立选项，默认均不包含运行环境。
- [ ] 正式安装向导、升级和卸载流程：留待发行阶段，不标为完成。

完成日期：2026-10-23；提交：当前工作区，待提交。

### 验证和修正

- Release 编译 0 警告、0 错误；核心检查 104 项、Windows 检查 3 项、UI 检查 16 项通过。报告分别在 artifacts/preview/unit-results.json、artifacts/preview/windows-results.json、artifacts/ui-results.json；界面截图 artifacts/ui.png。
- 新增检查覆盖 INI 创建、复制重启、中文/换行/特殊符号、空嵌套分类、错误主密码、修改口令、拒绝重复分节与键、启动模式选择、主机密钥编码、便携 AES 包络不含 DPAPI，以及菜单、图标、等宽测量和字体变更重算。
- UI 检查发现 ImageList 延迟创建导致临时位图释放后图像丢失；改为先建立句柄再添加位图，复验通过。安装版 DPAPI 在沙箱身份下失败，按当前 Windows 用户权限复验通过。
- 默认发布成功，产物为 artifacts/preview/PortHarbor-0.1.0-installed-win-x64.zip 和 PortHarbor-0.1.0-portable-win-x64.zip。安装版当前为目录布局 ZIP，尚无 Setup 安装向导。
- 未操作用户运行中的旧程序或配置。未在第二台真实电脑验证，未重新登录实际 SSH 设备，未重跑包含运行环境分支；字体校验目前基于本机等宽测量和 UI 网格。
- 只读参考用户提供的 D:/Ai-Workspace/References/mobaxterm-23.3/report.md 与 form-strings.tsv，借鉴静态界面/配置线索，未复制第三方实现或资源。INI 为本项目独立格式，不兼容其 INI。

### 后续

- 用户可先验收终端视觉、分类和两种配置布局；主线继续推进文件传输异步、取消及超时处理。


## 2026-10-23 - 终端交互与主密码命名

- [x] 用户界面中的“主密码”统一改称“主密码”，底层 `Vault` 和数据格式不变，旧配置继续兼容。
- [x] 终端增加右侧滚动条，保留鼠标滚轮和键盘滚动；终端输出更新时同步滚动范围。
- [x] 鼠标左键拖选后自动复制到剪贴板；右键直接粘贴，仍保留 Ctrl+Shift+C/V 和多行粘贴确认。
- [x] 终端右键菜单增加字体设置和配色设置，支持字体、字号、样式、前景色和背景色；终端 ANSI/VT 的颜色序列仍按输出内容绘制。
- [ ] 字体与配色持久化到会话/全局设置：当前设置作用于当前终端实例，后续再接入配置文件。

编译检查 0 警告、0 错误。MobaXterm 的“无需在连接对话框输入账号密码”行为仍仅做解释，未改 SSH 登录流程。


## 2026-10-23 - 今日开发收尾

### 今日最终状态

- [x] 将用户界面中的“总密码”统一改为“主密码”。
- [x] 主密码解锁框增加“跳过”：跳过后可以进入主界面和打开终端；已保存凭据保持加密且不可读，连接时要求手动输入本次密码，不会删除已保存密码。
- [x] SSH、Telnet、Serial、Shell 等 `TerminalView` 实例使用同一终端交互实现：右侧滚动条、历史滚动、ANSI/VT 前景色和背景色、字体与颜色菜单。
- [x] 左键选取后自动复制、右键直接粘贴改为两个独立的可选菜单项，默认关闭；快捷键复制/粘贴继续可用。
- [x] 字体菜单支持字体、字号和样式；配色菜单支持默认前景色和背景色。ANSI/VT 输出颜色仍由远端程序发送的控制序列决定。
- [x] SSH 认证方式本轮保持现状：连接前使用已保存或临时输入的用户名/密码；MobaXterm 风格的 keyboard-interactive 终端内认证只完成解释，未实现。
- [x] 安装版与便携版仍同步维护。当前发布脚本会生成目录和 ZIP 预览产物；用户已确认后续预览阶段可以只保留两个开发目录、不生成 ZIP/便携发行包，待下一次构建脚本调整时实施。单 EXE 便携版方案已解释，今天没有改动。

### 今日验证

- 产品和测试工程 Release 编译：0 警告、0 错误。
- 核心检查：104 项通过（终端/密码库/文件传输）；Windows 检查：3 项通过。
- UI 检查：此前 16 项通过；修正主密码“跳过”和 SSH 终端菜单后，未新增协议连接测试，最终发布前需要重新补充 UI 检查覆盖。
- 最新可运行预览目录：`artifacts/preview-ssh-fixed/installed-win-x64`、`artifacts/preview-ssh-fixed/portable-win-x64`。
- 未修改用户实际配置目录，未停止用户已有程序，未对真实 SSH 设备执行登录操作。

### 明确未完成项

- SSH keyboard-interactive 认证、在终端内输入用户名/密码后保存询问。
- 终端字体与配色持久化到全局或会话配置。
- 正式安装向导、卸载、升级，以及便携单 EXE 发布方案。
- 将预览构建默认改成只输出 installed/portable 开发目录。

今天的开发到此结束；下一次开发前先重新运行 UI 检查，再继续功能开发。


## 2026-10-23 - 文件传输原生异步、取消、超时与关闭等待

### 本次开发内容与原因

- 将公开 `IFileTransfer` 和内部 `IRemoteFileSystem` 改为原生异步契约；FTP 使用锁定版本 FluentFTP 55.1 的 `AsyncFtpClient`，SFTP 使用锁定版本 SSH.NET 2026.0 的异步连接、目录、属性、命令及文件流读写 API。`FileView` 不再用 `Task.Run` 包装同步文件操作。
- 所有连接与文件操作均要求 `CancellationToken` 和正数操作超时。异步门闩串行化单连接操作，超时包含门闩等待，并会取消底层请求；补充连接排队超时不得误关闭其他活跃连接的检查。
- 上传/下载改用异步流读写和释放；报告结构化传输方向、已传字节数及可选总量，并按 100ms/4MiB 节流。上传和下载继续经 `.part` 暂存，失败及取消时尽力清理并保留旧目标。
- 文件页和主窗口增加等待关闭流程：关闭页、退出应用会取消活动文件请求，等待请求退出和传输客户端释放后再销毁窗体。没有活动任务时也会等待 FTP 异步断开完成。
- 保持 FTP 覆盖移动。SSH.NET 2026.0 没有带取消令牌的 POSIX 覆盖重命名，因此异步 SFTP 替换改用旧目标 `.bak` 暂存、失败恢复策略，并明确替换期间路径可能短暂不存在。
- 更新文件传输技术说明、README 和阶段任务清单；扩大内存适配器测试，覆盖预先取消、超时诊断、队列超时竞态、操作取消、流提交失败和关闭竞态。

### 修改文件

- `src/PortHarbor/IFileTransfer.cs`、`FileConnection.cs`、`FileView.cs`、`MainForm.cs`
- `src/PortHarbor/RemoteFileSystems/IRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/FtpRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/SftpRemoteFileSystem.cs`
- `tests/PortHarbor.Checks/Program.cs`、`FileTransferChecks.cs`
- `README.md`、`docs/FILE-TRANSFER.md`、`docs/TECHNICAL-PLAN.md`、本开发日志

### 验证结果

- Release 构建：0 警告、0 错误。
- 核心检查：93 项通过，包括异步契约、预先取消、结构化进度、稳定超时诊断、连接队列超时竞态和连接释放。
- Windows 检查：3 项通过。
- UI 检查：15 项通过，含文件页关闭活动请求清理，以及主窗口退出等待异步文件清理。
- 终端 UI 冒烟输出包含 `CONPTY_OK`。
- 本机协议回环：20 项通过，覆盖 Telnet、SSH、FTP 和 SFTP；FTP/SFTP 中文路径、上传/下载、覆盖、取消保留目标及临时文件清理、重命名冲突和目录删除均通过。
- 报告保存在被 `.gitignore` 忽略的 `artifacts/async-transfer-unit.json` 与 `artifacts/async-transfer-validation/`；未新增运行时依赖。
- `git diff --check` 通过；仅有 Git 对一个新写源码文件的 LF/CRLF 提示，没有行尾空白错误。

### 遗留问题与边界

- SSH.NET 2026.0 未提供异步客户端断开 API。SFTP 网络操作会先异步响应取消并退出门闩，然后调用库的同步 `Dispose()` 关闭客户端；相关版本及这一例外已写入文件传输说明。
- SSH.NET 的异步重命名没有 POSIX 覆盖选项。SFTP 备份替换可能短暂暴露缺失的目标路径，服务器断线可能遗留 `.part`/`.bak`；本机 fixture 验证了常规替换，没有模拟生产服务器断线窗口。
- FTP 上传依赖服务器最终响应确认；若服务器不返回最终数据响应，客户端会保守地报告失败。当前回环服务器正常返回确认，其他 FTP/FTPS 服务器兼容性仍待扩大。
- 当前超时：连接 15 秒，单项文件操作 30 秒，远端临时文件尽力清理 5 秒。终端、RDP 等其他协议的全面异步和关闭生命周期仍未完成。
- 本次未实现本地化资源、传输队列、断点续传、递归传输或自动重连。

### 下一步

- 补齐 SSH、Telnet、Serial、Shell 和 RDP 的诊断展示与失败分类，继续完善其他后台任务及连接关闭/应用退出生命周期覆盖。
- 再将本地化资源、诊断消息目录和更广的 FTP/SFTP 服务器兼容性测试纳入阶段 1 清单。
