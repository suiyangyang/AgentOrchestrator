# TaskGraph 对话内 Orchestrator UI 改造方案

> 状态: UI 开发方案，待实施
> 日期: 2026-06-23
> 适用范围: AgentOrchestrator TaskGraph v3 对话内编排 UI

---

## 1. 文档目的

本文给出 TaskGraph 对话内 Orchestrator 的 UI 改造方案，目标是把“当前会话作为控制面”的产品体验落实到可开发的 Avalonia 界面结构中。

本文是独立 UI 方案，关注:

- Chat 主界面的结构调整
- TaskGraph 实时状态在 Chat 内的呈现方式
- 编排入口、检查点交互、状态反馈、分离/取消等动作的界面承载
- 首版 UI 范围与不做项

本文不替代执行器和数据模型方案；它与 `Docs/working/TaskGraph-对话内Orchestrator-开发方案.md` 配套使用。

---

## 2. UI 目标

### 2.1 核心目标

v3 UI 需要达成以下目标:

1. 用户在 Chat 中直接发起 TaskGraph 编排
2. 用户无需切换到独立工作区，就能看到当前图的实时状态
3. 用户能在检查点上通过 Chat 继续、暂停、取消、汇总
4. 用户能理解“当前会话正在控制一个图”，而不是只是看到一个普通状态条
5. UI 必须与当前“控制面 / 执行面 / 检查点推进”模型一致

### 2.2 体验目标

界面体验应当体现以下特征:

- **工作流一致性**: 入口、进度、控制动作都在 Chat 中
- **轻量但明确**: 不把独立工作区的全量编辑能力硬塞回 Chat
- **结果导向**: 重点展示当前阶段、下一步、异常与待决策项
- **低干扰**: 不用实时日志把聊天界面淹没

---

## 3. UI 设计原则

### 3.1 Chat 是主界面，不是次级容器

TaskGraph 对话内体验不是“在 Chat 里临时塞一个小工具”，而是 Chat 的正式工作模式之一。

因此，TaskGraph UI 应:

- 与消息流并列成为 Chat 的主要信息区
- 有自己的标题、状态和控制动作
- 在视觉层级上明显高于普通临时通知

### 3.2 实时面板展示“聚合状态”，不展示全量代理日志

首版 UI 只展示:

- 当前图
- 当前运行节点
- 最近完成/失败节点
- 待决策项
- 当前可执行动作

不展示:

- chunk 级实时原始输出
- 每一步工具调用详情
- 全量消息历史 diff

### 3.3 控制动作必须和检查点模型对齐

UI 上提供的动作必须匹配当前执行模型，不提供虚假的实时控制。

首版只强调:

- 继续
- 暂停
- 取消
- 汇总
- 分离

不提供:

- 拖拽改图
- 运行中切换节点策略
- 任意时间插队改依赖

### 3.4 Chat 与图状态必须视觉分层

用户需要一眼区分:

- 普通聊天内容
- 当前图的状态
- 当前会话可执行的图控制动作

因此不能把这些信息全部混成一串消息泡泡。

---

## 4. 当前 UI 基线

当前代码基础已经具备以下可复用条件:

- `ChatWorkspaceControl.axaml` 已是 Chat 主界面容器
- `ChatWorkspaceViewModel` 已新增 `ActiveGraph`、`ActiveExecutionContext`、`HasChatExecutionLease`
- Chat 中已出现活动图状态区域的初始实现
- 独立 `TaskGraphWorkspaceControl` 仍然存在，可作为高级编辑视图保留
- 全局样式仍集中在 `App.axaml`

这意味着:

- v3 UI 不需要另起一套新的壳
- 重点是扩展 ChatWorkspace，而不是重建聊天界面

---

## 5. 首版 UI 范围

### 5.1 在范围内

