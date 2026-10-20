# PortHarbor

轻量的 Windows 连接工作台，使用 C# / .NET 8 / WinForms 编写，MIT 开源。当前版本为 **0.1.0 预览版**。原生界面和终端，不依赖 Electron、浏览器运行时或内置 Linux 发行版。

本项目独立实现常用连接管理功能，本项目与其他同名产品没有关联。

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
| 会话 | 保存、分组、搜索、复制、编辑、删除、JSON 导入和导出、多标签连接 |
| 密码 | 可选保存，使用 Windows 用户级 DPAPI 加密；导出会话不含密码 |

## 运行

需要 Windows 10 1809 或更新版本 / Windows 11，x64。

发行包提供两种选择：

- `PortHarbor-0.1.0-portable-win-x64.zip`：自包含单文件包，包含 .NET 运行环境，解压后双击 `PortHarbor.exe` 即可运行，不需要另外安装 .NET Desktop Runtime。
- `PortHarbor-0.1.0-framework-win-x64.zip`：体积较小，需要 .NET 8 Desktop Runtime x64。双击 `Start-PortHarbor.cmd` 会先检测运行环境；缺少时提示并打开微软下载页。该版本的 `PortHarbor.exe` 也可在已安装运行环境的机器上直接启动。

两个版本都需要 Windows 系统组件支持 RDP、ConPTY 和 DPAPI；RDP 使用 Windows 自带的远程桌面客户端。

点击「新建连接」或首页协议按钮，填写参数后选择「保存并连接」或「仅保存」。左侧双击已保存会话即可连接。Shell 默认使用 Windows PowerShell；也可以设置 `cmd.exe`，参数 `/Q`，或已安装的 `pwsh.exe`。

快捷键：`Ctrl+N` 新建，`Ctrl+W` 关闭标签；终端内 `Ctrl+Shift+C` 复制选择内容（没有选择时复制全部），`Ctrl+Shift+V` 粘贴，鼠标滚轮查看历史。多行粘贴会要求确认。中键点击标签可关闭连接。

RDP 在系统窗口中打开，登录由 Windows 客户端处理。PortHarbor 保存的 RDP 密码不会自动注入 Windows 凭据管理器；有密码时可在标签中点击复制，30 秒后尝试清除本次复制的内容。关闭标签不会关闭远程桌面窗口。

## 数据与密码

默认配置：`%LOCALAPPDATA%\PortHarbor\settings.json`。首次启动时，如果发现 `%LOCALAPPDATA%\EasyConnect\settings.json` 且新目录尚无配置，会迁移旧配置。工具栏「配置目录」可以打开所在文件夹。写入采用临时文件加原子替换，`.bak` 保留前一次配置（其中也可能包含旧的加密密码）。损坏配置会被保留并报告错误，不会静默覆盖。

密码由当前 Windows 用户的 DPAPI 保护，复制配置到另一账户或电脑通常不能解密。导出功能仅输出会话参数，不含加密密码或主机信任记录；导入会分配新 ID，并关闭保存密码选项。私钥文件由用户自行保管，配置只记录其路径。

首次 SSH / SFTP 连接会显示 SHA256 主机指纹，核实后才应信任。后续指纹变化将拒绝连接；确认服务器确已更换密钥后，关闭应用，在配置文件 `HostKeys` 中移除对应条目再重新连接。Telnet 和普通 FTP 的流量不加密；FTP 显式 TLS 使用正常证书验证。

可用独立数据目录启动：

```powershell
.\PortHarbor.exe --data-dir 'D:\MyConnections'
```

同一配置目录请只运行一个实例；当前没有跨进程配置合并。

## 已知范围

- 内置终端支持 UTF-8、基础 ANSI/VT、光标移动、滚动区、16/256/真彩色、备用屏幕、CJK 双宽字符和 2000 行历史；尚未达到完整 xterm 兼容性，复杂 TUI、输入法组合、多种编码和终端鼠标协议仍需后续完善。
- 文件视图一次执行一个操作，暂不提供递归目录传输、断点续传、传输队列或自动重连。取消在数据块之间检查，阻塞中的网络操作仍需等待超时。
- 下载先写本地临时文件，完成后替换目标。上传先写远程 `.part`，再重命名。SFTP 优先使用 POSIX 覆盖；服务器不支持时暂存旧文件再替换，断线时可能留下 `.part` / `.bak`，并非所有服务器上都能保证原子替换。FTP 覆盖取决于服务器语义。
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

脚本在项目内缓存构建数据，恢复依赖、编译并执行检查，然后生成自包含和框架依赖两个 x64 发行 ZIP，放在 `artifacts`。自包含包把 .NET 运行环境打入单文件；框架依赖包附带 `Start-PortHarbor.cmd` 启动检测。运行时依赖版本与哈希记录在 `src\EasyConnect\packages.lock.json`。项目结构：

```text
src/EasyConnect/               界面、连接、终端及数据存储
tests/EasyConnect.Checks/      无测试框架依赖的检查程序
tests/protocol_servers.py     仅监听本机的 FTP / SSH / SFTP 测试服务
licenses/                     随包分发的第三方许可文本
build.ps1                     构建和发布
```

## 验证

本次 Windows 环境检查通过 42 项：DPAPI 和会话恢复、无密码导出、SSH 密钥信任、终端控制序列、Telnet 分片协商及真实 TCP 输入、本地 ConPTY Shell、取消下载保留原文件、FTP / SFTP 中文名称及上传下载覆盖重命名删除、SSH 登录及 PTY 输入断开。另完成主窗口和七种协议编辑页的界面启动检查。

RDP 验证覆盖配置生成，不含真实远程桌面登录；串口尚无实体设备测试。FTPS、私钥登录和其他服务器实现也尚未完成实机互通测试。

基本检查可单独运行：

```powershell
dotnet run --project tests/EasyConnect.Checks -c Release -- --results artifacts/check-results.json
```

完整回环测试需要 Python 及测试专用依赖，应用本身不需要 Python：

```powershell
python -m pip install -r tests/requirements.txt
# 在第一个终端运行，结束后按 Ctrl+C
python tests/protocol_servers.py --root artifacts/protocol-fixture
# 在第二个终端运行
dotnet run --project tests/EasyConnect.Checks -c Release -- --integration artifacts/protocol-fixture/servers.json --results artifacts/integration-results.json
```

测试使用固定的本机专用账号 `easytest` / `easytest-only`，服务仅绑定 `127.0.0.1`，操作限制在指定测试目录。

## 许可

项目代码使用 MIT 许可证，见 `LICENSE`。依赖及其许可证见 `THIRD-PARTY-NOTICES.md` 与 `licenses/`。开源代码已提供，尚未发布到公共代码托管平台。
