# TaskGraph 改造计划 (v1.0)

> 状态: 方案已锁定,待执行
> 决策日期: 2026-06-16
> 范围: TaskGraph v1 的**实施计划** —— 阶段任务、文件清单、子任务、验证步骤、技术债
> 配套文档: `TaskGraph-方案.md`(架构/模型/UI/生成策略等设计内容,本文不重复)

---

## 0. 阅读指引

- 想了解**为什么这样做**:读 `TaskGraph-方案.md`
- 想了解**怎么落地**:读本文档
- 两者配套使用,本文档的所有架构决定以方案文档为唯一权威

---

## 1. 项目基线现状

| 项 | 当前状态 |
|---|---|
| `MainWindow` | 单一 `ChatWorkspaceControl` 占用,无导航 |
| `MainWindowViewModel` | 只有 `Chat` 一个属性 |
| Chat VM | 有 `Messages`/`Attachments`/`DraftText`/`Send` 等,v1 不依赖真实 `IChatTransport` |
| Chat Control 工具栏 | `Grid ColumnDefinitions="Auto,Auto,Auto,*,Auto,Auto"`(6 列) |
| DI | 极简,只注册 `MainWindowViewModel` + `MainWindow` |
| Models/Chat | `IChatBlock`/`IChatMessage`/`IChatTransport`/`ChatBlockKind{Text,Thought,Image,Tool}` 已就位 |
| TaskGraph 入口 | 需要先提供一个可切换到的占位工作页 |
| 项目成熟度 | **Disciplined** — 严格遵循 MVVM + CompiledBindings + 类选择器 Style |

**Codebase 评估结论**:Disciplined,所有改动需严格遵循现有风格。

---

## 2. 改动清单(完整文件级)

### 2.1 新增文件

#### Models/TaskGraph/

| 文件 | 内容 |
|---|---|
| `TaskNodeKind.cs` | `enum { Plan, Execute, Verify, Decision, Parallel, HumanInput }` |
| `TaskNodeStatus.cs` | `enum { Pending, Running, Completed, Failed, Skipped }` |
| `TaskNode.cs` | `ObservableObject`,节点 |
| `TaskEdge.cs` | 边 |
| `TaskGraph.cs` | `ObservableObject`,图(含 `RebuildEdges()`) |
| `NodePosition.cs` | `record struct (double X, double Y)` |
| `TaskGraphSource.cs` | `(Kind, Content, FilePath?)` |
| `TaskGraphExtractionResult.cs` | `Graph + Diagnostics + Strategy` |
| `TaskGraphExtractionOptions.cs` | `AllowLlmFallback / SystemPromptOverride` |
| `ExtractionDiagnostic.cs` | `(Message, Severity)` |
| `ExtractionStrategy.cs` | `enum` |
| `ExtractionSeverity.cs` | `enum` |
| `TaskGraphSourceKind.cs` | `enum { File, InlineText, ChatMessages }` |

#### Services/TaskGraph/

| 文件 | 内容 |
|---|---|
| `ITaskGraphGenerator.cs` | 文档/列表 → 图接口 |
| `DefaultTaskGraphGenerator.cs` | 默认实现(规则 + LLM 兜底) |
| `IChatTaskExtractor.cs` | Chat → 图接口 |
| `DefaultChatTaskExtractor.cs` | 默认实现 |
| `ITaskGraphLlmGateway.cs` | LLM 抽象 |
| `MockTaskGraphLlmGateway.cs` | v1 默认(关键词启发式) |
| `ProductionLlmTaskGraphGateway.cs` | v2 留口(调用 IChatTransport) |
| `ITaskGraphStore.cs` | 持久化接口 |
| `JsonTaskGraphStore.cs` | JSON 实现 |
| `ITaskLayoutEngine.cs` | 布局接口 |
| `SugiyamaLiteLayoutEngine.cs` | Sugiyama-lite 实现 |
| `RuleBasedExtractor.cs` | 内部工具类:规则解析(JSON / Markdown / 关键词) |

