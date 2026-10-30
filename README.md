# PortHarbor

轻量的 Windows 连接工作台，使用 C# / .NET 8 / WinForms 编写。当前版本为 **0.1.0 私有预览版**，尚未选择或发布开源许可证。原生界面和终端，不依赖 Electron、浏览器运行时或内置 Linux 发行版。

本项目独立实现常用的连接管理功能。

## 功能

| 类型 | 当前实现 |
| --- | --- |
| SSH | 密码或私钥登录、交互终端、窗口大小同步、首次主机密钥确认及指纹保存 |
| Telnet | TCP 连接、基础选项协商、VT100 终端类型、IAC 转义 |
| RDP | 启动 Windows 自带的 mstsc，支持端口、用户名、全屏和剪贴板设置 |
| FTP | 被动模式，普通 FTP 或显式 TLS，浏览、上传、下载、重命名、新建目录、删除文件或空目录 |
| SFTP | SSH 密码或私钥登录、主机密钥校验，与 FTP 相同的基础文件管理 |
| 文件工作流 | 路径导航、列排序、多文件串行上传/下载队列、逐项进度与结果、失败暂停及核对后继续/重试、取消与可等待关闭 |
| SSH 认证 | 密码/私钥认证之外支持 keyboard-interactive 多轮挑战、隐藏输入、取消；SSH 终端与 SFTP 共用认证规则 |
| Serial | COM 口、波特率、数据位、校验、停止位和流控设置 |
| Shell | Windows ConPTY 内嵌交互命令行，可配置程序、参数和工作目录 |
| 会话 | 保存、分类文件夹（图标、嵌套和空分类）、收藏、最近连接、搜索、复制、重命名、编辑、按协议字段分组、就地校验、密码显示、路径选择、删除、JSON 导入和导出、多标签连接 |
| 密码 | 可选保存，主密码加密；安装版叠加 Windows 用户级 DPAPI；导出会话不含密码 |

## 运行

当前源码已实现 P01–P10，包含单文件便携交付与安装向导；旧体验包不会随源码自动更新。发行方式与数据保留见[发行说明](docs/DISTRIBUTION.md)，认证规则见[SSH 认证说明](docs/SSH-AUTHENTICATION.md)。

需要 Windows 10 1809 或更新版本 / Windows 11，x64。

新体验包在 `artifacts/releases/`，两种都自带 .NET 8 Desktop Runtime，无需另装 .NET：

- `PortHarbor-0.1.0-portable-win-x64.exe`：单文件便携版，放到可写目录双击运行；首次启动在程序旁创建 `PortHarbor.ini`，后续按使用情况生成偏好和日志。移动时携带整个使用目录。
- `PortHarbor-0.1.0-setup-win-x64.exe`：中文/英文安装向导，默认安装至 `%LOCALAPPDATA%\Programs\PortHarbor`，数据在 `%LOCALAPPDATA%\PortHarbor`。创建开始菜单及 Windows 卸载入口，升级/卸载保留用户数据。

单文件指交付时只有一个 EXE；.NET 原生组件运行时可能解包到用户临时缓存。第三方许可已嵌入程序，在“帮助 → 第三方许可”查看。历史 `*-installed-*.zip` 只是目录布局，仍可由原构建流程生成；`-IncludeRuntime` 控制历史目录包是否自带运行时，与数据模式独立。

应用需要 Windows 系统组件支持 RDP、ConPTY 和 DPAPI；RDP 使用 Windows 自带的远程桌面客户端。

菜单分为「文件 / 会话 / 窗口 / 设置 / 帮助」，各项操作在下拉菜单中。

「设置 → 界面主题」可选择浅色或深色，即时应用并在重启后保留。界面字体、按钮层级、选中/焦点和错误状态采用共享样式；终端仍使用自己的字体和配色。高对比度优先 Windows 系统颜色；标题栏、滚动条和系统弹窗沿用系统外观。样本及覆盖范围见[视觉规范](docs/VISUAL-DESIGN.md)。

菜单下的操作栏提供新建连接、连接、编辑、搜索和会话库切换；选择会话后可直接连接/编辑。`Ctrl+Shift+F` 搜索，`Ctrl+Shift+B` 显示/隐藏会话库，`F6` / `Shift+F6` 切换搜索、会话树和工作区；会话树内 Enter 连接、F2 编辑。窗口正常大小/位置、最大化状态和侧栏宽度/显隐自动记忆；「窗口 → 恢复默认布局」可重置，恢复时将窗口限制到现有屏幕工作区。布局示意、保存及恢复约定见[工作台布局](docs/WORKSPACE-LAYOUT.md)。

