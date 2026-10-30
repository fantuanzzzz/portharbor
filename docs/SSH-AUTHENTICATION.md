# P09 SSH 交互式认证

日期：2026-10-30。适用于 SSH 终端和 SFTP，复用同一 SSH.NET 认证接线。

## 认证流程

SSH 连接保留原密码或私钥认证；当服务器声明并实际发起 keyboard-interactive 时，追加交互认证方法。每一轮服务器可以发送说明文字和多个提示，客户端按原顺序收集响应后一次返回。提示的 `IsEchoed` 为 false 时使用隐藏输入，为 true 时显示输入。密码、私钥口令和一次性验证码都只存在于本次连接调用中，不保存到会话配置、主密码库、诊断日志或终端内容。

SSH 终端和 SFTP 使用同一个 `SshAuthenticationPromptHandler`。主窗口将 SSH.NET 的工作线程回调切回 UI 线程，逐个显示「SSH 交互式认证」输入框；任意一轮点击取消都会取消本次认证。主机密钥事件先按原有指纹确认和保存规则处理，交互认证不能绕过主机密钥校验。

认证提示显示期间由用户决定等待或取消；现有 15 秒连接期限仍在运行，但不会强制关闭正在等待用户输入的原生提示框。用户取消是及时结束等待的明确动作；若提示期间连接期限到期，则在用户提交或取消提示后连接立即以超时结束并释放 SSH client、通道和私钥资源。首版不提供超时自动关闭模态输入框。认证失败按 `AuthenticationFailed` 呈现，不显示厂商原始异常。

## 实现边界

Application 只定义 `SshAuthenticationPrompt`、`SshAuthenticationChallenge`、取消异常和响应委托；不引用 SSH.NET 或 WinForms。Infrastructure 在 `SshClientSupport` 中转换 SSH.NET prompt，终端与 SFTP 适配器只接收可选委托。UI 不记录响应内容，认证失败及取消沿用现有诊断生命周期。

首版不自动重试挑战、不缓存一次性响应、不并行显示多个提示、不提供跨连接的认证会话。服务器发来的提示数量与客户端返回数量必须一致；数量不一致按取消处理，避免部分响应被误解释。

## 验证

本地 Paramiko fixture 的 `keyboardtest` 用户要求两轮响应：`otp-123` 和 `device-yes`；`canceltest` 用于取消清理。回环检查覆盖 SSH 终端两轮成功、SFTP 同一处理器、错误响应、用户取消、状态关闭和既有主机密钥/网络故障。入口：

```powershell
./build.ps1 -ExtendedBaseline
```

本轮完整回环 **100 项**通过；核心 **489 项**、应用层 **225 项**、完整 UI **241 项**通过。真实服务器的认证提示文字、私钥供应商行为、多 DPI、高对比度及原生输入框人工体验仍需专项验收。决定见 [ADR 0019](adr/0019-ssh-interactive-authentication.md)。
