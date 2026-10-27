# 0005：应用数据路径解析与存储消费

日期：2026-10-26；状态：已实施，当前工作区待提交。

## 背景

启动参数、便携标志识别、LocalApplicationData/程序目录组合及 EasyConnect 旧路径曾分别由 UI 和 Infrastructure 隐式推导。RDP 临时文件传入窗体当前用户配置目录，启动逻辑与持久化策略难以分别测试。

## 决策

- Application 定义 `IAppDataPaths` 和不可变路径快照，表达便携模式、配置目录/文件、可选旧配置来源和临时目录；存储及 RDP 用例只消费解析结果。
- `WindowsAppDataPaths` 是 Windows 平台启动解析器，集中处理便携标记/INI 检测、`--portable`/`--installed`、`--data-dir`、安装默认路径 `%LOCALAPPDATA%\PortHarbor`、旧文件 `%LOCALAPPDATA%\EasyConnect\settings.json` 及相对路径绝对化。路径计算无副作用。
- 2026-10-27 修正：程序目录与显式数据目录的绝对路径使用 `Path.TrimEndingDirectorySeparator` 移除非根目录末尾的分隔符，保留盘符根目录语义。真实 `AppContext.BaseDirectory` 带末尾分隔符，原实现会与配置文件父目录字符串不相等，导致默认便携启动被错误拒绝；配置目录边界校验仍保留。
- 显式 `--data-dir` 不触发默认用户配置的旧文件迁移；只有隐式安装默认路径暴露旧配置来源。便携模式不暴露迁移源。未知参数或缺少目录值在任何目录写入前拒绝。
- Infrastructure 的 `Store` 接收并校验绝对路径，确认配置文件位于配置目录内后建立目录；格式适配器继续在首次启动时复制旧 JSON，保存 INI/JSON 及原子替换。旧源只复制、从不删除；已有目标配置优先，不覆盖。
- `Store.Paths` 在构造时复制为不可变快照。RDP 用例使用同一接口提供的临时目录，Windows 解析器继续把临时文件放在原用户配置目录，保持现有行为。UI 的配置目录入口也从解析路径读取。

## 影响与限制

没有改动 JSON/INI 名称、配置 schema、DPAPI 数据、便携识别规则或 RDP 临时文件位置；没有增加依赖或调整锁文件。`ConfigurationFileStorage` 仍负责旧文件复制与原子文件写入；路径策略和兼容数据格式已有清晰边界，但持久化迁移还不是独立 Application 用例。

路径接口是绝对本地路径模型，未定义云存储、用户漫游或路径符号链接策略。Infrastructure 继续为 `net8.0-windows`；此拆分不表示可以在非 Windows 上实例化当前完整 Store。

## 复审条件

引入另一个操作系统、默认目录/便携标志变更、显式导入配置迁移、移动 RDP 临时文件、启用单实例或重构配置存储时复审。必须继续覆盖旧数据优先级、旧源保留、用户选择目录不被意外迁移及路径写入失败不回退到其他位置。
