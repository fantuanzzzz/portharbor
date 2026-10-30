# 连接诊断消息约定

## 边界

Domain 的 `ConnectionDiagnostic` 保存稳定 OperationId、协议、状态、阶段、错误码与重试性。Application / Infrastructure 继续负责产生和分类诊断；Application 的 `DiagnosticPresenter` 组合安全文本；UI 的 `ConnectionStatusLabel` / FileView / 终端页面负责显示与线程调度。

`DiagnosticMessageCatalog` 读取内嵌 `Resources/DiagnosticMessages.resx`，无需访问磁盘或网络。详细决策见 [ADR 0008](adr/0008-diagnostic-message-catalog.md)。

## 目录与组合

| 资源前缀/名称 | 用途 |
| --- | --- |
| `Error_{枚举名称}` | 对应每个 ConnectionErrorCode；None 为空，Unknown 为未知错误 |
| `Stage_{枚举名称}` | 对应每个 ConnectionStage；None 为空，交由状态回退 |
| `State_{枚举名称}` | 所有生命周期状态的回退文字 |
| `ReadyToConnect` | 状态控件的初始准备连接文字 |
| `ExternalDesktopStarted` | 只表示 Windows RDP 外部客户端启动 |
| `StatusErrorFormat` | 阶段与错误的组合格式 |
| `OperationFormat` | 状态文字加 8 位操作编号 |

成功且 Closed 优先显示“已关闭”；成功且 Connected 区分 RDP 客户端启动与普通连接。其余先按阶段显示，阶段为空/未定义时回退到状态。有错误则追加对应错误文字；Error 方法追加操作编号。

示例：`正在验证身份（身份验证失败）。操作编号：01234567`。未知数值枚举得到稳定的“连接异常（未知错误）”等回退，不插入枚举数值或原始 Message。

## 网络故障分类

- Socket 连接被拒绝、主机解析失败及包装在 I/O 异常中的连接重置均按类型归为 `NetworkUnavailable`，不读取异常文本。随机 `.invalid` 主机名已经执行实际系统解析失败回归；不代表验证了真实企业 DNS 或外网互通。
- SSH.NET `SshConnectionException` 的 `DisconnectReason.ConnectionLost` 归为网络故障；密钥交换等其他 SSH 异常继续归为协议错误。已连接的 SSH 突然失去底层连接时，适配器解除阻塞读取，应用层保留读取阶段失败和操作编号，完成关闭。
- FTP 的 Connect 包含 TCP 连接及登录：初始阶段为 `Connecting`；捕获明确的 `FtpAuthenticationException` 后标注 `Authenticating`，错误类别为 `AuthenticationFailed`。TCP 连接拒绝不再误标认证阶段，密码错误不作为可重试网络错误。
- 正常 SSH Shell 退出和 Telnet EOF 不发布失败；主动取消保持 `Cancelled`。可重试性只是诊断元数据，不表示自动重连。
- 文件传输在数据复制阶段断线时保留 Reading/Writing 阶段及原操作编号。已知长度不符或 FTP 最终完成响应拒绝以协议错误报告，不当作成功下载替换旧文件；网络异常仍按原类型归类，失联后远端临时文件清理可能失败。
- SFTP 覆盖提交失败时，备份恢复的拒绝/超时不替换原始 Writing 错误；恢复成功也不伪装上传成功。新文件重命名已确认成功后，旧备份清理失败仍返回成功。`Recovery=RemoteCommitUnconfirmed` 表示提交结果需核对，取消提示不得承诺原路径一定保留旧文件；诊断包不导出备份路径，也不提供自动恢复向导。

## 安全与维护

- 呈现不读取原始 Message、端点、路径或终端内容；错误分类继续依据异常类型和阶段，不能靠解析异常文本决定文案。
- ConnectionStatusLabel 的失败保留规则不变：清理/取消不覆盖已显示的错误，新 Connecting 诊断清除上次失败。
- 新增已定义枚举必须同步新增资源。共享检查要求键集合与全部枚举严格对应，避免依靠静默回退掩盖漏项。
- 内嵌资源只包含中文默认文案，其他 UI culture 回退到中文；没有语言切换界面。平台能力说明及普通菜单尚未纳入此目录。
- 资源仅含纯文本；未来新增翻译要同时验证格式参数与操作编号，禁止加入终端控制字符或敏感参数。