- Chat 顶部 / 输入区附近新增编排入口
- Chat 内嵌活动图状态面板
- 活动图的运行状态、检查点状态、节点摘要展示
- 图控制按钮: 继续 / 暂停 / 取消 / 汇总 / 分离
- 与 `ConversationExecutionContext` 对应的摘要内容展示
- 在图执行期间禁用普通发送，体现 `ChatExecutionLease`

### 5.2 不在范围内

- Chat 内完整 DAG 画布编辑
- 节点拖拽布局
- 图内缩放和平移
- 节点级详情弹窗全量迁移
- 可视化动态扩图编辑
- 多图标签页并存

---

## 6. 整体界面结构

### 6.1 Chat 页面目标结构

首版推荐结构如下:

```text
┌─────────────────────────────────────────────────────────────┐
│ Chat Header                                                │
├─────────────────────────────────────────────────────────────┤
│ TaskGraph Orchestrator Panel (条件显示)                    │
├─────────────────────────────────────────────────────────────┤
│ Message List                                               │
│  - 普通消息                                                │
│  - 系统消息 / 汇总消息                                     │
├─────────────────────────────────────────────────────────────┤
│ Composer Toolbar                                           │
│  [智能编排] [模板编排] [注入上下文] ...                    │
│ Composer                                                   │
└─────────────────────────────────────────────────────────────┘
```

### 6.2 为什么面板放在消息流上方

原因:

- 当前图是当前会话的工作上下文，不只是普通消息中的一条附件
- 它需要在用户滚动消息时仍然维持较高可见性
- 控制动作不能埋在消息流深处

因此推荐位置是:

- Chat Header 下方
- Message List 上方

而不是:

- 消息流中的一条普通卡片

---

## 7. 核心控件拆分

### 7.1 `TaskGraphConversationPanel`

建议新增控件:

- `src/AgentOrchestrator.App/Controls/TaskGraphConversationPanel.axaml`
- `src/AgentOrchestrator.App/Controls/TaskGraphConversationPanel.axaml.cs`

职责:

- 作为 Chat 内嵌图状态面板
- 展示当前图标题、状态、节点摘要、检查点动作
- 不承担画布编辑职责

建议绑定数据:

- `ActiveGraph`
- `ActiveExecutionContext`
- `HasChatExecutionLease`
- `PauseActiveGraphCommand`
- `CancelActiveGraphCommand`
- `ResumeContinueGraphCommand`
- `ResumeSummarizeGraphCommand`
- `ExecuteDetachActiveGraphCommand`

### 7.2 `TaskGraphConversationNodeRow`

建议新增轻量子控件:

- `src/AgentOrchestrator.App/Controls/TaskGraphConversationNodeRow.axaml`

职责:

- 展示一条节点摘要
- 可用于“最近完成”“最近失败”“待决策”三种列表

展示内容:

- 节点标题
- 状态标签
- 摘要文本
- 时间信息

### 7.3 `TaskGraphConversationDecisionBar`

建议新增:

- `src/AgentOrchestrator.App/Controls/TaskGraphConversationDecisionBar.axaml`

职责:

- 在检查点状态下展示当前可执行动作
- 将“继续 / 汇总 / 暂停 / 取消”固定放在摘要列表下方

首版可与主面板写在一起，不一定单独拆 code-behind。

---

## 8. Chat 主界面改造点

### 8.1 `ChatWorkspaceControl.axaml`

文件:

- `src/AgentOrchestrator.App/Controls/ChatWorkspaceControl.axaml`

需要完成的改造:

1. 在 header 与 message list 之间留出活动图面板区域
2. 当 `HasActiveGraph == true` 时显示面板
3. 当 `HasActiveGraph == false` 时完全折叠
4. 图执行中调整 composer 提示和可用动作

推荐结构:

```xml
<Grid RowDefinitions="Auto,Auto,*,Auto">
  <ContentControl Grid.Row="0" ... />   <!-- header -->
  <controls:TaskGraphConversationPanel Grid.Row="1"
                                       IsVisible="{Binding HasActiveGraph}" />
  <ScrollViewer Grid.Row="2" ... />     <!-- message list -->
  <Border Grid.Row="3" ... />           <!-- composer -->
</Grid>
```

