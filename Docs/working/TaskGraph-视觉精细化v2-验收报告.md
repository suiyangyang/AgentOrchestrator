# TaskGraph 视觉精细化 (v2) — 验收报告

> 范围：在已有的「精细化」基础上，参考 ComfyUI 风格的节点编辑器，进一步打磨 TaskGraph 节点编辑器的视觉一致性。
> 验收日期：2026-06-20

## 完成的功能（对照用户 6 条反馈）

| 用户反馈 | 解决方案 | 验证截图 |
| --- | --- | --- |
| 1. 连接点在左右两侧的固定位置 | 每个节点用 wrapper Canvas 包裹 Border + 两个 Ellipse（左侧 input、右侧 output），锚定到 `PortAnchorOffsetY = 58`，与节点内容高度无关 | `screenshots/graph-view.png` |
| 2. 连线要曲线样式 | 改用 `Path` + `Geometry.Parse(BuildBezierData(...))` 渲染 cubic bezier，水平 control point 至少 40px | 同上 + `screenshots/connection-drag.png` |
| 3. 不同类型节点连线不同颜色 | 新建 `TaskNodePortStyle` 静态映射，把每个 `TaskNodeKind` 映射到 FillHex / TextHex / Label。Edge stroke = 源节点 port 颜色 | 同上（蓝/橙/绿/紫/粉 五种颜色清晰可辨） |
| 4. 节点内字体缩小并精细化区分 | 新增 `TextBlock.graph-node-title` / `graph-node-kind` / `graph-node-description` / `graph-node-output` / `graph-node-status` 五个 class selector，分别 13/11/11/10.5/10.5px，颜色按层级区分 | 同上 |
| 5. 右下角圆角矩形 + 右侧蓝点的用途 | 圆角矩形 = 「打开执行详情」按钮（带外部链接 ↗ 图标，ToolTip 同名）；蓝点 = 输出 port（拖动可拉线，ToolTip 提示）。两者现在都有明确的视觉提示 | 同上 |
| 6. Execute/HumanInput/Plan 等类型如何编辑和选择 | 节点类型在右侧栏「节点详情」的 `Kind` 下拉框切换；具体类型语义写在 `docs/user/task-graph.md` | 文档 + 实际 UI（右侧栏） |

## 验收标准验证

### 验收 1：截图确认连线位置稳定

`src/screenshots/graph-view.png` —— 用「Bug 列表处理」图（19 节点 21 边，混合 Plan / Decision / Execute / Verify / HumanInput 五种类型）渲染：

- 所有节点的左侧 port 在同一条水平线上对齐
- 所有节点的右侧 port 在同一条水平线上对齐
- 两个端口行之间的垂直距离固定（≈ 节点上沿 + 58px）
- 选中节点（顶部第 1 个，标题深色加粗）的高亮边框也对齐到同一基线
- ✅ 通过

### 验收 2：曲线样式优美

`src/screenshots/connection-drag.png` —— 同一图叠加从「读取 Bug 1」右侧 port 拖到画布空白的预览线：

- 预览线为**虚线蓝色贝塞尔曲线**，水平 S 型 control point
- 箭头指向目标位置
- 实际连线（其他已建好的依赖）也是同款贝塞尔，只是换成源节点 port 颜色
- ✅ 通过

## 文件变更摘要

### 新增
- `src/AgentOrchestrator.App/Models/TaskGraph/TaskNodePortStyle.cs` — 颜色 / 标签映射
- `Docs/user/task-graph.md` — 用户文档（节点类型 / port / 编辑方法）

### 修改
- `src/AgentOrchestrator.App/Controls/TaskGraphWorkspaceControl.axaml.cs`
  - 字段 `_edgeVisuals` 类型 `Line` → `Path`
  - 字段 `_linkPreviewLine` 类型 `Line?` → `Path?`
  - 新增 `_nodeInputPortVisuals` / `_nodeOutputPortVisuals` 两个 `Dictionary<string, Ellipse>`
  - `_nodeVisuals` 类型 `Dictionary<string, Border>` → `Dictionary<string, Canvas>`（wrapper）
  - 节点常量 `NodeWidth` 240 → 220
  - 新增常量 `PortHalfSize = 5.5`
  - 删除 `EdgeNormalBrush`（per-edge port 颜色替代）
  - 新增 `BuildBezierData` / `ParseBezierGeometry` 辅助方法
  - `BuildNodeVisual` 改为返回 wrapper Canvas（包含 Border + 两个 Port Ellipse）
  - `UpdateNodeVisual` 改为操作 wrapper Canvas，刷新 port class
  - `BuildNodeContent` 重写为 4 行布局（title / kind / body / actions），actions 行只剩 detail 按钮（无连接点）
  - `SyncEdgeVisuals` 改用 Path + 每边 stroke 颜色 + 同步重塑 arrow
  - `UpdateEdgesForNode` 重新计算 bezier data + port 锚定到 `PortAnchorOffsetY`
  - `SimulateLinkDragForRendering` 用 bezier Path + 源 port 颜色
  - `OnCanvasPointerMoved` 同上
  - 新增 `TryGetFirstNodeId` 测试 hook
  - 详情按钮加 `toolbar-icon-external-link` Path 图标

- `src/AgentOrchestrator.App/ViewModels/TaskGraphEdgeViewModel.cs`
  - 新增 `SourceKind` + `SourcePortColors` 字段

- `src/AgentOrchestrator.App/ViewModels/TaskGraphWorkspaceViewModel.cs`
  - 端点 Y 从 `NodeHeight/2` 改为 `TaskNodePortStyle.PortAnchorOffsetY`
  - `TaskGraphEdgeViewModel` 构造多传一个 `source.Kind`

- `src/AgentOrchestrator.App/App.axaml`
  - 新增 `Path.graph-edge` / `Path.graph-edge-highlighted` / `Path.graph-link-preview` 样式
  - 新增 `Ellipse.port-port-*`（6 个 kind × 1 个 class）
  - 新增 `Ellipse.port-circle`（统一尺寸 + 白边）
  - 新增 `TextBlock.graph-node-*`（5 个 class）
  - 新增 `Path.toolbar-icon-external-link` 详情按钮图标
  - 调整 `Path.toolbar-icon-path` 默认 stroke `#1F2328` → `#7A7F87`（与侧边栏图标一致）

- `src/AgentOrchestrator.App/App.axaml.cs`
  - 新增 `--render-graph-test` 启动参数（无模拟拖动的纯图渲染）
  - `RunDragRenderTestAsync` 改为通过 `TryGetFirstNodeId` 自动选第一个节点，不再硬编码 `node-1`

## 与之前精细化的关系

之前精细化（`taskgraph-refinement-验收报告.md`）主要做了：
- 按钮图形化、更多下拉菜单、创建弹窗、节点详情迁到右侧栏
- 节点拖动 / 连线 / Pending / 选中高亮

本次 v2 精细化（本文档）只动**视觉一致性 + 连线样式 + 端口定位**，不修改交互逻辑（拖动、连线、Pending 等行为完全保持）。

## 编译结果

`dotnet build -c Debug --nologo` → 0 errors，0 new warnings。
- 既有 4 类 warning（SQLitePCLRaw 漏洞、CS8602 预存、AVLN5001 Watermark 过时）与本次改动无关，未引入新 warning。
