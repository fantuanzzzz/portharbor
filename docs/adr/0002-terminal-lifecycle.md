# 0002：终端生命周期由应用用例持有

日期：2026-10-26

状态：已采用

## 背景与选项

原 `TerminalConnection` 位于 WinForms 项目，混合 SSH、TCP、串口、ConPTY、输入队列和资源清理。同步 Dispose 只启动后台释放，标签关闭无法确定后台任务已结束。仅把原类搬入 Infrastructure 会保留这些职责耦合，继续阻碍独立生命周期检查。

## 决策

- Application 定义 `ITerminalSession` 与 `ITerminalTransport`，由 `TerminalConnection` 管理一次性 Run、增量解码、输入队列、错误分类注入和取消/关闭；厂商类型和系统句柄仅出现在适配器。
- 端口的 `Abort` 表达永久停止请求，负责中断阻塞 I/O；异步释放负责等待。用例不能在活动写入结束前声明关闭完成，也不能在取消后的晚启动返回时再次发布 Connected。
- SSH/Telnet 实现在 Infrastructure，SSH 共用主机信任端口，Telnet 协商留在协议适配器。串口与 Shell 先以 Windows UI 项目内的适配器接入同一端口，后续迁入平台项目。
- 同步 Dispose 保留兼容入口；正常界面关闭使用可等待的 CloseAsync。读写失败的原异常在清理结束后传播，UI 继续只展示稳定错误码与操作编号。
- 输入与 resize 顺序执行，关闭时取消未发送输入，不重放可能执行命令的字节。

## 影响与限制

- Application 可独立构建和测试，不引用 WinForms、SSH.NET、串口或 Win32 API。UI 不再直接构造 SSH 客户端，SSH.NET 的直接包引用从 UI 移除，依赖版本不变。
- SSH channel 与 Windows 管道的一部分操作仍为同步 API，工作线程与关闭所属资源共同承担中断职责；不能只取消等待 Task 而把原任务和资源留在后台。
- Windows 平台项目和能力报告尚未完成，不表示新增操作系统支持。终端仍没有自动重连、用户可配置超时或会话录制。

## 验证与复审条件

独立应用检查覆盖启动中取消、晚启动、阻塞写入、异步清理、远端 EOF、输入顺序、分片编码及安全诊断；回环检查覆盖真实 SSH/Telnet 的连接、取消和断开。界面关闭检查要求清理完成前控件仍存活。

新增终端协议、改用原生异步 PTY、增加连接重试/超时或迁移 Windows 平台实现时复审。任何重试策略都必须明确不重放终端命令。

## 2026-10-26 Windows 平台项目复审

串口、本地 Shell 和 ConPTY 已迁入 `PortHarbor.Platform.Windows`。平台项目仅引用 Application 与既有 System.IO.Ports 包，不反向引用 UI 或网络 Infrastructure；UI 组合根选择对应的终端工厂。共享 `StreamTerminalTransport` 仅依赖 .NET 流、任务及 Application 端口，迁入 Application 供两类适配器复用，避免 Windows 平台为了关闭策略而引入 SSH.NET/FluentFTP。前文关于“后续迁入”的内容是决策初始状态，当前实现以本段复审记录为准。

新增 `ISerialPortProvider`，让设备枚举及串口参数选项都由 Windows 适配器提供，保留旧配置的字符串取值。System.IO.Ports 的直接包引用移入平台项目，版本不变。本段记录当时的阶段安排；DPAPI、mstsc、平台能力和路径服务随后已分别由 ADR 0003–0005 实施，原有取消、关闭及命令不重放约定仍然有效。

同日后续实施已将 DPAPI 与 mstsc 接入 Windows 平台项目，见 [ADR 0003](0003-secret-protection-and-rdp.md)。能力报告和路径服务继续按技术方案推进，不改变本文的终端生命周期决策。
