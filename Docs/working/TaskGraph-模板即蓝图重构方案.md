# TaskGraph 模板即蓝图重构方案

> **状态：已实施（2026-07）。** 全部 6 个 Phase 已完成并通过验收：
> 71 个 xUnit 测试全过、`dotnet build` 0 错误。
>
> 实施顺序：Phase 1（模型铺底）→ Phase 2（存储统一）→ Phase 3（统一画布）
> → Phase 4（实例化链路）→ Phase 5（Chat 打通）→ Phase 6（清理旧体系）。
>
> 实施后状态：
> - `TaskTemplate` / `ITaskTemplateStore` / `JsonTaskTemplateStore` /
>   `TaskTemplateListItem` / `TaskTemplateItemViewModel` 五个文件已删除
> - `TaskTemplateMigrationService` 改为用 `JsonDocument` 直接读取旧 JSON
> - `ITaskGraphTemplateInstantiator` 新增，承载模板 → 运行图的核心转换
> - 4 个内置模板：3 个用户可见（`builtin.task-list` / `builtin.feature-dev` /
>   `builtin.bug-list`）+ 1 个隐藏（`builtin.auto-orchestration`，供 Chat
>   自动编排使用）
>
> 本文件保留为设计记录。**不要把本文件当作"待办事项"参考 —— 实施已
> 全部完成。** 当前设计请参考 `Docs/developer/project.md` 与
> `Docs/developer/architecture.md`。
>
> 原始计划：

---

## 1. 文档目的

本文给出一份可直接进入开发的正式重构方案，用于解决当前系统中：

- 模板展示形态与任务图展示形态不一致
- 模板只是轻量元数据，无法直接表达图结构
- 动态编排规则散落在代码和说明文字中，不是图的一部分

本文要求做到：

1. 领域模型清晰
2. 数据迁移路径明确
3. UI 改造范围可控
4. 小模型可按阶段逐项执行

---

## 2. 当前问题

### 2.1 当前实现

当前系统中：

- `TaskTemplate` 是独立模型
- `TaskTemplate` 只保存：
  - `Name`
  - `Description`
  - `BaseKind`
  - `DefaultInput`
  - `IsBuiltIn`
- `TaskGraph` 才是真正的图结构和执行实体

当前模板页右侧主要是：

- 名称
- 说明
- 骨架类型
- 默认输入
- 基于模板生成任务图

当前任务图页右侧则是：

- 图画布
- 节点
- 连线
- 执行状态
- 执行控制

### 2.2 症结

问题不只是 UI 不统一，而是领域定义先把它们拆成了两类完全不同的对象：

- 模板像“表单资产”
- 任务图像“图执行对象”

这会带来以下后果：

1. 用户无法把模板理解为“蓝图”
2. 模板无法直接表达固定节点、扩展锚点、动态生成规则
3. 模板到任务图需要额外的投影/翻译层
4. 后续难以支持“从任务图提炼模板”

---

## 3. 重构目标

### 3.1 核心目标

本次重构只做一件事：

**把模板定义为一种特殊的 `TaskGraph` 蓝图。**

即：

- 模板本身就是图
- 模板与任务图使用同一套画布语言
- 模板不可直接执行
- 模板通过图内元数据定义动态编排规则

### 3.2 重构后的统一心智

用户应当看到：

`模板图 -> 实例化 -> 任务图实例 -> 执行`

而不是：

`模板表单 -> 任务图`

### 3.3 非目标

本阶段明确不做：

- 任意运行时自我改写图结构
- 图模板 DSL 独立语言
- 多模板嵌套继承
- 模板市场/远程模板仓库
- 完整版本化 diff 系统

---

## 4. 重构后的领域模型

## 4.1 总体思路

取消“模板是轻量元数据对象、任务图是图对象”的设计。

统一为：

- **BlueprintGraph**：模板图，底层仍然是 `TaskGraph`
- **RuntimeGraph**：运行图，底层仍然是 `TaskGraph`

二者的区别不在“是不是图”，而在图的用途与状态字段：

- BlueprintGraph：用于生成，不执行
- RuntimeGraph：用于执行，可持久化执行状态

### 4.2 推荐做法

不要新建 `BlueprintGraph` 类。

