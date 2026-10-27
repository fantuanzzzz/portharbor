# 0006：文件选择端口与 Windows Forms 平台适配

日期：2026-10-27；状态：已实施，当前工作区待提交。

## 背景

会话导入、导出及文件上传、下载直接在窗体中构造 Windows 文件对话框，无法通过替身验证取消选择和路径传递。现有 `PortHarbor.Platform.Windows` 不依赖 WinForms，终端、凭据与能力检查对这一边界已有回归保护。

## 决策

- Application 定义同步单文件选择端口 `IFilePicker` 及不可变 `FilePickerOptions`，只传过滤器、建议文件名和选中路径；取消返回 `null`，选择本身不读写文件。端口不包含窗口、控件或原生句柄。
- 新建轻量 `PortHarbor.Platform.Windows.Forms` 项目，只引用 Application 和 Windows Forms 框架，无新增 NuGet 包。`WindowsFilePicker` 保留现有 `OpenFileDialog` / `SaveFileDialog`，每次调用建立和释放独立实例，使用构造时注入的窗口作为模态 owner。界面在原有 UI 线程调用。
- 不给已有 `PortHarbor.Platform.Windows` 增加 WinForms 依赖，不重写原生 COM/P/Invoke 文件选择器。新项目隔离需要窗口框架的适配器，DPAPI、mstsc、ConPTY 和串口继续保留原边界。
- MainForm 和 FileView 接收可选选择器，默认装配当前窗口的 Windows 实现。导入确认仍在界面中，格式读写和导入导出策略本轮不迁移。下载保留远程文件名清理，保存对话框保留覆盖提示；上传保留远程覆盖确认。
- 保持会话 JSON 过滤器、`portharbor-sessions.json` 默认文件名、单文件打开以及取消不触发存储/传输的行为。会话导出不含密码、密码库和主机信任记录，导入仍忽略这些数据。

## 验证与限制

核心检查覆盖程序集依赖、模态 owner、过滤器/默认文件名、打开/保存默认选项、Unicode 路径、取消及异常释放。UI 检查使用替身选择器驱动真实存储与内存传输，覆盖取消、拒绝/确认导入、无密码导出和上传下载路径传递。

适配器测试拦截对话框显示调用，未自动操作 Windows 系统对话框或点击覆盖提示；原生交互继续使用与重构前相同的 WinForms 实现。尚未新增其他操作系统或 UI 支持。

## 复审条件

引入其他 UI、另一个操作系统、多文件上传、目录选择、异步选择或额外文件类型时复审。不得将窗口句柄带入 Application；文件选择取消不能被当成失败或空路径继续执行。