#### ViewModels/TaskGraph/

| 文件 | 内容 |
|---|---|
| `TaskGraphWorkspaceViewModel.cs` | 工作区主 VM |
| `TaskNodeViewModel.cs` | 节点 VM(`partial class`) |
| `TaskEdgeViewModel.cs` | 边 VM |
| `TaskNodeInspectorViewModel.cs` | Inspector VM |
| `ImportPreviewViewModel.cs` | ImportDialog VM |

#### Controls/TaskGraph/

| 文件 | 内容 |
|---|---|
| `TaskGraphWorkspaceControl.axaml(.cs)` | 主控件 |
| `TaskGraphCanvasControl.axaml(.cs)` | 画布视口 |
| `TaskNodeControl.axaml(.cs)` | 节点控件 |
| `EdgeRenderer.cs` | 边的自绘(普通类) |
| `TaskGraphImportDialog.axaml(.cs)` | 导入预览对话框 |
| `TaskNodeInspectorControl.axaml(.cs)` | 右侧 Inspector |

#### Converters/

| 文件 | 内容 |
|---|---|
| `TaskNodeStatusToBrushConverter.cs` | 状态 → 背景色 |
| `TaskNodeKindToIconConverter.cs` | 类型 → 图标文字 |

### 2.2 修改文件

| 文件 | 改动 |
|---|---|
| `App.axaml.cs` | DI 注册 + 事件接线 |
| `App.axaml` | 在 `Application.Styles` 末尾追加新 Style(不破坏已有) |
| `MainWindowViewModel.cs` | 加 `ActiveWorkspace` + `Workspaces` + `SwitchWorkspaceCommand` |
| `MainWindow.axaml` | 改布局为 `200, *` 的 Grid,左侧 200px 导航,右侧 `ContentControl` 承载聊天与任务编排 |
| `ChatWorkspaceViewModel.cs` | 加 `RequestExtractToGraph` 事件 + `/plan` 命令解析 |
| `ChatWorkspaceControl.axaml` | 工具栏列从 6 列扩到 7 列,新增 [Extract] 按钮 |

### 2.3 需新增的 App.axaml Style

- `Border.left-nav` / `Border.left-nav-item` / `Border.left-nav-item.selected`
- `Border.task-node` / `Border.task-node.pending` / `.running` / `.completed` / `.failed` / `.skipped`
- `Border.task-node-anchor`(节点四边连接点)
- `Path.task-edge` / `Path.task-edge-arrow`
- `Border.task-inspector`
- `Button.task-toolbar-button`
- `TextBlock.task-node-title` / `TextBlock.task-node-kind-icon`

---

## 3. DI 接线(代码片段)

`App.axaml.cs : ConfigureServices`:

```csharp
// Models (无状态 POCO, 不需要注册)

// ViewModels
services.AddSingleton<MainWindowViewModel>();
services.AddSingleton<ChatWorkspaceViewModel>();
services.AddSingleton<TaskGraphWorkspaceViewModel>();
services.AddTransient<MainWindow>();
services.AddTransient<TaskGraphWorkspaceControl>();
services.AddTransient<TaskGraphImportDialog>();
services.AddTransient<TaskNodeInspectorControl>();

// Services
services.AddSingleton<ITaskGraphGenerator, DefaultTaskGraphGenerator>();
services.AddSingleton<IChatTaskExtractor, DefaultChatTaskExtractor>();
services.AddSingleton<ITaskGraphLlmGateway, MockTaskGraphLlmGateway>();
services.AddSingleton<ITaskGraphStore, JsonTaskGraphStore>();
services.AddSingleton<ITaskLayoutEngine, SugiyamaLiteLayoutEngine>();
```

`OnFrameworkInitializationCompleted` 末尾(DI 构造完成后):

```csharp
var chat = provider.GetRequiredService<ChatWorkspaceViewModel>();
var graph = provider.GetRequiredService<TaskGraphWorkspaceViewModel>();
chat.RequestExtractToGraph += graph.HandleChatExtractRequest;
```

