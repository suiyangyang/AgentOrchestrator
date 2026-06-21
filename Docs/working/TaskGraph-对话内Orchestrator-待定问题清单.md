# TaskGraph 对话内 Orchestrator 待定问题清单

> 状态: 待定问题清单
> 日期: 2026-06-21
> 适用范围: Chat 内嵌 TaskGraph / 当前会话作为 Orchestrator 的实现设计

---

## 1. 目的

本清单用于收敛“当前会话作为 TaskGraph Orchestrator”在实现前必须明确的问题。

这里不讨论是否接入 OpenCode subagent。该方向已排除。当前设计前提为:

- TaskGraph 仍由应用层执行器负责实际调度与节点执行
- 当前 chat session 负责发起、消费结果、下达继续决策
- `NewSession` / `ChildSession` / `InSessionExecution` / `Inline` 为唯一执行路径

本清单按两个层级组织:

1. **必须先定**: 不先定会导致核心架构不稳，或代码落下去后很难回收
2. **可以后定**: 不影响首版落地，但需要保留扩展位

---

## 2. 必须先定

### 2.1 当前会话与执行器的分权模型

必须先明确:

- 当前会话负责哪些决策
- `TaskGraphExecutor` 负责哪些执行动作

建议边界:

- 当前会话负责:
  - 生成图
  - 决定是否启动图执行
  - 决定是否继续 / 暂停 / 跳过 / 汇总
  - 消费节点结果摘要
  - 在检查点上做下一步决策

- 执行器负责:
  - 根据图和策略选择执行路径
  - 创建 session / child session
  - 发送 prompt
  - 收集节点结果
  - 更新图状态
  - 触发运行时事件

必须避免的问题:

- 当前会话和执行器都能修改图推进状态
- 当前会话和执行器都能独立决定下一节点

结论要求:

- 需要形成一份明确的控制权定义
- 需要指定唯一的状态真相来源

---

### 2.2 执行器是否从“一次跑到底”改为“可交互状态机”

当前 `TaskGraphExecutor` 是一次性 `ExecuteAsync(...)` 模式。

如果当前会话要真正参与过程控制，必须先定以下问题:

- 是否允许每执行一个节点后停下来
- 是否允许在特定节点类型后停下来
- 是否允许等待当前会话继续命令
- 是否允许在失败后等待当前会话决定重试或跳过

建议方向:

- 首版采用**检查点驱动**
- 不做持续高频会话内干预

建议检查点:

- 节点完成后
- 节点失败后
- 图进入等待输入状态后
- 图发生受控扩图后

结论要求:

- 需要确定首版是“跑到底”还是“检查点推进”
- 如果采用检查点推进，需要明确执行器公开哪些控制接口

---

### 2.3 会话级上下文桥接层的数据模型

如果当前会话要“真正看到结果”，需要定义会话级执行上下文。

建议命名:

- `ConversationExecutionContext`
  或
- `ChatOrchestrationContext`

必须先定最小字段集。

建议最小字段:

```csharp
public sealed class ConversationExecutionContext
{
    public string ConversationSessionId { get; init; } = string.Empty;
    public string GraphId { get; init; } = string.Empty;
    public bool HasExecutionLease { get; set; }
    public string? CurrentRunningNodeId { get; set; }
    public List<NodeSummarySnapshot> RecentCompletedNodes { get; } = [];
    public List<NodeFailureSnapshot> RecentFailedNodes { get; } = [];
    public List<PendingDecisionSnapshot> PendingDecisions { get; } = [];
    public List<GraphMutationSnapshot> RecentMutations { get; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
```

必须先定的问题:

- 它存聚合视图，还是存完整事件历史
- 它是否与 `GraphId` 一一绑定
- 一个会话是否允许同时挂多个图

首版建议:

- 一个当前会话同一时间只绑定一个 `ActiveGraph`
- 上下文层保存“最近状态 + 少量近期摘要”，不保存全量历史

---

### 2.4 哪些结果回灌给当前会话

必须先定义回灌规则，否则当前会话很快会被日志淹没。

建议“必须回灌”内容:

- 节点完成摘要
- 节点失败摘要
- 待人工决策项
- 当前阻塞原因
- 关键文件变更摘要
- 图结构追加摘要

建议“不回灌”内容:

- 全量 chunk
- 大段原始模型输出
- 无结论价值的中间工具日志
- 重复性状态心跳

结论要求:

- 需要形成一张“节点结果 -> 是否回灌 -> 以什么形式回灌”的映射表

---

### 2.5 回灌时机

不仅要定义“回灌什么”，还要定义“什么时候回灌”。

必须先定的候选时机:

1. 节点完成时
2. 节点失败时
3. 图进入等待输入时
4. 用户下一次向当前会话发送消息前

首版建议:

- 运行中只更新 `ConversationExecutionContext`
- 在以下时机做 prompt 注入:
  - 当前会话准备继续决策前
  - 图等待输入时
  - 用户明确询问当前图状态时

这样可以避免把所有运行细节都写进 chat 历史。

---

### 2.6 当前会话如何消费这些结果

这是最容易含糊但最关键的一项。

“当前会话看到结果”不能只等于 UI 看到了。

必须先定:

- 结果是否进入真正的会话消息历史
- 结果是否只进入隐藏执行上下文
- 后续 prompt 如何自动注入这些上下文

