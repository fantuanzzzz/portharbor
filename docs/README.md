# PortHarbor 文档

这是 PortHarbor 的设计、实现记录和验证入口。面向 GitHub 浏览时，建议按下面顺序阅读：

1. [项目 README](../README.md)：功能范围、运行方式、构建命令和当前限制。
2. [技术方案](TECHNICAL-PLAN.md)：架构边界、阶段状态、验收条件和下一步安排。
3. [开发日志](DEVELOPMENT-LOG.md)：按时间倒序记录每轮开发、验证结果和遗留边界。
4. [文件传输约定](FILE-TRANSFER.md)：FTP/SFTP 接口、取消、超时、临时文件和覆盖语义。
5. [终端会话约定](TERMINAL-SESSIONS.md)：终端端口、输入顺序、取消和可等待关闭语义。
6. [会话配置导入导出约定](CONFIGURATION-TRANSFER.md)：分类合并、ID 分配、敏感字段排除、存储事务和兼容规则。
7. [连接诊断消息约定](DIAGNOSTICS.md)：内嵌中文目录、错误/阶段组合、回退、操作编号及脱敏规则。

当前产品开发安排见[技术方案第 13 节](TECHNICAL-PLAN.md#13-用户体验ui-交互与美观功能开发计划)：按用户体验、UI 交互与美观、功能三个方向组织，包含 P01–P10 的内容、顺序及验收条件。

P02 布局示意、操作路径和布局保存约定见[工作台布局](WORKSPACE-LAYOUT.md)。

P03 浅色/深色配色、按钮状态、覆盖范围及验收边界见[视觉规范](VISUAL-DESIGN.md)。

P04 收藏/最近、搜索与键盘操作、重命名及活动记录保存/清理约定见[会话库说明](SESSION-LIBRARY.md)。

P05 会话编辑器字段分组、三个明确操作、就地校验、密码显示和路径选择见[编辑器说明](SESSION-EDITOR.md)。

P06 终端重连、缓冲查找、复制反馈及标签操作见[终端工作流说明](TERMINAL-WORKFLOW.md)。

P07 目录导航、排序/选择、空目录与失败、单文件进度/取消反馈见[文件视图工作流](FILE-WORKFLOW.md)。

P08 批量上传/下载、串行任务列表、失败暂停/显式重试及队列取消/关闭见[队列说明](TRANSFER-QUEUE.md)。

P09 SSH keyboard-interactive 多轮认证、隐藏输入、取消及 SSH/SFTP 共用处理见[SSH 认证说明](SSH-AUTHENTICATION.md)。

P10 单文件自包含便携 EXE、中文/英文安装向导、升级卸载保留数据和构建证据见[发行说明](DISTRIBUTION.md)。

## 架构决策

- [ADR 0001：主密码与双版本密码库](adr/0001-master-password.md)
- [ADR 0002：终端生命周期由应用用例持有](adr/0002-terminal-lifecycle.md)
- [ADR 0003：凭据保护与外部远程桌面启动边界](adr/0003-secret-protection-and-rdp.md)
- [ADR 0004：平台运行能力检测与连接前检查](adr/0004-platform-capabilities.md)
- [ADR 0005：应用数据路径解析与存储消费](adr/0005-application-data-paths.md)
- [ADR 0006：文件选择端口与 Windows Forms 平台适配](adr/0006-windows-file-picker.md)
- [ADR 0007：会话配置导入导出策略与原子存储边界](adr/0007-configuration-transfer-policy.md)
- [ADR 0008：本地诊断消息目录与安全呈现](adr/0008-diagnostic-message-catalog.md)
- [ADR 0009：本地诊断日志与可审阅导出](adr/0009-local-diagnostic-log.md)
- [ADR 0010：协议运行配置类型化与旧数据兼容](adr/0010-typed-protocol-configurations.md)
- [ADR 0011：终端全局默认偏好与临时标签调整](adr/0011-terminal-preferences.md)
- [ADR 0012：工作台布局记忆与安全恢复](adr/0012-workspace-layout.md)
- [ADR 0013：共享界面样式与终端配色隔离](adr/0013-interface-theme.md)
- [ADR 0014：收藏与最近连接只关联会话身份](adr/0014-session-activity.md)
- [ADR 0015：会话编辑草稿、明确提交意图与字段校验](adr/0015-session-editor.md)
- [ADR 0016：终端重连、缓冲查找与标签操作边界](adr/0016-terminal-workflow.md)
- [ADR 0017：文件视图目录身份与最终操作结果](adr/0017-file-workflow.md)
- [ADR 0018：文件标签内的串行传输队列](adr/0018-transfer-queue.md)
- [ADR 0019：SSH keyboard-interactive 认证与 UI 响应](adr/0019-ssh-interactive-authentication.md)
- [ADR 0020：单文件便携与当前用户安装器](adr/0020-single-file-and-installer.md)
- [ADR 模板](adr/0000-template.md)

## 当前快照

最新 P10 交付与验收见[2026-10-30 P10 记录](DEVELOPMENT-LOG.md#2026-10-30---p10-单文件便携与安装交付)及[发行说明](DISTRIBUTION.md)。P05–P09 历史记录保留当时的交接上下文；本轮不提交或推送。

2026-10-30：P01–P10 已实现。P10 新增自包含便携单 EXE 与中文/英文安装向导，数据与程序分离、升级/卸载保留数据；核心 492、UI 243 项通过，发行证据与包位置见 P10 记录。干净 Windows、多 DPI/高对比度、真实服务器和原生输入框体验待补；技术主线维持 49/51，历史体验包不自动更新。

2026-10-27 收工点：功能提交 `16c5fa2`，T1–T5 软件收尾完成，下一轮产品体验开发尚未开始。本次仅整理文档、未执行 GitHub 推送；报告、产物位置和下次起点见[开发日志的收工记录](DEVELOPMENT-LOG.md#2026-10-27---收工记录)。

截至 2026-10-27，项目是 Windows 专用的 .NET 8 / WinForms 私有预览版。Domain、Application、Infrastructure、Windows 平台和 UI 的依赖方向已经建立；DPAPI、mstsc、ConPTY、串口、平台能力、应用数据路径和文件选择均有明确边界。Windows Forms 文件选择器单独成项目，既有平台库继续不引用 WinForms。

导入导出业务策略已迁入 Application；分类合并、新 ID 分配及敏感字段排除可脱离 Windows 检查，存储适配器保留原格式与原子保存。

2026-10-27 的 T1–T5 技术收尾完成发布回滚、网络/同步调用超时、后台生命周期与纯逻辑审计、本地诊断闭环和七协议运行配置类型化。当时完整扩展基线通过 Application 175、核心 366、Windows 17、协议 96、UI 61，共 **715 项**，另有四项冒烟及单独五个发布替换/回滚场景通过；报告在 ignored `artifacts/technical-closure/release/`，不代表今天重跑过这些发行与协议检查。帮助菜单可预览并导出诊断包，设置菜单可保存日志设置，旧 JSON/INI 及凭据关联保持兼容。

技术方案阶段 0–3 已完成 **49/51** 个清单项，余下标准测试框架迁移及物理网络/串口等环境故障验收；比例不等于工时或覆盖率。T1–T5 软件范围完成；产品 P01–P10 已实现，P10 干净 Windows 发行验收待补。新平台或更换框架按阶段 4 启动。

提交 `e110c4e` 已在 Windows 本机独立干净克隆和空构建/依赖缓存下完成扩展基线：Application 151 项、核心 320 项、Windows 14 项、协议回环 26 项、WinForms UI 57 项全部通过；ConPTY 及三项发布启动冒烟通过。默认便携首次启动与重启不指定数据目录，并检查 EXE 退出码和 INI 创建。记录及环境限制见[开发日志](DEVELOPMENT-LOG.md)。

扩展基线可用 `build.ps1 -ExtendedBaseline` 纳入协议回环、WinForms UI 和 ConPTY；`engineering-baseline.json` 记录耗时及检查数量，不是代码行覆盖率报告。协议集成失败时 fixture 日志留在本地报告目录。另一台机器复现、真实串口、物理断网、RDP 登录、FTPS、私钥及生产服务器兼容性仍需专项验收；原 Temp 克隆中的 FTP 550 根因尚未确认。SFTP 断线仍可能留下 `.part`/`.bak`，未知提交状态需要新连接显式核对，不能保证原路径始终保留旧文件。

## 发布前检查

- 项目当前没有开源许可证；上传仓库不等于授予复制、修改或再分发权限。
- `artifacts/`、本地构建缓存和测试结果不作为源码提交内容；发布包由 `build.ps1` 重新生成。
- 测试 fixture 只绑定 `127.0.0.1`，使用固定测试账号，不包含真实服务器或用户凭据。
- 安装版 DPAPI 密文绑定当前 Windows 用户；便携版使用主密码保护，不能把安装版密码库直接当成可移植密文。
- 上传前应确认仓库中没有本地配置、主机密钥、密码、私钥或个人路径。
