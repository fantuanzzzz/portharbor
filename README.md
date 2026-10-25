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
| Serial | COM 口、波特率、数据位、校验、停止位和流控设置 |
| Shell | Windows ConPTY 内嵌交互命令行，可配置程序、参数和工作目录 |
| 会话 | 保存、分类文件夹（图标、嵌套和空分类）、搜索、复制、编辑、删除、JSON 导入和导出、多标签连接 |
| 密码 | 可选保存，主密码加密；安装版叠加 Windows 用户级 DPAPI；导出会话不含密码 |

## 运行

需要 Windows 10 1809 或更新版本 / Windows 11，x64。

预览阶段提供两个 x64 目录包，默认都需要 .NET 8 Desktop Runtime x64：

- `PortHarbor-0.1.0-installed-win-x64.zip`：安装版目录布局，配置放在用户目录。目前是 ZIP 预览包，尚未提供安装向导。
- `PortHarbor-0.1.0-portable-win-x64.zip`：便携版，解压到可写目录，直接运行 `PortHarbor.exe`；首次启动在程序旁创建 `PortHarbor.ini`。整个目录可一起移动。

安装版目录中的 `Start-PortHarbor.cmd` 会检测运行环境。`-IncludeRuntime` 仅控制两种包是否包含 .NET 环境，与安装/便携数据模式独立。

应用需要 Windows 系统组件支持 RDP、ConPTY 和 DPAPI；RDP 使用 Windows 自带的远程桌面客户端。

菜单分为「文件 / 会话 / 窗口 / 设置 / 帮助」，各项操作在下拉菜单中。

「会话 → 新建根分类」创建顶层文件夹；选中文件夹后用「新建子分类」或右键同名操作创建下一级，可继续嵌套。空分类也会保存。文件夹、终端、远程桌面和文件传输有不同图标。新建会话默认使用当前分类，编辑器支持输入 `工作/交换机/核心` 这样的完整路径；`/` 是分类层级分隔符。搜索命中深层会话时保留祖先文件夹。

终端默认选择已安装的 Consolas，并依次尝试 Cascadia Mono、Lucida Console、Courier New 和系统等宽字体；按字体及 DPI 计算字符格。终端右键菜单可打开字体、字号、样式、前景色和背景色设置；左键自动复制与右键直接粘贴也可在菜单中分别启用。

点击「新建连接」或首页协议按钮，填写参数后选择「保存并连接」或「仅保存」。左侧双击已保存会话即可连接。Shell 默认使用 Windows PowerShell；也可以设置 `cmd.exe`，参数 `/Q`，或已安装的 `pwsh.exe`。

快捷键：`Ctrl+N` 新建，`Ctrl+W` 关闭标签；终端内 `Ctrl+Shift+C` 复制选择内容（没有选择时复制全部），`Ctrl+Shift+V` 粘贴，鼠标滚轮查看历史。多行粘贴会要求确认。双击标签标题、点击标签右侧 ×、中键点击或使用「窗口」菜单均可关闭标签，关闭前会要求确认；退出软件时若仍有会话标签，也会确认。

RDP 在系统窗口中打开，登录由 Windows 客户端处理。PortHarbor 保存的 RDP 密码不会自动注入 Windows 凭据管理器；有密码时可在标签中点击复制，30 秒后尝试清除本次复制的内容。关闭标签不会关闭远程桌面窗口。

## 数据与密码

安装版默认配置：`%LOCALAPPDATA%\PortHarbor\settings.json`。首次启动时，如果发现旧版 `%LOCALAPPDATA%\EasyConnect\settings.json` 且新目录尚无配置，会自动迁移旧配置。

便携版配置：程序旁的 `PortHarbor.ini`，包含 `[General]`、`[Groups]`、`[Session:ID]`、`[Vault]`、`[Credentials]` 和 `[HostKeys]` 分节。值使用 JSON 转义以保存中文、换行和特殊符号；主机密钥名称使用 Base64 编码。该格式独立设计，不兼容 MobaXterm INI。