「帮助 → 平台能力」可查看本地 Shell、串口、RDP 及 Windows 凭据保护的运行支持。缺少支持时，编辑器会说明原因并禁用连接，仍可仅保存会话；已有会话的连接页提供「重新检查」，系统支持恢复后可继续连接。串口列表为空或无法读取时仍允许手动输入端口；能力可用不保证设备访问或远程登录成功。

「会话 → 新建根分类」创建顶层文件夹；选中文件夹后用「新建子分类」或右键同名操作创建下一级，可继续嵌套。空分类也会保存。文件夹、终端、远程桌面和文件传输有不同图标。新建会话默认使用当前分类，编辑器支持输入 `工作/交换机/核心` 这样的完整路径；`/` 是分类层级分隔符。搜索命中深层会话时保留祖先文件夹。

会话库可切换「全部 / 收藏 / 最近」；会话菜单或右键菜单可收藏、重命名及分别清空收藏/最近。最近保留 20 个已保存会话的连接尝试，不代表登录成功。搜索框支持清空、无结果提示、上下键定位和 Enter 连接；新增、复制或创建分类后会定位结果。记录重启保留，仅关联会话 ID 与尝试时间，连接继续读取当前配置及原密码库。操作与失败处理见[会话库说明](docs/SESSION-LIBRARY.md)。

