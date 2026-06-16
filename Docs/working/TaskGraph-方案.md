# TaskGraph 方案 (v1.0)

> 状态: 方案已锁定,待执行
> 决策日期: 2026-06-16
> 范围: TaskGraph 工作区 v1 的功能、架构、模型、UI、生成策略、持久化、风险边界
> 配套文档: `TaskGraph-改造计划.md` (在方案基础上叠加阶段任务、文件清单、验证步骤)

---

## 1. 目标与范围

### 1.1 目标

让用户能用图形化的 DAG(有向无环图)编排一组可执行任务,并支持两种自动生成入口:

1. **文档/任务列表** → 自动编排成图
2. **Chat 内容** → 自动编排成图

### 1.2 v1 范围

- 图形化任务编排(可视化、增删改、连线)
- 文档/任务列表 → 图(规则 + LLM 兜底)
- Chat 内容 → 图(规则 + LLM 兜底)
- JSON 持久化
- 嵌入到现有 `MainWindow`(左导航 + Tab 切换)

### 1.3 显式不做(v1)

- 节点执行(运行图)
- Autosave
- 撤销/重做
- 多选 / 框选 / 复制粘贴多个节点
- 缩放 / 平移视口
- 真实 LLM 接入(走 mock gateway)
- 节点循环依赖检测算法
- 跨图导入 / 模板库

---

## 2. 架构总览

```text
┌──────────────────────── MainWindow (改) ─────────────────────────┐
│ <Grid ColumnDefinitions="200, *">                                │
│   <Border Classes="left-nav">                                    │
│     [💬] Chat       ──► ChatWorkspaceControl (现有)              │
│     [🔗] Task Graph ──► TaskGraphWorkspaceControl (新增)         │
│   </Border>                                                      │
│   <ContentControl Content="{Binding ActiveWorkspace}"/>          │
│ </Grid>                                                          │
└──────────────────────────────────────────────────────────────────┘

MainWindowViewModel
  ├─ ActiveWorkspace : IWorkspaceViewModel?   (tab 状态)
  ├─ SwitchWorkspaceCommand
  └─ Workspaces: { Chat, Graph }

ChatWorkspaceViewModel (现有, 改一处)
  └─ RequestExtractToGraph : event   ← 新增事件
       (由 Chat 工具栏按钮 / 解析 "/plan" 触发)

TaskGraphWorkspaceViewModel (新)
  ├─ 订阅 ChatWorkspaceViewModel.RequestExtractToGraph
  ├─ Graph : ObservableCollection<TaskNodeViewModel>
  ├─ Edges : ObservableCollection<TaskEdgeViewModel>
  ├─ SelectedNode / SelectionCommands / Import / Save / Layout
  ├─ ITaskGraphGenerator   ── 文档/列表 → 图
  ├─ IChatTaskExtractor    ── Chat messages → 图
  └─ ITaskGraphStore       ── JSON 持久化
```

### 2.1 模块职责

| 模块 | 职责 |
|---|---|
| `TaskGraphCanvasControl` | 画布视口:管理滚动/坐标,承载节点层和边层,分发 Pointer 事件 |
| `TaskNodeControl` | 单一节点渲染:状态色条、标题、4 个连接锚点、节点内拖拽 |
| `EdgeRenderer` | 自绘边:监听 Graph 集合,画贝塞尔曲线 + 箭头,处理 Pending 边 |
| `ITaskGraphGenerator` | 文档/列表 → 图(JSON / Markdown list / 关键词依赖) |
| `IChatTaskExtractor` | Chat messages → 图(规则 + LLM 兜底) |
| `ITaskGraphLlmGateway` | LLM 抽象层(v1 默认 Mock,v2 切真实) |
| `ITaskGraphStore` | JSON 持久化 |
| `ITaskLayoutEngine` | Sugiyama-lite 自动布局(分层 + x 排序) |

### 2.2 与现有架构的契合

- 复用 MVVM + CommunityToolkit.Mvvm + CompiledBindings
- 复用 DI 容器(`Microsoft.Extensions.DependencyInjection`)
- 复用类选择器 Style 体系(在 `App.axaml` 末尾追加,不破坏已有 Style)
- `Models/Chat/` 抽象不修改,TaskGraph 只读 `IChatMessage.Blocks`