首版建议:

- 不把大部分节点结果直接写成 chat 消息
- 默认只进入 `ConversationExecutionContext`
- 在当前会话下一次参与编排决策时，将聚合后的上下文注入 prompt

这样可以做到:

- 当前会话可消费图结果
- chat 历史不被节点日志污染

---

### claude prompt 注入策略

必须先限制注入规模，否则上下文会迅速膨胀。

建议注入结构:

```text
Current TaskGraph execution context:
- Active graph: ...
- Running node: ...
- Recently completed:
  - ...
  - ...
- Recently failed:
  - ...
- Pending decisions:
  - ...
- Suggested next action:
  - ...
```

必须先定:

- 注入最近几条节点摘要
- 多长的摘要会被截断
- 是否保留旧结果的聚合 summary

首版建议:

- 仅注入最近 3 个完成节点
- 仅注入最近 2 个失败节点
- 保留 1 个图级总体摘要

---

### 2.8 chat 写入权控制

一旦图以 `InSessionExecution` 或 `Inline` 路径运行，必须先解决写入竞争。

必须先定:

- 当前会话执行图时，用户普通消息是否允许继续发送
- 当前会话自动消息与图执行消息如何串行
- graph panel 操作是否能触发新的消息写入

建议方案:

- 引入 `ChatExecutionLease`
- 图执行期间由执行器占用当前会话写入权
- 普通消息入口禁用，直到当前检查点结束或图执行结束

注意:

- 这是首版最关键的运行时保护之一

---

### 2.9 状态真相来源

需要明确 3 套对象之间的关系:

1. `TaskGraph` 图和节点状态
2. `TaskGraphRuntimeHub` 事件流
3. `ConversationExecutionContext`

建议定义:

- `TaskGraph` 状态是执行真相
- `RuntimeHub` 是通知通道
- `ConversationExecutionContext` 是当前会话消费用投影

必须避免:

- 用会话上下文反向覆盖图状态
- 用 UI 临时状态充当图执行真相

---

### 2.10 首版是否允许执行中动态决策

需要先决定首版交互强度。

有两个路线:

#### 路线 A: 跑到底

- 用户发起后，图尽量自己跑完
- 当前会话只在开始和结束时参与

优点:

- 改动小
- 更贴近当前实现

缺点:

- 当前会话不是真正 orchestrator

#### 路线 B: 检查点式执行

- 图执行到检查点后暂停
- 当前会话消费结果并给出下一步决策
- 执行器再继续

优点:

- 更接近真正的对话内 orchestrator

缺点:

- 执行器和会话状态机都要改

首版建议:

- 选路线 B，但只做低频检查点，不做实时连续干预

---

## 3. 可以后定

### 3.1 节点结果是否写入真正 chat 历史

这件事会显著影响体验，但不必在首版全做。

可选方向:

- 只写关键事件
- 完全不写，只做隐藏上下文
- 允许用户切换“详细日志写入 chat”

首版建议:

- 默认不写入正式 chat 历史

---

### 3.2 批量图的汇总节奏

例如 bug list:

- 每个 bug 完成一次就回灌
- 每 3 个 bug 汇总一次
- 整批结束后一次性汇总

首版建议:

- 单 bug 完成后更新图状态
- 每 3 个 bug 或每个阶段结束时回灌一次聚合摘要

---

### 3.3 图结构变更的回灌形式

如果支持受控扩图，后续需要决定:

- 是给当前会话一条新增节点摘要
- 还是给它一个新的计划概览

首版建议:

- 只回灌“新增了哪些节点、原因是什么”

---

### 3.4 失败分类体系

后续可细分失败类型:

- 环境失败
- 权限失败
- 结构失败
- 推理失败
- 验证失败

首版建议:

- 先区分:
  - 可自动重试
  - 需人工决策
  - 不可恢复

---

### 3.5 会话切换与恢复

后续需要考虑:

- 用户切换到别的 chat 会怎样
- 关闭应用后回来如何恢复
- `ConversationExecutionContext` 是否持久化

首版建议:

- 先只保证单次会话内可用
- 关闭或切换后通过图状态重建最小上下文

---

### 3.6 多图并存

后续需要考虑:

- 一个当前会话是否能同时挂多张图
- 不同图的上下文如何隔离

首版建议:

- 同一当前会话同时只允许一个 `ActiveGraph`

---

## 4. 推荐的首版落地决策

为降低复杂度，建议首版直接采用以下组合:

1. **当前会话是控制面，执行器是执行面**
2. **执行器改为检查点式推进，不做持续高频互动**
3. **引入 `ConversationExecutionContext`**
4. **默认只把聚合结果注入隐藏上下文，不写满 chat 历史**
5. **引入 `ChatExecutionLease`，保证同一 session 写入串行**
6. **同一当前会话同时只允许一个 `ActiveGraph`**

这套组合可以让“当前会话作为 orchestrator”先成立，同时把实现复杂度控制在可落地范围内。

---

## 5. 建议的下一步产出

在本清单基础上，建议继续产出以下两份文档:

1. **对话内 Orchestrator 最小架构方案**
   - 明确控制面 / 执行面 / 上下文桥接层

2. **ConversationExecutionContext 数据契约**
   - 明确字段、更新规则、注入规则、回收规则

这两份文档完成后，再进入具体代码设计，会比直接改执行器稳很多。