便携包带有 `PortHarbor.portable` 标记；程序旁已有 INI 也会识别为便携模式。可显式使用 `--portable` 或 `--installed`；`--data-dir <目录>` 在两种模式下都可覆盖数据目录。便携版必须放在可写目录，写入失败时提示错误，不静默切换到用户目录。现有安装版 JSON 不会自动复制到便携版；可通过不含密码的导出/导入迁移会话后重新保存密码。

「设置 → 打开配置目录」可查看实际目录。写入使用临时文件与原子替换，`.bak` 保留前一次配置；首次启动便携版就会建立空 INI。损坏配置会被保留并报告错误。

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
- 文件视图一次执行一个操作，暂不提供递归目录传输、断点续传、传输队列或自动重连。FTP/SFTP 操作使用异步调用，每个操作有超时并传递取消；底层库或服务器响应取消仍有延迟时，关闭会等待当前操作及有限时长的远端临时文件清理。
- 下载先写本地临时文件，完成后替换目标。上传先写远程 `.part`，再重命名。SFTP 暂存旧文件后再重命名替换；替换期间目标路径可能短暂不存在，断线时可能留下 `.part` / `.bak`，并非所有服务器上都能保证原子替换。FTP 覆盖取决于服务器语义。
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

脚本在项目内缓存构建数据，恢复依赖、编译并执行检查，然后同时生成安装版目录布局和便携版 ZIP，放在 `artifacts`。默认都不包含 .NET 环境；需要包含时运行 `./build.ps1 -IncludeRuntime`。安装向导尚未实现。依赖版本与哈希见 `src\PortHarbor\packages.lock.json`。

若旧发行目录中的程序仍在运行，可通过 `./build.ps1 -OutputDirectory ./artifacts/preview` 输出到独立目录，避免覆盖正在使用的文件。切换新版前先关闭旧程序，同一配置目录不要同时运行两版。

```text
src/PortHarbor/                 界面、连接、终端及数据存储
tests/PortHarbor.Checks/        分层检查入口和 Windows UI 检查
tests/protocol_servers.py       仅监听本机的 FTP / SSH / SFTP 测试服务
.github/workflows/              Windows CI 工作流
licenses/                       随包分发的第三方许可文本
build.ps1                       构建和发布
```

## 验证

最近一轮预览优化验证包含核心存储/终端/文件传输策略、主密码及旧配置迁移、Windows ConPTY 与生命周期，以及主窗口、协议编辑器、嵌套分类、图标、下拉菜单、等宽字体和标签鼠标关闭路径。逐次检查数量、失败修复过程和结果见 [开发日志](docs/DEVELOPMENT-LOG.md)。此前还完成过本机 FTP/SFTP/SSH/Telnet 协议回环检查；本次 UI 与密码库调整未重新运行协议服务。

RDP 验证覆盖配置生成，不含真实远程桌面登录；串口尚无实体设备测试。FTPS、私钥登录和其他服务器实现也尚未完成实机互通测试。

快速核心检查、Windows 专属检查和 UI 冒烟可以分别运行：

```powershell
dotnet run --project tests/PortHarbor.Checks -c Release -- --unit --results artifacts/unit-results.json
dotnet run --project tests/PortHarbor.Checks -c Release -- --windows --results artifacts/windows-results.json
dotnet run --project tests/PortHarbor.Checks -c Release -- --ui "$env:TEMP\portharbor-ui.png"
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
## Development Workbook

Every development session is recorded in [`docs/DEVELOPMENT-LOG.md`](docs/DEVELOPMENT-LOG.md). Each entry includes the development scope, changed files, verification results, known problems, and the next step. Failed checks and environment limitations are recorded as well.

文件传输接口、协议适配器、扩展方式及当前取消语义见 [文件传输接口与扩展约定](docs/FILE-TRANSFER.md)。