---

## 3. 数据模型

### 3.1 枚举

```csharp
public enum TaskNodeKind    { Plan, Execute, Verify, Decision, Parallel, HumanInput }
public enum TaskNodeStatus  { Pending, Running, Completed, Failed, Skipped }
public enum TaskGraphSourceKind { File, InlineText, ChatMessages }
public enum ExtractionStrategy { RulesOnly, RulesPlusLlm, LlmOnly, Failed }
public enum ExtractionSeverity { Info, Warning, Error }
```

### 3.2 核心实体

```csharp
// 节点位置
public readonly record struct NodePosition(double X, double Y);

// 节点
public sealed partial class TaskNode : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新任务";
    public string Description { get; set; } = string.Empty;
    public TaskNodeKind Kind { get; set; } = TaskNodeKind.Execute;
    public TaskNodeStatus Status { get; set; } = TaskNodeStatus.Pending;
    public string? AgentHint { get; set; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public NodePosition Position { get; set; } = new(40, 40);
    public ObservableCollection<string> DependsOn { get; init; } = [];
}

// 边
public sealed class TaskEdge
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FromNodeId { get; init; } = string.Empty;
    public string ToNodeId { get; init; } = string.Empty;
    public string? Label { get; set; }
}

// 图
public sealed partial class TaskGraph : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名图";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ObservableCollection<TaskNode> Nodes { get; init; } = [];
    public ObservableCollection<TaskEdge> Edges { get; init; } = [];
    public void RebuildEdges() { /* 从 Nodes.DependsOn 重建 Edges */ }
}
```

### 3.3 派生数据约定

- `Edges` 是派生数据, Persist 前调用 `RebuildEdges()` 同步
- UI 渲染时直接读 `Edges`(便于直接连线)
- `Position` 由画布布局服务维护,支持手动拖拽和自动布局覆盖

---

## 4. 画布三层(自绘)

### 4.1 Canvas 视口层 — `TaskGraphCanvasControl`

```xml
<UserControl>
  <Panel>
    <Canvas x:Name="EdgeLayer" Background="Transparent" />  <!-- 最底,画边 -->
    <ItemsControl x:Name="NodeLayer" ItemsSource="{Binding Graph}">
      <ItemsControl.ItemsPanel><ItemsPanelTemplate><Canvas/></ItemsPanelTemplate></ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate DataType="vm:TaskNodeViewModel">
          <controls:TaskNodeControl
              Canvas.Left="{Binding Position.X}"
              Canvas.Top="{Binding Position.Y}" />
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </Panel>
</UserControl>
```

- 视口负责:滚动画布、缩放(v1.1)、拖拽平移(v1.1)
- 接收 `PointerPressed/Moved/Released`,把事件分发给 NodeControl / EdgeRenderer
- 选中节点 → 通过 `SelectedNode` 绑定到 Inspector

### 4.2 NodeControl 控件层 — `TaskNodeControl`

- 单一职责:渲染一个节点,暴露 4 个边缘锚点(N/E/S/W)的 hit-test 区域
- 视觉:`Border` + `TextBlock`(标题) + 状态色条
- 4 个连接锚点(6×6,放在节点四边中点)
- 自带拖拽:节点内部 `PointerPressed` 进入拖拽模式,移动时改 `Position`,松手停止
- 锚点 `PointerPressed` 通知 Canvas 进入"连线模式",由 EdgeRenderer 接管
- 不依赖画布逻辑,可单独预览/测试

### 4.3 EdgeRenderer 自绘层

- 单一职责:给定节点集合,重画所有边到 `EdgeLayer` Canvas
- 边用 `Path` 画贝塞尔曲线:控制点 From 右锚点 → To 左锚点,中点偏移形成 S 曲线
- 箭头:`Marker` (用 `PathGeometry` 画三角)
- 订阅 `Graph` 集合变化,重画全部边
- 连线中(Pending Edge)单独画一条临时虚线