推荐直接在现有 `TaskGraph` 上增加图级类型字段：

```csharp
public enum TaskGraphDocumentKind
{
    Runtime = 0,
    Template = 1,
}
```

然后在 `TaskGraph` 上新增：

```csharp
public TaskGraphDocumentKind DocumentKind { get; set; }
```

这样：

- 模板和任务图仍共用一套模型
- 存储、画布、节点、边、工具栏可以最大化复用
- 后续“模板实例化为任务图”只需要复制图并清洗运行态字段

---

## 5. 数据模型设计

## 5.1 `TaskGraph` 新增字段

在 [src/AgentOrchestrator.App/Models/TaskGraph/TaskGraph.cs](E:/Work/Code/Tools/AgentOrchestrator/src/AgentOrchestrator.App/Models/TaskGraph/TaskGraph.cs) 中新增：

```csharp
public TaskGraphDocumentKind DocumentKind { get; set; } = TaskGraphDocumentKind.Runtime;
public string? TemplateNotes { get; set; }
public string? TemplatePlannerPrompt { get; set; }
public string? BasedOnTemplateId { get; set; }
public bool IsBuiltInTemplate { get; set; }
```

字段语义：

- `DocumentKind`
  - 区分当前图是模板还是运行实例
- `TemplateNotes`
  - 模板说明文本，面向人和 Agent
- `TemplatePlannerPrompt`
  - 结构化规划时附加给 Agent 的模板提示
- `BasedOnTemplateId`
  - 运行图来源模板 id
- `IsBuiltInTemplate`
  - 标记官方模板

## 5.2 `TaskGraph` 新增模板规则对象

新增文件：

- `src/AgentOrchestrator.App/Models/TaskGraph/TaskGraphTemplateMetadata.cs`

定义：

```csharp
public sealed class TaskGraphTemplateMetadata
{
    public bool AllowDynamicExpansion { get; set; }
    public string? ExpansionEntryNodeId { get; set; }
    public string? ExpansionTerminalNodeId { get; set; }
    public List<string> FixedNodeIds { get; set; } = [];
    public List<string> FixedEdgeKeys { get; set; } = [];
    public List<DynamicZoneDefinition> DynamicZones { get; set; } = [];
    public List<string> NodeGenerationRules { get; set; } = [];
    public List<string> EdgeGenerationRules { get; set; } = [];
    public List<string> ExecutionRules { get; set; } = [];
}
```

并在 `TaskGraph` 上新增：

```csharp
public TaskGraphTemplateMetadata? TemplateMetadata { get; set; }
```

## 5.3 动态区域模型

新增文件：

- `src/AgentOrchestrator.App/Models/TaskGraph/DynamicZoneDefinition.cs`

建议定义：

```csharp
public sealed class DynamicZoneDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string AnchorNodeId { get; set; } = string.Empty;
    public string? InsertAfterNodeId { get; set; }
    public string? ConnectToTerminalNodeId { get; set; }
    public bool AllowParallelNodes { get; set; }
    public int MaxGeneratedNodeCount { get; set; } = 12;
    public string GenerationInstruction { get; set; } = string.Empty;
}
```

用途：

- 明确哪个区域允许动态生成
- 明确扩图挂载点
- 明确生成规模边界
- 明确生成节点的原则

## 5.4 节点级模板标记

在 [src/AgentOrchestrator.App/Models/TaskGraph/TaskNode.cs](E:/Work/Code/Tools/AgentOrchestrator/src/AgentOrchestrator.App/Models/TaskGraph/TaskNode.cs) 新增：

```csharp
public bool IsTemplateLocked { get; set; }
public bool IsDynamicPlaceholder { get; set; }
public string? TemplateRole { get; set; }
```

字段语义：

- `IsTemplateLocked`
  - 模板中的固定节点，不应在实例化时自动删除
- `IsDynamicPlaceholder`
  - 表示该节点是动态扩展占位节点/扩展锚点
- `TemplateRole`
  - 例如：
    - `Input`
    - `Plan`
    - `Checkpoint`
    - `ExpansionAnchor`
    - `ExpansionTerminal`
    - `Report`

---

## 6. 要删除或废弃的旧模型

## 6.1 `TaskTemplate` 的处理策略

