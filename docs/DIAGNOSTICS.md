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

## 安全与维护

- 呈现不读取原始 Message、端点、路径或终端内容；错误分类继续依据异常类型和阶段，不能靠解析异常文本决定文案。
- ConnectionStatusLabel 的失败保留规则不变：清理/取消不覆盖已显示的错误，新 Connecting 诊断清除上次失败。
- 新增已定义枚举必须同步新增资源。共享检查要求键集合与全部枚举严格对应，避免依靠静默回退掩盖漏项。
- 内嵌资源只包含中文默认文案，其他 UI culture 回退到中文；没有语言切换界面。平台能力说明及普通菜单尚未纳入此目录。
- 资源仅含纯文本；未来新增翻译要同时验证格式参数与操作编号，禁止加入终端控制字符或敏感参数。

## 验证入口

- `tests/Shared/DiagnosticMessageChecks.cs`：独立 Application 和核心检查共用，验证目录完整性、语言回退、状态优先级、未知值及七种协议的组合脱敏。
- `tests/PortHarbor.Checks/DiagnosticChecks.cs`：厂商异常分类、原始消息脱敏及真实协议失败诊断。
- `tests/PortHarbor.Checks/DiagnosticUiChecks.cs`：后台 UI 更新、失败保留/清除、未知诊断回退、操作编号和实际终端/RDP 页面。

本轮验证及发布记录见[开发日志](DEVELOPMENT-LOG.md)。