### 8.2 Composer Toolbar

需要新增两个主要入口:

- `智能编排`
- `模板编排`

建议放在:

- 发送按钮左侧
- 与现有附件、模型选择、权限选择保持同一工具栏层级

### 8.3 Composer 禁用状态

当 `HasChatExecutionLease == true` 时:

- 普通发送按钮禁用
- 输入框允许继续输入文本，但默认不允许发送
- 若需要引导，可显示一条细提示:
  - `当前图正在占用会话执行，可在检查点继续或汇总。`

不建议:

- 把整个输入框彻底灰掉不可编辑

原因:

- 用户可能希望先写好下一条问题或总结要求

---

## 9. 活动图面板设计

### 9.1 面板总体结构

```text
┌─ TaskGraph · 当前图名称 ───────────────────────────────┐
│ 状态: 执行中 / 等待决策 / 已完成 / 已失败              │
│ 当前节点: fix_bug_3                                    │
│ 图摘要: 第 1 阶段已完成 3/7，当前进入修复阶段         │
│                                                       │
│ 最近完成                                               │
│  - 分析 Bug 2: ...                                     │
│  - 修复 Bug 2: ...                                     │
│                                                       │
│ 最近失败                                               │
│  - 分析 Bug 5: ...                                     │
│                                                       │
│ 待决策                                                 │
│  - 是否继续修复第 5 个 bug                             │
│                                                       │
│ [继续] [汇总] [暂停] [取消] [分离]                     │
└───────────────────────────────────────────────────────┘
```

### 9.2 面板信息层级

从高到低建议如下:

1. 图标题 + 全局状态
2. 当前运行节点 / 当前检查点
3. 图级摘要
4. 最近完成
5. 最近失败
6. 待决策
7. 动作栏

### 9.3 面板高度策略

首版不做复杂折叠动画，建议:

- 默认自动高度
- 最大高度控制在 `260px ~ 340px`
- 内容超出时内部滚动

理由:

- 避免图信息无限压缩消息流
- 避免实现复杂动画拖慢首版

---

## 10. 状态映射与视觉表达

### 10.1 图级状态

建议使用以下视觉映射:

- `Draft`: 中性灰
- `Running`: 蓝色或橙色强调
- `WaitingForInput`: 黄色强调
- `Completed`: 绿色
- `Failed`: 红色
- `Cancelled`: 灰红色

### 10.2 节点状态

节点摘要行建议使用小状态胶囊:

- `已完成`
- `失败`
- `待决策`
- `执行中`

### 10.3 当前检查点

当 `ActiveGraph.IsCheckpointPending == true` 时:

- 面板顶部出现一条强调提示
- 显示:
  - 当前检查点节点
  - 原因说明
  - 建议动作

示例:

```text
检查点: 分析 Bug 5 失败，等待你决定继续、汇总或取消。
```

---

## 11. 控制动作设计

### 11.1 首版动作清单

首版固定提供:

- `继续`
- `汇总`
- `暂停`
- `取消`
- `分离`

### 11.2 动作启用规则

#### `继续`

启用条件:

- `HasActiveGraph == true`
- 图处于检查点或等待输入阶段

#### `汇总`

启用条件:

- `HasActiveGraph == true`
- 当前已有至少一个完成节点

#### `暂停`

启用条件:

- `HasActiveGraph == true`
- `HasChatExecutionLease == true`

#### `取消`

启用条件:

- `HasActiveGraph == true`

#### `分离`

启用条件:

- `HasActiveGraph == true`

含义:

- 只把图从当前 Chat UI 分离
- 不等于删除图
- 不等于取消底层执行

### 11.3 首版不做的动作

- 节点级重试按钮
- 节点级改策略按钮
- 节点级跳过按钮
- 图内手工改依赖