**优点**:三件事互不耦合,NodeControl 可在其他场景复用,EdgeRenderer 可替换为 OrthogonalRouter / CurveRouter。

---

## 5. 两种生成方式(规则 + LLM 兜底)

### 5.1 统一接口

```csharp
public interface ITaskGraphGenerator
{
    Task<TaskGraphExtractionResult> ExtractAsync(
        TaskGraphSource source,
        TaskGraphExtractionOptions options,
        CancellationToken ct = default);
}
```

```csharp
public sealed record TaskGraphSource(
    TaskGraphSourceKind Kind,    // File | InlineText | ChatMessages
    string Content,
    string? FilePath = null);

public sealed class TaskGraphExtractionOptions
{
    public bool AllowLlmFallback { get; init; } = true;
    public string? SystemPromptOverride { get; init; }
}

public sealed class TaskGraphExtractionResult
{
    public TaskGraph Graph { get; init; } = new();
    public IReadOnlyList<ExtractionDiagnostic> Diagnostics { get; init; } = [];
    public ExtractionStrategy Strategy { get; init; }
}
```

### 5.2 生成流程

```text
ITaskGraphGenerator.ExtractAsync(source, options)
  │
  ├─ step1: 规则解析
  │    ├─ 命中 JSON 模式?   → 反序列化 → Graph (Strategy=RulesOnly, 结束)
  │    ├─ 命中 Markdown 列表? → 解析为节点(无依赖) → Graph
  │    └─ 命中 "depends on" 关键词? → 解析依赖 → 加边
  │
  ├─ step2: 判定是否需要 LLM 兜底 (任一触发)
  │    - options.AllowLlmFallback == false
  │    - 规则产出 < 2 个节点
  │    - 规则产出 0 条依赖边且源文本长度 > 200
  │    - 源文本含模糊词("先", "接着", "然后", "after that", "finally")
  │
  ├─ step3: LLM 兜底
  │    把 source.Content + 已抽到的节点作为 prompt
  │    要求 LLM 返回 JSON: { nodes:[{title, dependsOn[]}], edges? }
  │    解析失败 → Strategy=Failed,记录 diagnostic
  │
  └─ step4: 合并去重
       - 规则 + LLM 结果按 Title 模糊去重
       - 依赖合并,边 ID 重生成
```

### 5.3 LLM 抽象(为 mock 留口)

```csharp
public interface ITaskGraphLlmGateway
{
    Task<string> CompleteAsync(string systemPrompt, string userContent, CancellationToken ct);
}
```

- v1 默认实现:`MockTaskGraphLlmGateway` —— 基于关键词的启发式,确保 v1 不依赖真实 LLM 也能跑通全流程
- v2 切换:`ProductionLlmTaskGraphGateway` — 调真实 `IChatTransport`,由 DI 注册切换

### 5.4 IChatTaskExtractor

```csharp
public interface IChatTaskExtractor
{
    Task<TaskGraphExtractionResult> ExtractAsync(
        IReadOnlyList<IChatMessage> messages,
        TaskGraphExtractionOptions options,
        CancellationToken ct);
}
```

- 实现复用 `ITaskGraphGenerator` 内部规则 + LLM 逻辑(把 messages 拼成 source)
- 只取 `ChatBlockKind.Text` 块,按时间顺序拼成 markdown 风格列表
- 同样走"规则 + LLM 兜底"流程

---

## 6. UI 布局与视觉

### 6.1 MainWindow 整体

```text
<Grid ColumnDefinitions="200, *">
  <Border Classes="left-nav">
    <!-- 200px 宽侧边栏,图标+标题组合 -->
  </Border>
  <ContentControl Content="{Binding ActiveWorkspace}" />
</Grid>
```

### 6.2 左导航(200px 宽)

- 列宽 `200`
- 两个导航项:[💬] Chat / [🔗] Task Graph
- 选中态:背景 `#FFF5ED`,文字 `#FF6A00`
- 未选中:背景透明,文字 `#40444B`

### 6.3 TaskGraph 内部布局