当前 `TaskTemplate` 不应立即删除，而应分两阶段处理。

### 阶段 A

保留 `TaskTemplate` 读取能力，仅用于迁移旧数据。

### 阶段 B

当模板图存储稳定后：

- 停止在业务层使用 `TaskTemplate`
- `TaskTemplateStore` 改为兼容旧数据导入器
- 新模板统一存为 `DocumentKind = Template` 的 `TaskGraph`

### 结论

`TaskTemplate` 的最终定位不是长期主模型，而是：

- 旧模板兼容输入格式
- 一次性迁移来源

---

## 7. 存储层重构

## 7.1 总体策略

当前有两套存储：

- `ITaskTemplateStore`
- `ITaskGraphStore`

重构后应统一到：

- `ITaskGraphStore`

并通过 `DocumentKind` 区分模板图与运行图。

## 7.2 接口改造

在 `ITaskGraphStore` 增加：

```csharp
Task<IReadOnlyList<TaskGraphListItem>> ListTemplatesAsync(CancellationToken ct = default);
Task<IReadOnlyList<TaskGraphListItem>> ListRuntimeGraphsAsync(CancellationToken ct = default);
Task<TaskGraph?> LoadTemplateAsync(string id, CancellationToken ct = default);
Task<TaskGraph> InstantiateTemplateAsync(string templateId, TemplateInstantiationOptions options, CancellationToken ct = default);
```

新增：

- `TemplateInstantiationOptions.cs`

建议定义：

```csharp
public sealed class TemplateInstantiationOptions
{
    public string? RuntimeGraphName { get; set; }
    public string? UserInput { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
}
```

## 7.3 持久化目录策略

建议统一落在：

- `%LOCALAPPDATA%/AgentOrchestrator/Datas/TaskGraphs`

不再单独使用：

- `%LOCALAPPDATA%/AgentOrchestrator/Templates`

推荐文件命名增加前缀：

- `template.{id}.json`
- `runtime.{id}.json`

也可只保留单目录单格式，靠 `DocumentKind` 区分。

## 7.4 旧模板迁移

新增迁移器：

- `TaskTemplateMigrationService`

职责：

1. 扫描旧 `Templates/*.json`
2. 读取 `TaskTemplate`
3. 转换为模板图 `TaskGraph`
4. 保存到 `TaskGraphStore`
5. 成功后将旧模板文件移动到：
   - `Templates/_migrated/`

---

## 8. 模板实例化规则

## 8.1 基本原则

模板实例化不是“重新生成一张完全新的图”，而是：

1. 复制模板图
2. 转成 `DocumentKind = Runtime`
3. 清空运行态字段
4. 根据模板元数据执行受控扩图

## 8.2 运行态清洗字段

实例化时必须清空：

- `ExecutionState`
- `ExecutionStartedAt`
- `ExecutionCompletedAt`
- `ConversationSessionId`
- `IsCheckpointPending`
- `ActiveCheckpointNodeId`

每个节点上必须清空：

- `Status`
- `AgentSessionId`
- `AttemptCount`
- `LastError`
- `OutputSummary`
- `RawOutput`
- `StartedAt`
- `CompletedAt`
- `TouchedFiles`
- `ResultTags`
- `StructuredSummary`

## 8.3 动态扩图执行时机

首版只支持两种时机：

1. **实例化时扩图**
   - 根据用户输入生成动态节点
2. **运行中在指定锚点扩图**
   - 仅当模板元数据明确允许

不要在首版支持任意节点完成后自动自我改写整图。

## 8.4 动态扩图生成原则

扩图必须同时满足：

1. 只能在 `DynamicZones` 中声明的区域发生
2. 生成节点数不得超过区域限制
3. 新节点必须连接到指定锚点或终止点
4. 不允许破坏 `FixedNodeIds` 和 `FixedEdgeKeys`
5. 不允许产生环

---

## 9. UI 重构方案

## 9.1 总体目标

模板页与任务图页统一使用同一套图编辑界面。

区别只体现在：

- 是否可执行
- 是否显示运行态信息
- 是否显示模板规则编辑区

## 9.2 独立任务编排工作区

当前左侧分组仍保留：

- 模板
- 任务图

但右侧内容区不再分成：

- 模板详情表单
- 任务图概要

