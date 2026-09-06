# MASTER — 多栏资源管理器设计系统（v3 · Windows 11 原生 Fluent）

> **v3 修订（2026-09-06，用户验收否决 v2 后）**：v2 的冷蓝灰阶偏离原生质感，本版依据微软官方规则
> 重订——来源：microsoft/microsoft-ui-reactor `skills/design.md`（已编译为本地 skill
> `win11-fluent-design`）+ WinUI 3 浅色主题标准值 + Windows 11 资源管理器实机解剖。
> 检索方式说明：本机无可用 Python 3，ui-ux-pro-max 的 search.py 不可运行，数据来自其 CSV 直查；
> Fluent 规则来自微软官方仓库抓取。

## 0. v3 铁律（对 v2 的纠偏）

1. **中性灰，无色相**：Mica 窗底 #F3F3F3、描边 #E1E1E1、文字 #1A1A1A——禁止冷蓝调灰阶（v2 之误）。
2. **Explorer 解剖**：标题栏/工具栏/标签条/状态栏同为 Mica 一整块（无分隔线）；文件内容区是
   **浮在 Mica 上的白色圆角卡**（顶部 8px 圆角、四周留 4px Mica 边、发丝描边）；文件树直接坐在 Mica 上。
3. **圆角两档**：控件 4px、浮层（菜单/气泡/ComboBox 弹层）8px，禁 6px 等非标值。
4. **行内交互**：悬停淡蓝 #E8F2FB、选中 #CCE4F7（强调色 Light3），不加左缘指示条等非原生装饰。
5. **四态成套**：rest/hover/pressed/disabled 全部经 W11.* 画刷切换，组件内禁裸 hex。
6. **图标**：`Segoe Fluent Icons, Segoe MDL2 Assets` 字体栈；**栅格**：4px 倍数。

## 1. 产品定位（Step 1 分析）

- 产品类型：桌面生产力工具（多栏文件管理器，Windows 11 平台）
- ui-ux-pro-max 数据命中：products #16 Productivity Tool → Flat + 微交互，层级清晰 + 功能色
- 品类一致性：文件管理器与 Explorer 同心智 → 直接采用 Windows 11 Fluent 原生视觉语言

## 2. 色彩 Token（全部进 W11.*；值 = Fluent 2 浅色标准）

### 强调色
- `Accent` #0067C0 / `AccentHover` #1975C5 / `AccentPressed` #3183CD / `AccentTint` #E8F2FB
- `SelectionBackground` #CCE4F7（行选中）/ `RowHover` #E8F2FB（行悬停）

### 表面（Mica 中性）
- `WindowBackground` = `LayerBackground` = #F3F3F3（Mica 一体）
- `CardBackground` #FFFFFF（内容卡/输入/激活标签）
- `SubtleBackground` #F7F7F7 / `HoverBackground` #E9E9E9 / `PressedBackground` #DEDEDE
- `DisabledBackground` #FAFAFA（配 #F0F0F0 描边）

### 线
- `CardBorder` #EDEDED（内容卡发丝）/ `ControlBorder` #E1E1E1 / hover #D6D6D6 / pressed #C9C9C9
- `Divider` = `Splitter` = #E5E5E5

### 文字
- `TextPrimary` #1A1A1A / `TextSecondary` #5D5D5D / `TextTertiary` #8A8A8A / `TextDisabled` #A3A3A3

### 功能色与深色元素
- `DangerBrush` #C42B1C（关闭钮悬停）/ pressed #A82A22 · `SuccessBrush` #0F7B0F · `WarningBrush` #9D5D00
- `MenuBackground` #F9F9F9（浮层亚克力近似）· ToolTip #262626
- 滚动条 #C8C8C8 → 悬停 #8A8A8A → 拖动 #6E6E6E

## 3. 组件细则

- 标准 Button：#FBFBFB 底 + #E1E1E1 边 + 4px 圆角，hover #F9F9F9/#D6D6D6，pressed #F1F1F1/#C9C9C9
- `W11.IconButton`：工具栏无边框透明钮，hover #E9E9E9 / pressed #DEDEDE
- `W11.AccentButton`：实心强调底白字（对话框主决策）
- TextBox：白底灰边；**聚焦 = 灰边保持 + 底部 2px 强调线**（Win11 招牌式）
- 标题栏与工具栏：同层 Mica、无分隔线；关闭钮悬停 #C42B1C 白字
- 列表行：4px 圆角、悬停 #E8F2FB、选中 #CCE4F7，无左缘装饰条
- 内容卡（DropZone）：Margin 4,2,4,0 / 顶部 8px 圆角 / #EDEDED 发丝边 / 内衬 2px
- 文件树：透明坐 Mica，左上留 4px 边

## 4. 反模式（禁止）

- 冷蓝调灰阶（v2 之误）；组件内裸 hex；6px 等非标圆角
- 标题栏/工具栏横加分隔线（Mica 一体，靠白卡的边缘分层）
- 行左缘强调条等 Explorer 没有的装饰
- 只定义 rest 态的交互元素