---

## 4. 实施阶段(Phase A-G)

| Phase | 范围 | 验收 | 估时 |
|---|---|---|---|
| **A** | 数据 + 渲染骨架 | TaskGraph tab 可打开,看到静态图 | 1.0d |
| **B** | 交互(拖拽/连线/Inspector/布局) | 能手动建图、自动布局生效 | 1.5d |
| **C** | 生成 — 规则 | 粘贴 markdown 可生成;Chat 按钮可弹预览 | 1.0d |
| **D** | LLM 兜底 (mock) | 模糊输入也能生成合理图 | 0.5d |
| **E** | 集成到 MainWindow | Chat↔Graph 切换通;事件触发通 | 0.5d |
| **F** | 持久化 | 保存/打开图可往返 | 0.5d |
| **G** | 验证 + 文档 | 5 条主路径全过;AGENTS.md 更新 | 0.5d |

> 总计约 5.5 工作日(单人串行)。如并行派 subagent 可压缩到 2-3 天。

### 4.1 Phase A: 数据 + 渲染骨架

**任务**:
- A1. 创建 `Models/TaskGraph/*` 全部文件
- A2. 创建 `ViewModels/TaskGraph/TaskNodeViewModel.cs` / `TaskEdgeViewModel.cs` / `TaskGraphWorkspaceViewModel.cs`(空 Commands 占位)
- A3. 创建 `TaskGraphWorkspaceControl` 占位页,保证点击侧边栏 [Task Graph] 能切换过去
- A4. 创建 `TaskGraphCanvasControl` + `TaskNodeControl` + `EdgeRenderer` 三个控件,只渲染 seed(3 节点 2 边)
- A4. 创建空壳 `TaskGraphImportDialog` 和 `TaskNodeInspectorControl`

**验收**:
- `dotnet build` 干净,无 warning
- 启动应用,点击左导航 [Task Graph] → 至少能进入任务编排工作页
- 若静态图已接入,显示 3 节点 2 边的静态 demo 图
- 节点不响应任何交互(此阶段纯渲染)

**关键文件**:
- `Models/TaskGraph/TaskNode.cs`、`TaskEdge.cs`、`TaskGraph.cs`
- `ViewModels/TaskGraph/TaskGraphWorkspaceViewModel.cs`(含 `SeedDemoGraph()` 临时方法)
- `Controls/TaskGraph/TaskGraphCanvasControl.axaml(.cs)`
- `Controls/TaskGraph/TaskNodeControl.axaml(.cs)`
- `Controls/TaskGraph/EdgeRenderer.cs`
- `Controls/TaskGraph/TaskGraphWorkspaceControl.axaml(.cs)`

**子任务提示**(可并行):
- 任务组 1:Models(纯 POCO,可独立)
- 任务组 2:ViewModels(等 Models)
- 任务组 3:Controls(等 ViewModels,visual-engineering 派发)

### 4.2 Phase B: 交互

**任务**:
- B1. 节点拖拽(NodeControl 内 Pointer 事件)
- B2. 节点选中 + Inspector 显示(只读 → 双向编辑)
- B3. 锚点连线(EdgeRenderer 加 Pending 边,松手提交)
- B4. 删除节点/边(选中 + Delete 键 / 右键菜单)
- B5. `ITaskLayoutEngine` + `SugiyamaLiteLayoutEngine` 实现 + 工具栏 [Layout] 按钮
- B6. 双击空白新增节点

**验收**:
- 能手动建图(新增/拖动/连线/删除)
- Inspector 编辑实时回写
- [Layout] 按钮自动布局生效
- 选中节点高亮,Inspector 同步内容

**关键文件**:
- `Services/TaskGraph/ITaskLayoutEngine.cs`
- `Services/TaskGraph/SugiyamaLiteLayoutEngine.cs`
- `ViewModels/TaskGraph/TaskNodeInspectorViewModel.cs`
- `Controls/TaskGraph/TaskNodeInspectorControl.axaml(.cs)`
- `Controls/TaskGraph/TaskNodeControl.axaml.cs`(扩 Pointer 事件)
- `Controls/TaskGraph/EdgeRenderer.cs`(扩 Pending 边)

