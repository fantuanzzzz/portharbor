# 0003：凭据保护与外部远程桌面启动边界

日期：2026-10-26；状态：已实施，当前工作区待提交。

## 背景

Windows 平台项目已有 ConPTY 和串口实现，但 DPAPI 仍在存储 Infrastructure，mstsc 的配置生成、诊断及进程启动混在 UI。两者需要可替换的实现，同时保留已发布配置和密码库行为。

## 决策

- Application 定义 `ISecretProtector`，Windows 的 `WindowsSecretProtector` 保留原 DPAPI 调用、用户作用域、描述字符串和 Base64 编码。Infrastructure 的 `Store` 构造时必须注入保护器，不引用 Windows 平台项目，也不提供替代固定密钥或静默降级。
- 密码库编排仍由 `StoredVault` 管理：旧 schema v1 凭据显式迁移；安装版 vault v1 使用 DPAPI 后再由主密码 AES-GCM 包装，便携版 vault v2 使用主密码加密。便携 vault v2 的保存、读取及改密不调用平台保护器；读取旧 DPAPI 密文或显式改密转换原有安装版密码库仍需要原 Windows 用户环境。
- `RdpLauncher` 迁入 Application，负责校验、RDP 配置文本及安全诊断。新增 `IRemoteDesktopLauncher` 接受无密码配置、目录和阶段回调；Windows 实现负责 UTF-16 临时文件、mstsc 启动及退出/失败清理。调用以启动完成返回，不等待远程登录或把外部窗口归为标签页资源。
- Windows 实现使用系统目录中的 mstsc、`UseShellExecute=false` 和单一 `ArgumentList` 参数，保持包含空格的路径安全。测试可替换进程创建入口以验证资源所有权，不需要真实远程桌面服务器。
- UI 组合根装配 Windows 实现，RDP 页面通过用例启动及重试；对用户显示“已启动 Windows 远程桌面客户端”，不声称远程认证成功。

## 影响与限制

`Store` 构造器接收 `ISecretProtector` 和 `IAppDataPaths`；项目内调用方已更新，磁盘格式、算法参数和数据目录不变。没有新增包或更改锁文件。路径解析现由 Windows 平台服务提供；Infrastructure 暂保留 `net8.0-windows` 目标，这些拆分不表示支持新操作系统。

Windows 检查验证 DPAPI 及客户端进程替身的退出清理；真实 mstsc 登录与服务器互通尚未验证。临时配置清理对 I/O/权限失败为尽力而为，不影响外部客户端退出结果；异常终止应用后的遗留文件清理仍需后续策略。

## 复审条件

新增凭据库或操作系统实现、调整密码库版本、引入凭据同步、替换 RDP 外部客户端或实现路径/能力报告时复审。必须继续验证原用户 DPAPI 迁移、失败回滚、便携库不依赖 DPAPI，以及外部客户端启动与远程认证的区别。