```text
+------------------------------------------------------------+
| Toolbar (顶部 44px, 圆角 22)                                |
| [+ Node] [Import] [From Chat] | [Layout] [Save] [Open] [New] |
+----------------------------------+-------------------------+
|                                  |  Inspector (280px)      |
|  Canvas (剩余空间)               |  选中节点:              |
|  - 节点 200x80,圆角 12           |    Title / Description  |
|  - 边:贝塞尔曲线,带箭头         |    Kind / Status        |
|  - 双击空白 = 新节点              |    AgentHint            |
|  - 拖节点头 = 移动                |    Depends on           |
|  - 拖锚点 = 连线                  |    Tags                 |
+----------------------------------+-------------------------+
```

### 6.4 节点视觉规格

- 圆角 `12`,内边距 `12,10`,宽度 200,高度 80
- 顶 4px 状态色条:
  - Pending = `#C5C8CE`
  - Running = `#FF6A00`
  - Completed = `#18A558`
  - Failed = `#E5484D`
  - Skipped = `#9096A0`
- 标题:12px SemiBold,`#1F2328`
- 类型图标:左 16×16,按 `TaskNodeKind` 显示
- 4 个锚点:6×6 圆形,半透明,鼠标悬浮变 `#FF6A00`

### 6.5 边视觉

- 贝塞尔曲线,`StrokeThickness = 1.5`
- 颜色:默认 `#7B8088`,选中节点相关边 `#FF6A00`
- 箭头:三角 marker,12×8
- Pending 连线(拖拽中):虚线 `#FF6A00`

---

## 7. Chat → Graph 集成

### 7.1 触发入口

1. **Chat 工具栏按钮 [Extract as Graph]**(在 composer 工具栏新增)
2. **Chat 命令 `/plan <query>`**:截取命令,正常 Send,收到 assistant 回复后自动触发

### 7.2 事件流

```text
[Chat 工具栏] [Extract as Graph] 按钮
  ↓ Command
ChatWorkspaceViewModel.RequestExtractToGraph()
  ↓
  1. 拿 Messages 快照
  2. var e = RequestExtractToGraph;
  3. e?.Invoke(this, new ExtractToGraphRequest(messages, defaultOptions));
  ↓ 订阅
TaskGraphWorkspaceViewModel.HandleChatExtractRequest(sender, request)
  ↓
  1. var result = await ChatExtractor.ExtractAsync(request.Messages, request.Options, ct);
  2. ShowImportDialog(result, mode=AppendOrReplace)  // ImportDialog 提供 [追加]/[替换]/[取消]
```

### 7.3 `/plan` 命令

- `ChatWorkspaceViewModel.Send()` 内:`DraftText.TrimStart().StartsWith("/plan ")` → 截取命令,把剩余文本当 prompt
- 触发后:先正常 Send(走 mock assistant 回复),再自动发 `RequestExtractToGraph`
- v1 mock:检测到 `/plan` 时,assistant 块注入固定模板(包含可识别的任务列表)以确保能抽到东西

### 7.4 解耦原则

- Chat VM 不引用 Graph VM,只发事件
- Graph VM 不硬绑 Chat VM,通过 `event` 订阅注入
- 事件接线在 `App.OnFrameworkInitializationCompleted` 内、DI 构造完成后执行

---

## 8. ImportDialog 行为

ImportDialog 是 v1 中用户体验的关键节点,三种入口共享:

1. TaskGraph 工具栏 [Import] → 弹文本输入
2. Chat [Extract] → 直接显示预览
3. Chat `/plan` → 同上

**UI 规格**(600×500,模态):

```text
+--------------------------------------+
| 标题(导入任务图) | 策略标签          |
+--------------------------------------+
|                          |           |
|  左侧 280px              |  右侧     |
|  原始输入(可编辑)        |  节点预览 |
|                          |  (只读)   |
+--------------------------------------+
| [取消] [☐ 允许 LLM 兜底] [追加 ●替换 ○新建] [采纳] |
+--------------------------------------+
```

**采纳行为**:

- **追加**:把 result.Graph.Nodes 全部 import 到当前 graph,Edges 同样,ID 重新生成
- **替换**:清空当前 graph,导入新内容
- **新建**:建一个新 `TaskGraph` 并设为 `ActiveWorkspace.Graph`