### 4.3 Phase C: 生成 — 规则

**任务**:
- C1. `ITaskGraphGenerator` + `DefaultTaskGraphGenerator`(JSON / Markdown list / "depends on" 关键词)
- C2. `IChatTaskExtractor` + `DefaultChatTaskExtractor`(复用 Generator 内部规则)
- C3. `TaskGraphExtractionResult` / `Diagnostics` 完整实现
- C4. `TaskGraphImportDialog` 显示预览 + [采纳]/[取消] + 模式选择(追加/替换/新建)
- C5. 工具栏 [Import] 按钮 + [From Chat] 按钮(注:From Chat 按钮在 Phase E 接入事件)

**验收**:
- TaskGraph 工具栏 [Import] → 粘贴以下 markdown → 采纳(追加)→ 节点出现并自动布局:
  ```markdown
  - 需求分析
  - 架构设计
  - 编码实现
    depends on: 架构设计
  - 测试验证
    depends on: 编码实现
  ```
- 粘贴 JSON 格式也能直接导入

**关键文件**:
- `Models/TaskGraph/TaskGraphSource.cs`
- `Models/TaskGraph/TaskGraphExtractionResult.cs`
- `Models/TaskGraph/TaskGraphExtractionOptions.cs`
- `Models/TaskGraph/ExtractionDiagnostic.cs`
- `Services/TaskGraph/ITaskGraphGenerator.cs`
- `Services/TaskGraph/DefaultTaskGraphGenerator.cs`
- `Services/TaskGraph/IChatTaskExtractor.cs`
- `Services/TaskGraph/DefaultChatTaskExtractor.cs`
- `Services/TaskGraph/RuleBasedExtractor.cs`
- `ViewModels/TaskGraph/ImportPreviewViewModel.cs`
- `Controls/TaskGraph/TaskGraphImportDialog.axaml(.cs)`

### 4.4 Phase D: LLM 兜底 (mock)

**任务**:
- D1. `ITaskGraphLlmGateway` 接口
- D2. `MockTaskGraphLlmGateway` —— 关键词启发式实现
  - 输入:systemPrompt + userContent
  - 输出:模拟 LLM 行为的 JSON 字符串(基于关键词如 "依赖"、"然后" 等推断)
- D3. Generator 内 "规则不足 → 走 LLM" 分支接入
- D4. ImportDialog 加 `☐ 允许 LLM 兜底` 复选框,绑定到 `TaskGraphExtractionOptions.AllowLlmFallback`

**验收**:
- 输入一段模糊文本(无明确列表格式),开启 LLM 兜底,Generator 走 mock → 仍能生成图
- 关闭 LLM 兜底,只走规则,结果只含规则能识别的部分
- `ExtractionStrategy` 在 ImportDialog 标题旁显示("规则+LLM" / "仅规则" / "失败")

**关键文件**:
- `Services/TaskGraph/ITaskGraphLlmGateway.cs`
- `Services/TaskGraph/MockTaskGraphLlmGateway.cs`
- `Services/TaskGraph/ProductionLlmTaskGraphGateway.cs`(v2 留口,仅骨架)
- `Services/TaskGraph/DefaultTaskGraphGenerator.cs`(扩展)
- `Controls/TaskGraph/TaskGraphImportDialog.axaml`(扩 UI)

### 4.5 Phase E: 集成到 MainWindow

**任务**:
- E1. `MainWindowViewModel` 加 `ActiveWorkspace` + `Workspaces` + `SwitchWorkspaceCommand`
- E2. `MainWindow.axaml` 改布局为左导航 + ContentControl
- E3. `ChatWorkspaceViewModel` 加 `RequestExtractToGraph` 事件 + `/plan` 命令解析
- E4. `ChatWorkspaceControl.axaml` 工具栏列从 6 列扩到 7 列,新增 [Extract] 按钮
- E5. `App.OnFrameworkInitializationCompleted` 内做事件接线
- E6. DI 注册所有新 ViewModel/Service

