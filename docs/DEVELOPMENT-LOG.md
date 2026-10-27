# PortHarbor Development Workbook

This workbook records every development session. Each entry covers what changed, why it changed, files affected, verification, remaining problems, and follow-up work.

本文档保留历史开发与验证记录；各条目的“下一步”表示当时的安排。当前阶段状态、任务勾选及验收条件统一维护在[技术方案第 8 节](TECHNICAL-PLAN.md#8-分阶段实施)，文件传输当前契约见[文件传输约定](FILE-TRANSFER.md)。历史条目不随当前实现变化而回写。

## 2026-10-27 - 收工记录

- 今日开发停在功能提交 `16c5fa2`：T1–T5 软件收尾完成。随后只整理交接文档，尚未开始下一轮 UI/功能开发。本次未执行 GitHub 推送。
- 阶段 0–3 清单维持 **49/51**：T6 标准测试框架迁移待开发，真实网络/串口故障专项待环境验收；真实 RDP、FTPS、私钥、多服务器互通和另一台 Windows 复现仍保留验收边界。阶段 4 未启动，正式安装向导及单文件便携包未实现。
- 今日最后一次完整验收报告生成于 `2026-10-27 16:31:02 +08:00`：Application 175、核心 366、Windows 17、协议 96、UI 61，共 **715 项检查**通过，另有 **4 项冒烟**及单独 **5 个发布替换/回滚场景**通过。收工整理只核对已有报告、文档链接与差异，没有重跑构建或将历史结果记作新验收。
- 本机产物和报告位于 ignored `artifacts/technical-closure/release/`，包含 `PortHarbor-0.1.0-portable-win-x64.zip`、`PortHarbor-0.1.0-installed-win-x64.zip` 及 `engineering-baseline.json`。两个 ZIP 均为框架依赖目录包，安装布局包不是安装向导，便携包不是单文件；运行需 .NET 8 Desktop Runtime。源码推送不包含这些本地产物。
- 下次开发起点建议：持久化终端字体、配色及复制/粘贴偏好。先核对现有菜单和默认行为，再实现保存、重启恢复及配置兼容；T6 可独立并行推进，实机专项在取得对应环境后验收。

## 2026-10-27 - T1–T5 软件收尾、完整发布验收与产品开发交接

### 实现

- T1：发布流程跟踪旧备份及新目标，失败逆序恢复；回滚受阻保留恢复资料。补齐四种故障及成功替换共五个场景，路径变更前限制到输出目录及私有 staging，故障注入接入 Windows CI。
- T2：新增实际随机 `.invalid` 解析失败、静默 SSH 握手和同步 SSH shell 通道无响应检查。终端启动及输入/resize 默认各限时 15 秒，期限可经构造参数设置；超时中止并等待底层所有者，空闲读取不设固定超时。连接异常分类和释放保留在文件连接门禁内，避免排队请求与清理竞争。
- T3：`BackgroundTask.Observe` 观察故障，未发布诊断的终端任务提供安全兜底；UI 退出等待文件、终端及 RDP 观察任务。ConPTY 持有并等待子进程退出观察器。分类路径校验、会话筛选/排序及独立快照迁入 Application，控件布局/绘图留在 UI。
- T4：终端、文件、RDP 诊断接入配置目录 `logs/`。记录强类型允许字段和实际时长、收发/传输字节、解码字符数、超时参数及恢复状态；不写端点、路径、内容或原始异常。设置菜单保存启用、大小、保留天数和归档数量；默认约 1 MiB、14 天、8 个归档，写入故障隔离并保留有界内存事件。
- T4：帮助菜单提供最多 500 条固定事件的诊断包预览，通过文件选择端口导出完全相同的 JSON，取消选择不写文件；不自动上传。新增界面回归验证预览/取消/导出一致及设置重启保留，决策见 ADR 0009。
- T5：公共元数据与七种不可变协议运行配置分离；网络/Windows 终端、FTP/SFTP 适配器及 RDP 配置生成已消费对应类型。`Session` 保留为兼容编辑/存储 DTO，七协议非默认字段通过 JSON/INI 往返，旧 fixture、凭据关联及导入失败回滚继续通过，决策见 ADR 0010。

### 生命周期和纯逻辑审计结果

| 范围 | 所有者及关闭/失败处理 |
| --- | --- |
| 主窗口任务、标签关闭、应用退出 | 观察启动任务；关闭等待活动连接；后台失败安全记录，晚到的控件更新忽略 |
| 文件视图/文件连接 | 操作取消后等待 finally 及连接释放；尽力清理不覆盖原错误，以恢复状态表达不确定结果 |
| 终端/流适配器 | 启动、读取、写入及关闭有明确所有者；已报告异常由关闭流程消费，不重复抛出阻止清理 |
| ConPTY | 子进程、管道和观察任务由实例持有；关闭先终止/释放，再等待观察器 |
| RDP | 外部进程退出或软件退出时停止观察并清理临时配置；不杀外部客户端，文件占用时删除为尽力操作 |
| 原有清理 catch | 凭据剪贴板竞争、已报告连接失败、资源逐项释放和临时文件删除保留尽力语义；不把这些 catch 改为泄露原异常的 UI 弹窗 |
| 剩余纯逻辑 | 会话库/分类规则移入 Application；已有导入导出、诊断呈现和生命周期用例保持 Application；模型/校验保持 Domain；格式解析留在存储，显示留在 UI |

### 验证与边界

- 最终 `build.ps1 -ExtendedBaseline -OutputDirectory artifacts\technical-closure\release` 成功，Release 编译 0 警告、0 错误。Application 175、核心 366、Windows 17、协议回环 96、UI 61，共 **715 项**，另有 ConPTY 与三种双版本启动共四项冒烟通过。
- `tests/build-rollback.ps1 -OutputDirectory artifacts\technical-closure\rollback` 的五个场景通过；受阻回滚的警告为预期注入，旧备份和 staging 保留可恢复内容。成功替换则清除 staging/备份。
- 最终报告为 ignored `artifacts/technical-closure/release/engineering-baseline.json` 及同目录各 `*-results.json`；便携及安装布局 ZIP 同目录生成。没有新增安装向导、单文件便携包或运行时内嵌。
- 本轮验证当前工作区，没有重新执行独立干净 checkout。另一台 Windows、物理断网/串口阻塞、真实 RDP 登录、FTPS、私钥和多服务器互通仍待环境；原 Temp 克隆 FTP 550 根因未确认。远端提交未知状态仍需显式核对，无自动恢复承诺。

### 下一步

- 阶段 0–3 清单 **49/51**：剩余 T6 标准测试迁移和环境故障验收。约 96% 是条目比例，不是工时或代码覆盖率；阶段 4 按新平台/框架需求启动。
- T1–T5 软件范围完成，允许进入现有 WinForms UI、功能和体验开发。下一步建议持久化终端字体、配色及复制/粘贴偏好，重启后恢复；T6 与产品开发可并行，实机验收按对应功能发布前安排。

## 2026-10-27 - T1 发布回滚故障注入与备份保全

### 问题与实现

- 按用户要求依次开展技术方案剩余 T1–T5，本条先记录 T1。旧 `build.ps1` 在发布回滚本身失败时仍会由 `finally` 删除整个 staging，可能一起删除尚未恢复的旧产物备份。
- 将发布移动/回滚抽至 `scripts/Publish-StagedArtifacts.ps1`，构建仍先完成验证并通过 staging 提交最终产物。对安装目录、便携目录及两个 ZIP 跟踪备份和已安装目标并逆序回滚。
- 若占用或权限错误阻止恢复，保留 staging 下的 previous 备份，并在错误中给出恢复目录；成功发布或完整回滚才清理 staging。
- 重写 `tests/build-rollback.ps1` 为不需重建应用的直接故障注入，覆盖替换前失败、装入两个目标后失败、旧 ZIP 被占用；额外测试新目标被锁住导致回滚不完整，确认旧版文件仍在备份目录中且恢复路径仍存在。

### 验证

- `tests/build-rollback.ps1 -OutputDirectory artifacts/release-rollback-t1` 四类场景通过。旧 ZIP 占用时没有覆盖旧版本；部分安装后目标被占用时，脚本输出恢复目录并保留旧目录备份。
- PowerShell 7 执行策略默认拒绝直接脚本启动，使用显式 `-ExecutionPolicy Bypass` 的测试命令运行；不是产品构建失败。
- 完整发布构建/扩展基线会在 T1–T5 收尾后统一重新执行；本轮测试产物位于 ignored `artifacts/release-rollback-t1/`。

### 下一项

- 继续 T2：补充实际主机名解析失败及静默 SSH 对端连接超时，并检查超时分类是否穿过文件连接的适配器清理边界。

## 2026-10-27 - SFTP 覆盖提交恢复限时与技术方案进度核对

### 问题与实现

- 用户要求继续下一步，并明确了解技术方案距 UI、功能与用户体验开发还有多少工作。本轮只完成既定 SFTP 提交/恢复增量及进度盘点，不实施其余收尾包或更换 UI。
- 新增 `SftpCommitChecks.cs` 和 loopback fixture 的 9 个故障场景：恢复无响应、提交拒绝后恢复、恢复拒绝、备份移动前/后断线、新文件重命名前/后断线、恢复已执行但确认丢失，以及成功提交后备份删除无响应。每场景检查原始结果/操作编号、关闭期限、目标/备份/暂存内容及新连接显式恢复/清理，共增加 36 项协议检查。
- 修复前运行协议入口，在 `SFTP stall-restore preserves the original outcome and bounds recovery/close time` 处失败；记录与 fixture 日志保留于 ignored `artifacts/sftp-commit/before-fix/`。旧实现对恢复和备份清理使用 `CancellationToken.None`，可能阻塞到协议操作超时。
- `SftpRemoteFileSystem` 将两次重命名都纳入失败恢复范围；独立 5 秒内，仅在目标缺失且本次备份存在时恢复。恢复失败仍重抛原始提交错误，保留备份，不自动重连或重放；确认成功后的备份清理另有独立 5 秒，不把清理失败改报上传失败。
- 测试验证恢复无响应与后续暂存清理合计约 10 秒退出，仍报告原始 `SftpPermissionDeniedException`；成功后的备份清理无响应约 5 秒返回成功。两段清理时限可顺序累加，不能宣称整个操作/关闭只需 5 秒。
- 修正文件视图的取消文案：不再承诺旧目标必然在原路径，提示刷新并核对结果。远端已执行但确认丢失时，新文件可能已提交，或旧文件仅在 `.bak` 中；现有端口可通过新连接显式恢复，没有自动恢复向导。

### 验证

- `build.ps1 -ExtendedBaseline -OutputDirectory artifacts/sftp-commit/release` 在当前 Windows 用户桌面环境完整通过；锁定恢复与 Release 构建 0 警告、0 错误。
- Application 163、核心 336、Windows 14、协议 93、WinForms UI 57 项通过，共 **663 项**；ConPTY 标记、安装隔离启动、默认便携首次启动和重启四项冒烟通过。
- JSON 报告、截图、终端输出及框架依赖安装布局/便携 ZIP 位于 ignored `artifacts/sftp-commit/release/`。本轮未重新执行干净克隆、自包含分支或发布回滚故障注入，没有改依赖或用户数据格式。

### 进度核对与下一步

- 原阶段清单未列出正文第 4、6、7 节已要求的协议配置类型化、本地持久诊断、可预览诊断包及标准测试框架迁移；本轮补成四个未完成项，并将已完成 SFTP 提交故障从剩余故障任务中单列勾选。没有把未实现能力计为完成，也没有扩展原产品范围。
- 阶段 0–3 共 51 项、完成 42 项、剩余 9 项：阶段 0 为 10/12、阶段 1 为 12/17、阶段 2 为 10/12、阶段 3 为 10/10 实现但实机验收未收齐。约 82% 仅是条目完成率，不是工时、代码覆盖率或整个含阶段 4 蓝图的百分比。
- 技术方案第 8 节列出 T1–T6 六个软件交付包及独立环境验收，明确转入现有 Windows UI/功能/体验开发的建议门槛；T6 标准测试迁移可并行，新平台/新框架另按阶段 4 启动。没有据此提前宣布技术方案全部完成。
- **下一步 T1：发布故障注入与回滚收尾**，补齐替换前、部分替换后及旧产物被占用的检查。真实设备、FTPS、私钥、多服务器互通及原 Temp 克隆 FTP 550 的原因仍待对应环境专项验证。

## 2026-10-27 - 传输中断时保护旧目标与下载完成门禁

### 问题与修复

- 检查发现下载只读到 EOF 就替换本地文件，没有校验已知长度或 FTP 最终完成响应。先新增共享回归：远端声称 100 字节、实际只返回 3 字节，旧实现未抛出预期异常；失败记录为 ignored `artifacts/transfer-interruption/application-before-fix.json`。
- `FileConnection` 下载先写本地唯一 `.part`，核对已知字节数并关闭两条流，再调用新增 `IRemoteFileSystem.CompleteReadAsync`；确认成功且未取消才替换本地目标。FTP 等待最终控制响应，失败响应拒绝提交；SFTP 文件流关闭已完成协议确认，新增端口只检查取消。
- 公共复制策略拒绝提前 EOF 和超额数据；已知长度不符不提交，未知长度继续支持且仍要求协议确认。长度核对不是内容哈希校验。上传旧目标保护沿用暂存后提交语义，没有引入自动重连或重放。
- 共享检查增加每协议 6 项：提前 EOF 保留旧文件、不创建新目标、拒绝超额字节、未知长度最终确认失败保护、未知长度正常完成，以及关闭时取消待确认下载并清理本地暂存。已有正常下载检查同时要求先关闭读流再确认。
- 协议 fixture 增加 FTP/SFTP 上传/下载中途断开，以及 FTP 数据收全后返回 426。新增 `FileTransferInterruptionChecks.cs` 接入 `--integration`，检查真实故障标记、Reading/Writing 阶段、目标保护、下载临时文件、关闭期限和通过新连接显式清理上传残片。FTP 故障关闭延后到 I/O 循环，避免内联关闭数据通道造成 fixture 自身状态异常。

### 验证与交付

- `build.ps1 -ExtendedBaseline -OutputDirectory artifacts/transfer-interruption/release` 在当前 Windows 用户桌面环境完整通过；锁定恢复、Release 构建 0 警告、0 错误。
- Application 163、核心 336、Windows 14、协议 57、WinForms UI 57 项全部通过，共 627 项检查；ConPTY 标记、安装隔离启动、默认便携首次启动与重启四项冒烟通过。
- JSON 报告、截图、终端输出及两个框架依赖 ZIP 位于 ignored `artifacts/transfer-interruption/release/`。本轮是工作区验收，没有重跑干净克隆、自包含分支或发布回滚故障注入；没有改变用户配置、数据格式或依赖版本。
- 同步 README、文档索引、技术方案、文件传输及诊断约定。故障夹具只绑定 loopback，不进入应用发行包。

### 限制与下一步

- 控制连接失效后，远端 `.part` 的 5 秒尽力清理可能失败；测试验证残片仍保持独立临时名称，旧目标不被覆盖，且可通过新连接显式删除。不能承诺断网后服务器端无残留。
- 本轮覆盖的是上传数据复制阶段断线；SFTP 将旧目标移到 `.bak` 后的覆盖提交窗口仍可能暂时没有目标路径，恢复失败时可能留下 `.part`/`.bak`。下一步补齐该窗口的断线与备份恢复检查，再完善恢复策略。
- 真实服务器、FTPS、静默丢包、磁盘满、等长远端内容变化及实际 DNS 失败仍待专项验证。确认等待中的关闭通过共享端口检查，既有 UI 生命周期回归通过，没有声称完成实体设备验收。

## 2026-10-27 - SSH 突然掉线清理与 FTP 连接失败阶段

### 问题与实现

- 新增真实 loopback 对照用例，复现 SSH 底层连接断开后 ShellStream 同步读取在 10 秒检查期限内仍未退出；正常 Shell 退出原本可完成。修复 `SshTerminalTransport`：建连后保存客户端 ErrorOccurred 的原异常并请求中止，释放流及客户端以解除阻塞，通道创建/读取优先传回原始连接故障；应用层完成关闭且保留安全失败提示。
- `DiagnosticClassifier` 明确将 SSH.NET 的 ConnectionLost 归类为可重试网络故障，密钥交换失败继续归为协议错误。不新增自动重连或输入重放，主动取消仍保持原流程。
- 连接拒绝回归暴露 FTP 在 TCP 建立前就报告 Authenticating。改为初始 Connecting；库明确抛出 FtpAuthenticationException 后标注 Authenticating，分类器单独识别认证错误，避免拒绝连接与密码错误混淆。
- 新增 `NetworkFailureChecks.cs` 并接入既有 `--integration`，覆盖四种协议拒绝连接、FTP 错误密码、SSH 正常 Shell 退出/传输连接丢失、Telnet 正常 EOF/TCP reset。用绑定但不监听的端口避免端口释放竞态；reset 直接关闭零 linger socket，避免 TcpClient.Dispose 的正常关闭掩盖重置。
- Python fixture 增加仅测试使用的正常 Shell 退出和突然断开命令；不进入发行包。核心新增 SSH 连接丢失/密钥交换、HostNotFound 和 FTP 认证类型分类检查，均不解析原始异常消息。

### 验证与交付

- 在当前 Windows 用户桌面环境运行 `build.ps1 -ExtendedBaseline -OutputDirectory artifacts/network-failures/release`，锁定恢复、Release 构建、协议/UI/ConPTY 与发布完整通过，0 警告、0 错误。
- Application 151、核心 324、Windows 14、协议 37、UI 57 项通过，共 583 项检查；ConPTY 标记、安装隔离启动、默认便携首次启动与重启四项冒烟均通过。报告、截图、终端输出和两个框架依赖 ZIP 在 ignored `artifacts/network-failures/release/`。
- 先前未修复版本的协议检查报告在 `artifacts/network-failures/integration-before-fix.json`，记录正常 SSH 退出通过、随后连接丢失读取超时；最终报告验证该问题已解除且取消/正常退出未误报。
- 更新 README、文档索引、技术方案及终端/诊断/文件传输约定。未改配置格式、用户配置或依赖版本，也未覆盖既有即用自包含便携目录。

### 遗留与下一步

- 本轮是当前工作区完整验收，不冒充新增提交的干净克隆验收，也没有重跑自包含分支或发布回滚故障注入。
- 真实 DNS 失败只有类型分类检查；静默丢包、远端无响应时长、文件传输中断线、真实串口与生产服务器互通仍待专项验证。SSH 的同步 Shell API 仍依赖关闭所属资源解除阻塞，不表示底层全部原生异步化。
- 下一步补齐 FTP/SFTP 上传或下载中途断线，验证错误阶段、旧目标内容保留、临时文件清理和关闭等待。

## 2026-10-27 - 独立干净 checkout 验收及协议失败日志归档

### 验收对象与结果

- 验收提交为 `e110c4e27da743c926515076300bbfed6004089b`（`v1.0.5`）。通过 `git clone --no-hardlinks` 创建独立克隆，没有复制原工作区的 `bin/obj`、`.packages` 或 `.build-home`；运行前后克隆的 Git 工作树均为空。
- 在当前 Windows 用户的交互式桌面环境，以 .NET SDK 8.0.426 完成锁定恢复、Release 构建和 `build.ps1 -ExtendedBaseline`。构建 0 警告、0 错误；Application 151、核心 320、Windows 14、协议回环 26、WinForms UI 57 项全部通过，共 568 项检查。该数字不是代码行/分支覆盖率。
- ConPTY 界面 `CONPTY_OK`、安装模式隔离目录启动、默认便携首次启动和重启四项冒烟均通过；生成安装布局及便携 ZIP。本轮为框架依赖包，没有重跑 `-IncludeRuntime` 或发布回滚故障注入。
- 原始报告位于 ignored `artifacts/clean-checkout-acceptance-20261027/artifacts/baseline/engineering-baseline.json`，同目录包含各组 JSON、截图、终端输出与构建产物。GitHub 源码只保留本文摘要，不提交生成文件。

### 排查与脚本改动

- 沙箱身份运行时 DPAPI 返回系统错误；切换当前 Windows 用户环境后核心及 Windows 检查通过。这一前置环境条件不作为产品缺陷修复。
- 更早的系统 Temp 克隆在 FTP 首次上传临时文件时重复返回 `550 No such file or directory`；同一提交在项目盘独立克隆及主工作区完整基线均通过。**根因尚未确认**，不能据此断定所有 Temp 路径或所有 FTP 服务器均正常，也没有修改 FTP/SFTP 产品实现。
- `build.ps1` 增加协议集成失败时将 `protocol-fixture-out.log` 和 `protocol-fixture-error.log` 复制到报告目录的处理，避免 staging 清理后失去诊断现场。主工作区完整扩展基线已通过；独立干净克隆验收对象仍是上述提交，不包含这项新增脚本改动。
- 单独提取实际 `Invoke-ExtendedChecks` 函数、启动真实 loopback fixture，并在协议计时入口注入预期失败：原错误继续抛出，两份日志保留，APPDATA 恢复，fixture 进程停止。检查产物在 ignored `artifacts/acceptance-investigation/log-retention-check/`。脚本语法、四份文档本地链接及 `git diff --check` 均通过。
- 同步更新项目 README、文档索引和技术方案，区分本机干净克隆已验收与另一台机器、真实设备/服务器尚待验收。原始 fixture 日志仅供本地诊断，不新增 CI 自动上传。

### 下一步

- 按技术方案阶段 1 补齐断网、无效主机和连接中断等故障场景；真实串口、RDP 登录、FTPS、私钥登录和生产服务器互通留待对应环境验收。
- 阶段 0 的本机干净 checkout 和计时基线已验收；发布失败的更多故障注入与回滚范围仍待补齐，暂不将整个阶段标为完成。

## 2026-10-27 - 扩展工程基线覆盖所有 Windows 自动化入口

### 本次开发内容

- `build.ps1 -ExtendedBaseline` 在既有发布基线外启动仅绑定 loopback 的协议 fixture，并运行协议集成、WinForms 界面截图和 ConPTY 输入/输出冒烟。报告同时汇总 Application、核心、Windows、协议和 UI 的检查数；ConPTY 单独验证 `CONPTY_OK` 标记。
- 每个扩展入口和协议 fixture 启动都写入 `engineering-baseline.json` 耗时项；报告注明需 Python 3.12+ 及 `tests/requirements.txt`，普通发布构建不增加 Python 依赖。
- README 增加扩展基线使用命令，技术方案下一步收敛到独立干净 checkout 复核及真实硬件/服务器专项验收。

### 验证与边界

- 使用已安装项目锁定的 Paramiko 5.1.0 / pyftpdlib 2.2.0 执行完整扩展基线；本轮实际结果和各入口耗时见 `artifacts/engineering-baseline-extended-20261027/engineering-baseline.json`。
- 扩展入口需要桌面会话，CI 当前工作流已运行相同的协议、UI/ConPTY 入口但未统一计时；干净 checkout 和另一环境的可复现性仍待验收。该报告是检查计数，不代表源代码行/分支覆盖率。

## 2026-10-27 - 阶段 0 工程基线报告

### 本次开发内容

- 发布脚本现在为差异检查、锁定恢复、Application/Windows 构建与检查、发布、启动冒烟和两种 ZIP 压缩记录实际耗时，并记录 SDK 版本，输出 `engineering-baseline.json`。
- 基线报告汇总各检查入口的通过/失败数量，并明确范围：当前发布脚本覆盖 Application、核心 Windows 检查和发布启动；协议回环、WinForms UI/ConPTY 是独立入口；没有引入 coverlet，因此不把检查数量冒充源代码行/分支覆盖率。
- 新增 ADR 统一模板，要求记录背景、选项、决策、验证、影响限制和复审条件。

### 验证与遗留

- 本轮完整 Release 基线报告见 `artifacts/engineering-baseline-20261027/engineering-baseline.json`；干净 checkout、协议回环/UI/ConPTY 统一耗时仍需在独立环境补齐。
- 阶段 0 清单的“耗时/覆盖范围基线”和“ADR 模板”已完成；阶段 0 仍因干净 checkout 验收未完成而保持进行中。

## 2026-10-27 - 修复默认便携启动并重发自包含包

### 问题与修复

- 用户运行即用便携版时，Store 报“Resolved application paths must be absolute and keep the configuration file inside its directory.”。对旧包不带 `--data-dir` 运行启动冒烟，复现退出码 1。原因为真实 `AppContext.BaseDirectory` 自带末尾目录分隔符，而配置文件父目录不带，原字符串边界校验错误拒绝同一目录。
- 上一条即用包的独立测试显式指定了隔离数据目录，未覆盖默认便携目录；此前的通过结果不代表默认双击启动通过。旧 `ready-to-use-20261027` 包存在该缺陷，本条的新包取代其可用性结论。
- Windows 路径解析对程序目录和显式数据目录使用 `Path.TrimEndingDirectorySeparator(Path.GetFullPath(...))`，保留盘符根目录分隔符；Store 的绝对路径与目录边界校验保留。未更改配置格式或用户原配置。
- 新增 7 项核心回归：默认便携路径、首次 INI、写入会话后重启、便携及安装显式目录的末尾分隔符、替代分隔符及盘符根目录。
- 发布脚本改为等待实际 EXE 并校验退出码，覆盖安装隔离目录、无数据目录覆盖的便携首次启动和重启，要求程序旁生成 INI；仅清理私有 staging 中测试产生的空配置，发行 ZIP 不夹带用户配置。增加 `startup-smoke-results.json` 报告。

### 验证与交付

- Application 151、核心 320、Windows 14、WinForms UI 57 项通过；Release 编译 0 警告、0 错误。完整 `build.ps1 -IncludeRuntime -OutputDirectory artifacts/portable-startup-fix-20261027/release` 通过，三项发布启动检查退出码均为 0。
- 从最终便携 ZIP 重新解压至含中文和空格的独立目录，以系统临时目录作为工作目录，完成首次启动、重启和完全无参数的主窗口启动/正常关闭，三次退出码均为 0，配置均落在 EXE 旁。无参数验证确认主窗口标题为 PortHarbor 连接工作台，而非错误对话框。
- 新版可直接运行 `artifacts/portable-startup-fix-20261027/release/portable-win-x64/PortHarbor.exe`；ZIP 为同目录 `PortHarbor-0.1.0-portable-win-x64.zip`，75,535,590 字节，包含 Windows x64 运行时且没有 INI/JSON 用户配置。包含当前诊断消息目录增量。
- ZIP SHA256：`8587C23A33D9870EFB67B61E86BD55F0CB4D3F6AF76B7BF6B443F39F8D3A3A08`。重新解压与无参数验证报告位于 ignored `artifacts/portable-startup-fix-20261027/extracted-startup-results.json`，未覆盖用户原即用目录。
- `git diff --check` 通过。本次只处理便携启动阻断；阶段 0 基线开发仍为后续任务。

## 2026-10-27 - 即用便携版与本地诊断消息目录

### 先交付的便携版

- 在继续改动源码前运行 `build.ps1 -IncludeRuntime -OutputDirectory artifacts/ready-to-use-20261027`，冻结上一轮会话导入导出策略完成后的可用版本。Application 109 / 核心 271 / Windows 14 项及发布启动退出冒烟通过。
- 生成 Windows x64 自包含便携包，ZIP 75,533,733 字节，包含 coreclr、hostfxr、Windows Forms 运行时和便携标志，不含用户配置。目录为 `artifacts/ready-to-use-20261027/portable-win-x64/`，可直接运行 PortHarbor.exe；移动时保留完整目录。
- 独立便携 EXE 使用隔离 `--data-dir` 完成启动/退出检查，自动便携模式生成 PortHarbor.ini。后续开发发布到另一目录，不覆盖用户即用目录。
- 即用便携 ZIP SHA256：`D852BEF3FBCFC2B061D6AA4F9508DF4C75F490E6546480273E4C0FE2E422B6DE`。它不包含随后开发的诊断消息目录增量。

### 本次开发内容与原因

- Application 内嵌 `Resources/DiagnosticMessages.resx`，集中错误码、阶段、生命周期回退、准备连接、外部 RDP 启动及组合格式。新增 `DiagnosticMessageCatalog`，已定义枚举必须有明确资源，未知数值按稳定规则回退。
- 把无控件依赖的 `DiagnosticPresenter` 从 UI 移入 Application，继续保留中文文案、正常关闭/连接优先级、RDP 仅客户端启动提示和 8 位操作编号。ConnectionStatusLabel 初始提示也使用目录，后台 UI 调度与失败保留规则不变。
- 呈现仅使用受信任资源、枚举及操作编号，不读取原始 Message 或异常内容；没有引入日志、遥测、其他语言界面、第三方包或运行时外部文件。平台能力及普通菜单提示不在本轮范围。
- 新增共享消息检查，覆盖资源键精确匹配、全部枚举、未定义值、四种 UI culture 默认回退、七种协议所有状态/阶段/错误组合的脱敏与格式。新增 UI 的准备提示、未知错误回退及清理后保留失败检查。
- 新增诊断消息约定与 ADR 0008，更新技术方案、索引、README 和下一步。

### 验证结果

- Application 151 项、核心 313 项、Windows 14 项、WinForms UI 57 项通过；Release 构建 0 警告、0 错误，ConPTY UI 输出包含 `CONPTY_OK`。
- `build.ps1 -OutputDirectory artifacts/diagnostic-messages/release` 完整通过，锁定恢复、应用/核心/Windows 门禁、发布 EXE 启动退出冒烟以及安装版/便携版 ZIP 生成通过。消息资源内嵌在 Application，不依赖源码目录；开发产物与即用自包含便携版分开。
- 报告、截图和开发发布产物位于 ignored `artifacts/diagnostic-messages/`。本轮没有改动协议适配器，未重跑协议回环；最近一次 26 项通过见文件选择器条目。`git diff --check`、本轮文档链接与即用便携 ZIP 哈希复核通过。

### 边界与下一步

- 默认资源仍只有中文；en-US 等仅回退中文，不表示已有多语言产品。未知枚举安全回退不掩盖已定义枚举资源遗漏；发布前检查必须同步维护资源键。
- 下一步补齐阶段 0 可重复验证及耗时/覆盖范围基线，记录各入口真实执行耗时和报告；干净 checkout 需要独立环境与待提交增量的明确处理，不能把当前工作区通过记为干净 checkout 已验收。

## 2026-10-27 - 会话导入导出策略与原子配置更新

### 本次开发内容与原因

- Application 新增 `SessionConfiguration`、`IConfigurationTransfer` 和 `ConfigurationTransferService`，承担分类稳定合并/不区分大小写去重、导入新 ID 分配、导入/导出关闭 SavePassword，以及仅允许分类/会话进入传输 DTO 的规则。既有会话、凭据、密码库与主机信任保持原值。
- `ConfigurationService` 调用应用用例；Store 保留公开 Import/Export 兼容入口并委托同一用例。格式适配器继续验证/读取 JSON v1/v2 与 INI，输出原有无密码 JSON v2 外形，不再持有合并/新 ID/SavePassword 清除算法。
- 导入通过共享锁内最新快照计算并单次原子保存，失败恢复原分类和会话集合，成功后才返回数量；快照与提交结果复制 Session，防止可变记录引用泄漏。没有改成逐条保存，也未新增跨进程事务承诺。
- 新增共享 Application 策略检查和真实存储检查，覆盖源文件保留、ID 冲突/重复导入、空分类、所有协议、快照隔离、读写/提交失败、JSON/INI 交叉格式、并发合并、原子备份及重启凭据关联。发现原 `StorageAdapterChecks` 标为 INI 的分支实际为 JSON 模式，已修正为真正便携 INI。
- 新增 ADR 0007 与会话配置导入导出约定，更新文档索引、README、技术方案任务和下一步。本轮未增加包或改变依赖版本。

### 验证结果

- 独立 Application 检查 109 项、核心检查 271 项通过；Release 构建 0 警告、0 错误。首次沙箱核心运行因当前用户 DPAPI 不可用失败，改在授权 Windows 用户环境重跑后全部通过，没有替换密码保护实现或跳过失败项。
- WinForms UI 检查 54 项通过，继续覆盖取消选择、导入信任确认、真实存储导入与无密码导出。
- 完整 `build.ps1 -OutputDirectory artifacts/configuration-transfer/release` 成功，锁定恢复、Application 109 / 核心 271 / Windows 14 门禁及发布 EXE 启动退出冒烟通过，生成安装版和便携版 ZIP。报告和产物位于 ignored `artifacts/configuration-transfer/`。
- 本轮未修改协议适配器，未重复 FTP/SFTP/SSH/Telnet 回环；最近一次回环 26 项和 ConPTY UI 输出验证见前一文件选择器条目。`git diff --check` 与本轮文档本地链接检查通过。

### 边界与下一步

- 读取和写出文件仍同步；导出目标保持原有直接 JSON 写入行为，不承诺原子替换/备份。进程内共享锁不解决多进程同时写同一配置目录。导入信任确认仍在 UI，真实串口、RDP 登录和 FTPS 等验收未扩大。
- 下一步建立本地诊断消息目录，将错误码/阶段到中文提示的映射与呈现代码分离，补齐提示完整性、未知值回退和敏感信息不泄漏检查；已在技术方案阶段 1 增加待办。

## 2026-10-27 - 文件选择端口与 Windows Forms 平台适配

### 本次开发内容与原因

- Application 新增 `IFilePicker` 和不可变 `FilePickerOptions`，统一单文件打开/保存、建议文件名、过滤器与取消返回 `null` 的契约，不依赖窗口类型。
- 新建 `PortHarbor.Platform.Windows.Forms`，把 `OpenFileDialog` / `SaveFileDialog` 的创建、模态 owner、显示及释放收拢为 `WindowsFilePicker`。此项目只引用 Application 和 Windows Forms 框架，未新增 NuGet 包；已有 Windows 平台库继续不引用 WinForms。
- MainForm 导入/导出和 FileView 上传/下载改为可注入选择器。保持 JSON 过滤器、导出默认名、导入信任确认、无密码/密码库/主机信任导出，以及下载建议文件名清理和覆盖提示。取消选择不触发存储或传输。
- 增加适配器核心检查与真实存储/内存文件传输的 UI 接入检查，覆盖 Unicode/空格路径、取消、异常释放、拒绝/确认导入及精确传递上传下载路径。原有单元入口与 CI 自动包含新增检查。
- 新增 ADR 0006，更新技术方案、README、文档索引和文件传输约定，依赖锁只增加项目引用。

### 验证结果

- 锁定恢复、Application 与 Windows 检查项目 Release 构建通过，最终 0 警告、0 错误。首次沙箱恢复因 NuGet 审计网络不可达产生 NU1900，授权联网重试后消除。
- Application 检查 92 项、核心检查 226 项、Windows/ConPTY 检查 14 项、WinForms UI 检查 54 项、FTP/SFTP/SSH/Telnet 本机回环 26 项均通过；ConPTY 界面输出包含 `CONPTY_OK`。
- 完整 `build.ps1 -OutputDirectory artifacts/file-picker/release` 成功，重新执行锁定恢复及 Application/核心/Windows 门禁、发布 EXE 启动退出冒烟，并生成安装版与便携版目录/ZIP。两种 ZIP 均包含 `PortHarbor.Platform.Windows.Forms.dll`，便携标志仅存在于便携包。
- 报告和截图位于 ignored `artifacts/file-picker/`，发布门禁报告及产物位于 `release/`。`git diff --check` 通过。

### 边界与下一步

- 选择器测试通过拦截显示调用验证对话框配置及资源释放，UI 使用选择器替身，不自动点击系统文件选择/覆盖提示；仍使用重构前相同的 WinForms 原生实现。真实串口、RDP 登录及 FTPS 等兼容验收仍待完成。
- 本轮为架构边界收拢，没有改变配置格式、凭据保护或添加新协议。下一步把会话导入导出的合并、ID 分配与敏感数据排除策略迁入 Application，存储适配器继续负责格式读写和原子保存，补齐失败回滚及 JSON/INI 兼容验证。

## 2026-10-26 - 应用数据路径服务与配置目录边界

### 本次开发内容与原因

- Application 新增 `IAppDataPaths` 和不可变路径快照；Infrastructure 的 `Store`、JSON/INI 存储和 RDP 用例改为消费解析后的配置文件与临时目录，不再自行读取用户目录或程序目录。
- Windows 平台新增 `WindowsAppDataPaths`，集中解析便携标志、`--portable`/`--installed`、`--data-dir`、安装默认目录和 EasyConnect 旧配置源，并把路径标准化为绝对路径。
- 保持现有兼容规则：隐式安装默认目录可复制旧 JSON，旧源保留；便携模式、显式数据目录和已有目标不触发旧目录迁移；RDP 临时文件仍位于原配置目录；UI“打开配置目录”使用同一解析结果。
- Store 校验配置文件必须位于配置目录内，拒绝越界路径；路径解析本身不创建目录，格式存储继续负责目录创建、空便携 INI、原子替换及损坏文件保留。
- 增加路径解析、命令行优先级、迁移优先级、显式目录隔离、RDP 临时目录和路径边界检查；新增 ADR 0005，更新技术方案后续顺序。

### 验证结果

- Application 检查 92 项通过；核心路径检查最终 217 项通过，包含安装/便携、旧配置复制、显式目录、路径边界及既有协议/凭据回归。
- Release 构建无警告、无错误；此前一次路径边界断言错误已修正后重新构建通过。
- Windows/ConPTY 检查 14 项、WinForms UI 检查 45 项和协议回环 26 项均通过；最终发布命令重新执行 Application/核心/Windows 门禁、发布 EXE 启动退出冒烟并生成安装版与便携版 ZIP。

### 边界与下一步

- 本轮没有改变 JSON/INI 格式、DPAPI、配置文件名或 RDP 临时文件位置；真实串口、RDP 登录和路径符号链接策略仍未验证。
- 下一步将 Windows 文件打开/保存对话框边界迁入平台项目，并保留导入/导出文件名、过滤器及无密码数据策略。

## 2026-10-26 - 平台能力检测与连接入口提示

### 本次开发内容与原因

- Application 新增 `IPlatformCapabilities`、能力/不可用原因契约和 `PlatformConnectionPolicy`，仅为 Shell、Serial、RDP 查询各自运行前提，网络协议不依赖本地能力。
- Windows 实现逐次检查系统目录中 DPAPI/ConPTY 所需导出及 mstsc 路径，不启动进程、不打开串口、不使用测试秘密；缺少组件、非 Windows 和探测失败返回固定原因，避免显示原始系统错误。
- 会话编辑器提示不可用原因并禁用连接，仍可仅保存原协议；切换协议、窗口激活及点击连接时重新检查。串口列表为空或枚举失败不会阻止编辑其他协议，均支持手动输入端口。
- MainForm 在创建终端/客户端资源及读取已保存 RDP 密码前检查本地能力；缺失页显示原因、稳定诊断与“重新检查”，恢复后在原标签继续连接。RDP 再次启动也检查能力；恢复已保存会话时从解锁密码库读取凭据，保留复制入口。
- 新增“帮助 → 平台能力”概览，包含 Windows 凭据保护。替换 RDP 页面时同步释放该页面的剪贴板计时器；没有改动密码库格式、数据目录、依赖或锁文件。
- 新增 ADR 0004，更新 README、终端约定与技术方案状态/后续顺序。

### 验证结果

- Application 与主程序/检查项目 Release 构建通过，最终 0 警告、0 错误；差异空白检查通过。
- Application 检查 92 项、核心检查 203 项通过，新增能力映射、网络协议不受限、状态恢复、探测无副作用、缺失组件、探测失败和无效能力检查。
- Windows 检查 14 项通过，验证当前系统实际 DPAPI/ConPTY/串口运行支持与启动器使用的 mstsc 路径一致，原密码库迁移及进程清理检查继续通过。
- WinForms UI 检查 45 项通过，覆盖三个本地协议不可用时仍能保存、空/失败串口枚举可手输、切换网络协议、点击前复查、阻止资源创建、缺失状态重查、原标签恢复、已保存密码恢复及 RDP 再次启动前复查；终端界面输出包含 `CONPTY_OK`。
- FTP/SFTP/SSH/Telnet 本机回环 26 项通过。
- 完整 `build.ps1 -OutputDirectory artifacts/platform-capabilities/release` 成功，锁定恢复、Application/核心/Windows 门禁、发布 EXE 启动/退出冒烟通过；安装版与便携版目录和 ZIP 均生成。
- 报告及产物位于 ignored `artifacts/platform-capabilities/`，最终 Application/核心/Windows 报告位于 `release/`。

### 边界与下一步

- 运行能力可用不保证设备存在/未占用、用户拥有访问权限或远程认证成功；实际连接错误继续沿用诊断。真实串口与 RDP 登录仍需实机验收，没有新增操作系统支持。
- 下一步定义应用数据路径服务，隔离安装/便携配置、旧目录迁移及 RDP 临时文件路径策略，保持既有目录和凭据兼容；之后继续 Windows 文件选择器和导入导出业务策略。

## 2026-10-26 - DPAPI 凭据保护与 mstsc 平台隔离

### 本次开发内容与原因

- Application 新增 `ISecretProtector` 与 `IRemoteDesktopLauncher`；DPAPI 实现迁为 Windows 平台项目的 `WindowsSecretProtector`，原用户作用域、描述字符串、密文编码、密码库算法及参数不变。
- `Store` 构造时显式注入机密保护器，Infrastructure 不依赖平台项目；程序入口及检查调用方完成装配。锁定密码库在调用平台保护前即拒绝保存，平台失败不会回退为便携保护或修改会话。
- RDP 校验、配置文本生成与诊断迁为 Application 的 `RdpLauncher`。Windows 实现负责 UTF-16 临时文件、系统 mstsc 路径、独立文件参数、启动失败和进程退出清理；UI 经用例启动及重试，继续区分外部客户端启动与远程认证成功。
- 补充无平台依赖的 RDP 用例检查、安装/便携库改密转换和保护失败检查，以及真实 DPAPI、受控进程退出清理与 RDP 界面重试检查。框架错误分类补充 `PlatformNotSupportedException` 为平台不可用。
- 新增 ADR 0003，更新 README 与技术方案阶段清单和后续顺序。未引入依赖或调整锁文件。

### 验证结果

- 独立 Application 与主程序/检查项目 Release 构建通过，最终 0 警告、0 错误。
- Application 检查 86 项、核心检查 189 项、Windows 检查 12 项通过；覆盖旧 DPAPI 迁移、便携库不调用平台保护、双向显式改密转换、保护失败保持文件/密钥、篡改密文拒绝，以及 RDP 配置/诊断/退出清理。
- WinForms UI 检查 27 项通过，新增 RDP 页面平台注入、启动失败提示与再次启动；终端界面输出包含 `CONPTY_OK`。
- FTP/SFTP/SSH/Telnet 本机协议回环 26 项通过。
- 完整 `build.ps1 -OutputDirectory artifacts/windows-services/release` 成功，锁定恢复、应用/核心/Windows 门禁、发布 EXE 启动/退出冒烟全部通过，安装版与便携版目录和 ZIP 已生成。
- 首次 UI 检查结束时因新用例留下 RDP 标签而停在交互式退出确认；修正测试清理后重跑通过。期间一次检查项目重建因旧测试进程占用 apphost 失败，进程结束后最终构建通过。产品逻辑未因此改变。
- 结果报告与产物在 ignored `artifacts/windows-services/`，其中最终 Application/核心/Windows 报告位于 `release/`；差异空白检查通过。

### 边界与下一步

- 本次推进技术方案中的 Windows 平台隔离。真实 mstsc 登录、实体串口互通仍未验证；Infrastructure 暂保留 Windows 目标，路径/文件选择器和平台能力报告尚待实施。
- 下一步实施平台能力报告，并接入会话编辑器与连接入口，让本地 Shell、串口或 RDP 不可用时说明原因；之后继续隔离 Windows 路径/文件选择器，再推进导入导出业务策略。

## 2026-10-26 - Windows 终端平台项目与串口设备端口

### 本次开发内容与原因

- 建立 `PortHarbor.Platform.Windows`（`net8.0-windows`），只引用 Application 与原有 System.IO.Ports 8.0.0；平台实现不引用 WinForms、网络 Infrastructure、SSH.NET 或 FluentFTP。
- 将 ConPTY 从 UI 项目迁入平台项目，将过渡 Windows 终端适配器拆为独立 `LocalShellTerminalTransport` 与 `SerialTerminalTransport`，由 `WindowsTerminalTransportFactory` 显式选择协议。保留 Shell 参数、工作目录、串口参数、退格转换和可等待关闭语义。
- 无平台调用的 `StreamTerminalTransport` 从 Infrastructure 移入 Application，供网络与 Windows 适配器共用同一启动/中止/释放策略，避免平台项目依赖网络厂商库。
- Application 新增 `ISerialPortProvider`，由 Windows 提供设备名及校验/停止位/流控的字符串选项；会话编辑器通过端口读取，移除 UI 中 System.IO.Ports 类型使用和直接包引用。既有配置字符串与用户目录保持兼容。
- 增加平台依赖方向、工厂选择/拒绝、预取消、编辑器注入和关闭空闲 ConPTY Shell 的检查。平台项目随主程序/检查项目的依赖图进入现有 CI 与发布流程；新增平台锁文件并更新主项目锁。
- 更新 README、终端约定、技术方案及 ADR 0002 的平台复审记录。

### 验证结果

- 平台项目独立及主程序/检查项目 Release 构建通过，最终 0 警告、0 错误。首次沙箱恢复出现 NuGet 漏洞源不可达的 NU1900，随后在允许联网环境下强制锁定恢复成功，最终构建和发布未再出现告警。
- Application 独立检查 77 项、核心检查 179 项通过；新增 5 项平台工厂、程序集边界和预取消检查。
- Windows/ConPTY 检查 4 项通过，包含输入/输出/resize/正常退出、启动失败以及关闭空闲 Shell 等待进程清理。
- WinForms UI 检查 25 项通过，包含串口编辑器通过注入端口读取设备和参数选项；界面终端输出包含 `CONPTY_OK`。
- FTP/SFTP/SSH/Telnet 本机回环检查 26 项通过，验证共享流基类迁移后网络连接及取消行为。
- 完整 `build.ps1 -OutputDirectory artifacts/windows-terminals/release` 成功，锁定恢复、Application/核心/Windows 门禁、安装版发布 EXE 启动/退出冒烟全部通过，两个目录与 ZIP 均已生成。报告及产物位于 ignored `artifacts/windows-terminals/`。
- 最终差异空白检查通过。

### 边界与下一步

- 本轮完成 Windows 平台隔离的终端部分。真实串口设备互通仍未验证；DPAPI 保留在存储 Infrastructure，mstsc 和路径/文件选择器仍在 UI，平台能力报告尚未实施。
- 下一步定义机密保护与远程桌面启动端口，将 DPAPI/mstsc 接入 Windows 平台项目，继续验证安装版凭据迁移、便携密码库及 RDP 配置/启动失败兼容性；之后完善能力报告及其余平台服务边界。

## 2026-10-26 - 终端用例、网络适配器与可等待关闭

### 本次开发内容与原因

- 在 Application 定义 `ITerminalSession`/`ITerminalTransport`，将 `TerminalConnection` 从 UI 迁入 Application，集中处理一次性启动、配置校验、UTF-8 增量解码、输入/resize 顺序、诊断及关闭生命周期，不引用 WinForms、SSH.NET、串口或 Win32。
- SSH/Telnet 协议实现迁入 Infrastructure，SSH 使用原生 ConnectAsync 和共享主机信任逻辑，Telnet 适配器负责协商/转义与异步 TCP I/O。UI 移除 SSH.NET 直接引用并更新锁文件，依赖版本和哈希不变。
- `StreamTerminalTransport` 管理启动中资源登记及永久中止。中止后才创建的资源立即释放，异步释放等待启动返回及资源清理，避免关闭后出现未被追踪的客户端/流。
- 串口/ConPTY 暂由 UI 项目中的 Windows 过渡适配器接入端口；组合入口 `TerminalConnectionFactory` 根据协议装配用例。此处尚未建立独立 Windows 平台项目。
- MainForm 绑定终端会话接口，关闭标签及应用退出均等待 CloseAsync 后才销毁控件；同时断开控件的终端输出订阅。输入入队时复制数据，取消后不再发送排队命令。
- 增加共享终端用例检查、流适配器晚资源检查、真实网络取消检查，以及终端标签/软件退出等待清理的 UI 检查。补充终端契约文档及 ADR 0002，更新技术方案与 README。

### 验证结果

- 独立 Application 和主程序/检查项目 Release 构建均通过，最终 0 警告、0 错误。
- Application 检查 77 项、核心检查 174 项通过；新增覆盖输入复制/顺序、resize 串行、分片 UTF-8、远端 EOF、预取消、重复 Run 拒绝、晚启动、阻塞写入、异步释放及失败诊断。
- Windows/ConPTY 检查 3 项、WinForms UI 检查 24 项通过，界面终端输出包含 `CONPTY_OK`。UI 新增关闭标签/软件退出等待终端清理检查。
- 首次新增网络取消检查因测试会话缺少用户名，在 SSH.NET 参数校验时提前失败，表现为监听端等待超时；补齐专用测试用户名，并使检查在连接任务提前失败时直接传播错误。修复后协议回环 26 项通过，包括 SSH 不返回握手 banner 与 Telnet 不发送数据时取消。
- 完整 `build.ps1 -OutputDirectory artifacts/terminal-layering/release` 成功：锁定恢复、Application/核心/Windows 门禁、发布 EXE 启动/退出冒烟全部通过；安装版与便携版目录及 ZIP 均已生成。
- 最终报告位于 ignored `artifacts/terminal-layering/`（协议最终报告为 `integration-results-final.json`），最终差异空白检查通过。

### 边界与下一步

- SSH channel/读取及 Windows 同步管道仍借助工作线程和关闭所属资源中断，不表示所有底层 I/O 已原生异步；终端用户可配置超时、自动重连、真实串口及更多服务器兼容性仍待后续验收。
- 下一步建立 `PortHarbor.Platform.Windows`，先迁入串口、本地 Shell 和 ConPTY 适配器，保留统一终端端口及关闭契约；随后分离 DPAPI/mstsc，接入平台能力报告，并继续保持旧配置和 Windows 行为兼容。

## 2026-10-26 - 文件传输用例与协议适配器分层

### 本次开发内容与原因

- 将 `FileConnection` 和共享超时策略从 WinForms 项目迁入 Application，目标仍为 `net8.0`，仅依赖 Domain。通过公开构造函数注入 `IRemoteFileSystem`，维持操作串行、超时、取消、临时文件提交、进度及异步关闭语义。
- 将 FTP/SFTP 适配器、工厂和厂商异常分类迁入 Infrastructure；FluentFTP 的直接包引用随适配器迁移，版本及依赖哈希不变，补齐 Infrastructure 锁文件并验证锁定恢复。
- Application 提供无厂商依赖的默认 `ConnectionErrorPolicy`，实际协议工厂注入 `DiagnosticClassifier`，保持 SSH.NET 认证/超时/协议异常的现有分类。
- 共享 SSH 认证和主机指纹配置抽入 Infrastructure 的 `SshClientSupport`，终端 SSH 与 SFTP 共同使用，消除 SFTP 对 UI 中 `TerminalConnection` 的依赖。
- 将内存远端文件系统与既有传输策略检查放入 `tests/Shared`，由独立 Application 检查和 Windows 检查共用；增加传输用例所在程序集及依赖方向检查。更新 README、技术方案和文件传输约定。

### 验证结果

- 独立 Application 及主程序/检查项目 Release 构建通过，0 警告、0 错误。
- Application 检查 56 项、核心检查 150 项通过，包含取消保留原目标、失败清理、超时、连接重试、关闭等待及排队连接超时竞态。
- Windows/ConPTY 检查 3 项、WinForms UI 检查 20 项通过，终端界面输出包含 `CONPTY_OK`。
- FTP/SFTP/SSH/Telnet 本机回环 24 项通过，包含 Unicode 文件、覆盖、取消、主机信任拒绝/变更和 SFTP 认证失败分类。
- 完整 `build.ps1 -OutputDirectory artifacts/file-transfer-layering/release` 成功：锁定恢复、重复执行 Application/核心/Windows 门禁及发布 EXE 启动/退出冒烟通过；安装版、便携版目录与 ZIP 均已生成。
- 报告及发布产物位于 ignored `artifacts/file-transfer-layering/`，最终差异空白检查通过。

### 边界与下一步

- 文件传输用例已可脱离 WinForms 和厂商库构建/检查；Infrastructure 仍包含 DPAPI，目标保持 `net8.0-windows`。本次未升级依赖，也未验证 FTPS、真实串口或新的服务器实现。
- 下一步定义终端会话端口，将终端生命周期、输入队列、取消与关闭编排迁入 Application，先抽出 SSH/Telnet 适配器并覆盖启动中取消、远端断开及关闭竞态；随后隔离串口、本地 Shell、ConPTY、DPAPI 和 mstsc 等平台实现。

## 2026-10-26 - 独立存储基础设施及失败回滚

### 本次开发内容与原因

- 新建 `PortHarbor.Infrastructure`，目标 `net8.0-windows`，只引用 Application，不引用 WinForms 或第三方协议库。
- 将 Store、AppData、JSON/INI 编解码、主密码 AES/PBKDF2 和 DPAPI 实现移出 UI 项目；`ConfigurationFileStorage` 处理格式校验、路径和原子文件替换，`StoredVault` 管理密钥/凭据迁移，`StoredHostKeyTrust` 管理指纹确认与保存。
- 适配器共享 `ConfigurationState` 和写入锁，维持同一配置文件中会话、密码和主机指纹的一致性。Store 保留兼容入口，用户配置路径和密文格式不变；命令行存储模式解析仍在 Windows UI 入口。
- 主机密钥拒绝异常移入 Application，存储适配器和 SSH/SFTP 诊断分类共用同一类型。
- 修复导入保存失败时内存会话/分类未回滚，以及指纹保存失败后仍被内存视作已信任的问题；失败后保留原文件，后续连接必须重新确认该指纹。

### 验证结果

- Infrastructure 独立 Release 构建及主程序/检查项目 Release 构建：0 警告、0 错误。
- 核心检查 149 项通过，包含 JSON v1/v2、DPAPI 凭据迁移、便携 INI、密码库改密/会话保存回滚及新增 17 项存储边界检查。
- Windows/ConPTY 检查 3 项、WinForms UI 检查 20 项通过，终端界面输出包含 `CONPTY_OK`。
- Application 独立检查 17 项、FTP/SFTP/SSH/Telnet 回环检查 24 项通过，报告位于 ignored `artifacts/storage-adapters/`。
- 锁定恢复和 `git diff --check` 通过。完整 `build.ps1 -OutputDirectory artifacts/storage-adapters/release` 成功，构建无警告/错误，发布安装版与便携版目录及 ZIP，安装版发布 EXE 启动/退出冒烟通过。

### 边界与下一步

- Infrastructure 仍是 Windows 项目，保留 DPAPI；本次不表示已支持其他操作系统，也未将 ConPTY、mstsc 或协议厂商适配器移出 UI 项目。
- 下一步继续抽取连接编排与文件传输用例，再隔离协议适配器和其余 Windows 平台服务。

## 2026-10-26 - Application 配置、密码库与主机信任端口

### 本次开发内容与原因

- Application 增加 `IConfigurationRepository`、`IVault` 和 `IHostKeyTrust`，由 `ConfigurationService` 组合会话用例、分类、导入导出、主密码生命周期和主机指纹决策。
- 主界面通过配置服务执行分类、JSON 导入导出、凭据读取/解锁/迁移/锁定；终端 SSH 和 SFTP 客户端只依赖 `IHostKeyTrust`。
- `Store` 作为兼容适配器实现上述端口，仍维持 JSON/便携 INI 格式、密码加密、首次指纹确认和用户数据目录不变。
- 跨平台 Application 检查增加接口委托覆盖；Windows 侧继续以 v1/v2 和便携配置 fixture 核验适配行为。

### 验证结果与边界

- Application 独立 Release 构建：0 警告、0 错误；主项目/Windows 检查 Release 构建通过。
- 配置、密码库及主机信任 mock 检查纳入新的 Application 检查入口，共 17 项通过；完整核心检查 132 项通过，Windows/ConPTY 检查 3 项通过，WinForms UI 检查 20 项通过。
- FTP/SFTP/SSH/Telnet 本机回环检查 24 项通过，包含主机信任端口接线、错误密码、拒绝主机密钥、主机密钥变更和 SFTP 认证失败分类。
- 当前 Store 类仍同时包含文件格式、凭据加密和主机指纹持久化，独立基础设施仓储尚未完成；本轮只完成 Application 端口和兼容适配。
- NuGet 漏洞源当前网络不可达，Windows 包项目恢复可能出现 `NU1900` 网络告警。

### 下一步

- 运行核心/UI/Windows 与协议服务回环回归；随后再拆 JSON/INI 持久化、密码库加密和主机信任存储实现。

## 2026-10-26 - Application 会话用例与配置兼容性

### 本次开发内容与原因

- 新建 `src/PortHarbor.Application`（`net8.0`），定义 `ISessionRepository` 和 `SessionService`，承载会话读取、创建、编辑、删除、校验和密码保存策略；Application 不引用 WinForms、DPAPI 或协议厂商库。
- WinForms 主界面通过 `SessionService` 读取树、保存编辑结果和删除会话；`Store` 实现仓储端口，底层 JSON/INI、凭据和主机信任实现暂留原项目，避免改变用户目录和配置格式。
- 会话写入改为先生成新快照，再持久化；保存/删除失败恢复内存中的会话和凭据字典，避免 UI 看到未落盘状态。
- 添加 v1/v2 JSON fixture、便携 INI 检查，以及旧凭据迁移、ID/密码关联、分类和主机信任保留、创建/编辑/删除失败回滚检查。

### 验证结果

- Application 独立 Release 构建：0 警告、0 错误；独立检查 11 项通过。
- 主程序及检查项目 Release 构建通过；完整核心检查 **132 项通过**，包含新增配置兼容性检查。
- 上一轮 UI 20 项和 ConPTY 冒烟已通过；本次 Application 接入后尚未重新执行 UI 检查，下一轮补跑。
- 报告：ignored `artifacts/application-validation/application-results.json`（11 项）和 `artifacts/application-validation/unit-results.json`（132 项）。
- 锁定恢复路径已验证；NuGet 漏洞源在当前网络不可达时仍会产生 `NU1900` 网络告警，不影响本地锁定构建。

### 下一步

- 将导入导出、主机信任和凭据解锁继续抽成 Application 端口，随后补跑 UI/Windows/协议回归并更新 CI 的独立项目门禁。

## 2026-10-26 - 终端状态模型迁入 Domain

### 本次开发内容与原因

- 将 `TerminalScreen` 和 `Cell` 原样迁入 `src/PortHarbor.Domain/TerminalScreen.cs`，由主程序的项目引用提供，保持现有命名空间及终端解析行为。
- Domain 保留 `System.Drawing.Color` 基础值类型；不引用 WinForms、GDI 渲染组件或第三方协议库，目标仍为 `net8.0`。
- 检查入口增加 `--terminal`，复用既有终端和 Telnet 检查，可独立于存储、DPAPI 和 UI 运行。
- 同步 README 和技术方案，修正阶段 2 仍显示“未开始”的过期状态。

### 修改文件

- 新增 `src/PortHarbor.Domain/TerminalScreen.cs`，删除原 `src/PortHarbor/TerminalScreen.cs`。
- `src/PortHarbor/PortHarbor.csproj` 移除已删除源文件的多余编译排除项。
- `tests/PortHarbor.Checks/Program.cs`、`README.md`、`docs/TECHNICAL-PLAN.md`、本日志。

### 验证结果与边界

- Domain 独立 Release 构建通过；主程序及检查项目 Release 构建通过，均为 0 警告、0 错误。
- `--terminal` 检查 16 项通过，覆盖中文宽字符、ANSI/真彩色、备用屏幕、清行、光标查询、缩放、输入模式、分片代理字符、异常 ANSI 参数和 Telnet 协商/转义。
- 报告：ignored `artifacts/terminal-domain/terminal-results.json`。
- 完整 `--unit` 测试停在沙箱外 DPAPI 执行的权限确认，本轮被中断，未生成结果；本次未执行 UI、ConPTY 或协议服务回环回归，不沿用历史结果作为本次验收。
- 当前验证环境仍为 Windows；独立 `net8.0` 构建通过不代表已经完成 Linux/macOS 实机运行验收。

### 下一步

- 拆分 Application 项目与配置仓储/凭据端口，保持旧配置读取和迁移兼容；补跑完整核心及 UI 回归。

## 2026-10-26 - 终端与 RDP 诊断统一

### 本次开发内容与原因

- 将终端连接的校验、连接、认证、主机密钥、通道、读写和关闭阶段通过安全状态栏呈现；失败保留稳定错误码与短操作编号，关闭清理不会覆盖失败信息。
- RDP 启动改为带诊断回调的生命周期：配置校验、临时文件写入、启动 Windows 客户端和启动失败均可定位；页面保留重试入口，不把外部客户端启动等同于远程登录成功。
- 修正 SSH.NET 认证异常、超时、网络包装异常、主机密钥拒绝和协议异常的分类顺序；诊断展示不再包含原始异常、主机、路径、密码或终端控制字符。
- 增加核心、UI 和协议检查，覆盖错误密码、拒绝主机密钥、主机密钥变更、SFTP 认证失败、拒绝连接、RDP 启动失败及后台线程诊断更新。

### 修改文件

- `src/PortHarbor/Diagnostics.cs`、`DiagnosticPresenter.cs`、`ConnectionStatusLabel.cs`
- `src/PortHarbor/TerminalConnection.cs`、`MainForm.cs`、`RdpLauncher.cs`、`Models.cs`
- `src/PortHarbor/RemoteFileSystems/SftpRemoteFileSystem.cs`
- `tests/PortHarbor.Checks/DiagnosticChecks.cs`、`DiagnosticUiChecks.cs`、`Program.cs`
- `docs/TECHNICAL-PLAN.md`、本开发日志

### 验证结果

- Release 构建：0 警告、0 错误。
- 核心检查：113 项通过；Windows/ConPTY 检查：3 项通过；UI 检查：20 项通过。
- 本机 FTP/SFTP/SSH/Telnet 回环：24 项通过，包含新增 SSH/SFTP 认证与主机密钥失败分类检查。
- 报告保存在 ignored `artifacts/diagnostics-validation/`；UI 截图和终端输出也已生成。

### 仍存在的问题与边界

- SSH、SFTP 底层库仍可能通过同步资源释放关闭连接；串口实体设备、FTPS、私钥认证和真实 RDP 登录仍未完成实机兼容性验证。
- 终端 I/O 的部分第三方同步调用仍使用后台任务包装；统一取消、超时和关闭等待需要后续继续抽取。

### 下一步

- 补齐串口、FTPS、私钥认证和更广服务器的兼容性矩阵，继续覆盖终端后台 I/O 的取消、超时和进程退出竞态。
- 完成阶段 0 的格式检查、锁定恢复、发布冒烟和回滚故障注入，再开始 Domain/Application 内核拆分。

## 2026-10-26 - 工程门禁与发布冒烟

### 本次开发内容与原因

- `build.ps1` 和 Windows CI 现在使用 `dotnet restore --locked-mode`，并在构建前执行 `git diff --check`，防止锁文件漂移和新增差异出现空白错误。
- 发布到 staging 后自动以 `--smoke --installed --data-dir` 启动真实 `PortHarbor.exe`，创建临时空配置并验证主窗口初始化、关闭和进程退出；失败时不会替换现有发布目录或 ZIP。
- 应用入口增加 smoke 模式，启动失败返回非零退出码且不弹出阻塞对话框，正常用户启动行为保持不变。

### 验证结果

- 完整 `build.ps1 -OutputDirectory artifacts/engineering-validation` 成功；锁定恢复、差异空白检查、发布和发布 EXE 启动冒烟均通过。
- 构建内核心检查 113 项、Windows/ConPTY 检查 3 项通过；发布输出包含 installed/portable 两个目录和对应 ZIP。
- 恢复过程中 NuGet 漏洞源因网络不可达产生 `NU1900` 警告，但锁定依赖、编译和运行检查均成功；该网络告警仍应在 CI 网络环境复核。
- `tests/build-rollback.ps1` 使用模拟替换后失败验证旧 installed/portable 目录和两个 ZIP 均保留，staging 目录被清理。

### 修改文件

- `src/PortHarbor/Program.cs`
- `build.ps1`、`.github/workflows/windows-ci.yml`
- `docs/TECHNICAL-PLAN.md`、本开发日志

### 下一步

- 在独立干净 checkout 复核完整构建，再开始 Domain/Application 项目边界设计。

## 2026-10-26 - Domain 内核第一切片

### 本次开发内容与原因

- 新建 `src/PortHarbor.Domain`，目标框架为 `net8.0`，不引用 WinForms、Windows API、SSH.NET、FluentFTP 或存储实现。
- 将 `Session`、协议枚举、配置校验、连接状态/阶段/错误码/诊断记录和 Telnet 编解码迁入 Domain；现有 WinForms 项目通过项目引用继续使用相同命名空间和数据契约。
- 将 `RemoteEntry`、传输方向/进度记录、`IFileTransfer` 和 `IRemoteFileSystem` 迁入 Domain；FTP/SFTP 工厂和厂商适配器仍留在原项目。
- 保持 `Store`、密码库、平台服务和协议适配器留在原项目，避免本次边界抽取改变配置格式或运行时行为。

### 修改文件

- `src/PortHarbor.Domain/PortHarbor.Domain.csproj`
- `src/PortHarbor.Domain/Session.cs`、`ConnectionContracts.cs`、`TelnetCodec.cs`、`FileTransferContracts.cs`
- `src/PortHarbor/Models.cs`、`Diagnostics.cs`、`PortHarbor.csproj`、`packages.lock.json`
- 删除原 `src/PortHarbor/TelnetCodec.cs`
- `docs/TECHNICAL-PLAN.md`、本开发日志

### 验证结果

- Domain 项目独立 Release 构建：0 警告、0 错误。
- 主项目及 `PortHarbor.Checks` Release 构建：0 警告、0 错误。
- 项目引用已更新到锁文件；`dotnet restore --locked-mode` 已验证完整恢复路径。

### 仍存在的问题与边界

- `TerminalScreen` 和应用用例仍在 WinForms 项目中；本切片只建立 Domain 边界，不代表阶段 2 完成。

### 下一步

- 迁移终端屏幕状态和文件传输纯契约，随后拆出 Application 项目和配置仓储端口。

## 2026-10-22 - Connection diagnostics baseline

### What was developed

- Added a structured connection diagnostics contract in `src/PortHarbor/Diagnostics.cs`.
- Added stable connection states, operation stages, error codes, retryability, and a per-connection `OperationId`.
- Connected `TerminalConnection` to diagnostic events for connection, authentication, host-key verification, channel creation, read/write, cancellation, failure, and close paths.
- Added a lifecycle check that verifies a platform failure produces a stable diagnostic and reaches the closed state.
- Confirmed the project path and write permissions after the workspace migration.

### Files changed

- `src/PortHarbor/Diagnostics.cs`
- `src/PortHarbor/TerminalConnection.cs`
- `tests/PortHarbor.Checks/Program.cs`
- `README.md`, `build.ps1`, `src/PortHarbor/PortHarbor.csproj`, and `.github/` contain the earlier engineering baseline changes.

### Verification

- .NET SDK `8.0.426` is installed and available.
- Locked restore completed with `NuGet.Config`.
- Release build completed with 0 warnings and 0 errors.
- Unit checks passed: 30 checks.
- Windows checks passed: 3 checks, including the diagnostic lifecycle assertion.
- `git diff --check` passed.
- Results were written to `artifacts/unit-results.json` and `artifacts/windows-results.json`.

### Known problems

- Existing source files contain legacy encoding artifacts in some Chinese user-facing strings; this should be handled as a separate encoding cleanup to avoid mixing behavior changes with text migration.
- File transfer operations do not yet publish the same diagnostic contract as terminal connections.
- UI subscription to the new diagnostic events is not implemented yet.

### Next step

1. Add the diagnostic contract to `FileConnection`.
2. Add a small UI-safe diagnostic presenter that shows stage and error code without exposing secrets or terminal content.
3. Treat encoding cleanup as a separate change and add focused checks before changing user-facing strings.

## Workbook rule

Every future development session must append an entry with: date, development scope, files changed, verification performed, remaining problems, and next step. Failed checks and environment limitations must be recorded instead of being omitted.

每次开发结束还应同步更新技术方案第 8 节对应任务。阶段达到全部完成条件后，标记“已完成”，记录完成日期、对应提交及本日志的验收依据；没有验证依据的任务保持未勾选。

## 2026-10-22 - File transfer diagnostics, safe presentation, and encoding audit

### What was developed

- Added the structured diagnostics lifecycle to `FileConnection`, including operation ID, connection state, connection stage, error code, retryability, and cleanup transitions.
- Added `DiagnosticPresenter` for UI-safe status and error text. It shows a short operation identifier and stable error category without exposing host names, paths, credentials, or raw exception text.
- Updated `FileView` to subscribe to diagnostics, show stage progress in the status bar, and use the safe presenter for failures.
- Added unit checks for file-connection failure/close diagnostics and endpoint redaction in the presenter.
- Audited the reported Chinese encoding issue. The source files are valid UTF-8 when read as bytes; the mojibake was produced by the PowerShell output encoding. No source re-encoding was applied because it would risk changing valid text.

### Files changed

- `src/PortHarbor/FileConnection.cs`
- `src/PortHarbor/DiagnosticPresenter.cs`
- `src/PortHarbor/FileView.cs`
- `tests/PortHarbor.Checks/Program.cs`
- `docs/DEVELOPMENT-LOG.md`

### Verification

- Release build passed with 0 warnings and 0 errors.
- Unit checks passed: 32 checks.
- Windows checks passed: 3 checks.
- New checks confirm file connection diagnostics include failed and closed lifecycle events.
- New checks confirm the UI presenter does not expose the endpoint value.

### Remaining problems

- File operation diagnostics currently use generic English internal messages; the UI presents localized stable categories, but the diagnostic catalog still needs a localization policy.
- `FileConnection.Exists` is still a direct protocol call and does not emit a diagnostic when it fails.
- The PowerShell console encoding should be configured separately if Chinese source text needs to be inspected directly in that shell.

### Next step

1. Extract protocol-specific file operations behind a shared file transfer interface.
2. Add cancellation-aware async methods so UI work does not rely on synchronous network calls inside `Task.Run`.
3. Add a localization resource for diagnostic categories and user-facing status text.

## 2026-10-22 - 文件传输接口与 FTP/SFTP 适配器拆分

### 本次开发内容与修改原因

- 新增协议无关的 `IFileTransfer` 和 `RemoteEntry`，由 `FileTransferFactory` 构造连接，`FileView` 不再持有具体的 `FileConnection` 类型。
- 为 `FileView` 增加接口注入构造入口，明确由视图负责释放连接，方便更换实现和检查界面。
- 新增内部 `IRemoteFileSystem`，将 FluentFTP 和 SSH.NET 调用分别迁入 `FtpRemoteFileSystem`、`SftpRemoteFileSystem`。
- `FileConnection` 专注连接状态、诊断、累计进度、取消检查、临时文件与上传提交，不再引用 FTP/SFTP 厂商库。
- 保留 FTP 覆盖和 SFTP POSIX 覆盖/旧文件暂存回退逻辑；统一两种协议的非空目录删除限制和重命名目标冲突规则。
- 修复 `Exists` 失败时缺少诊断的问题；新增未连接/已释放操作保护、重复连接和释放保护，以及预先取消时避免打开远程流的检查。
- 将连接失败单元检查改为内存替身，不再依赖外部 DNS；增加公共策略、清理失败、取消与真实协议回归检查。
- 在方案与 README 中登记实际边界，新增 `docs/FILE-TRANSFER.md`，说明协议扩展、资源所有权和目前的限制。

### 修改文件

- `src/PortHarbor/IFileTransfer.cs`
- `src/PortHarbor/FileConnection.cs`
- `src/PortHarbor/FileView.cs`
- `src/PortHarbor/RemoteFileSystems/IRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/FtpRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/SftpRemoteFileSystem.cs`
- `src/PortHarbor/Properties/AssemblyInfo.cs`（仅向检查程序集提供内部测试接缝）
- `tests/PortHarbor.Checks/FileTransferChecks.cs`
- `tests/PortHarbor.Checks/Program.cs`
- `tests/PortHarbor.Checks/UiShell.cs`
- `docs/FILE-TRANSFER.md`、`docs/TECHNICAL-PLAN.md`、`README.md`、本工作簿

### 验证结果与过程中的问题

- 最终 Release 构建：0 警告、0 错误。
- 核心检查：69 项通过，涵盖原有存储/终端检查以及本次文件传输公共策略。
- Windows 检查：3 项通过，覆盖 ConPTY 交互和失败生命周期。
- 本机协议检查：20 项通过，覆盖 Telnet、SSH 及真实 FTP/SFTP 的 Unicode 文件名、上传下载、覆盖、取消后目标保留、临时文件清理、重命名冲突、非空目录拒绝删除和空目录删除。
- UI 冒烟：4 项通过，覆盖主窗口、七种协议编辑器、通过注入接口加载 Unicode 文件列表及关闭视图释放连接。
- 终端 UI 冒烟通过，输出包含 `CONPTY_OK`。
- 验证报告：`artifacts/unit-results.json`、`artifacts/windows-results.json`、`artifacts/integration-results.json`、`artifacts/ui-results.json`、`artifacts/ui-shell-results.json`；截图与终端输出也保存在 `artifacts`。
- `git diff --check` 通过；Git 仅提示既有文件的 LF/CRLF 转换。
- 初次编译出现 2 个 CS0436 警告：测试程序集获取内部可见性后，两个程序集的 WinForms 自动生成 `ApplicationConfiguration` 类型冲突。测试改用显式 UI 初始化，最终构建无警告。
- 当前沙箱曾拒绝项目内源码写入；通过权限提升后完成保存。工作路径仍为 `D:/Ai-Workspace/Projects/portharbor`。
- 系统 Python 缺少协议测试依赖；在已忽略的 `artifacts/protocol-venv` 中创建隔离环境并安装 `tests/requirements.txt` 指定版本，未修改系统 Python。每次测试完成后均关闭本次启动的回环服务。

### 仍存在的问题与边界

- 本阶段完成同步接口及协议隔离；界面仍通过 `Task.Run` 调用同步网络操作。取消在数据块之间生效，阻塞读写或目录操作需等待协议超时。
- 断线可能导致远程 `.part` / `.bak` 清理失败；FTP 与不同 SFTP 服务器不能统一保证原子替换。
- 本机 SFTP fixture 支持 POSIX 覆盖；此次未额外模拟不支持该扩展时的回退失败或断线恢复，相关代码保留原有策略，仍需服务器兼容性验证。
- FTPS、私钥认证、多种实际服务器尚未完成本次验证。
- SFTP 认证和主机密钥检查仍复用 `TerminalConnection` 的 SSH 辅助函数，后续可提取为共享 SSH 基础设施。
- 诊断内部消息及界面文案尚未统一迁入本地化资源；没有新增传输队列、自动重连或断点续传。

### 下一步

1. 在现有接口边界上设计异步操作与结构化进度，贯穿取消令牌及超时设置。
2. FTP/SFTP 逐步使用底层库的异步能力，移除文件视图中的同步调用包装；补充取消响应和连接关闭竞态检查。
3. 再建立本地化资源和诊断消息目录，扩大服务器兼容性覆盖。

## 2026-10-23 - 文档分工与阶段进度维护

### 本次开发内容与修改原因

- 保留三份文档，明确技术方案负责路线图与阶段状态、开发日志负责历史记录、文件传输文档负责当前模块契约。
- 将阶段进度统一放在技术方案第 8 节，增加状态总览、任务勾选、完成日期/提交字段和验收依据。
- 根据当前源码、CI 工作流、构建脚本和历史日志标注已完成子项；将未完成或尚未验证的工作保留为未勾选。
- 修正技术方案中“暂无 CI”的过时描述，更新当前后续顺序；文件传输文档改为链接统一进度入口，保留完整技术约定。
- 约定每次开发结束同步更新任务清单；阶段全部达到完成条件后，记录完成日期、提交及验证日志。

### 修改文件

- `docs/TECHNICAL-PLAN.md`
- `docs/DEVELOPMENT-LOG.md`
- `docs/FILE-TRANSFER.md`

### 验证范围

- 对照源码、构建脚本与 CI 工作流核对任务状态，历史测试数量仅引用此前日志。
- 文档本地链接及阶段清单检查通过；`git diff --check` 最终通过。
- 初次差异检查发现新增行尾空格及文件末尾空行，已修正。
- 本次只修改文档，未重新构建或运行应用测试。

### 仍存在的问题与边界

- 阶段 0、1 尚未完成；阶段 3 仅同步文件传输边界拆分提前完成，不能视为整个阶段验收通过。
- 未联网核实远端 CI 运行结果，也未重新验证历史构建和测试记录。
- README 及项目版本标识、旧验证摘要的同步不在本次文档整理范围内。

### 下一步

- 按技术方案第 12 节推进文件传输异步、取消、超时及关闭竞态处理，并在开发结束后同步阶段清单。

## 2026-10-23 - 分类、标签关闭、主密码与预览打包

### 本次开发内容与修改原因

- 增加独立分类列表及工具栏/右键新建入口，支持保存空分类、选中分类后新建会话、编辑器选择分类和分类导入导出；目前为单层分类。
- 增加带 × 的标签控件，双击标题、×、中键、工具栏和 Ctrl+W 共用关闭确认；取消时保留标签及连接。存在会话标签时退出应用也需确认。
- 增加主密码库，使用 PBKDF2-HMAC-SHA256 派生密钥和 AES-256-GCM，对原 DPAPI 密文再加密；启动解锁、首次保存设置、修改密码、旧配置迁移及保存失败回滚均有实现。数据格式升级至 v2，仍能读取 v1；导出排除密码库。
- 默认只生成不含 .NET 环境的框架依赖包；`-IncludeRuntime` 可选生成自包含包。增加 `-OutputDirectory`，保留核心及 Windows 检查的 JSON 报告，避免报告随 staging 清理丢失。
- 更新 README、阶段清单及 ADR 0001。按用户要求，字体和安装/便携数据模式仅分析并提供建议，未改变实现。

### 修改文件

- `src/PortHarbor/MainForm.cs`、`ConnectionTabs.cs`、`SessionEditor.cs`
- `src/PortHarbor/Models.cs`、`PasswordVault.cs`、`Program.cs`
- `tests/PortHarbor.Checks/Program.cs`、`VaultChecks.cs`、`TabUiChecks.cs`
- `build.ps1`、`README.md`、`docs/TECHNICAL-PLAN.md`、`docs/adr/0001-master-password.md`、本日志

### 验证结果与过程中发现的问题

- Release 编译通过，0 警告、0 错误；核心检查 88 项通过，Windows 检查 3 项通过。
- UI 检查 11 项通过，包括空分类显示、取消关闭保留页面、确认关闭释放指定页面、原生消息触发双击/×/中键，以及既有文件视图检查。
- 密码库检查覆盖锁定访问拒绝、错误口令、修改后拒绝旧口令、重启锁定、凭据篡改与跨会话替换拒绝、v1 DPAPI 迁移、无密码导出及保存失败回滚。
- 默认发布成功生成框架依赖包；检查 ZIP 未包含 `coreclr.dll`、`clrjit.dll`、`System.Private.CoreLib.dll`，runtimeconfig 使用外部框架。预览产物位于 `artifacts/preview`。
- 初次编译被沙箱限制写入 .NET 用户初始化文件；使用项目内构建缓存并经权限提升后完成。DPAPI 检查在沙箱身份下失败，提升到当前 Windows 用户环境后通过。
- 初次 UI 冒烟通过，但新增原生鼠标消息检查发现 WinForms 标签没有可靠触发托管双击/点击事件；修正为原生消息处理后，三条鼠标关闭路径全部通过。
- 首次默认发布因旧应用正在占用 `artifacts/framework-win-x64` 而失败；未终止旧程序或连接，改用独立预览输出目录后成功。
- 读取本机字体清单确认缺少 Cascadia Mono；请求该字体实际回退为 Microsoft Sans Serif。已建议使用等宽字体回退并校准字符格，未修改 `TerminalView.cs`。
- 文档本地链接及 `git diff --check` 检查通过。本次未重跑网络协议服务、真实 SSH 登录及 `-IncludeRuntime` 自包含分支。

### 仍存在的问题与边界

- 迁移前配置与 `.bak` 仍按旧 DPAPI 或旧主密码保护，不会自动被新主密码重新加密；旧版应用不能读取 schema v2。切换新版前应关闭旧程序，避免同一配置目录并行写入。
- 忘记主密码没有找回功能；当前凭据继续绑定 Windows 用户，不支持把密码随便携目录直接跨机器使用；不包含自动锁定功能。
- 安装版、便携数据目录模式、字体修正及复杂终端兼容性仍未实施；总路线图阶段 0–4 的状态保持按各自完成条件判断。

### 下一步

- 等用户确定字体优化与安装/便携数据策略后另行实施；后续主线仍见技术方案第 12 节。
## 2026-10-23 - 等宽终端、嵌套分类、下拉菜单与双版本配置

### 已完成

- [x] 终端默认改为已安装的 Consolas，按 Cascadia Mono、Lucida Console、Courier New、系统等宽字体回退；测量多字符宽度，字体及 DPI 改变时重算网格。
- [x] 会话树增加独立绘制的文件夹、终端、文件传输和远程桌面图标；支持根分类、子分类及继续嵌套，完整路径保存在原 Group 字段；深层会话搜索保留祖先节点。
- [x] 顶部收拢为文件、会话、窗口、设置、帮助五个下拉菜单；保留会话库右键菜单与已有快捷键。
- [x] 安装版使用用户目录 settings.json；便携版通过随包标记、相邻 INI 或 --portable 选择，首次启动在程序旁创建 PortHarbor.ini，可用 --data-dir 指定测试目录。
- [x] INI 分节保存配置、分类、会话、密码库、凭据和主机密钥；字符串采用 JSON 转义，主机密钥名称编码；保留原子写入与备份。现有安装版配置不自动迁移到便携版。
- [x] 安装版密码库版本 1 保留 DPAPI + 主密码，便携版新密码库版本 2 使用主密码直接加密，不依赖 Windows 账户；更改主密码按当前模式重新加密，失败回滚。
- [x] 构建同时生成 installed-win-x64 和 portable-win-x64 两版目录包，IncludeRuntime 作为独立选项，默认均不包含运行环境。
- [ ] 正式安装向导、升级和卸载流程：留待发行阶段，不标为完成。

完成日期：2026-10-23；提交：当前工作区，待提交。

### 验证和修正

- Release 编译 0 警告、0 错误；核心检查 104 项、Windows 检查 3 项、UI 检查 16 项通过。报告分别在 artifacts/preview/unit-results.json、artifacts/preview/windows-results.json、artifacts/ui-results.json；界面截图 artifacts/ui.png。
- 新增检查覆盖 INI 创建、复制重启、中文/换行/特殊符号、空嵌套分类、错误主密码、修改口令、拒绝重复分节与键、启动模式选择、主机密钥编码、便携 AES 包络不含 DPAPI，以及菜单、图标、等宽测量和字体变更重算。
- UI 检查发现 ImageList 延迟创建导致临时位图释放后图像丢失；改为先建立句柄再添加位图，复验通过。安装版 DPAPI 在沙箱身份下失败，按当前 Windows 用户权限复验通过。
- 默认发布成功，产物为 artifacts/preview/PortHarbor-0.1.0-installed-win-x64.zip 和 PortHarbor-0.1.0-portable-win-x64.zip。安装版当前为目录布局 ZIP，尚无 Setup 安装向导。
- 未操作用户运行中的旧程序或配置。未在第二台真实电脑验证，未重新登录实际 SSH 设备，未重跑包含运行环境分支；字体校验目前基于本机等宽测量和 UI 网格。
- 只读参考用户提供的 D:/Ai-Workspace/References/mobaxterm-23.3/report.md 与 form-strings.tsv，借鉴静态界面/配置线索，未复制第三方实现或资源。INI 为本项目独立格式，不兼容其 INI。

### 后续

- 用户可先验收终端视觉、分类和两种配置布局；主线继续推进文件传输异步、取消及超时处理。


## 2026-10-23 - 终端交互与主密码命名

- [x] 用户界面中的“主密码”统一改称“主密码”，底层 `Vault` 和数据格式不变，旧配置继续兼容。
- [x] 终端增加右侧滚动条，保留鼠标滚轮和键盘滚动；终端输出更新时同步滚动范围。
- [x] 鼠标左键拖选后自动复制到剪贴板；右键直接粘贴，仍保留 Ctrl+Shift+C/V 和多行粘贴确认。
- [x] 终端右键菜单增加字体设置和配色设置，支持字体、字号、样式、前景色和背景色；终端 ANSI/VT 的颜色序列仍按输出内容绘制。
- [ ] 字体与配色持久化到会话/全局设置：当前设置作用于当前终端实例，后续再接入配置文件。

编译检查 0 警告、0 错误。MobaXterm 的“无需在连接对话框输入账号密码”行为仍仅做解释，未改 SSH 登录流程。


## 2026-10-23 - 今日开发收尾

### 今日最终状态

- [x] 将用户界面中的“总密码”统一改为“主密码”。
- [x] 主密码解锁框增加“跳过”：跳过后可以进入主界面和打开终端；已保存凭据保持加密且不可读，连接时要求手动输入本次密码，不会删除已保存密码。
- [x] SSH、Telnet、Serial、Shell 等 `TerminalView` 实例使用同一终端交互实现：右侧滚动条、历史滚动、ANSI/VT 前景色和背景色、字体与颜色菜单。
- [x] 左键选取后自动复制、右键直接粘贴改为两个独立的可选菜单项，默认关闭；快捷键复制/粘贴继续可用。
- [x] 字体菜单支持字体、字号和样式；配色菜单支持默认前景色和背景色。ANSI/VT 输出颜色仍由远端程序发送的控制序列决定。
- [x] SSH 认证方式本轮保持现状：连接前使用已保存或临时输入的用户名/密码；MobaXterm 风格的 keyboard-interactive 终端内认证只完成解释，未实现。
- [x] 安装版与便携版仍同步维护。当前发布脚本会生成目录和 ZIP 预览产物；用户已确认后续预览阶段可以只保留两个开发目录、不生成 ZIP/便携发行包，待下一次构建脚本调整时实施。单 EXE 便携版方案已解释，今天没有改动。

### 今日验证

- 产品和测试工程 Release 编译：0 警告、0 错误。
- 核心检查：104 项通过（终端/密码库/文件传输）；Windows 检查：3 项通过。
- UI 检查：此前 16 项通过；修正主密码“跳过”和 SSH 终端菜单后，未新增协议连接测试，最终发布前需要重新补充 UI 检查覆盖。
- 最新可运行预览目录：`artifacts/preview-ssh-fixed/installed-win-x64`、`artifacts/preview-ssh-fixed/portable-win-x64`。
- 未修改用户实际配置目录，未停止用户已有程序，未对真实 SSH 设备执行登录操作。

### 明确未完成项

- SSH keyboard-interactive 认证、在终端内输入用户名/密码后保存询问。
- 终端字体与配色持久化到全局或会话配置。
- 正式安装向导、卸载、升级，以及便携单 EXE 发布方案。
- 将预览构建默认改成只输出 installed/portable 开发目录。

今天的开发到此结束；下一次开发前先重新运行 UI 检查，再继续功能开发。


## 2026-10-23 - 文件传输原生异步、取消、超时与关闭等待

### 本次开发内容与原因

- 将公开 `IFileTransfer` 和内部 `IRemoteFileSystem` 改为原生异步契约；FTP 使用锁定版本 FluentFTP 55.1 的 `AsyncFtpClient`，SFTP 使用锁定版本 SSH.NET 2026.0 的异步连接、目录、属性、命令及文件流读写 API。`FileView` 不再用 `Task.Run` 包装同步文件操作。
- 所有连接与文件操作均要求 `CancellationToken` 和正数操作超时。异步门闩串行化单连接操作，超时包含门闩等待，并会取消底层请求；补充连接排队超时不得误关闭其他活跃连接的检查。
- 上传/下载改用异步流读写和释放；报告结构化传输方向、已传字节数及可选总量，并按 100ms/4MiB 节流。上传和下载继续经 `.part` 暂存，失败及取消时尽力清理并保留旧目标。
- 文件页和主窗口增加等待关闭流程：关闭页、退出应用会取消活动文件请求，等待请求退出和传输客户端释放后再销毁窗体。没有活动任务时也会等待 FTP 异步断开完成。
- 保持 FTP 覆盖移动。SSH.NET 2026.0 没有带取消令牌的 POSIX 覆盖重命名，因此异步 SFTP 替换改用旧目标 `.bak` 暂存、失败恢复策略，并明确替换期间路径可能短暂不存在。
- 更新文件传输技术说明、README 和阶段任务清单；扩大内存适配器测试，覆盖预先取消、超时诊断、队列超时竞态、操作取消、流提交失败和关闭竞态。

### 修改文件

- `src/PortHarbor/IFileTransfer.cs`、`FileConnection.cs`、`FileView.cs`、`MainForm.cs`
- `src/PortHarbor/RemoteFileSystems/IRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/FtpRemoteFileSystem.cs`
- `src/PortHarbor/RemoteFileSystems/SftpRemoteFileSystem.cs`
- `tests/PortHarbor.Checks/Program.cs`、`FileTransferChecks.cs`
- `README.md`、`docs/FILE-TRANSFER.md`、`docs/TECHNICAL-PLAN.md`、本开发日志

### 验证结果

- Release 构建：0 警告、0 错误。
- 核心检查：93 项通过，包括异步契约、预先取消、结构化进度、稳定超时诊断、连接队列超时竞态和连接释放。
- Windows 检查：3 项通过。
- UI 检查：15 项通过，含文件页关闭活动请求清理，以及主窗口退出等待异步文件清理。
- 终端 UI 冒烟输出包含 `CONPTY_OK`。
- 本机协议回环：20 项通过，覆盖 Telnet、SSH、FTP 和 SFTP；FTP/SFTP 中文路径、上传/下载、覆盖、取消保留目标及临时文件清理、重命名冲突和目录删除均通过。
- 报告保存在被 `.gitignore` 忽略的 `artifacts/async-transfer-unit.json` 与 `artifacts/async-transfer-validation/`；未新增运行时依赖。
- `git diff --check` 通过；仅有 Git 对一个新写源码文件的 LF/CRLF 提示，没有行尾空白错误。

### 遗留问题与边界

- SSH.NET 2026.0 未提供异步客户端断开 API。SFTP 网络操作会先异步响应取消并退出门闩，然后调用库的同步 `Dispose()` 关闭客户端；相关版本及这一例外已写入文件传输说明。
- SSH.NET 的异步重命名没有 POSIX 覆盖选项。SFTP 备份替换可能短暂暴露缺失的目标路径，服务器断线可能遗留 `.part`/`.bak`；本机 fixture 验证了常规替换，没有模拟生产服务器断线窗口。
- FTP 上传依赖服务器最终响应确认；若服务器不返回最终数据响应，客户端会保守地报告失败。当前回环服务器正常返回确认，其他 FTP/FTPS 服务器兼容性仍待扩大。
- 当前超时：连接 15 秒，单项文件操作 30 秒，远端临时文件尽力清理 5 秒。终端、RDP 等其他协议的全面异步和关闭生命周期仍未完成。
- 本次未实现本地化资源、传输队列、断点续传、递归传输或自动重连。

### 下一步

- 补齐 SSH、Telnet、Serial、Shell 和 RDP 的诊断展示与失败分类，继续完善其他后台任务及连接关闭/应用退出生命周期覆盖。
- 再将本地化资源、诊断消息目录和更广的 FTP/SFTP 服务器兼容性测试纳入阶段 1 清单。
