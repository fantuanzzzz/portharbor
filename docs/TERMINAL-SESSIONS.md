# 终端会话与生命周期

终端当前边界由 Application 的 `ITerminalSession`、`ITerminalTransport` 和 `TerminalConnection` 定义。阶段状态与下一步统一见[技术方案第 8 节](TECHNICAL-PLAN.md#8-分阶段实施)，分层决策见 [ADR 0002](adr/0002-terminal-lifecycle.md)。

## 组成

- `TerminalConnection` 只依赖 Domain 与端口，负责配置校验、一次性启动、状态诊断、UTF-8 增量解码、输入/resize 队列和关闭编排。
- Infrastructure 的 `SshTerminalTransport` 和 `TelnetTerminalTransport` 负责网络连接与字节 I/O。Telnet 适配器处理协商、NVT 编码和 IAC 转义，应用层只接收终端负载字节；SSH 复用既有认证和主机指纹配置。
- Application 的 `StreamTerminalTransport` 跟踪启动中已创建的资源。永久中止后，新创建的资源在交还给启动流程前立即释放，异步释放等待启动返回及已登记资源清理。它只使用 .NET 流与任务，由网络和平台适配器共同继承。
- `PortHarbor.Platform.Windows` 中的 `LocalShellTerminalTransport` 和 `SerialTerminalTransport` 分别提供 ConPTY 与串口终端，`WindowsTerminalTransportFactory` 装配两者；平台项目只引用 Application 和 System.IO.Ports，不引用 WinForms 或网络厂商库。
- `ISerialPortProvider` 提供设备名称、校验/停止位/流控的持久化字符串选项，Windows 实现执行设备枚举，编辑器只消费端口，保留现有会话字符串格式。
- Windows UI 的 `TerminalConnectionFactory` 组合网络与 Windows 工厂，仍是当前终端组合入口。DPAPI 和 mstsc 已迁入平台项目；`IPlatformCapabilities` 与应用连接策略让 UI 在创建本地连接资源前检查运行支持，并允许恢复后重新检查，不改变底层适配器的错误与取消契约。
- 界面绑定 `ITerminalSession`，负责主机密钥确认、诊断展示和控件线程调度。关闭标签或软件会先等待 `CloseAsync`，再销毁控件。

## 使用与所有权

1. 每个会话实例只允许调用一次 `Run(columns, rows, token)`；重新连接必须创建新实例，避免复用已取消令牌或重放旧输入。
2. `Run` 在所有读写及资源清理结束后完成。主动取消保留原行为：结束会话并发布 Cancelled 诊断；协议、读写和配置失败发布安全诊断，清理后将原异常传给调用方。
3. `Send` 在入队时复制字节。输入及 resize 共用容量 256 的队列，由一个消费者按顺序调用适配器。输入队列满时显示已有提示；resize 队列满时丢弃此次尺寸更新。关闭后不再接收输入，不自动重放命令。
4. `CloseAsync` 幂等，永久取消会话、请求适配器中止，并等待启动和活动写入退出，再等待异步释放。启动晚于关闭请求返回时，不得再次发布 Connected。
5. 同步 `Dispose` 只发起关闭；需要确定资源已释放时使用 `CloseAsync` 或 `await DisposeAsync()`。主界面使用可等待关闭流程。
6. 传输适配器属于会话用例。`Abort` 必须线程安全、幂等且快速返回，通过关闭底层资源解除阻塞；`DisposeAsync` 才等待资源清理和启动结束。实现不能在关闭后留下刚创建的流或客户端。
7. SSH 使用原生 `ConnectAsync`；SSH.NET 的 shell channel 创建/读取与 Windows 同步管道仍使用工作线程，取消依赖关闭其所属客户端或句柄，并不意味着所有底层 API 都支持原生异步取消。
8. 网络连接仍采用现有库和系统的超时设置，本轮没有新增用户可配置的终端连接/读写超时或自动重连。串口阻塞、FTPS、私钥及多服务器兼容性仍需各自实机验收。

## 验证

`tests/Shared/TerminalSessionChecks.cs` 注入脚本化端口，在独立 `net8.0` 检查和 Windows 核心检查中共同验证输入顺序、分片 UTF-8、取消、晚启动、写入阻塞、异常及异步释放。Windows 核心检查另行核验真实流适配器的晚资源释放；UI 检查覆盖关闭标签和退出软件时等待终端清理。

协议回环检查验证 SSH 认证、主机指纹、PTY、输入和断开，Telnet 协商与远端断开，以及 SSH 服务不发握手 banner、Telnet 服务不发数据时的取消。ConPTY 检查验证本地 Shell 输入、输出、resize、正常退出及关闭空闲进程。平台检查核验分层依赖、协议工厂拒绝、预取消和编辑器设备/选项注入；串口实机互通仍待验收。运行命令见 [README](../README.md#验证)。