**采纳后**:跑一次 `SugiyamaLiteLayoutEngine.Layout(graph, canvasWidth)` 自动排版。

---

## 9. 持久化

### 9.1 路径与序列化

- 路径:`Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "AgentOrchestrator", "graphs", $"{graph.Id}.json")`
- 序列化:`System.Text.Json`,`WriteIndented = true`,`PropertyNamingPolicy = CamelCase`

### 9.2 接口

```csharp
public interface ITaskGraphStore
{
    Task SaveAsync(TaskGraph graph, CancellationToken ct = default);
    Task<TaskGraph?> LoadAsync(string id, CancellationToken ct = default);
    IAsyncEnumerable<TaskGraph> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
```

### 9.3 行为约定

- 启动不自动加载(开应用从空图开始)
- 工具栏:[New] 清空当前 graph 新建 / [Save] 覆盖保存 / [Save As] 选路径 / [Open] 选已有图
- v1 **不做** autosave(避免误覆盖)

---

## 10. DI 接线

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

**事件接线**(`OnFrameworkInitializationCompleted` 内):

```csharp
var chat = provider.GetRequiredService<ChatWorkspaceViewModel>();
var graph = provider.GetRequiredService<TaskGraphWorkspaceViewModel>();
chat.RequestExtractToGraph += graph.HandleChatExtractRequest;
```

---

## 11. 风险与边界

### 11.1 v1 明确不做(scope guard)

| 不做项 | 后续阶段 |
|---|---|
| 节点执行 | v2 |
| Autosave | v1.1(可选) |
| 撤销/重做 | v1.1 |
| 多选/框选/复制粘贴 | v1.1 |
| 缩放/平移视口 | v1.1 |
| 真实 LLM 接入 | v2 |
| 节点循环依赖检测算法 | v1 仅展示,v2 加检测 |
| 跨图导入/模板库 | v2+ |

### 11.2 v1 阶段可接受的技术债

- Sugiyama-lite 布局仅做纵向 DAG,横向密度不做优化
- Mock LLM Gateway 基于关键词启发式,可能不总返回合理结果
- Inspector 中 `Depends on` 编辑用 `ComboBox`(节点列表),新建时只支持追加

### 11.3 潜在风险

- **Chat 抽取准确率**:v1 走 mock 启发式,对真实 LLM 接入前的 demo 体验是主要变量。需要在 mock gateway 上投入足够测试用例。
- **依赖关系同步**:`Nodes.DependsOn` 与 `Edges` 双写不一致的风险。需在 `AddNode/RemoveNode/UpdateDependencies` 处加 assertion。
- **持久化 schema 演进**:v1 写出的 JSON,未来字段新增需要向前兼容。建议保留 `[JsonIgnore(Condition = WhenWritingNever)]` 等显式标注。

---

## 12. 决策记录(锁定清单)

| 决策点 | 选择 |
|---|---|
| UI 布局 | 左侧导航(图标+标题组合,200px 宽)+ Tab 切换 |
| 画布实现 | 自绘分层(Canvas 视口 / NodeControl 控件 / EdgeRenderer 自绘) |
| 解析深度 | 规则 + LLM 兜底(v1 mock,v2 切真实) |
| 执行能力 | v1 不做 |
| 抽取触发 | 显式(Chat 工具栏按钮 + `/plan` 命令) |
| Inspector 位置 | 右侧常驻 280px 面板 |
| Extract 按钮位置 | Chat composer 工具栏新增按钮 |
| 自动布局 | Sugiyama-lite(分层 + x 排序) |
| 导入合并策略 | 弹 ImportDialog 让用户选(追加/替换/新建) |
| 持久化格式 | JSON(`System.Text.Json`) |
| 持久化时机 | 仅手动 Save,不 autosave |

---

## 13. 文档交付

- 本文档:`Docs/TaskGraph-方案.md`
- 配套改造计划:`Docs/TaskGraph-改造计划.md`
- 实施完成后需更新:`AGENTS.md`(增加 "TaskGraph Architecture" 章节)
