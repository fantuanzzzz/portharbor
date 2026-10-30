# PortHarbor

<img src="assets/portharbor.svg" width="88" height="88" alt="PortHarbor 图标">

**一个轻量、原生的 Windows 多协议连接工作台。**

在同一个窗口管理 SSH、Telnet、本地 Shell、串口、FTP/SFTP 和远程桌面入口，提供会话分类、多标签终端与文件传输队列。

Windows x64 · C# / .NET 8 / WinForms · MIT License · **0.2.0 预览版**

[下载与版本记录](https://github.com/fantuanzzzz/portharbor/releases) · [使用与开发文档](docs/README.md) · [报告问题](https://github.com/fantuanzzzz/portharbor/issues) · [MIT 许可](LICENSE)

> 当前处于首个公开预览阶段，适合试用、反馈和参与开发。真实串口、多种服务器、混合 DPI 及干净 Windows 等环境验收仍在进行；重要会话和密码库请先备份。

## 可以做什么

| 能力 | 当前支持 |
| --- | --- |
| SSH | 密码、私钥及 keyboard-interactive 交互认证；主机指纹确认；交互终端与窗口大小同步 |
| Telnet | 基础选项协商、VT100 终端类型、IAC 转义 |
| 本地 Shell | 通过 Windows ConPTY 内嵌 PowerShell、cmd 或其他兼容命令行 |
| 串口 | COM 口、波特率、数据位、校验、停止位和流控参数 |
| FTP / SFTP | 浏览、上传、下载、新建目录、重命名、删除文件或空目录；FTP 支持显式 TLS |
| 文件队列 | 多文件串行上传/下载、逐项进度、覆盖确认、取消、失败暂停及核对后的显式重试 |
| 远程桌面 | 调用 Windows mstsc，支持端口、用户名、全屏及剪贴板选项 |
| 会话管理 | 嵌套分类、收藏、最近连接、搜索、复制、重命名、JSON 导入/导出 |
| 工作台 | 多标签、浅色/深色主题、布局记忆、终端字体/配色、历史查找与快捷键 |
| 凭据保护 | 主密码加密；安装模式叠加 Windows 用户级 DPAPI；会话导出不包含密码 |

采用原生 Windows 界面和终端，不依赖 Electron、浏览器运行时或内置 Linux 发行版。

## 下载与运行

到 [GitHub Releases](https://github.com/fantuanzzzz/portharbor/releases) 选择对应预览版的 **Assets**。如果尚未看到发行附件，说明该版本还在准备上传，可按下文从源码构建。

要求 **Windows 10 1809 或更新版本 / Windows 11，x64**。发布的两个 EXE 均自带 .NET 8 Desktop Runtime，无需另装 .NET。

| 选择 | 文件 | 用法 |
| --- | --- | --- |
| 便携版 | `PortHarbor-0.2.0-portable-win-x64.exe` | 放到可写文件夹双击运行，无需安装 |
| 安装版 | `PortHarbor-0.2.0-setup-win-x64.exe` | 中文/英文当前用户安装向导，提供开始菜单、可选桌面快捷方式及卸载入口 |

发布时附带的 `release-manifest.json` 记录文件大小、SHA-256 与构建身份，可用于核对下载文件：

```powershell
Get-FileHash .\PortHarbor-0.2.0-portable-win-x64.exe -Algorithm SHA256
```

当前构建尚未代码签名；如果系统提示来源或信誉未知，请先确认下载来自本项目 Release 并核对哈希。不要关闭系统安全防护来运行来源不明的副本。

### 第一次连接

1. 打开程序，点击「新建连接」或首页协议按钮。
2. 选择协议，填写主机、端口及相应认证/设备参数。
3. 选择「仅连接」「仅保存」或「保存并连接」。已保存会话可在左侧双击连接。
4. 首次 SSH/SFTP 连接时，核对服务器指纹后再信任；需要保存密码时先设置主密码。

会话名称可留空，默认使用主机/IP；SSH/SFTP 用户名必填。会话库统一在 `User sessions` 下管理，分类入口合并为「新建分类」：选中根节点新建顶层分类，选中已有分类则新建子分类。

本地 Shell 可使用 Windows PowerShell，或指定已安装的 `cmd.exe` / `pwsh.exe`。RDP 在 Windows 自带客户端窗口中打开；PortHarbor 显示“已启动”不代表远端登录已成功，关闭标签不会关闭 mstsc。

### 常用快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Ctrl+N` | 新建连接 |
| `Ctrl+W` | 关闭当前标签 |
| `Ctrl+Shift+F` | 搜索会话 |
| `Ctrl+Shift+B` | 显示/隐藏会话库 |
| `F6` / `Shift+F6` | 切换工作区焦点 |
| `Enter` / `F2`（会话树） | 连接 / 编辑会话 |
| `Ctrl+Shift+C` / `Ctrl+Shift+V`（终端） | 复制 / 粘贴；多行粘贴需要确认 |

## 数据保存在哪里

| 模式 | 程序位置 | 数据位置 |
| --- | --- | --- |
| 便携 | 你选择的可写文件夹 | EXE 旁的 `PortHarbor.ini`，以及偏好、活动记录和日志 |
| 安装 | 默认 `%LOCALAPPDATA%\Programs\PortHarbor` | `%LOCALAPPDATA%\PortHarbor` |

- 「设置 → 打开配置目录」可查看实际使用目录。备份或移动便携副本时，应携带整个使用目录。
- “单文件”指下载交付为一个 EXE。首次使用会产生数据文件，.NET 原生组件也可能解包到用户临时缓存。
- 升级前退出程序；安装版卸载保留用户数据和未知文件。安装/便携模式不自动相互迁移，可通过不含密码的会话导出/导入迁移连接配置。
- 同一配置目录只允许一个实例；空 `.portharbor.lock` 文件会保留，无需删除。需要独立工作区时可指定其他目录：

```powershell
.\PortHarbor-0.2.0-portable-win-x64.exe --portable --data-dir 'D:\MyConnections'
```

完整的模式优先级、升级/卸载及发行约定见[发行说明](docs/DISTRIBUTION.md)。

## 密码与连接安全

- 保存非空密码前，需要设置至少 8 位主密码；主密码不会写入磁盘，遗忘后无法恢复保存的密码。
- 两种模式均使用主密码派生密钥和 AES-256-GCM。安装模式额外使用当前 Windows 用户的 DPAPI，密文不能直接移到另一账户解锁；便携模式新密码库使用主密码保护。
- 会话名称、主机等元数据不加密；目前没有自动锁定功能。旧配置备份不会随更改主密码而重新加密，请妥善保管。
- 会话导出不含密码库、保存的密码和主机信任记录；私钥文件由用户自行保管，配置只记录其路径。
- SSH/SFTP 指纹变化会拒绝连接。Telnet 和普通 FTP 不加密；显式 FTPS 校验证书。
- 本地诊断只记录允许的状态、错误码、时长和计数。可在「帮助」中预览后导出诊断包，软件不自动上传。

认证细节见[SSH 认证说明](docs/SSH-AUTHENTICATION.md)，日志字段与边界见[诊断说明](docs/DIAGNOSTICS.md)。

## 预览版的已知限制

- 内置终端支持 UTF-8、基础 ANSI/VT、16/256/真彩色、备用屏幕、CJK 双宽字符与历史缓冲，尚未达到完整 xterm 兼容性；复杂 TUI、输入法组合、多种编码及鼠标协议仍待完善。
- 文件传输不支持递归目录传输、断点续传、目录同步或自动重连。当前文件连接期限为 15 秒，单次文件操作整体期限为 30 秒，慢速大文件可能超时。
- 上传/覆盖使用暂存文件；断线或提交确认丢失可能留下 `.part` / `.bak`。请先核对远端目标与备份，再显式恢复或重试，软件不会自动重放未知结果的写入。
- RDP 使用外部 mstsc，不提供内嵌桌面或自动填写密码；串口、真实 RDP 登录、FTPS 及不同服务器实现仍需要更多实机验证。
- 暂不提供 X11、代理跳板、SSH Agent、隧道、宏、终端内容录制、自动更新或非 Windows UI。

实机环境与执行步骤见[验收手册](docs/REAL-ENVIRONMENT-ACCEPTANCE.md)。功能实现、本机自动测试通过和真实设备兼容性验收分别记录。

## 从源码构建

开发环境：Windows x64、Git、.NET 8 SDK 和 PowerShell 7（`pwsh`）。可选协议回环测试还需要 Python 3.12+；应用运行不需要 Python。

```powershell
git clone https://github.com/fantuanzzzz/portharbor.git
Set-Location portharbor

# 恢复依赖、运行基线检查并生成历史目录 ZIP
pwsh -File build.ps1

# 获取并验证固定版本的 Inno Setup；只需首次准备工具时执行
pwsh -File packaging/Get-InnoSetup.ps1 -Download

# 生成自包含便携 EXE、安装器和发行验证报告
pwsh -File build.ps1 -P10
```

最终体验产物位于 `artifacts/releases/`；构建缓存、测试结果和本地历史档案不纳入源码提交。完整测试、协议 fixture、格式检查及 CI 入口见[测试说明](docs/TESTING.md)。

源码划分为 Domain、Application、Infrastructure、Windows Platform 和 WinForms UI；平台和网络适配器通过应用端口接入，依赖方向见[技术方案](docs/TECHNICAL-PLAN.md)。

## 反馈与参与

欢迎通过 [Issues](https://github.com/fantuanzzzz/portharbor/issues) 反馈问题或提出建议。问题报告建议包含：

- 版本、安装/便携模式、Windows 版本及显示缩放比例。
- 使用的协议、复现步骤、预期结果与实际结果。
- 脱敏截图或已检查过的诊断包；请勿上传密码、私钥、真实配置或业务文件。

修复和功能改动请附验证方式；存储、凭据、协议或生命周期变更需要相应回归测试。当前技术规划、模块约定和架构决策从[文档索引](docs/README.md)进入。

## 许可证

PortHarbor 自有代码及项目文档采用 [MIT License](LICENSE)。

Copyright (c) 2026 fantuanzzzz

第三方组件保留各自版权和许可条款，见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 与 [licenses/](licenses/)。项目许可和第三方许可均已嵌入新构建，可在「帮助 → 第三方许可」离线查看。
