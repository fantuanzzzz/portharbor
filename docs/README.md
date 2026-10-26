# PortHarbor 文档

这是 PortHarbor 的设计、实现记录和验证入口。面向 GitHub 浏览时，建议按下面顺序阅读：

1. [项目 README](../README.md)：功能范围、运行方式、构建命令和当前限制。
2. [技术方案](TECHNICAL-PLAN.md)：架构边界、阶段状态、验收条件和下一步安排。
3. [开发日志](DEVELOPMENT-LOG.md)：按时间倒序记录每轮开发、验证结果和遗留边界。
4. [文件传输约定](FILE-TRANSFER.md)：FTP/SFTP 接口、取消、超时、临时文件和覆盖语义。
5. [终端会话约定](TERMINAL-SESSIONS.md)：终端端口、输入顺序、取消和可等待关闭语义。

## 架构决策

- [ADR 0001：主密码与双版本密码库](adr/0001-master-password.md)
- [ADR 0002：终端生命周期由应用用例持有](adr/0002-terminal-lifecycle.md)
- [ADR 0003：凭据保护与外部远程桌面启动边界](adr/0003-secret-protection-and-rdp.md)
- [ADR 0004：平台运行能力检测与连接前检查](adr/0004-platform-capabilities.md)
- [ADR 0005：应用数据路径解析与存储消费](adr/0005-application-data-paths.md)

## 当前快照

截至 2026-10-26，项目是 Windows 专用的 .NET 8 / WinForms 私有预览版。Domain、Application、Infrastructure、Windows 平台和 UI 的依赖方向已经建立；DPAPI、mstsc、ConPTY、串口、平台能力和应用数据路径均有明确边界。

当前验证结果：Application 92 项、核心 217 项、Windows 14 项、WinForms UI 45 项、协议回环 26 项。完整 Release 构建、发布程序启动冒烟、安装版 ZIP 和便携版 ZIP 均已通过。

当前下一步是把 Windows 文件打开/保存对话框迁入平台项目，再完善导入导出业务策略。真实串口设备、真实 RDP 登录、FTPS、私钥登录和多种生产服务器兼容性仍需专项验收。

## 发布前检查

- 项目当前没有开源许可证；上传仓库不等于授予复制、修改或再分发权限。
- `artifacts/`、本地构建缓存和测试结果不作为源码提交内容；发布包由 `build.ps1` 重新生成。
- 测试 fixture 只绑定 `127.0.0.1`，使用固定测试账号，不包含真实服务器或用户凭据。
- 安装版 DPAPI 密文绑定当前 Windows 用户；便携版使用主密码保护，不能把安装版密码库直接当成可移植密文。
- 上传前应确认仓库中没有本地配置、主机密钥、密码、私钥或个人路径。
