# P10 发行与数据位置

P10 将体验产物集中到 ignored `artifacts/releases/`：单文件自包含便携 EXE、真正的安装向导 EXE、使用说明和 SHA-256 清单。旧 artifacts、历史 ZIP、缓存及用户数据不自动清理；日常使用只需选一个 EXE。

## 交付方式

| 方式 | 交付文件 | 运行时与数据 |
| --- | --- | --- |
| 单文件便携 | `PortHarbor-0.2.0-portable-win-x64.exe` | 自带 .NET 8；编译元数据指定默认便携模式，无需标记或启动脚本；数据在 EXE 旁 |
| 当前用户安装 | `PortHarbor-0.2.0-setup-win-x64.exe` | 自带 .NET 8；Inno Setup 中文/英文向导；程序默认 `%LOCALAPPDATA%\Programs\PortHarbor`，数据 `%LOCALAPPDATA%\PortHarbor` |
| 历史目录发布 | 原流程的 `*-installed-*.zip` / `*-portable-*.zip` | 保留工程验收兼容；installed ZIP 仅表示数据布局，不是安装器；默认需独立运行时，可用 `-IncludeRuntime` |

新包均要求 Windows 10 1809+ / Windows 11 x64。自包含增加下载体积，换取免安装 .NET、离线启动；不裁剪运行时。实际字节数与 SHA-256 见 `release-manifest.json`。

“单文件”描述交付，不承诺使用后永远只有 EXE。便携首次启动创建 INI，按使用情况生成布局、终端偏好、活动记录及日志。.NET 原生组件由宿主解包到用户临时缓存，通常在 `%TEMP%\.net` 下，可由 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 控制；缓存不是会话备份。Windows 自带的 RDP/mstsc、DPAPI、ConPTY 不随包重新实现。

PortHarbor 自有代码及项目文档采用根目录 `LICENSE` 中的 MIT License，Copyright (c) 2026 fantuanzzzz。项目许可、应用依赖与随包 .NET runtime 的许可文本均嵌入程序集，通过“帮助 → 第三方许可”查看；便携交付无需单独携带许可目录。源码保留 `LICENSE`、`THIRD-PARTY-NOTICES.md`、`licenses/` 供审阅，第三方组件条款不变；历史已生成 EXE 不会随源码自动更新，MIT 版需重新构建。

## 安装、升级与卸载

仅为当前用户安装，无需管理员权限；向导可选择程序目录、桌面快捷方式，默认创建开始菜单快捷方式和 HKCU 卸载入口。固定 AppId 为 `PortHarbor`。程序目录内 `PortHarbor.installed` 使同一单文件程序使用安装模式。

模式优先级：显式 `--portable` / `--installed`（重复时后者优先） > installed 标记 > portable 标记/已有 INI/编译便携默认 > 常规安装默认。`--data-dir` 只指定目录，不隐式改变格式。调试和历史普通目录发布的编译默认保持安装模式。

安装器拒绝用户数据目录及其子目录、已有 `PortHarbor.ini` 或 `PortHarbor.portable` 的便携目录，静默安装同样检查。不自动导入便携副本，不覆盖便携主密码库；可导出/导入会话，密码需重新设置。

升级前退出程序，运行新安装器；固定 AppId 使其沿用原目录与快捷方式，替换受管理程序文件。卸载通过 Windows 设置或 `unins000.exe`；只删除安装器登记文件、快捷方式及卸载入口，不递归删除整个安装目录。未知文件和用户数据保留。没有自动更新、自动删除数据、机器范围安装或代码签名。

## 构建与证据

先安装 .NET 8 SDK，构建工具只保存在 ignored 项目缓存：

```powershell
pwsh -File packaging/Get-InnoSetup.ps1 -Download
pwsh -File build.ps1 -P10
```

工具脚本从 [官方固定 6.7.4 发布](https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe)下载，要求有效的 Pyrsys B.V. Authenticode 签名，再用官方 `/PORTABLE=1` 解包到 `.build-home/tools`，不注册工具卸载项或文件关联。[官方校验说明](https://jrsoftware.org/isdl-verify.php)。已有编译器可传给 `packaging/Build-P10.ps1 -Compiler ...`；构建不主动下载编译器。

中文翻译来自[官方翻译页](https://jrsoftware.org/files/istrans/)列出的 [ChineseSimplified.isl](https://raw.githubusercontent.com/jrsoftware/issrc/refs/heads/main/Files/Languages/ChineseSimplified.isl)，2026-10-30 下载，固定副本在 `packaging/ChineseSimplified.isl`，SHA-256 为 `E0B0B350E2245F3C5E65586DFE43D574F6E7F06F2261149ABA284954B3FC9A8D`。上游地址可变，构建使用仓库副本，保留维护者声明。

构建从项目文件读取正式版本，锁定恢复并断言 publish 目录恰好只有一个 EXE，生成正式安装器和使用独立随机 AppId/开始菜单组的验收安装器。在含中文、空格的隔离目录验证便携首次启动/重启、安装、快捷方式及模拟升级/卸载。当前为 0.2.0 → 0.2.1 模拟升级，检查数据与未知文件保留；模拟版本只用于验收，正式包为 0.2.0，不表示历史版本实机升级已验证。

报告及安装日志在 ignored `artifacts/distribution-validation/<随机ID>/`。失败 staging 移到对应报告目录，发布回滚不完整时原地保留恢复材料；所有发行检查通过后复用原子替换/回滚脚本，仅更新四个明确产物文件，不清理整个目录。manifest 记录相对仓库根目录的验收报告路径及哈希，不公开本机绝对路径。GitHub v0.2.0 上传附件集中在 `artifacts/releases/v0.2.0/`，额外附 MIT 正文和 SHA256SUMS 校验文件。

manifest schema 2 补入提交号、Dirty、源码文件内容摘要、SDK、实际 NETCore/WindowsDesktop runtime pack 版本、仓库各依赖锁 SHA-256 和本次发行检查摘要。程序内诊断包 schema 2 包含已过滤的构建身份，与预览一致；普通 IDE 构建未提供的元数据为 null，不伪造为干净发布。未提交源码的摘要可区分同一 commit 的不同本地构建，复现仍应先提交并在干净 checkout 构建。

新实例取得数据目录的 `.portharbor.lock` 独占句柄后才加载数据；同目录的其他窗口会收到明确提示，不同目录可并行。RDP 文件放在 `rdp-temporary/<GUID>/`，有独占归属锁和客户端 PID/启动时间；启动与下次 RDP 前清理可证明已结束的遗留文件。未知归属、仍运行/无法核实的客户端、启动身份记录空窗、旧根目录 `.rdp` 均保守保留，详见手册 E09。

安装验收通过 Unicode Shell 接口读取快捷方式，按实际文件身份核对目标及无附加参数；不依赖中文路径、长短路径或目录别名的字面形式。`packaging/ShortcutInspection.cs` 仅用于构建测试，不打入用户程序；失败时检查 `shortcut-details.json` 和 distribution-results 的 Failure。专项命令见[测试说明](TESTING.md)。

本机验收不等于无 .NET 的干净 Windows 验收。完整环境、步骤、预期与证据要求见[实机验收手册](REAL-ENVIRONMENT-ACCEPTANCE.md)；测试入口见[测试说明](TESTING.md)。还需 Windows Sandbox/VM 或另一台未装 .NET 的 x64 Windows 离线启动、完整向导、真实新旧版本升级及卸载保留数据；真实多 DPI、高对比度和服务器互通继续单列。