而统一为：

- 图画布区
- 下方属性/说明区

## 9.3 模板选中时的右侧结构

模板模式下右侧应显示：

1. 图画布
2. 模板基本信息
3. 模板规则区
4. 模板说明 Note
5. 基于此模板生成任务图

### 模板规则区必须包含

- 是否允许动态扩图
- 扩图锚点
- 扩图终止点
- 动态区域列表
- 节点生成规则
- 连线生成规则
- 执行注意事项

## 9.4 任务图选中时的右侧结构

任务图模式下右侧显示：

1. 图画布
2. 当前执行状态
3. 运行控制
4. 节点详情/报告/检查点

## 9.5 画布复用策略

不要为模板单独做一套假画布。

应直接复用当前：

- `TaskGraphWorkspaceViewModel`
- `TaskGraphWorkspaceControl`

通过模式切换控制：

- `IsTemplateDocument`
- `IsRuntimeDocument`

### 模板模式下要禁用的能力

- 执行
- 重试失败
- 取消执行
- 运行态状态显示
- 节点执行详情打开

### 模板模式下要新增的能力

- 编辑模板规则
- 标记固定节点
- 标记动态占位节点
- 标记模板角色
- 保存模板
- 实例化为任务图

---

## 10. ViewModel 重构方案

## 10.1 `TaskOrchestrationWorkspaceViewModel`

当前职责要调整为：

- 左侧资产导航
- 当前选中文档加载
- 在右侧承载统一图编辑工作区

不再自己维护两套完全不同的详情区模型。

### 建议删除或弱化的现有字段

- `TemplateDetailName`
- `TemplateDetailDescription`
- `TemplateDetailBaseKind`
- `TemplateDetailDefaultInput`
- `TemplateGenerationInput`
- `SelectedTaskGraphName`
- `SelectedTaskGraphStatusText`

这些应迁移到统一的“当前文档编辑上下文”。

## 10.2 新增统一文档编辑上下文

新增文件：

- `TaskGraphDocumentEditorViewModel.cs`

职责：

- 承载当前选中的模板图或运行图
- 统一暴露画布、节点、边、标题、说明、模板规则、运行状态

建议字段：

```csharp
public TaskGraph? CurrentDocument { get; }
public bool IsTemplateDocument { get; }
public bool IsRuntimeDocument { get; }
public string NotesDraft { get; set; }
public bool AllowDynamicExpansion { get; set; }
public ObservableCollection<DynamicZoneDefinitionViewModel> DynamicZones { get; }
```

## 10.3 `TaskGraphWorkspaceViewModel`

当前 `TaskGraphWorkspaceViewModel` 应保留为画布核心 VM，
但要补充“文档模式”概念，而不是默认只服务运行图。

新增：

```csharp
public bool IsTemplateDocument => CurrentGraph?.DocumentKind == TaskGraphDocumentKind.Template;
public bool IsRuntimeDocument => CurrentGraph?.DocumentKind == TaskGraphDocumentKind.Runtime;
```

然后将按钮可用性改为基于模式判断。

---

## 11. 服务层改造

## 11.1 新增模板实例化服务

新增：

- `ITaskGraphTemplateInstantiator`
- `TaskGraphTemplateInstantiator`

职责：

1. 复制模板图
2. 清洗运行态字段
3. 应用用户输入
4. 按模板元数据做受控扩图
5. 返回运行图

## 11.2 扩图逻辑迁移

当前动态扩图逻辑部分在：

- `TaskGraphTemplateBuilder`
- `TaskGraphDynamicExpander`

重构后建议拆分为：

1. **模板图构造**
   - 只负责生成官方内置模板图
2. **模板实例化扩图**
   - 根据模板图元数据进行扩图
3. **运行中二次扩图**
   - 根据模板图中的动态区规则做有限扩展

## 11.3 官方模板生成器

把当前内置模板生成逻辑改为生成：

- `DocumentKind = Template`
- 含 `TemplateMetadata`
- 含 `TemplateNotes`

也就是说：

- `BuildTaskListGraph(...)` 不再直接理解为运行图
- 而是优先生成“任务列表模板图”

必要时再单独提供：

- `BuildRuntimeGraphFromTemplate(...)`

---

## 12. 官方模板落地要求

