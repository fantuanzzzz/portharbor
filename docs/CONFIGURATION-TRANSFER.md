# 会话配置导入导出约定

## 调用与依赖

`MainForm -> ConfigurationService -> ConfigurationTransferService -> IConfigurationTransfer (Store)`。文件选取经 `IFilePicker`，导入信任确认仍在 UI；Application 不引用 JSON、INI、Windows 或密码库实现。

应用层的 `SessionConfiguration` 只携带 `Groups` 和 `Sessions`。存储适配器先完成格式/schema 校验再投影到此 DTO；配置文件中的 `Vault`、`Credentials`、`HostKeys` 不进入传输用例。设计依据见 [ADR 0007](adr/0007-configuration-transfer-policy.md)。

## 导入

1. 兼容现有 JSON v1/v2 与 INI 读取；UI 选择框仍只提供 JSON 过滤器，不新增菜单或输出格式。
2. 输入分类、会话及重复 ID 校验失败时不写入。源文件只读，保留原内容。
3. 分类在现有分类之后追加，按不区分大小写去重，保留先出现的拼写与顺序；空分类也保留。
4. 现有会话、密码保存设置、加密凭据和已确认主机信任保持原值。导入会话获得新 ID，SavePassword 为 false；重复导入仍新增会话，不隐式覆盖同主机记录。
5. 协议、用户名、私钥路径、Shell 命令、串口参数等非凭据会话设置仍按旧格式传递；导入 Shell 配置必须继续经过现有信任提示。私钥文件内容不在配置 DTO 中。
6. 存储在共享锁内读取最新快照并运行纯转换回调，更新分类/会话后只保存一次。失败恢复旧内存集合并向上抛出；成功后才返回导入数量。配置仍使用临时文件、Flush 和原子替换/备份。

## 导出

- 读取分类与会话的一致快照，复制会话并关闭 SavePassword，不改变源会话或活动密码库。保留会话 ID、排序、分类与其余设置。
- 继续输出 JSON v2、默认名 `portharbor-sessions.json`，不输出密码库或凭据、主机信任内容；不提供带密码导出。
- 写出失败抛给调用方，不显示导出成功；导出目标仍使用原有直接 JSON 写入行为，不承诺原子替换或备份。

## 原子更新端口

`UpdateSessionConfiguration` 的回调只能计算结果，不应做文件访问、网络 I/O、用户交互或再次调用存储。存储交给回调的会话与发布结果都复制，防止保留引用后修改活动数据。共享锁仅覆盖同一进程，不解决同时启动两个应用写同一目录的问题。

## 验证入口

- `tests/Shared/ConfigurationTransferChecks.cs`：平台无关策略与失败端口，两个检查项目共用。
- `tests/PortHarbor.Checks/ConfigurationTransferStorageChecks.cs`：真实 JSON/INI 存储、交叉格式、密码/信任保留、并发、备份与重启。
- `tests/PortHarbor.Checks/StorageAdapterChecks.cs`：导入失败及存储适配兼容；本轮修正 INI 分支使用便携模式。
- `tests/PortHarbor.Checks/FilePickerChecks.cs`：界面取消、确认导入与无密码导出。

报告与本轮验证结果见[开发日志](DEVELOPMENT-LOG.md)。