## 本地日志与诊断包

Application 的 `IDiagnosticLog` 定义允许字段，Infrastructure 的 `FileDiagnosticLog` 将终端、文件、RDP 和后台失败写到配置目录 `logs/diagnostics.jsonl`，设置为 `logs/settings.json`。默认启用，约 1 MiB 轮转、归档保留 14 天且最多 8 个；设置菜单可保存启用、单文件大小、天数及归档上限。阈值在下一条写入前检查，可能超出一条记录的大小；过期归档在后续写入时清理。诊断 I/O 失败不使连接失败，最多保留 500 条内存事件。

允许字段为 UTC 时间、操作/会话编号、协议、状态、阶段、错误码、重试性、实际时长、超时参数、传输字节数/已知总数、终端收发字节和解码字符数、恢复状态。终端计数对应适配器的 UTF-8 负载，解码字符数按 .NET UTF-16 字符单元统计；不包含连接提示文本。文件计数是本次实际写入目标流的字节，不意味着远端提交已确认；恢复状态区分本地暂存已删、远端暂存已删、清理未确认和提交未确认。

始终排除端点、用户名、会话名称、路径、凭据、原始异常对象/消息/堆栈、终端内容和文件数据。不提供关闭脱敏的开关。详见 [ADR 0009](adr/0009-local-diagnostic-log.md)。

帮助菜单“预览并导出诊断包…”生成固定 JSON 快照，包含 schema、版本、运行时、平台和最多 500 条安全事件。仅导出用户已看到的内容，经文件选择端口保存；取消选择保留预览且不写文件。快照重新解析允许字段，不复制原日志行、设置或其他文件。不自动上传，分享由用户自行决定。

2026-10-30 诊断包升级为 schema 2，增加 `Build`：提交 SHA、Dirty、源码摘要 SHA-256 和 SDK 版本，未提供的值为 null。提交和摘要只允许十六进制长度范围，SDK 仅允许可解析版本号；预览与导出边界都重新验证，不接受任意路径或原始文本。JSONL 事件字段与已有日志 schema 不变。发行 manifest 另包含 runtime pack 和依赖锁摘要，见[发行说明](DISTRIBUTION.md)。

后台任务由 `BackgroundTask.Observe` 消费故障；已发布的连接错误保留原分类，未发布错误的任务提供安全 Unknown 兜底。清理错误不能覆盖原始操作失败。

## 验证入口

- `tests/Shared/DiagnosticMessageChecks.cs`：独立 Application 和核心检查共用，验证目录完整性、语言回退、状态优先级、未知值及七种协议的组合脱敏。
- `tests/PortHarbor.Checks/DiagnosticChecks.cs`：厂商异常分类、原始消息脱敏及真实协议失败诊断。
- `tests/PortHarbor.Checks/NetworkFailureChecks.cs`：四种网络协议的拒绝连接、FTP 错误密码、SSH 正常退出/连接丢失和 Telnet 正常 EOF/TCP reset。
- `tests/PortHarbor.Checks/FileTransferInterruptionChecks.cs`：FTP/SFTP 上传/下载断线及 FTP 最终确认拒绝，检查原阶段、目标保留和关闭完成。
- `tests/PortHarbor.Checks/SftpCommitChecks.cs`：覆盖提交/恢复的断线及拒绝、恢复/备份清理无响应，核验原始结果、恢复文件和显式新连接恢复。
- `tests/PortHarbor.Checks/DiagnosticUiChecks.cs`：后台 UI 更新、失败保留/清除、未知诊断回退、操作编号和实际终端/RDP 页面。
- `tests/PortHarbor.Checks/DiagnosticLogChecks.cs`：本地字段、轮转、归档数量、禁用/设置重启、I/O 失败隔离及后台异常观察。
- `tests/PortHarbor.Checks/DiagnosticBundleChecks.cs`：固定预览/导出一致、500 条上限和敏感字段排除；`DiagnosticUiChecks.cs` 验证实际预览/取消/导出及设置保存。

本轮验证及发布记录见[开发日志](DEVELOPMENT-LOG.md)。