## 12.1 任务列表模板

固定部分：

- 输入说明

动态部分：

- 根据每行任务生成串行执行节点链

规则要求：

- 每行对应一个 `Execute` 节点
- 自动串行连接
- 禁止生成环

## 12.2 功能开发模板

固定部分：

- 需求输入
- 方案生成
- 用户确认
- 开发计划生成
- 执行收尾

动态部分：

- 在“开发计划生成”之后插入开发执行节点

规则要求：

- 动态节点必须挂在计划节点之后
- 最终必须回连到收尾节点
- 动态节点类型默认 `Execute/Verify`

## 12.3 Bug 列表模板

固定部分：

- 汇总报告节点

动态部分：

- 每个 bug 生成一组分析/决策/修复/验证节点

规则要求：

- 每个 bug 的子链必须闭合到最终报告
- 不允许跨 bug 链无规则串联

---

## 13. Chat 侧改造

## 13.1 使用模板

Chat 中 `使用模板` 的语义改为：

1. 选择一个模板图
2. 输入本次实例化输入
3. 生成运行图
4. 切入对话内编排或独立任务图页

## 13.2 自动编排

`自动编排` 不依赖模板列表项，但其内部可以使用一个隐藏的系统模板：

- `builtin.auto-orchestration`

这样自动编排和模板编排最终走同一条“模板实例化”主链路。

---

## 14. 文件改造清单

## 14.1 必改文件

- `src/AgentOrchestrator.App/Models/TaskGraph/TaskGraph.cs`
- `src/AgentOrchestrator.App/Models/TaskGraph/TaskNode.cs`
- `src/AgentOrchestrator.App/ViewModels/TaskGraphWorkspaceViewModel.cs`
- `src/AgentOrchestrator.App/ViewModels/TaskOrchestrationWorkspaceViewModel.cs`
- `src/AgentOrchestrator.App/Controls/TaskOrchestrationWorkspaceControl.axaml`
- `src/AgentOrchestrator.App/Services/TaskGraph/ITaskGraphStore.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/JsonTaskGraphStore.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/TaskGraphTemplateBuilder.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/TaskGraphDynamicExpander.cs`

## 14.2 新增文件

- `src/AgentOrchestrator.App/Models/TaskGraph/TaskGraphDocumentKind.cs`
- `src/AgentOrchestrator.App/Models/TaskGraph/TaskGraphTemplateMetadata.cs`
- `src/AgentOrchestrator.App/Models/TaskGraph/DynamicZoneDefinition.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/ITaskGraphTemplateInstantiator.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/TaskGraphTemplateInstantiator.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/TaskTemplateMigrationService.cs`
- `src/AgentOrchestrator.App/ViewModels/TaskGraphDocumentEditorViewModel.cs`

## 14.3 后续可删文件

- `src/AgentOrchestrator.App/Models/TaskGraph/TaskTemplate.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/ITaskTemplateStore.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/JsonTaskTemplateStore.cs`
- `src/AgentOrchestrator.App/Services/TaskGraph/TaskTemplateListItem.cs`
- `src/AgentOrchestrator.App/ViewModels/TaskTemplateItemViewModel.cs`

注意：

这些文件只在旧数据迁移完成且 UI 已完全切走后再删除。

---

## 15. 分阶段实施计划

## Phase 1: 模型铺底

目标：

- 让 `TaskGraph` 能表达模板图

子任务：

1. 新增 `TaskGraphDocumentKind`
2. 新增 `TaskGraphTemplateMetadata`
3. 新增 `DynamicZoneDefinition`
4. 给 `TaskGraph` / `TaskNode` 增加模板相关字段
5. 保证 JSON 可正常序列化/反序列化

验收：

- 能创建一个 `DocumentKind = Template` 的 `TaskGraph`
- 本地保存后再次读取字段不丢失

## Phase 2: 存储统一

目标：

- 模板和运行图共用 `TaskGraphStore`

子任务：

1. 扩展 `ITaskGraphStore`
2. 在 `JsonTaskGraphStore` 中支持按 `DocumentKind` 查询
3. 新增旧模板迁移器
4. 将内置模板转成模板图种子

验收：

- 启动后能列出模板图
- 能列出运行图
- 旧模板可迁移成功

