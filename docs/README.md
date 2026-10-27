# PortHarbor 文档

这是 PortHarbor 的设计、实现记录和验证入口。面向 GitHub 浏览时，建议按下面顺序阅读：

1. [项目 README](../README.md)：功能范围、运行方式、构建命令和当前限制。
2. [技术方案](TECHNICAL-PLAN.md)：架构边界、阶段状态、验收条件和下一步安排。
3. [开发日志](DEVELOPMENT-LOG.md)：按时间倒序记录每轮开发、验证结果和遗留边界。
4. [文件传输约定](FILE-TRANSFER.md)：FTP/SFTP 接口、取消、超时、临时文件和覆盖语义。
5. [终端会话约定](TERMINAL-SESSIONS.md)：终端端口、输入顺序、取消和可等待关闭语义。
6. [会话配置导入导出约定](CONFIGURATION-TRANSFER.md)：分类合并、ID 分配、敏感字段排除、存储事务和兼容规则。
7. [连接诊断消息约定](DIAGNOSTICS.md)：内嵌中文目录、错误/阶段组合、回退、操作编号及脱敏规则。

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
- [ADR 模板](adr/0000-template.md)

## 当前快照

截至 2026-10-27，项目是 Windows 专用的 .NET 8 / WinForms 私有预览版。Domain、Application、Infrastructure、Windows 平台和 UI 的依赖方向已经建立；DPAPI、mstsc、ConPTY、串口、平台能力、应用数据路径和文件选择均有明确边界。Windows Forms 文件选择器单独成项目，既有平台库继续不引用 WinForms。

导入导出业务策略已迁入 Application；分类合并、新 ID 分配及敏感字段排除可脱离 Windows 检查，存储适配器保留原格式与原子保存。

最新 T1–T5 完成发布回滚、网络/同步调用超时、后台生命周期与纯逻辑审计、本地诊断闭环和七协议运行配置类型化。完整扩展基线通过 Application 175、核心 366、Windows 17、协议 96、UI 61，共 **715 项**，另有四项冒烟及单独五个发布替换/回滚场景通过；报告在 ignored `artifacts/technical-closure/release/`。帮助菜单可预览并导出诊断包，设置菜单可保存日志设置，旧 JSON/INI 及凭据关联保持兼容。

技术方案阶段 0–3 已完成 **49/51** 个清单项，余下标准测试框架迁移和物理网络/串口等环境故障验收；清单比例不等于工时或源码覆盖率。T1–T5 软件范围完成，可以转入当前 Windows UI、功能和体验开发，下一步建议持久化终端字体、配色及复制粘贴偏好。详见[技术方案第 8 节](TECHNICAL-PLAN.md#8-分阶段实施)。新平台或更换框架按阶段 4 启动；正式安装向导和单文件便携包仍未实现。

提交 `e110c4e` 已在 Windows 本机独立干净克隆和空构建/依赖缓存下完成扩展基线：Application 151 项、核心 320 项、Windows 14 项、协议回环 26 项、WinForms UI 57 项全部通过；ConPTY 及三项发布启动冒烟通过。默认便携首次启动与重启不指定数据目录，并检查 EXE 退出码和 INI 创建。记录及环境限制见[开发日志](DEVELOPMENT-LOG.md)。

扩展基线可用 `build.ps1 -ExtendedBaseline` 纳入协议回环、WinForms UI 和 ConPTY；`engineering-baseline.json` 记录耗时及检查数量，不是代码行覆盖率报告。协议集成失败时 fixture 日志留在本地报告目录。另一台机器复现、真实串口、物理断网、RDP 登录、FTPS、私钥及生产服务器兼容性仍需专项验收；原 Temp 克隆中的 FTP 550 根因尚未确认。SFTP 断线仍可能留下 `.part`/`.bak`，未知提交状态需要新连接显式核对，不能保证原路径始终保留旧文件。

## 发布前检查

- 项目当前没有开源许可证；上传仓库不等于授予复制、修改或再分发权限。
- `artifacts/`、本地构建缓存和测试结果不作为源码提交内容；发布包由 `build.ps1` 重新生成。
- 测试 fixture 只绑定 `127.0.0.1`，使用固定测试账号，不包含真实服务器或用户凭据。
- 安装版 DPAPI 密文绑定当前 Windows 用户；便携版使用主密码保护，不能把安装版密码库直接当成可移植密文。
- 上传前应确认仓库中没有本地配置、主机密钥、密码、私钥或个人路径。
