# PortHarbor v0.2.0 预览版

首个公开预览版本：Windows 原生多协议连接工作台，采用 MIT License，作者 fantuanzzzz。请在 GitHub Release 中将本版本标记为 **Pre-release**。

本版提供 SSH/Telnet 终端、Windows 本地 Shell、串口、外部 RDP、FTP/SFTP 文件管理与多文件串行队列，包含会话分类/收藏/最近/搜索、浅色/深色主题、终端偏好和主密码保护。

两个 EXE 都自带 .NET 8 桌面运行时，适用于 Windows 10 1809+ / Windows 11 x64，无需另装 .NET。

| 文件 | 使用方式 | 数据位置 |
| --- | --- | --- |
| `PortHarbor-0.2.0-portable-win-x64.exe` | 放到可写文件夹，双击运行；无需安装 | EXE 旁的 `PortHarbor.ini`、偏好及日志；移动时带上整个使用目录 |
| `PortHarbor-0.2.0-setup-win-x64.exe` | 双击进入中文/英文安装向导，可选择目录及桌面快捷方式 | `%LOCALAPPDATA%\PortHarbor`；程序默认放在 `%LOCALAPPDATA%\Programs\PortHarbor` |

单文件指**交付时只有一个程序文件**。便携版首次启动会生成 INI，后续按使用情况产生偏好、主机密钥及日志等数据。.NET 原生库可能解包到系统用户临时缓存（通常 `%TEMP%\.net`）；无需手工复制 DLL，也无需联网下载运行时。

安装版会创建开始菜单快捷方式，并出现在 Windows“设置 → 应用”中。升级前先退出程序，再运行新版安装器；默认沿用上次目录。卸载只删除安装器管理的程序及快捷方式，保留会话、凭据、偏好、日志和目录中的其他文件。不要安装到已有便携副本或数据目录。两种模式不会自动互相迁移；可通过会话导出/导入迁移会话，导出不含密码。

PortHarbor 采用 MIT License 开源，Copyright (c) 2026 fantuanzzzz。项目 MIT 正文与第三方许可已嵌入 EXE，可在“帮助 → 第三方许可”查看；第三方组件保留各自版权与条款。当前为预览版，尚未签名，不包含自动更新服务。

`release-manifest.json` 记录文件大小、SHA-256、提交及源码快照和相对验收报告位置；`SHA256SUMS.txt` 可核对全部附件，`LICENSE` 为项目 MIT 正文。验收报告保存在构建仓库，不作为发行附件。本机验收不能代替干净 Windows、真实服务器和多 DPI 验收。安装器由 [Inno Setup](https://jrsoftware.org/) 生成。

预览边界：终端尚非完整 xterm；文件传输不提供递归目录、断点续传或自动重连，断线后需核对 `.part` / `.bak`；RDP 调用系统 mstsc。真实串口、不同服务器、混合 DPI 和干净 Windows 仍需进一步验证。请先备份重要会话及密码库。

项目与问题反馈：https://github.com/fantuanzzzz/portharbor
