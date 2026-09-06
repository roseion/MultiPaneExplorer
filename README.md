# 多栏资源管理器 MultiPaneExplorer

一个 Windows 桌面**多栏文件管理器**：单窗口内 2/3/4 个资源管理器窗格并列，跨目录对比与文件搬运无需来回切换窗口。
采用 .NET 8 / WPF 构建，界面遵循 Windows 11 Fluent 设计语言。

> A multi-pane Windows file manager built with .NET 8 / WPF, following the Windows 11 Fluent design language.

[License: MIT](LICENSE) ![Version](https://img.shields.io/badge/version-1.9.0-blue)

## 功能特性

- **多栏布局**：双栏 / 三栏 / 四栏（田字、并排），分隔条均可拖拽；各窗格状态独立保留
- **每窗格多标签页**：Ctrl+T 新建、Ctrl+W 关闭、中键关闭、双击空白新建、拖拽重排
- **文件树侧栏**：懒加载目录树、盘符用量条、known folders（桌面/文档/下载…）分组、宽度可调
- **完整文件操作**：复制/剪切/粘贴（与资源管理器剪贴板互通）、拖拽（同盘移动/Ctrl 复制）、
  压缩为 ZIP、解压、发送到桌面快捷方式、重命名（F2 原位内联）、新建、回收站删除与永久删除
- **撤销 / 重做**（Ctrl+Z/Y）：覆盖粘贴、移动、删除、重命名、新建；替换/合并类传输不可撤销
- **回收站视图**：还原 / 全部还原 / 永久删除 / 清空，直读 `$Recycle.Bin` 不经 Shell COM
- **三种视图**：详细信息（可排序列、列宽拖拽、列显隐记忆）、大图标、列表
- **缩略图**：图片 WPF 解码；视频 / PDF 走 Shell 缩略图提供程序（IShellItemImageFactory）
- **预览窗格**：Alt+P 开关，图片 / 文本 / 文件信息三种形态，跟随激活窗格选中项
- **面包屑地址栏**：逐段跳转、子目录下拉、路径补全、此电脑 / 回收站入口
- **搜索**：当前列表即时过滤 + 递归子目录搜索（异步增量上屏、可取消、结果带相对路径列）
- **UAC 提权重试**：访问被拒时可一键以管理员身份重试该批操作（提权辅助进程）
- **Windows 11 风格**：Fluent 主题、自绘标题栏（右键系统菜单、贴靠布局弹窗）、此电脑驱动器宽卡
- **会话记忆**：布局、路径、标签、列布局、缩放、开关状态随会话恢复；常用目录自动计数

## 环境要求

- Windows 10 / 11（x64）
- 构建：.NET 8 SDK（或更高，含 .NET 8 目标包）
- 运行：.NET 8 Desktop Runtime

## 构建与运行

```cmd
:: 构建
dotnet build MultiPaneExplorer.slnx

:: 运行（或直接打开 bin 下生成的 exe）
start src\MultiPaneExplorer.App\bin\Debug\net8.0-windows\MultiPaneExplorer.App.exe

:: 单元测试（115+ 用例）
dotnet test MultiPaneExplorer.slnx

:: 发布单文件 exe（输出到 dist\）
publish.cmd
```

## 常用快捷键

| 快捷键 | 功能 | 快捷键 | 功能 |
|--------|------|--------|------|
| Ctrl+C / X / V | 复制 / 剪切 / 粘贴 | Ctrl+Z / Y | 撤销 / 重做 |
| Ctrl+A / F6 | 全选 / 窗格间切换焦点 | F2 / Del / Shift+Del | 重命名 / 删到回收站 / 永久删除 |
| Ctrl+F | 聚焦搜索框 | Ctrl+Shift+C | 复制文件路径 |
| Ctrl+Shift+N | 新建文件夹 | Ctrl+D | 删除到回收站 |
| Alt+Enter / Alt+←/→ | 属性 / 后退 / 前进 | Ctrl+T / Ctrl+W | 新建 / 关闭标签 |
| Ctrl+N | 新窗口 | Ctrl+= / Ctrl+- / Ctrl+0 | 界面缩放 |
| Alt+P | 预览窗格 | F5 | 刷新 |

## 目录结构

```
├── src/
│   ├── FileOps.Core/            # 文件操作核心库（复制/移动/删除/回收站/撤销/搜索/ZIP/提权辅助）
│   └── MultiPaneExplorer.App/   # WPF 应用（窗格、主题、预览、对话框）
├── tests/FileOps.Core.Tests/    # xUnit 单元测试
├── docs/                        # 产品与迭代文档（设计文档、功能对比分析、迭代计划）
├── design-system/               # UI 设计系统规范
└── publish.cmd                  # 单文件发布脚本
```

## 架构说明

核心逻辑与 UI 严格分离：`FileOps.Core` 是与 WPF 无关的类库（复制/移动的冲突策略、回收站
`$I/$R` 直解、撤销栈、提权辅助进程协议等），全部能力都有 xUnit 覆盖；`MultiPaneExplorer.App`
只做视图与交互。UI 视觉 token 集中在 `Themes/Win11Theme.xaml`，设计规范见
[design-system/multipane-explorer/MASTER.md](design-system/multipane-explorer/MASTER.md)。

## 已知限制

- 完整系统右键菜单（IContextMenu）：第三方 Shell 扩展在非 Explorer 宿主进程内会崩溃，
  已以"打开方式…/属性"替代，恢复需扩展宿主隔离（见 docs）
- 不接管系统默认资源管理器；OneDrive 占位文件、网络邻居为非目标

## License

[MIT](LICENSE)
