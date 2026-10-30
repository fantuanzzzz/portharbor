# PortHarbor 应用图标

蓝色圆角底、白色港湾轮廓、青色终端 `>_`，适合浅色/深色 Windows 桌面。图形不使用外部字体或第三方素材，沿用项目根 MIT License，Copyright (c) 2026 fantuanzzzz。

- `portharbor.svg`：可编辑原稿，256 单位画布，背景外部透明。
- `portharbor.png`：512×512 透明预览，可作仓库/项目头像素材。
- `portharbor.ico`：16、20、24、32、48、64、128、256 像素，32 位 RGBA，用于 EXE、主窗口和安装器。

修改 SVG 后，在 Windows PowerShell 执行：

```powershell
pwsh -File scripts/New-AppIcon.ps1
```

脚本使用 Windows System.Drawing，直接读取当前 SVG 的渐变、圆角矩形和 M/L/C 描边路径，以四倍采样生成 PNG 与 ICO，不依赖网络、外部字体或图像服务。增加其他 SVG 指令时须同步扩展生成器，不能假定其为通用 SVG 渲染器。

项目文件分别设置 ApplicationIcon 和内嵌 ICO，主窗口使用独立 Icon 副本并在释放时销毁；Inno Setup 使用同一 ICO。开始菜单/桌面/卸载入口沿用应用 EXE 图标。更新源码资产不会修改历史 EXE，需要重新构建。
