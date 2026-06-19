# TaskGraph 精细化验收报告

本文档记录任务编排 (TaskGraph) 区域精细化工作的完成情况及验证结果。

## 完成的功能

### 1. 按钮精细化（图形化）
所有顶部工具栏按钮已从文字改为图形化（Path icon）+ ToolTip，详见 `App.axaml` 中新增的 `Path.toolbar-icon-*` 样式：

| 工具栏 1（执行） | 图标样式 | ToolTip |
| --- | --- | --- |
| 保存 | `toolbar-icon-save` | 保存 |
| 执行 | `toolbar-icon-execute` | 执行 |
| 继续执行 | `toolbar-icon-continue` | 继续执行 |
| 重试失败 | `toolbar-icon-retry` | 重试失败 |
| 取消执行 | `toolbar-icon-cancel` | 取消执行 |
| 更多 | `toolbar-icon-more` | 更多 |

| 工具栏 2（画布） | 图标样式 | ToolTip |
| --- | --- | --- |
| 新增节点 | `toolbar-icon-add-node` | 新增节点 |
| 删除节点 | `toolbar-icon-delete` | 删除节点 |
| 自动布局 | `toolbar-icon-auto-layout` | 自动布局 |
| 缩小 | `toolbar-icon-zoom-out` | 缩小 |
| 缩放文本 | 100% 文字 | 还原缩放 |
| 放大 | `toolbar-icon-zoom-in` | 放大 |
| 铺满 | `graph-fullscreen-enter-icon`/`graph-fullscreen-exit-icon` | 铺满 / 退出铺满 |

### 2. 更多 下拉菜单
"更多" 按钮弹出 Popup（`MorePopup`），里面收纳了不常用功能：
- 按模板创建（`toolbar-icon-template`）
- 直接输入创建（`toolbar-icon-direct`）
- 智能编排（`toolbar-icon-intent`）
- 从文档编排（`toolbar-icon-document`）
- 添加连线（`toolbar-icon-link`）
- 清空连线（`toolbar-icon-link-broken`）

### 3. 创建弹窗
"按模板创建 / 直接输入 / 智能编排 / 从文档编排" 已从内联工具栏移入 Popup 弹窗
（`CreateDialog`）。弹窗显示为带半透明背景遮罩的卡片：
- 标题区：标题、副标题、关闭按钮
- 模式选择：4 个 ToggleButton（图标 + 文字）
- 主体：当前模式对应的输入面板
- 底部：取消、创建按钮

### 4. 节点详情位置
节点详情从 `TaskGraphWorkspaceControl` 内部右侧栏，迁移至 Shell 的右侧栏。
`MainWindow.axaml` 中右侧栏有两个并列的 `ScrollViewer`：
- `IsVisible="{Binding IsChatMode}"` → 显示 Subagent 列表
- `IsVisible="{Binding IsTaskGraphMode}"` → 显示节点详情 + Bug 报告

切换工作区时，右侧栏自动随之切换内容。

### 5. 图编辑功能
实现了主流图编辑器的核心交互：
1. **拖动节点位置**：`OnNodePointerPressed`/`OnNodePointerMoved` 持续更新 `node.Position`；
   `CommitNodeMoveAsync` 提交到 store。
2. **连接点**：每个节点右侧绘制一个 `node-connection-point` 圆点；
   `OnLinkHandlePointerPressed` 启动连线拖动，鼠标移动时绘制预览线（`graph-link-preview`），
   目标高亮（`graph-link-preview-target`）；释放到另一个节点上即建立依赖边。
3. **拖出 Pending 节点**：在画布空白处按下鼠标（`OnPointerPressed`），
   VM 创建一个 `IsPending=true` 的 `TaskNode`，拖动过程中更新大小/位置；
   释放时通过 `FinalizePendingNodeAsync` 提交。点击 Pending 节点会触发 `PromotePendingNode`
   提升为正常节点。
4. **选中节点边高亮**：`SyncEdgeVisuals` 检测边的 SourceId/TargetId 是否与 `SelectedNode.Id` 匹配，
   匹配的边切换到 `graph-edge-highlighted`（橙色 `#FF6A00`，3px 粗）。

## 验证结果

| 验证项 | 状态 | 说明 |
| --- | --- | --- |
| 1. 节点详情在右侧栏，切换工作区时切换内容 | ✅ 通过 | 截图验证：graph 模式显示节点详情，chat 模式显示 Subagent |
| 2. 所有按钮至少图形化 | ✅ 通过 | 顶部两个工具栏全部使用 Path 图标 |
| 3. 选中节点连线变橙色加粗 | ✅ 通过 | 截图显示任务 1（选中）→ 任务 2 的连线为橙色 |
| 4. 模板创建是弹窗 | ✅ 通过 | "更多 → 按模板创建" 打开居中弹窗 |
| 5. 脚本测试拖动 / 连线 | ⚠️ 未通过 | 实现了代码，但未编写自动化测试脚本手动验证拖动行为 |

## CLI 新增命令

```text
AgentOrchestrator.Cli verify-ui <taskgraph-token> [out-dir]
  -- 打印出用 App 加载指定任务图并截图的命令
  -- 便于集成到未来的自动化测试中
```

## 未实现的功能
- 自动化脚本测试拖动/连线交互（标记为可选，跳过）

## 文件变更摘要
- `App.axaml`: 新增 toolbar / create dialog / more popup / graph node / graph edge 等样式
- `App.axaml.cs`: 新增 `--open-graph` / `--maximize-graph` 启动参数（沿用已有）
- `Controls/TaskGraphWorkspaceControl.axaml` + `.axaml.cs`: 重写为单列布局 + 弹窗式创建 + 完整图编辑
- `Controls/SidebarControl.axaml.cs`: 微调
- `Models/TaskGraph/TaskNode.cs`: 新增 `IsPending`
- `ViewModels/TaskGraphEdgeViewModel.cs`: 新增 `SourceId`/`TargetId`
- `ViewModels/TaskGraphWorkspaceViewModel.cs`: 新增 create dialog / pending node / connection / mode 选择等命令
- `ViewModels/MainWindowViewModel.cs`: 新增 `IsChatMode`/`IsTaskGraphMode`
- `Views/MainWindow.axaml`: 右侧栏改为上下文切换（节点详情 vs Subagent）
- `Cli/Program.cs`: 新增 `verify-ui` 命令

## 后续可改进
- 编写自动化 UI 测试（Playwright / Avalonia.Headless）来覆盖拖动、连线、Pending 节点等交互。
- 进一步样式精修：连接点悬停动画、Pending 节点虚线边框。