**验收**:
- 启动应用,默认 Chat tab
- 点左导航 [Task Graph] → 切换到 Graph tab,状态保持
- 点左导航 [Chat] → 切回 Chat tab
- Chat 工具栏 [Extract as Graph] → 弹 ImportDialog(可能为空,Phase C/D 已就位)
- Chat 输入 `/plan 你好` → 收到 mock 回复 → 自动弹 ImportDialog

**关键文件**:
- `App.axaml.cs`(改)
- `App.axaml`(改,加 Style)
- `MainWindowViewModel.cs`(改)
- `MainWindow.axaml`(改)
- `ChatWorkspaceViewModel.cs`(改)
- `ChatWorkspaceControl.axaml`(改)

### 4.6 Phase F: 持久化

**任务**:
- F1. `ITaskGraphStore` + `JsonTaskGraphStore`
- F2. 工具栏 [Save] / [Save As] / [Open] / [New] 四个按钮
- F3. 路径处理:跨平台 `SpecialFolder.ApplicationData`
- F4. 启动不自动加载(开应用从空图开始)

**验收**:
- 手动建图 → [Save] → 文件出现在 `%APPDATA%/AgentOrchestrator/graphs/`
- [Open] 选已存图 → 状态恢复
- [New] 清空当前 graph,新 ID
- 关闭再开应用,默认空图(无 autosave)

**关键文件**:
- `Services/TaskGraph/ITaskGraphStore.cs`
- `Services/TaskGraph/JsonTaskGraphStore.cs`
- `ViewModels/TaskGraph/TaskGraphWorkspaceViewModel.cs`(扩 Commands)

### 4.7 Phase G: 验证 + 文档

**任务**:
- G1. `dotnet build -c Debug` 干净,无 warning
- G2. `dotnet run` 跑通以下 5 条主路径
- G3. 更新 `AGENTS.md` 增加 "TaskGraph Architecture" 章节
- G4. 不创建 README.md(按项目约定)

**5 条主路径**:
1. **手动建图持久化**:手动建 3 节点 2 边 → Save → 关闭 → 重新打开 → Open 选该图 → 状态完全恢复
2. **Markdown 导入**:TaskGraph [Import] → 粘贴 markdown 列表(含 depends on)→ 采纳(追加)→ 节点出现 + 自动布局
3. **Chat 抽取**:Chat 工具栏 [Extract] → 弹预览 → 采纳(替换)→ 图出现
4. **/plan 命令**:Chat 输入 `/plan 你好` → 收到 mock 回复 → 自动弹预览 → 采纳
5. **Tab 切换状态保持**:Chat 输入一些内容 → 切到 Graph 占位/工作页 → 切回 Chat → DraftText 不丢

---

## 5. 任务派发建议(给 orchestrator)

### 5.1 派发原则

- **disciplined codebase** → 严格遵循现有风格(类选择器、`partial class`、`[ObservableProperty]`/`[RelayCommand]`、`x:DataType` 必填)
- **Visual 工作** → `visual-engineering` category
- **纯数据/服务层** → `deep` 或 `unspecified-high`
- **跨 phase 任务** → 串行;phase 内独立任务 → 并行

### 5.2 推荐派发策略

| Phase | 推荐派发方式 |
|---|---|
| A | 3 个并行:①Models POCO、②ViewModels(等 ①)、③Controls(等 ②) |
| B | 2 个并行:①Inspector+布局引擎、②NodeControl+EdgeRenderer 交互 |
| C | 2 个并行:①Services、②ImportDialog |
| D | 串行(基于 C) |
| E | 1 个 deep(集成改动多)+ 1 个 visual-engineering(左导航样式) |
| F | 1 个 deep(服务层) |
| G | 主 orchestrator 自己跑验证 + 文档更新 |