这些交互应该保留在独立 TaskGraph 工作区。

---

## 12. 模板入口设计

### 12.1 入口形式

模板入口建议使用菜单按钮，而不是一排很多按钮。

建议文案:

- 主按钮: `模板编排`
- 下拉项:
  - `任务列表`
  - `复杂功能开发`
  - `Bug 列表`

### 12.2 首版模板输入

首版不强制新开复杂窗口，可优先使用:

- 当前 DraftText 作为模板原始输入

如果需要更明确 UX，可后续新增:

- `TemplateInputDialog`

首版建议保持简单，避免 UI 分支过多。

---

## 13. 自动编排入口设计

### 13.1 按钮文案

建议使用:

- `智能编排`

不建议:

- `自动任务图`
- `开始编排引擎`

前者更符合用户语言。

### 13.2 按钮行为

点击后:

1. 读取当前 DraftText
2. 若为空，不触发
3. 若当前已有 `ActiveGraph`，拒绝重复触发
4. 进入 `TriggerAutoTaskGraphAsync(...)`

### 13.3 触发后界面反馈

建议反馈顺序:

1. 面板出现
2. 图状态显示为 `执行中`
3. 普通发送按钮禁用
4. 若首节点为 `Inline` 或 `InSessionExecution`，显示“当前会话正在执行图任务”

---

## 14. 检查点 UI

### 14.1 检查点不是弹窗优先

首版不建议把检查点默认做成 modal dialog。

原因:

- 当前产品主场景是 Chat
- 检查点决策本质上是对当前会话的控制动作
- 弹窗会打断消息流和上下文阅读

推荐:

- 在活动图面板内原位显示检查点提示与动作

### 14.2 检查点展示内容

必须显示:

- 触发检查点的节点
- 当前原因
- 推荐动作
- 操作按钮

示例:

```text
检查点 · 分析 Bug 5
原因: 当前信息不足，建议先汇总已完成结果或继续等待补充。
[继续] [汇总] [取消]
```

### 14.3 用户输入类检查点

若图进入 `WaitingForInput`:

- 面板内显示待决策项列表
- 聊天输入框可继续编辑
- 允许用户通过聊天继续提供补充信息

---

## 15. 消息流中的内容策略

### 15.1 消息流不显示全量图日志

首版消息流中只建议加入:

- 图启动摘要
- 图完成摘要
- 用户显式要求汇总时的汇总消息

不建议加入:

- 每节点一条系统消息
- 每个 chunk 一条消息
- 每个检查点都塞一条长消息

### 15.2 面板与消息流分工

分工应固定:

- 面板负责“过程”
- 消息流负责“阶段性结论”

这样界面不会变成一整页日志。

---

## 16. 样式方案

### 16.1 全局样式位置

样式放在:

- `src/AgentOrchestrator.App/App.axaml`

### 16.2 样式命名建议

建议新增以下选择器:

```xml
<Style Selector="Border.taskgraph-conversation-panel" />
<Style Selector="TextBlock.taskgraph-panel-title" />
<Style Selector="Border.taskgraph-state-badge" />
<Style Selector="Border.taskgraph-state-badge.running" />
<Style Selector="Border.taskgraph-state-badge.waiting" />
<Style Selector="Border.taskgraph-state-badge.completed" />
<Style Selector="Border.taskgraph-state-badge.failed" />
<Style Selector="Border.taskgraph-node-row" />
<Style Selector="Border.taskgraph-decision-strip" />
```

### 16.3 视觉风格建议

采用:

- 中性色底
- 单层边框
- 紧凑间距
- 小号状态胶囊

不建议:

- 大圆角营销卡片
- 多层嵌套 card
- 过度动画

原因:

- 当前产品是工作台式工具，不是展示页

---

## 17. 可复用现有能力

### 17.1 可直接复用

