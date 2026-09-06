# MASTER — 多栏资源管理器设计系统（v2 重设计）

> 依据 ui-ux-pro-max（github.com/nextlevelbuilder/ui-ux-pro-max-skill，125k★）方法论生成。
> 检索方式说明：本机无可用 Python 3，无法运行 skill 的 search.py，改为直接检索其数据文件
> （products.csv / styles.csv / colors.csv / typography.csv / stacks/wpf.csv）——属 skill 规定的
> fallback 路径，本档所有结论均来自上述数据文件的命中结果。

## 1. 产品定位（Step 1 分析）

- 产品类型：桌面生产力工具（多栏文件管理器，Windows 11 平台）
- 数据命中：`products.csv` #16 Productivity Tool → 主风格 **Flat Design + Micro-interactions**，
  辅助 **Minimalism & Swiss Style** + **Soft UI Evolution**；关键词"Clear hierarchy + functional
  colors, Ease of use, Speed & efficiency"
- Stack：`stacks/wpf.csv`（skill 原生支持）——XAML 声明式样式、资源字典主题化
- 宿主一致性约束：文件管理器品类在 Windows 上与 Explorer 同心智，主强调色保持蓝色系
  （对照 colors.csv 命中的"语义化功能色"原则重新平衡，而非换色相）

## 2. 风格裁定

| 维度 | 裁定 | 来源 |
|------|------|------|
| 骨架 | Flat + 微交互（悬停/按下 120ms 级过渡感） | products #16 主风格 |
| 层次 | 三层表面：窗底 → 工具层 → 卡片白，配柔和投影 | Soft UI Evolution（styles #19） |
| 几何 | 控件 4px / 容器 8px 圆角；1px 冷灰发丝线代替中灰硬边 | Minimalism & Swiss（styles #1） |
| 密度 | 工具型中高密度：行高紧凑、4px 间距栅格 | products #16 "efficiency focus" |
| 文字 | Segoe UI Variable 13px 基准 + 11/12/13/14/16 层级，Secondary 辅助文字 | typography #5/#13（中性 UI 无衬线映射到平台字体） |

## 3. 色彩 Token（语义化，全部进 W11.* 资源；禁止组件内裸 hex）

### 强调色（保留 Windows 蓝相，按 WinUI3 端值重校）
- `Accent` #0F6CBD / `AccentHover` #115EA3 / `AccentPressed` #0C3B5E
- `AccentTint` #EAF3FC（强调色微底）
- `SelectionBackground` #CFE4FA（选中底）+ 行左缘 3px Accent 指示条
- `RowHover` #ECF3FB（强调色染悬停，替代纯灰悬停）

### 表面（三层 + 悬停态，冷中性）
- `WindowBackground` #F2F4F8（窗底/槽位）
- `LayerBackground` #F7F9FC（标题栏/工具栏/状态栏层）
- `CardBackground` #FFFFFF（输入框/列表面/卡片/激活标签）
- `SubtleBackground` #EDF1F6 / `HoverBackground` #E6ECF4 / `PressedBackground` #DCE4EF
- `DisabledBackground` #EFF2F6

### 线与分隔
- `CardBorder` #E4E9F0（卡片发丝线）/ `ControlBorder` #D6DDE7（控件边）
- `ControlBorderHover` #C3CDDC / `ControlBorderPressed` #A9B7CB
- `Divider` #E7EBF1（分隔线/分隔条，替代 #E5E5E5 硬线）

### 文字（冷灰阶 4 档）
- `TextPrimary` #171D29 / `TextSecondary` #5B6675
- `TextTertiary` #8C96A6 / `TextDisabled` #AEB6C2

### 功能色与深色元素
- `DangerBrush` #D13438（关闭钮/危险操作）/ `SuccessBrush` #0E8345 / `WarningBrush` #B54708
- ToolTip 深色气泡 #26323F / 菜单底 `MenuBackground` #FBFCFE + 8px 圆角 + 柔影
- 滚动条胶囊 #BCC5D2 → 悬停 #97A2B3 → 拖动 #6F7B8D

## 4. 组件细则

- 标准 Button：白底 + 发丝线 + 4px 圆角，悬停微亮边框加深；禁用降透明
- `W11.IconButton`（新增）：工具栏无边框图标钮，透明底、悬停 Subtle、按下 Pressed
- `W11.AccentButton`（新增）：主操作实心 Accent 底白字，用于对话框主决策（如冲突"替换"）
- 列表行：悬停 RowHover、选中 Selection + 左缘指示条；行圆角 4
- 标签页（窗格内）：激活 = Card 白底 + 发丝线 + 6px 圆角；非激活透明，悬停 Subtle
- 标题栏：LayerBackground + Divider 发丝底线，关闭钮悬停 DangerBrush 白字
- 地址栏/输入：白底发丝线，聚焦 Accent 边 + AccentTint 微底
- 分隔条：Divider 色、悬停时 AccentTint 反馈

## 5. 反模式（避免）

- 组件内硬编码 hex（本次重构后为硬约束）
- 多种灰阶边框混用（一律 Card/Control/Divider 三级）
- 纯灰悬停选中（选中/悬停必须带强调色倾向）
- 0ms 状态切换 / 无悬停反馈的可点击元素