## Phase 3: 统一右侧画布

目标：

- 模板页和任务图页都显示同一套图编辑画布

子任务：

1. 让 `TaskGraphWorkspaceViewModel` 支持模板模式
2. 让模板模式禁用执行态按钮
3. 在模板模式显示模板规则区和 Note
4. 替换当前模板详情表单

验收：

- 点击模板和任务图，右侧都进入图形编辑视图
- 模板图不可执行
- 任务图可执行

## Phase 4: 模板实例化链路

目标：

- 模板图可直接实例化为运行图

子任务：

1. 新增模板实例化服务
2. 清洗运行态字段
3. 按动态区规则受控扩图
4. 写回 `BasedOnTemplateId`

验收：

- 可从模板图生成运行图
- 运行图不污染模板图
- 动态节点生成位置符合规则

## Phase 5: Chat 打通

目标：

- Chat 中“使用模板”走统一模板图链路

子任务：

1. 模板选择改为模板图列表
2. 生成运行图后进入 Chat 编排
3. 自动编排复用统一实例化机制

验收：

- Chat 可使用模板图发起编排
- 独立工作区与 Chat 链路一致

## Phase 6: 清理旧模板体系

目标：

- 业务层不再依赖旧 `TaskTemplate`

子任务：

1. 删除旧模板表单逻辑
2. 删除旧模板存储写路径
3. 保留只读迁移兼容或彻底移除

验收：

- 新建、编辑、复制、实例化模板均只基于 `TaskGraph`

---

## 16. 小模型执行指引

本节是给小模型直接执行用的，按顺序做，不允许跳阶段。

### 16.1 执行规则

1. 一次只做一个 Phase
2. 每个 Phase 完成后先编译
3. 编译通过后再做下一个 Phase
4. 不要在同一个提交里同时做“模型重构 + UI 全改 + Chat 改造”

### 16.2 每阶段标准动作

1. 读本方案对应章节
2. 列出本阶段文件清单
3. 只修改该清单内文件
4. 执行：

```bash
dotnet build -c Debug
dotnet test -c Debug --no-build
```

5. 记录：
   - 已完成项
   - 未完成项
   - 阻塞项

### 16.3 小模型禁止事项

1. 不要把 `TemplateMetadata` 做成只有一大段字符串
2. 不要保留“模板详情表单”和“模板图画布”双入口长期并存
3. 不要在首版支持任意运行期改写整图
4. 不要直接删除旧 `TaskTemplate` 而不做迁移

---

## 17. 验收标准

当以下条件全部满足，才算本重构完成：

1. 模板与任务图使用同一套图展示语言
2. 模板本身就是 `TaskGraph`
3. 模板图可保存、复制、重命名、删除
4. 模板图不可直接执行
5. 模板图可实例化为运行图
6. 模板图可表达固定节点、动态区域、扩图规则和 Note
7. Chat 中“使用模板”走模板图实例化链路
8. 旧模板数据可迁移

---

## 18. 风险与注意事项

## 18.1 最大风险

最大风险不是代码量，而是中途出现“双模型并存太久”：

- 旧 `TaskTemplate` 继续写
- 新模板图也开始写

这样会导致后续逻辑长期分叉。

因此应尽快完成：

- 存储统一
- 模板右侧视图统一

## 18.2 次级风险

1. 模板规则做成纯文本，程序不可消费
2. 动态扩图规则过度复杂，首版不可控
3. `TaskGraphWorkspaceViewModel` 同时背负运行态与模板态后变得过重

对应策略：

1. 结构化元数据优先，Note 作为补充
2. 首版只支持受控动态区
3. 必要时拆 `TaskGraphDocumentEditorViewModel`

---

## 19. 结论

本方案的核心结论是：

**Template 不再被定义为“用于生成 TaskGraph 的外部资产”，而被定义为“不可直接执行、但可实例化为运行图的 TaskGraph 蓝图”。**

这是一次领域模型统一，而不只是 UI 统一。

它会带来：

1. 模板与任务图的形态统一
2. 动态编排规则进入图本身
3. 模板到运行图的链路更自然
4. 后续“从任务图提炼模板”成为自然能力

建议按本文的 6 个 Phase 顺序实施，不要跨阶段并行大改。
