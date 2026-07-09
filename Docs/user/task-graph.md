# TaskGraph 图编辑与模板使用指南

## 模型统一

模板和任务图在系统中是**同一种对象** —— 都是 `TaskGraph` 文档，
仅靠 `DocumentKind` 区分：

- `DocumentKind = Template` 表示这是一个**蓝图**（模板），不可直接执行
- `DocumentKind = Runtime` 表示这是一个**运行实例**，可以执行

这意味着：模板本身就是一张可编辑的图，它的节点、连线、注释都会
随模板一起保存；模板通过 `基于此模板生成任务图` 操作实例化出
一份运行图。运行图上的 `基于模板` 字段会指向它来自哪个模板。

**反向沉淀（另存为模板）**：运行图也可以"另存为模板"——系统会深拷贝当前
运行图，清空所有运行态数据（节点状态、输出摘要、会话 ID 等），
将 `DocumentKind` 转为 `Template`，并保存为新模板文档。模板默认命名为
"`{当前图名} - 模板 `"，不会覆盖原始运行图。

## 进入方式

- **任务编排工作区**：点击窗口顶部标题栏的「任务编排」按钮。
  - 左侧「模板」与「任务图」两组分别展示当前所有模板和运行图。
  - 右侧上方是统一的图编辑画布（与任务图页同一套），下方是
    模板规则 / 任务图信息的编辑面板。
  - 在此可以新建、重命名、复制、删除模板，浏览所有运行图，
    并在画布上直接编辑模板的节点、连线、注释。
- **直接打开某个图**：可通过左侧聊天侧栏的任务图列表或 CLI 指令。
- **聊天页底部「编排选项」**：仍是轻量入口（普通对话 / 自动编排 / 使用模板）。
  - 「自动编排」使用内置的 `builtin.auto-orchestration` 系统模板
  - 「使用模板」会切到任务编排工作区选模板

## 模板和任务图的状态

| 状态 | 显示位置 | 说明 |
| --- | --- | --- |
| 草稿 | 任务图名称旁 | 刚创建或保存后未运行 |
| 执行中 | 任务图名称旁 | `TaskGraphExecutor` 正在跑节点 |
| 等待确认 | 任务图名称旁 | 撞到 `HumanInput` 检查点（如功能开发的"用户确认方案"） |
| 已完成 | 任务图名称旁 | 全部节点跑完 |
| 失败 | 任务图名称旁 | 至少一个节点失败 |
| 已取消 | 任务图名称旁 | 用户主动取消或被分离 |

模板（`DocumentKind = Template`）始终是「草稿」状态，且不能执行 —
工具栏上的「执行 / 重试 / 取消 / 继续」按钮在模板模式下都是禁用的。

## 连接点（Ports）

## 连接点（Ports）

每个节点在左右两侧各有一个**固定位置**的圆点（port）：

- **左侧圆点（输入 / Input）**：表示上游依赖连入此位置。
  - 仅作为视觉锚点，悬停时会显示 ToolTip「从此处拖动以连接其他节点」。
- **右侧圆点（输出 / Output）**：从此处按住鼠标拖动到另一个节点的左侧圆点，即可建立依赖连线。
  - 拖动时会绘制一条**蓝色虚线贝塞尔预览**，箭头指向目标位置。

**固定位置**：所有节点的 port 都锚定在节点上沿 + 58px 处的水平线上（`TaskNodePortStyle.PortAnchorOffsetY`），与节点内容高度无关 —— 拖动节点时，port 位置永远不变，连线也会自动跟随。

**颜色编码**：port 圆点的颜色表示该节点所代表的任务类型，不同类型一眼可辨：

| 节点类型 | 中文 | 颜色 | Hex |
| --- | --- | --- | --- |
| `Plan` | 方案 | 蓝 | `#2459B8` |
| `Execute` | 执行 | 橙 | `#FF6A00` |
| `Verify` | 验证 | 绿 | `#18A558` |
| `Decision` | 决策 | 紫 | `#8B5CF6` |
| `Parallel` | 并行 | 青 | `#06B6D4` |
| `HumanInput` | 人工 | 粉 | `#EC4899` |