终端默认选择已安装的 Consolas，并依次尝试 Cascadia Mono、Lucida Console、Courier New 和系统等宽字体；按字体及 DPI 计算字符格。「设置 → 终端默认设置」可预览并保存字体、字号、样式、前景/背景、选取复制和右键粘贴偏好，供新标签和重启后使用；恢复默认后需点击保存。终端右键菜单的调整仅作用于当前标签。找不到指定字体时安全回退，多行粘贴仍需确认。偏好文件及异常处理见[终端约定](docs/TERMINAL-SESSIONS.md#终端默认偏好)。

点击「新建连接」或首页协议按钮，填写参数后选择「保存并连接」或「仅保存」。左侧双击已保存会话即可连接。Shell 默认使用 Windows PowerShell；也可以设置 `cmd.exe`，参数 `/Q`，或已安装的 `pwsh.exe`。

快捷键：`Ctrl+N` 新建，`Ctrl+W` 关闭标签；终端内 `Ctrl+Shift+C` 复制选择内容（没有选择时复制全部），`Ctrl+Shift+V` 粘贴，鼠标滚轮查看历史。多行粘贴会要求确认。双击标签标题、点击标签右侧 ×、中键点击或使用「窗口」菜单均可关闭标签，关闭前会要求确认；退出软件时若仍有会话标签，也会确认。

RDP 在系统窗口中打开，登录由 Windows 客户端处理。PortHarbor 保存的 RDP 密码不会自动注入 Windows 凭据管理器；有密码时可在标签中点击复制，30 秒后尝试清除本次复制的内容。关闭标签不会关闭远程桌面窗口。

FTP/SFTP 文件列表支持表头排序、保留选择及目录优先；空目录与加载失败分别提示，未选文件时下载禁用。路径框输入后按 Enter 或「前往」导航，修改操作始终使用最近成功加载的目录。传输显示累计字节、真实已知总量和平均速率，未知大小不显示百分比；取消后需刷新并核对结果。详细约定见[文件视图工作流](docs/FILE-WORKFLOW.md)。

## 数据与密码

安装版默认配置：`%LOCALAPPDATA%\PortHarbor\settings.json`。首次启动时，如果发现旧版 `%LOCALAPPDATA%\EasyConnect\settings.json` 且新目录尚无配置，会自动迁移旧配置。

便携版配置：程序旁的 `PortHarbor.ini`，包含 `[General]`、`[Groups]`、`[Session:ID]`、`[Vault]`、`[Credentials]` 和 `[HostKeys]` 分节。值使用 JSON 转义以保存中文、换行和特殊符号；主机密钥名称使用 Base64 编码。该格式独立设计，不兼容 MobaXterm INI。

便携包带有 `PortHarbor.portable` 标记；程序旁已有 INI 也会识别为便携模式。可显式使用 `--portable` 或 `--installed`；`--data-dir <目录>` 在两种模式下都可覆盖数据目录。便携版必须放在可写目录，写入失败时提示错误，不静默切换到用户目录。现有安装版 JSON 不会自动复制到便携版；可通过不含密码的导出/导入迁移会话后重新保存密码。

「设置 → 打开配置目录」可查看实际目录。启动参数和便携标志由 Windows 平台路径服务统一解析；显式 `--data-dir` 只使用指定目录，不会从默认 EasyConnect 位置复制旧配置。写入使用临时文件与原子替换，`.bak` 保留前一次配置；首次启动便携版就会建立空 INI。损坏配置会被保留并报告错误。

密码保存在对应配置的 `Credentials` 中。首次保存非空密码必须设置至少 8 位的主密码并重复确认；之后启动已有密码库时可输入主密码解锁；也可以点击“跳过”进入软件，此时已保存密码不可读取，连接时需临时输入。「设置 → 主密码」可设置或修改，修改前验证当前口令。主密码不会写入磁盘；遗忘后无法恢复密码。

两版均使用主密码派生的密钥进行 AES-256-GCM 加密。安装版密码库版本 1 另外使用当前 Windows 用户的 DPAPI，因此密码绑定原账户；便携版新密码库版本 2 不使用 DPAPI，复制 INI 后可用主密码解锁，不依赖原账户。更改主密码会按当前模式重新加密全部凭据。解锁密钥仅保留在进程内存，退出清除；会话名称、主机等元数据不加密，目前没有自动锁定功能。密码库版本与配置 schema 版本是两套独立编号。

旧版配置会在设置主密码时迁移到 schema v2；迁移失败保留原配置，不丢弃不可解密的旧密码。旧版程序不能读取 v2。迁移或更改主密码前形成的 `.bak`、自行复制的旧配置仍按原来的密码/DPAPI 规则保护，不会自动被新主密码重新加密；请自行保管这些备份。设计依据见[主密码保护决策](docs/adr/0001-master-password.md)。

导出功能仅输出会话参数及分类，不含密码库元数据、加密密码或主机信任记录；导入会分配新 ID，并关闭保存密码选项。私钥文件由用户自行保管，配置只记录其路径。

首次 SSH / SFTP 连接会显示 SHA256 主机指纹，核实后才应信任。后续指纹变化将拒绝连接；确认服务器确已更换密钥后，关闭应用，在配置文件 `HostKeys` 中移除对应条目再重新连接。Telnet 和普通 FTP 的流量不加密；FTP 显式 TLS 使用正常证书验证。

可用独立数据目录启动：

```powershell
.\PortHarbor.exe --data-dir 'D:\MyConnections'
```

同一配置目录请只运行一个实例；当前没有跨进程配置合并。

## 已知范围

- 内置终端支持 UTF-8、基础 ANSI/VT、光标移动、滚动区、16/256/真彩色、备用屏幕、CJK 双宽字符和 2000 行历史；尚未达到完整 xterm 兼容性，复杂 TUI、输入法组合、多种编码和终端鼠标协议仍需后续完善。
- 文件视图支持多文件串行传输队列，每个文件标签一次执行一个操作；暂不提供递归目录传输、断点续传或自动重连。FTP/SFTP 操作使用异步调用，每个操作有超时并传递取消；底层库或服务器响应取消仍有延迟时，关闭会等待当前操作及有限时长的远端临时文件清理。失败暂停等待任务，核对结果后显式继续或重试。
- 下载先写本地临时文件，完成后替换目标。上传先写远程 `.part`，再重命名。SFTP 暂存旧文件后再重命名替换，失败恢复和成功后的备份清理各自限时 5 秒；替换期间目标路径可能短暂不存在，断线或确认丢失时需通过新连接核对目标及 `.part` / `.bak`，不自动重放。FTP 覆盖取决于服务器语义。
- RDP 采用外部系统客户端，没有内嵌桌面或自动填写密码；串口没有实体设备检测报告。
- 暂不包含 X11、代理跳板、SSH Agent、隧道、宏、自动化脚本、连接日志或自动更新。
- 当前为 Windows 专用预览版，可用于试用和继续开发，尚未在多种真实服务器及串口设备上完成兼容性认证。

## 从源码构建

安装 .NET 8 SDK，在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

也可以指定便携 SDK 和离线 NuGet 源：

```powershell
.\build.ps1 -Dotnet 'D:\Code\rea-work\tools\dotnet\dotnet.exe' -PackageSource 'D:\Code\rea-work\tools\nuget-feed'
```

该命令保留原工程验收流程：项目内缓存、锁定依赖恢复、编译、核心/Windows 检查和发布冒烟，生成历史安装目录布局/便携 ZIP 到 `artifacts`；默认不含 .NET，可用 `-IncludeRuntime`。依赖版本与哈希见 `src\PortHarbor\packages.lock.json`。

生成新的单文件便携版与安装向导，使用 PowerShell 7：

```powershell
pwsh -File packaging/Get-InnoSetup.ps1 -Download
pwsh -File build.ps1 -P10
```

工具仅缓存到 `.build-home`；最终四个产物在 `artifacts/releases`，发行检查与日志在 `artifacts/distribution-validation`。P10 构建执行单文件发布和隔离安装/模拟升级/卸载验收；原有核心、系统、UI、协议回归仍使用上面的工程入口。详见[发行说明](docs/DISTRIBUTION.md)。

发布冒烟会等待实际 EXE 退出并检查退出码，分别验证隔离数据目录的安装模式，以及不指定 `--data-dir` 的默认便携首次启动和重启；便携模式必须在程序旁创建 INI。报告为 `startup-smoke-results.json`，冒烟生成的空配置不进入发行包。

要测完整 Windows 基线（另含协议回环、WinForms 渲染和 ConPTY 交互），先安装 `tests/requirements.txt` 中的测试依赖，再运行 `./build.ps1 -ExtendedBaseline -OutputDirectory ./artifacts/engineering-baseline`。该入口要求 Python 3.12+、当前 Windows 用户的可交互桌面环境（DPAPI 检查需要该用户的凭据保护能力）；所有计时与检查数量写入 `engineering-baseline.json`，截图和 UI/协议报告仅位于忽略的 `artifacts` 目录。协议集成失败时，同目录保留 `protocol-fixture-out.log` 和 `protocol-fixture-error.log` 供本地排查。

若旧发行目录中的程序仍在运行，可通过 `./build.ps1 -OutputDirectory ./artifacts/preview` 输出到独立目录，避免覆盖正在使用的文件。切换新版前先关闭旧程序，同一配置目录不要同时运行两版。

发布回滚可在当前 Windows 用户环境复核：`powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\build-rollback.ps1 -OutputDirectory .\artifacts\rollback-validation`。该检查会在发布替换完成后注入失败，并确认上一版目录和 ZIP 仍然存在。

```text
src/PortHarbor.Domain/          无 UI 的会话、诊断、终端状态、Telnet 和文件传输契约
src/PortHarbor.Application/     无 UI 的会话/配置/导入导出/文件传输/终端/RDP/路径编排及应用端口
src/PortHarbor.Infrastructure/  Windows 配置存储、凭据、FTP/SFTP 及 SSH/Telnet 适配器
src/PortHarbor.Platform.Windows/ 能力检测、DPAPI、mstsc、串口、本地 Shell/ConPTY 及串口设备/选项提供器
src/PortHarbor.Platform.Windows.Forms/ 原生打开/保存文件对话框适配；Application 选择端口不含窗口类型
src/PortHarbor/                 界面、终端渲染及 Windows 组合入口
tests/PortHarbor.Application.Checks/  跨平台会话、配置、文件传输及终端生命周期检查
tests/Shared/                  两个检查项目共用的用例检查与脚本化适配器
tests/PortHarbor.Checks/        分层检查入口和 Windows UI 检查
tests/protocol_servers.py       仅监听本机的 FTP / SSH / SFTP 测试服务
.github/workflows/              Windows CI 工作流
licenses/                       随包分发的第三方许可文本
build.ps1                       构建和发布
```

## 验证

本轮完成 T1–T5 软件收尾：发布失败恢复、网络/同步通道超时、后台生命周期和纯逻辑审计、本地诊断设置与可审阅导出、七协议运行配置类型化。发布替换前失败、部分替换、旧 ZIP 占用、回滚受阻及成功替换五个场景通过，回滚受阻保留备份与恢复路径。

最终完整扩展基线通过 Application 175、核心 366、Windows 17、协议 96、UI 61，共 **715 项检查及四项冒烟**，Release 编译 0 警告、0 错误。报告与双版本 ZIP 在 ignored `artifacts/technical-closure/release/`。既有 SFTP 提交/恢复的 9 个故障场景继续通过；确认丢失仍需显式核对，不自动重放。

当前 Windows 技术主线清单完成 **49/51 项**，剩余 T6 标准测试迁移和物理断网/串口等环境故障验收；真实 RDP、FTPS、私钥及多服务器互通继续专项验证。产品 P01–P10 已实现，P10 增加单文件便携与当前用户安装向导，升级/卸载保留数据。干净 Windows 发行验收、多 DPI/高对比度及真实服务器体验待补，当前证据见[发行说明](docs/DISTRIBUTION.md)及[技术方案第 13 节](docs/TECHNICAL-PLAN.md#13-用户体验ui-交互与美观功能开发计划)。该比例是清单统计，不是工时或源码覆盖率；新平台/更换框架属于阶段 4。

上一轮 SSH 掉线及 FTP 连接失败诊断的完整结果为 583 项，报告为 ignored `artifacts/network-failures/release/engineering-baseline.json`。这些正常退出、主动取消、拒绝连接、连接重置与错误密码场景均在本轮继续回归，不引入自动重连。

2026-10-27 对提交 `e110c4e`（`v1.0.5`）进行了 Windows 本机独立干净克隆验收，没有复制原目录的构建产物或依赖缓存。锁定恢复和 Release 编译通过（0 警告、0 错误）；Application 151、核心 320、Windows 14、协议回环 26、UI 57 项检查全部通过，共 568 项。ConPTY、安装隔离目录、默认便携首次启动及重启四项冒烟也通过。原始结果位于 ignored `artifacts/clean-checkout-acceptance-20261027/artifacts/baseline/`；该数字为检查计数，不是源码覆盖率。

检查包括默认便携路径与 INI 创建、消息资源完整性、未知枚举/语言回退、全协议提示脱敏、JSON/INI 交叉格式和原子回滚。此次是同一台机器上的独立克隆验证；此前系统 Temp 克隆的 FTP 550 根因尚未确认，另一台机器复现仍需验收。逐次记录见 [开发日志](docs/DEVELOPMENT-LOG.md)，当前约定见 [诊断消息](docs/DIAGNOSTICS.md)和[会话配置导入导出](docs/CONFIGURATION-TRANSFER.md)。

RDP 验证覆盖配置、启动失败、进程退出清理及界面重试，不含真实远程桌面登录；串口尚无实体设备测试。FTPS、私钥登录和其他服务器实现也尚未完成实机互通测试。

快速核心检查、Windows 专属检查和 UI 冒烟可以分别运行：

```powershell
dotnet run --project tests/PortHarbor.Checks -c Release -- --unit --results artifacts/unit-results.json
dotnet run --project tests/PortHarbor.Checks -c Release -- --terminal --results artifacts/terminal-results.json
dotnet run --project tests/PortHarbor.Application.Checks -c Release -- --results artifacts/application-results.json
dotnet run --project tests/PortHarbor.Checks -c Release -- --windows --results artifacts/windows-results.json
dotnet run --project tests/PortHarbor.Checks -c Release -- --ui "$env:TEMP\portharbor-ui.png"
dotnet run --project tests/PortHarbor.Checks -c Release -- --ui-preferences "$env:TEMP\portharbor-preferences.png"
dotnet run --project tests/PortHarbor.Checks -c Release -- --ui-workspace "$env:TEMP\portharbor-workspace.png"
dotnet run --project tests/PortHarbor.Checks -c Release -- --ui-shell "$env:TEMP\portharbor-terminal.txt"
```

完整协议回环测试需要 Python 及测试专用依赖，应用本身不需要 Python。先启动本地 fixture，再运行协议集成入口：

```powershell
python -m pip install -r tests/requirements.txt
# 在第一个终端运行，结束后按 Ctrl+C
python tests/protocol_servers.py --root artifacts/protocol-fixture
# 在第二个终端运行；包括 Telnet、FTP、SFTP 和 SSH 回环检查
dotnet run --project tests/PortHarbor.Checks -c Release -- --integration artifacts/protocol-fixture/servers.json --results artifacts/integration-results.json
```

GitHub Actions 在 Windows Runner 上分别执行核心、Windows、协议和 UI 检查，并上传 JSON 结果报告。`build.ps1` 执行核心与 Windows 检查；发布文件先写入 `artifacts` 下的暂存目录，全部成功后再替换现有 ZIP 和发布目录。

测试使用固定的本机专用账号 `easytest` / `easytest-only`，服务仅绑定 `127.0.0.1`，操作限制在指定测试目录。

## 许可证

PortHarbor 自有代码尚未采用开源许可证；获取仓库或预览构建不代表获得复制、修改或再分发项目代码的授权。第三方依赖仍按各自许可证提供，相关许可文本和声明见 `THIRD-PARTY-NOTICES.md` 与 `licenses/`。

## 文档

GitHub 文档入口见 [`docs/README.md`](docs/README.md)。技术方案、开发日志、文件传输约定、终端会话约定和 ADR 均从该页面进入。

开发日志 [`docs/DEVELOPMENT-LOG.md`](docs/DEVELOPMENT-LOG.md) 按时间倒序记录每轮开发、验证结果、已知边界和下一步；历史条目中的“下一步”不覆盖技术方案中的当前安排。



