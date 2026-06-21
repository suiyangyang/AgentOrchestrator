# TaskGraph 连线显示修复

修复日期：2026-06-20
涉及文件：
- `src/AgentOrchestrator.App/Controls/TaskGraphWorkspaceControl.axaml.cs`
- `src/AgentOrchestrator.App/ViewModels/TaskGraphWorkspaceViewModel.cs`
- `src/AgentOrchestrator.App/App.axaml.cs`

## 修复内容

### 1. 连线终点与鼠标不重合

**问题**：拖动节点右侧连接点画线时，预览线的终点与实际鼠标位置之间存在偏移（被
工具栏 / "图编辑" 标题栏的高度偏移），并且没有按画布缩放比例补偿。

**根因**：
- `OnCanvasPointerMoved` / `OnNodePointerPressed` / `OnNodePointerMoved` / `CompleteLinkDragAsync`
  使用 `e.GetPosition(this)`，拿到的是鼠标在 `UserControl` 坐标系内的位置。
- `GraphCanvas` 在 `UserControl` 内有一个垂直偏移（顶部工具栏 + "图编辑" 标题栏 +
  可能的用户确认横幅），并且 `GraphCanvas` 上挂了 `ScaleTransform(GraphZoom)`。
- `NormalizeCanvasPoint` 把 `e.GetPosition(this)` 除以 `GraphZoom`，但忽略了
  UserControl → Canvas 的原点偏移。

**修复**：
- 改为 `e.GetPosition(GraphCanvas)`。Avalonia 12 的 `GetPosition` 在元素自身的局部
  坐标系内返回坐标（不含 RenderTransform），正好等于 canvas-local pre-RenderTransform
  坐标 —— 与 `Canvas.SetLeft` / 节点 `Position` / EdgeViewModel 的 `X1/Y1/X2/Y2`
  处在同一坐标系，可以直接放到 Line/Polygon 上，不需要额外变换。
- `NormalizeCanvasPoint` 简化为 identity（仍然保留以便兼容现有调用方，但语义是
  "输入已经是 canvas-local 坐标，不需要再变换"）。

### 2. 连线看不出任务流向

**问题**：边只画了无方向的 `Line`，没有箭头，看不出依赖关系的方向。

**修复**：
- 新增 `CreateArrowhead(Point tip, Point from)` 工具方法，根据 `tip - from` 的方向
  生成一个三角形 `Polygon`（尖端在 `tip`，基线沿连线反向延伸）。
- 在 `SyncEdgeVisuals` 里为每条边维护对应的 `Polygon` 箭头，紧跟在 `Line` 后插入到
  `GraphCanvas.Children`（Line 在最底层 → 箭头 → 节点）。拖动节点时由
  `UpdateEdgesForNode` 同步重塑箭头形状。
- 在连接拖动预览（`OnCanvasPointerMoved` / `ClearLinkPreviewState`）里也给虚线预览
  加了一个箭头，提升交互直观度。

## 验证

新增两个截图 hook：

- 启动参数 `--render-drag-test`：以合成方式在 `node-1` 到 `(720, 460)` 之间建立
  一个连接拖动预览，使用 Avalonia `RenderTargetBitmap` 渲染整个窗口到
  `screenshots/connection-drag.png`，然后退出。配合 `TaskGraphWorkspaceControl`
  上的 `SimulateLinkDragForRendering` 公开方法（仅用于回归测试）使用。

截图见 `docs/working/connection-drag-fixed.png`：
- 所有边（橙色的高亮边 + 灰色的普通边）都有箭头清晰指向目标节点。
- 蓝色虚线预览从 `node-1` 右边缘出发，箭头尖端落在画布坐标 `(720, 460)` 转换后
  的屏幕位置 —— 与拖动时鼠标所在的视觉位置完全重合（canvas-local → 视觉坐标的
  映射与鼠标 `GetPosition(GraphCanvas)` 的输出完全互逆）。

## 备注

- 工具栏的 `App.axaml.cs` 改动是新增的 `--render-drag-test` 启动选项 + 异步渲染
  钩子。生产环境跑 `dotnet run --project src/AgentOrchestrator.App`（不传该参数）
  时不影响任何行为。
- `SimulateLinkDragForRendering` 没有 `InternalsVisibleTo` 限定，但仅由
  `App.RunDragRenderTestAsync` 通过反射 / Avalonia 视觉树调用，未来如果要做正式
  单元测试可以直接走这条路径。