- `ChatWorkspaceViewModel` 中已有的活动图相关属性和命令
- `TaskGraphRuntimeHub` 的运行时事件
- `TaskGraph` / `TaskNode` 状态字段
- 现有独立工作区中的状态语义

### 17.2 可以部分借鉴但不直接搬运

- 独立 `TaskGraphWorkspaceControl` 的节点视觉
- 侧栏或详情窗中的信息组织方式

不建议直接复用:

- 独立工作区的完整 DAG 画布

---

## 18. 关键 ViewModel 绑定清单

### 18.1 面板级绑定

面板至少需要绑定:

- `HasActiveGraph`
- `ActiveGraph.Name`
- `ActiveGraph.ExecutionState`
- `ActiveGraph.IsCheckpointPending`
- `ActiveExecutionContext.CurrentRunningNodeId`
- `ActiveExecutionContext.GraphSummary`
- `ActiveExecutionContext.RecentCompletedNodes`
- `ActiveExecutionContext.RecentFailedNodes`
- `ActiveExecutionContext.PendingDecisions`
- `HasChatExecutionLease`

### 18.2 动作绑定

- `PauseActiveGraphCommand`
- `CancelActiveGraphCommand`
- `ResumeContinueGraphCommand`
- `ResumeSummarizeGraphCommand`
- `ExecuteDetachActiveGraphCommand`

### 18.3 Composer 绑定

- `CanTriggerAutoGraph`
- `HasChatExecutionLease`
- `CanInjectTaskGraphContext`

---

## 19. 开发拆分建议

### 阶段 1: 结构接入

1. 在 `ChatWorkspaceControl.axaml` 中接入活动图面板区域
2. 面板先只显示标题、状态和动作按钮
3. 不做样式细化

### 阶段 2: 摘要视图

1. 接入最近完成 / 最近失败 / 待决策
2. 接入图摘要
3. 调整布局和滚动行为

### 阶段 3: 模板与自动入口

1. 加 `智能编排`
2. 加 `模板编排`
3. 完成与 DraftText 的交互

### 阶段 4: 视觉完善

1. 补齐样式
2. 调整状态胶囊
3. 优化空状态与检查点状态提示

---

## 20. 首版验收标准

满足以下条件即可认为 UI 首版可用:

1. 用户能在 Chat 中启动自动编排或模板编排
2. 启动后，活动图面板稳定显示
3. 面板能显示当前图名、图状态、当前节点
4. 面板能显示最近完成、最近失败、待决策摘要
5. 用户能在检查点上通过面板继续 / 汇总 / 暂停 / 取消
6. 图执行期间普通发送入口受到正确限制
7. 图完成或取消后，面板状态与租约状态同步收敛

---

## 21. 风险与缓解

### 21.1 面板过高挤压消息流

缓解:

- 限制最大高度
- 内部滚动
- 默认展示聚合摘要而非全量内容

### 21.2 动作入口过多导致困惑

缓解:

- 首版固定 5 个动作
- 不开放节点级复杂操作

### 21.3 用户误以为消息流就是图日志

缓解:

- 明确把图过程放在面板中
- 消息流只保留阶段性总结

### 21.4 独立工作区与 Chat 内体验割裂

缓解:

- 两者复用相同的状态语义
- Chat 内展示聚合视图，工作区保留详细视图

---

## 22. 最终落地结论

TaskGraph 对话内 Orchestrator 的 UI 首版采用以下路线:

1. 以 Chat 为主界面承载编排体验
2. 在 Chat Header 与消息流之间放置活动图面板
3. 面板展示聚合状态、检查点和控制动作
4. 消息流只展示阶段性结论，不承担全量运行日志职责
5. 独立 TaskGraph 工作区继续保留，负责高级编辑和详细查看

这一路线能在不把 Chat 变成复杂 DAG 编辑器的前提下，把“当前会话是控制面”的产品体验真正做出来，并保持首版 UI 可实现、可维护、可逐步增强。