### 5.3 Subagent Prompt 必备要素

派发时 prompt 必须包含(每个 subagent):

```text
1. TASK: 具体的 atomic 目标
2. EXPECTED OUTCOME: 明确的可验证交付物
3. REQUIRED TOOLS: 工具白名单
4. MUST DO: 详尽要求(参考文件路径、风格约束)
5. MUST NOT DO: 禁止事项(不破坏现有 Style、不引入未授权依赖)
6. CONTEXT: 基线说明(指向本改造计划)
```

---

## 6. 验证矩阵(Phase G 必过)

### 6.1 编译验证

```bash
dotnet restore
dotnet build -c Debug
# 期望: 0 error, 0 warning(允许的 warning 需在 AGENTS.md 注明)
```

### 6.2 主路径验证

| 路径 | 操作 | 期望 |
|---|---|---|
| P1 | 手动建图 → Save → 关 → 开 → Open | 图完全恢复 |
| P2 | TaskGraph [Import] → 粘贴 markdown → 采纳(追加) | 节点出现 + 自动布局 |
| P3 | Chat [Extract] → 弹预览 → 采纳(替换) | 图出现 |
| P4 | Chat `/plan 你好` → 自动弹预览 → 采纳 | 流程通 |
| P5 | Chat ↔ Graph 反复切换 | 状态不丢 |

### 6.3 边界情况

| 情况 | 期望 |
|---|---|
| 空图 + 采纳 ImportDialog(追加) | 仅追加节点,无报错 |
| 节点 A 依赖不存在的 B | UI 显示断开边,不崩溃 |
| 删除有依赖的节点 | 依赖该节点的下游节点 DependsOn 自动移除,RebuildEdges 一致 |
| 关闭应用时未保存 | 状态丢失(无 autosave,符合预期) |
| 损坏的 JSON 加载 | 抛清晰异常,不崩溃 |

---

## 7. 风险与缓解

| 风险 | 缓解措施 |
|---|---|
| Chat 抽取准确率(v1 mock 启发式) | 在 `MockTaskGraphLlmGateway` 投入 5+ 测试用例,人工跑过 |
| `Nodes.DependsOn` 与 `Edges` 不一致 | `AddNode/RemoveNode/UpdateDependencies` 处加 `Debug.Assert`,持久化前必 `RebuildEdges()` |
| 持久化 schema 演进 | v1 字段全部显式标注,后续加 `[JsonIgnore(Condition=...)]` 或新加 version 字段 |
| 画布交互性能(节点数 > 50) | v1 接受此限制,v1.1 加 viewport 裁剪 |
| Mock LLM 不合理输出 | UI 上显示 `Diagnostics` 警告,用户可手动改 |

---

## 8. 技术债清单(待 v1.1 / v2 处理)

| 债项 | 原因 | 后续 |
|---|---|---|
| 无 autosave | 避免误覆盖 | v1.1 |
| 无撤销/重做 | 复杂度高 | v1.1 |
| 无缩放/平移 | 范围控制 | v1.1 |
| 无多选/框选 | 范围控制 | v1.1 |
| 真实 LLM 接入 | v1 走 mock | v2 |
| 节点执行 | 不在 v1 范围 | v2 |
| 循环依赖检测 | v1 仅展示 | v2 |
| 跨图导入/模板库 | 不在 v1 范围 | v2+ |

---

## 9. 文档交付清单

| 时机 | 文档 | 路径 |
|---|---|---|
| 本阶段(已完成) | 纯方案 | `Docs/TaskGraph-方案.md` |
| 本阶段(已完成) | 改造计划 | `Docs/TaskGraph-改造计划.md`(本文件) |
| Phase G 完成后 | AGENTS.md 更新 | `AGENTS.md`(增加 TaskGraph Architecture 章节) |

> **不主动创建**: `README.md`(按项目约定)

---

## 10. 变更日志

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-06-16 | v1.0 | 锁定方案 + 改造计划,确认范围 / 阶段 / 验收 |
