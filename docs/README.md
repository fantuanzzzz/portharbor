# PortHarbor 文档

PortHarbor 是采用 MIT License 的 Windows 连接工作台。首次使用请先阅读[项目首页](../README.md)。以下是当前维护的文档；历史开发流水和过期验收记录保留在本地归档，不作为当前功能承诺。

## 使用与发行

| 文档 | 内容 |
| --- | --- |
| [发行说明](DISTRIBUTION.md) | 单文件便携、安装向导、数据目录、升级/卸载与构建追溯 |
| [会话库](SESSION-LIBRARY.md) | 分类、收藏、最近、搜索、重命名与活动记录 |
| [会话编辑器](SESSION-EDITOR.md) | 协议字段、提交意图、校验、密码和路径选择 |
| [工作台布局](WORKSPACE-LAYOUT.md) | 操作栏、焦点、窗口/侧栏记忆与恢复 |
| [视觉规范](VISUAL-DESIGN.md) | 浅色/深色、高对比度、控件状态及终端配色隔离 |
| [终端工作流](TERMINAL-WORKFLOW.md) | 显式重连、缓冲查找、复制和标签操作 |
| [文件工作流](FILE-WORKFLOW.md) | 导航、排序、选择、进度与取消反馈 |
| [传输队列](TRANSFER-QUEUE.md) | 多文件串行、覆盖确认、失败暂停、显式重试与关闭 |
| [SSH 认证](SSH-AUTHENTICATION.md) | 密码/私钥、多轮挑战、取消及超时策略 |

## 开发与验证

| 文档 | 内容 |
| --- | --- |
| [技术方案](TECHNICAL-PLAN.md) | 架构边界、阶段清单、产品规划与验收条件 |
| [开发与版本摘要](DEVELOPMENT-LOG.md) | 对外保留的重要里程碑及本机验证依据 |
| [测试说明](TESTING.md) | 标准测试、旧检查器、覆盖率、格式门禁和 CI |
| [实机验收手册](REAL-ENVIRONMENT-ACCEPTANCE.md) | E01–E12 的环境、操作步骤、预期结果与证据模板 |
| [文件传输契约](FILE-TRANSFER.md) | 取消、超时、暂存、提交及恢复语义 |
| [终端会话契约](TERMINAL-SESSIONS.md) | 端口、输入顺序、偏好及可等待关闭 |
| [配置导入导出](CONFIGURATION-TRANSFER.md) | 合并、新 ID、敏感字段排除、原子事务和兼容性 |
| [诊断说明](DIAGNOSTICS.md) | 消息目录、脱敏字段、本地日志及可审阅导出 |
| [源码与发行整理](REPOSITORY.md) | 应上传内容、忽略目录、归档策略和发布前核对 |

截至 2026-10-30，技术阶段 0–3 清单完成 **50/51 项**，T6 已接入标准测试；剩余主清单为实机故障专项。产品 P01–P10 已实现，本机测试结果不代表全部设备/服务器互通。Linux/macOS 仅增加纯核心测试 CI 配置，待推送验证，不表示提供这些平台的 UI。新平台/更换 UI 框架按阶段 4 另行确定目标。

## 架构决策

- [ADR 0001：主密码与双版本密码库](adr/0001-master-password.md)
- [ADR 0002：终端生命周期](adr/0002-terminal-lifecycle.md)
- [ADR 0003：凭据保护与外部 RDP](adr/0003-secret-protection-and-rdp.md)
- [ADR 0004：平台能力检测](adr/0004-platform-capabilities.md)
- [ADR 0005：应用数据路径](adr/0005-application-data-paths.md)
- [ADR 0006：Windows 文件选择器](adr/0006-windows-file-picker.md)
- [ADR 0007：配置导入导出](adr/0007-configuration-transfer-policy.md)
- [ADR 0008：诊断消息目录](adr/0008-diagnostic-message-catalog.md)
- [ADR 0009：本地诊断日志](adr/0009-local-diagnostic-log.md)
- [ADR 0010：协议配置类型化](adr/0010-typed-protocol-configurations.md)
- [ADR 0011：终端默认偏好](adr/0011-terminal-preferences.md)
- [ADR 0012：工作台布局](adr/0012-workspace-layout.md)
- [ADR 0013：界面主题](adr/0013-interface-theme.md)
- [ADR 0014：收藏与最近](adr/0014-session-activity.md)
- [ADR 0015：会话编辑](adr/0015-session-editor.md)
- [ADR 0016：终端工作流](adr/0016-terminal-workflow.md)
- [ADR 0017：文件工作流](adr/0017-file-workflow.md)
- [ADR 0018：传输队列](adr/0018-transfer-queue.md)
- [ADR 0019：SSH 交互式认证](adr/0019-ssh-interactive-authentication.md)
- [ADR 0020：单文件与安装器](adr/0020-single-file-and-installer.md)
- [ADR 0021：工程基线收尾](adr/0021-technical-baseline-completion.md)
- [ADR 模板](adr/0000-template.md)

## 许可

项目代码与文档采用根目录 [MIT License](../LICENSE)，Copyright (c) 2026 fantuanzzzz。第三方依赖按各自条款分发，保留 [NOTICE](../THIRD-PARTY-NOTICES.md) 和 [licenses/](../licenses/)。
