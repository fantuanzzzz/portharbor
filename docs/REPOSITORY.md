# 源码、发行与本地归档

## 应纳入 GitHub 的内容

- `src/`、`tests/`、`scripts/`、`packaging/`、`.github/`：实现、自动化、构建及发行来源。
- 根 `README.md`、`LICENSE`、`THIRD-PARTY-NOTICES.md`、`licenses/`：项目首页与必须保留的版权/许可材料。
- `docs/` 的当前技术方案、功能/接口约定、测试和实机手册、精简里程碑及 ADR：供用户和贡献者理解当前状态。
- `.editorconfig`、`.gitignore`、`Directory.Build.props`、`NuGet.Config`、各项目依赖锁和合成测试 fixture：保持可复现构建与回归。

ADR 记录决策背景，历史日期不表示过期；仍被实现与测试引用的文档不能仅因较旧就移除。第三方法律文本同样不能当作可有可无的开发资料归档。

## 不纳入源码提交

| 目录/文件 | 处理方式 |
| --- | --- |
| `local-archive/` | 本地历史文档与整理前快照，由 `.gitignore` 排除 |
| `artifacts/` | 构建包、验收报告、截图、日志，已有忽略规则；发行 EXE 通过 GitHub Release 附件上传 |
| `.build-home/`、`.packages/`、`**/bin/`、`**/obj/` | 工具/依赖缓存和构建输出，保留原构建路径并忽略 |
| IDE 状态、临时文件、TestResults、Python 缓存 | 忽略，不影响从源码恢复 |
| 运行配置、密码库、主机信任、日志、私钥及 `.env` | 不应上传；默认根目录运行数据有忽略规则，外部自定义目录仍需自行核对 |

本轮归档目录为 `local-archive/2026-10-30-github-preview/`，含完整开发流水、2026-10-20 旧验收报告、旧项目 README 及旧文档首页；归档内有清单与 SHA-256。没有移动仍被构建脚本使用的缓存、测试 fixture、源代码或许可。

`.gitignore` 只影响未跟踪内容；已被 Git 跟踪的文件不会自动取消跟踪，过去提交中的内容也不会消失。本轮旧验收文件在工作区表现为删除，完整流水原路径改为公开精简版；提交这些变更后，新版本文件树不再包含被移走的完整材料。未重写 Git 历史。如历史中发现真实秘密，应先撤销/轮换秘密，再另行处理历史。

## 首个公开预览的本地准备

1. 核对根 `LICENSE` 的版权行。当前个人署名为 `Copyright (c) 2026 fantuanzzzz`；第三方版权不改。
2. 查看 `git status --short` / `git diff --check`，确认新测试和新文档会被提交，`local-archive/`、运行数据和 artifacts 未进入暂存区。可用 `git check-ignore -v local-archive/2026-10-30-github-preview/README.md` 核对。
3. 查看 `git ls-files` 是否残留真实配置、日志或密钥；忽略规则不替代人工审查，也不应排除合成 fixture 和依赖锁。
4. 提交准备发布的源码。应用/打包脚本当前版本为 **0.2.0**；已有 Git 提交消息不是产品版本号。选择实际发布标签时与源码和附件版本保持一致，不自动改写既有标签。
5. 在干净工作区运行[测试说明](TESTING.md)中的锁定恢复与适用回归，执行 `pwsh -File build.ps1 -P10`，生成 `artifacts/releases/` 的 MIT 版候选。若缺 Inno Setup，先执行工具下载脚本。
6. 推送代码后查看新增 core/validate/distribution CI job，再按[实机手册](REAL-ENVIRONMENT-ACCEPTANCE.md)记录有环境的项目，未做项如实标明。
7. 在 GitHub 创建 **Pre-release**，上传 portable EXE、setup EXE、`release-manifest.json` 及 `packaging/README.md` 的发行说明副本。不要将 EXE、完整 artifacts、密码库或原始调试日志提交进源码仓库。

MIT 文字、关于信息或文档更新不会修改已经生成的 EXE。历史候选仍带旧说明，公开发行必须重新构建；manifest 中 Dirty 应为 false，提交 SHA 与发布标签对应，并再次核对文件哈希。