**连线**采用**平滑水平 S 曲线**（cubic bezier），颜色 = **上游节点**的端口颜色。下游节点的颜色只影响自身的 port 颜色，不影响连线颜色 —— 这样可以从连线一眼看出「这条数据 / 控制流从哪里来」。

选中节点时，相关的连线会高亮为橙色（`#FF6A00`）并加粗，便于在复杂图中追踪依赖。

## 节点结构

节点内部分为四行：

| 行 | 内容 | 字号 | 颜色 |
| --- | --- | --- | --- |
| 0 | 状态点 + 标题 + 状态文字 | 13px SemiBold | `#1F2328` / 状态文字 `#6E727A` |
| 1 | 节点类型标签（方案 / 执行 / 决策 / 验证 / 人工） | 11px SemiBold | port 颜色 |
| 2 | 描述 + 输出摘要（自动隐藏空摘要） | 11px / 10.5px | `#40444B` / `#6E727A` |
| 3 | 打开执行详情按钮（外部链接图标） | — | — |

### 右下角的图标

那是**「打开执行详情」**按钮（带外部链接箭头 ↗ 图标）。点击后弹出该节点的**完整 Agent 会话窗口**，仅在该节点已执行过、有 `AgentSessionId` 时才可点（`CanOpenDetail` 绑定控制启用状态）。

### 右侧的彩色圆点

那是**输出 port**（按节点类型着色）。从此处拖动即可拉出一条新连线。

## 节点类型编辑（Execute / HumanInput / Plan 等）

1. **点击选中**一个节点（节点边框会变成深色加粗）。
2. 点击节点卡片内的**编辑按钮**，节点会直接切换到可编辑模式，可以编辑：
   - 标题（`Title`）
   - 描述（`Description`）
   - 节点类型（`Kind`）—— 节点内下拉框可选 `Plan / Execute / Verify / Decision / Parallel / HumanInput`
   - 保存按钮立即生效并落盘
3. 节点类型的差异：

| 类型 | 含义 | 何时使用 |
| --- | --- | --- |
| **Plan** | 仅做规划 / 分析，不修改文件 | 复杂任务前的方案设计、需求分析 |
| **Execute** | 启动 Agent 实际编码或执行命令 | 主要任务步骤（默认类型） |
| **Verify** | 对上游输出做断言式校验 | 防止破坏性改动、自动 review |
| **Decision** | 根据上游 Agent 输出决定下游分支 | 模板驱动的动态图（如 bug 列表处理） |
| **Parallel** | 同时 fork 出多个分支 | 互不依赖的子任务并行执行 |
| **HumanInput** | 暂停执行，等待用户确认后继续 | 需要人介入审批的检查点 |

## 自动布局

工具栏的「自动布局」按钮（4 宫格图标）会重新计算每个节点的位置，按层级铺开，避免连线交叉。

## 调试截图

新增两个 CLI 截图选项：

```bash
# 渲染当前打开的图到 screenshots/graph-view.png
dotnet run --project src/AgentOrchestrator.App -- --open-graph "Bug 列表处理" --maximize-graph --render-graph-test

# 渲染 + 模拟从第一个节点拉一条连线预览到 screenshots/connection-drag.png
dotnet run --project src/AgentOrchestrator.App -- --open-graph "Bug 列表处理" --maximize-graph --render-drag-test
```

## 关键文件

- `Models/TaskGraph/TaskNodePortStyle.cs` — 颜色 / 标签映射
- `ViewModels/TaskGraphEdgeViewModel.cs` — Edge 携带 SourceKind + SourcePortColors
- `ViewModels/TaskGraphWorkspaceViewModel.cs` — 用 PortAnchorOffsetY 计算端点
- `Controls/TaskGraphWorkspaceControl.axaml.cs` — bezier 路径生成、port 定位、wrapper Canvas
- `App.axaml` — 端口颜色样式（`port-port-*`）、外部链接图标（`toolbar-icon-external-link`）
