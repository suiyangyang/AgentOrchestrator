# TaskGraph 模板统一实例化链路执行文档

> 状态：已执行
> 适用范围：TaskGraph 模板创建链路收口、实例化契约收口、Runtime 反向沉淀模板
> 目标读者：主模型、小模型、子代理
> 文档类型：工作文档
> 更新日期：2026-07-10
> 完成情况：阶段 A/B/C/D/E/F 全部完成，构建通过，测试 21/21 通过

---

## 1. 文档目的

本文不是讨论型方案，而是一份可直接执行的实施文档。

目标是把当前 TaskGraph 相关能力收口为一条统一业务链：

`模板文档 -> InstantiateTemplateAsync -> Runtime 图 -> 执行/编辑 -> 可另存为模板`

本文要求细化到“小模型可以按步骤独立完成单个任务单元”的程度。

---

## 2. 当前代码事实

以下判断基于当前仓库代码，不以旧方案为准。

### 2.1 已经成立的能力

1. `TaskGraph` 已同时承载 Template 和 Runtime 两种文档类型。
2. `JsonTaskGraphStore.InstantiateTemplateAsync(...)` 已存在。
3. `TaskGraphTemplateInstantiator` 已支持：
   - 校验 `DocumentKind == Template`
   - 深拷贝
   - 重写节点 ID
   - 清空运行态字段
   - 设置 `BasedOnTemplateId`
   - 基于 `DynamicZones` 做首轮动态扩展
4. Chat 的两条主链路已经基本走统一实例化出口：
   - `TriggerAutoTaskGraphAsync(...)`
   - `TriggerTemplateTaskGraphAsync(...)`

### 2.2 仍然存在的问题

1. `TaskGraphWorkspaceViewModel.CreateTemplateGraphAsync()` 仍直接调用本地构图逻辑，没有走模板实例化链路。
2. `TaskGraphTemplateBuilder` 仍同时承担“模板构造”和“直接构造 Runtime”两类职责。
3. `TemplateInstantiationOptions` 还没有承载完整实例化契约。
4. Runtime 图缺少“另存为模板”闭环。
5. `{{user_input}}` 之类输入注入语义没有在实例化阶段被正式定义和收口。
6. 内置模板 ID 仍分散，调用方没有统一查询入口。

### 2.3 本次改造后的业务真相

改造完成后，以下规则必须成立：

1. 所有“基于模板创建 Runtime 图”的入口都必须经过 `InstantiateTemplateAsync(...)`。
2. `TaskGraphTemplateBuilder` 只负责生成 Template 文档，不再直接给业务入口生成 Runtime 图。
3. Runtime 图若来源于模板，必须具备 `BasedOnTemplateId`。
4. Runtime 图可以清洗后另存为 Template。
5. 输入注入规则必须有明确、可测试、可复用的实现。

---

## 3. 非目标

本次不做以下内容：

1. 不做任意运行时图自我改写机制升级。
2. 不做 LLM 驱动的复杂动态扩图策略框架。
3. 不做模板市场、远程模板仓库。
4. 不做模板版本 diff。
5. 不做大规模 UI 重构，只做支撑统一链路所需最小修改。

---

## 4. 统一设计结论

### 4.1 统一出口

统一出口固定为：

`ITaskGraphStore.InstantiateTemplateAsync(templateId, options, ct)`

任何入口只负责：

1. 决定模板 ID
2. 组织 `TemplateInstantiationOptions`
3. 接收返回的 Runtime 图
4. 激活、执行或继续编辑

### 4.2 Builder 的最终职责

`TaskGraphTemplateBuilder` 只做两类事：

1. 构造内置 Template 文档
2. 提供与 Template 结构相关的辅助逻辑

它不再承担：

1. Workspace 入口的直接 Runtime 生成
2. Chat 入口的直接 Runtime 生成

### 4.3 输入注入的统一语义

本次先收口为最小可控规则：

1. `options.UserInput` 写入 Runtime 图的 `SourceContent`
2. 若节点 `Prompt` 中包含 `{{user_input}}`，实例化时替换为实际输入
3. 若没有占位符，则不自动拼接 prompt
4. 动态扩图仍以最终生效的输入文本为来源

这样可以避免“有的模板靠 `SourceContent`，有的模板靠 prompt 文本，行为不一致”的问题。

### 4.4 Runtime 另存为模板的统一语义

Runtime 另存为模板时：

1. `DocumentKind = Template`
2. 清空全部运行态字段
3. 清空节点运行态字段
4. `BasedOnTemplateId = null`
5. `IsBuiltInTemplate = false`
6. 若原图没有 `TemplateMetadata`，创建空对象

---

## 5. 执行边界

本次改造只允许修改以下模块：

1. `src/AgentOrchestrator.App/Services/TaskGraph/`
2. `src/AgentOrchestrator.App/ViewModels/`
3. `src/AgentOrchestrator.App/Models/TaskGraph/`
4. `tests/AgentOrchestrator.App.Tests/`
5. 本工作文档

本次改造不允许顺手改：

1. MainWindow 导航结构
2. 大规模样式系统
3. 与本链路无关的 Chat UI 行为
4. 无关的性能重构

---

## 6. 任务拆分总览

按顺序执行，禁止跳步。

| 阶段 | 名称 | 目标 |
|---|---|---|
| A | 现状收口 | 补齐实例化契约与共享常量 |
| B | Workspace 收口 | 让 Workspace 模板创建走统一出口 |
| C | Builder 瘦身 | 去掉业务入口对 Builder 生成 Runtime 的依赖 |
| D | Save As Template | 建立 Runtime -> Template 闭环 |
| E | 测试补齐 | 用测试锁定统一链路 |
| F | 文档回填 | 更新当前工作文档和必要技术文档 |

每个阶段完成后都必须执行：

```powershell
dotnet build -c Debug
dotnet test -c Debug --no-build
```

---

## 7. 阶段 A：现状收口

### 7.1 目标

补齐统一实例化链路的基础契约，避免后续入口修改时继续复制零散逻辑。

### 7.2 必改文件

1. `src/AgentOrchestrator.App/Services/TaskGraph/TemplateInstantiationOptions.cs`
2. `src/AgentOrchestrator.App/Services/TaskGraph/TaskGraphTemplateInstantiator.cs`
3. `src/AgentOrchestrator.App/Services/TaskGraph/BuiltInTemplateSeeder.cs`
4. 如有必要：新增一个内置模板目录类或查询方法

### 7.3 任务单元

#### A-1：补齐 `TemplateInstantiationOptions`

修改目标：

1. 增加 `OriginHint`
2. 增加 `ConversationSessionId`
3. 保留现有字段不破坏兼容

建议结果：

```csharp
public string? RuntimeGraphName { get; set; }
public string? UserInput { get; set; }
public string? ProjectId { get; set; }
public string? ProjectName { get; set; }
public TaskGraphOriginHint? OriginHint { get; set; }
public string? ConversationSessionId { get; set; }
```

完成标准：

1. 编译通过
2. 所有现有调用点无需立即全改也能编译

#### A-2：实例化阶段应用新增契约

修改目标：

1. 若 `OriginHint` 有值，写入 Runtime 图
2. 若 `ConversationSessionId` 有值，写入 Runtime 图
3. 保持旧行为兼容

完成标准：

1. 不影响已有 `BasedOnTemplateId`
2. 不影响运行态清理

#### A-3：实现 `{{user_input}}` 占位符替换

修改目标：

1. 在实例化阶段遍历节点 `Prompt`
2. 替换 `{{user_input}}`
3. 若 `UserInput` 为 null，则替换为空字符串

限制：

1. 本阶段只替换 `Prompt`
2. 不扩展到 Description、Title、TemplateNotes

完成标准：

1. 模板中的占位符被替换
2. 不含占位符的模板行为不变

#### A-4：统一内置模板 ID 查询入口

修改目标：

1. 提供 `GetBuiltInTemplateId(TaskGraphTemplateKind kind)` 或等价方法
2. Chat/Workspace 后续统一通过该入口取内置模板 ID

完成标准：

1. 不再在多个 VM 手写相同 ID 字符串映射

### 7.4 本阶段验收

1. `InstantiateTemplateAsync(...)` 可以承载来源和会话信息
2. `{{user_input}}` 替换有测试覆盖
3. 内置模板 ID 查询有唯一入口

---

## 8. 阶段 B：Workspace 收口

### 8.1 目标

把 Workspace 的模板创建入口从“直接构图”改成“加载内置模板并实例化”。

### 8.2 必改文件

1. `src/AgentOrchestrator.App/ViewModels/TaskGraphWorkspaceViewModel.cs`
2. 可能涉及 `src/AgentOrchestrator.App/Services/TaskGraph/BuiltInTemplateSeeder.cs`
3. 对应测试文件

### 8.3 任务单元

#### B-1：替换 `CreateTemplateGraphAsync()` 内部实现

当前问题：

1. 它最终走 `BuildGraphFromTemplateKind(...)`
2. 没有经过实例化器

修改目标：

1. 根据 `SelectedTemplate.Key` 映射到 `TaskGraphTemplateKind`
2. 通过统一目录查到内置模板 ID
3. 调用 `_store.InstantiateTemplateAsync(...)`

建议传入参数：

```csharp
new TemplateInstantiationOptions
{
    RuntimeGraphName = 最终图名,
    UserInput = TemplateInputText,
    ProjectId = _sidebar.CurrentProject?.Id,
    ProjectName = _sidebar.CurrentProject?.Name,
    OriginHint = TaskGraphOriginHint.WorkspaceDirect,
}
```

完成标准：

1. Workspace 模板创建后生成的是 Runtime 图
2. `BasedOnTemplateId` 不为空
3. 动态区扩展正常

#### B-2：清理 Workspace 内部重复命名逻辑

当前问题：

1. `graphName`
2. `TemplateGraphName`
3. `NewGraphNameDraft`

存在多处覆盖关系。

修改目标：

1. 明确最终图名优先级
2. 只保留一套最终命名决策

建议优先级：

1. `NewGraphNameDraft`
2. `TemplateGraphName`
3. `options.RuntimeGraphName` 默认值

完成标准：

1. 创建完成后图名符合预期
2. 不存在创建后又被二次覆盖的逻辑

#### B-3：删除或废弃 Workspace 中直接 Runtime 构图入口

处理原则：

1. 若仅被模板创建路径使用，删除
2. 若仍有其他调用，先标记为过渡私有方法，再逐步收口

至少要做到：

1. 模板创建主路径不再调用 `BuildGraphFromTemplateKind(...)`

### 8.4 本阶段验收

1. Workspace 的三种模板都经由实例化链路生成 Runtime 图
2. 新图具备 `BasedOnTemplateId`
3. 节点 ID 为新生成 ID，而不是模板语义 ID

---

## 9. 阶段 C：Builder 瘦身

### 9.1 目标

让 `TaskGraphTemplateBuilder` 从“业务入口构图器”收缩为“模板文档构造器”。

### 9.2 必改文件

1. `src/AgentOrchestrator.App/Services/TaskGraph/TaskGraphTemplateBuilder.cs`
2. `src/AgentOrchestrator.App/Services/TaskGraph/BuiltInTemplateSeeder.cs`
3. 相关测试

### 9.3 任务单元

#### C-1：限制 Builder 的公开职责

修改目标：

1. 让公开 API 明确表达“生成 Template”
2. 尽量减少 `documentKind == Runtime` 分支

推荐方式二选一：

1. 保留现有方法名，但默认只生成 Template，并让 Runtime 分支内部转私有
2. 新增 `BuildTemplate(...)` / `BuildTaskListTemplate(...)` 风格方法，再逐步替换旧调用

优先建议：

采用“新增 Template 命名 API，再切调用点”的方式，风险更低。

#### C-2：Seeder 改为只依赖 Template API

修改目标：

1. 内置模板播种不再传 `TaskGraphDocumentKind.Template` 这种双态参数
2. Seeder 的意图变成“播种模板文档”

完成标准：

1. Seeder 代码一眼可看出只在处理 Template

#### C-3：为 Builder 行为补测试

至少覆盖：

1. 任务列表模板结构
2. 功能开发模板结构
3. Bug 列表模板结构
4. `builtin.auto-orchestration` 仍可被实例化链路正确消费

### 9.4 本阶段验收

1. 业务入口不再依赖 Builder 直接生成 Runtime
2. Builder 代码中 Runtime 分支显著减少或退出公开入口

---

## 10. 阶段 D：Save As Template

### 10.1 目标

让 Runtime 图可以反向沉淀为模板，形成闭环。

### 10.2 必改文件

1. `src/AgentOrchestrator.App/ViewModels/TaskGraphDocumentEditorViewModel.cs`
2. 如需要：相关 UI 绑定文件
3. 相关测试

### 10.3 任务单元

#### D-1：新增 `SaveAsTemplateAsync()`

方法职责：

1. 只允许从 Runtime 文档触发
2. 深拷贝当前图
3. 清理运行态
4. 转成 Template 文档
5. 保存到 store

建议最小步骤：

1. 校验当前文档存在且为 Runtime
2. 深拷贝当前图
3. 设置 `Id = new Guid`
4. 设置 `DocumentKind = Template`
5. 设置 `BasedOnTemplateId = null`
6. 设置 `IsBuiltInTemplate = false`
7. 清空图级运行态
8. 清空节点级运行态
9. 若 `TemplateMetadata == null`，创建空对象
10. 调用 `_store.SaveAsync(...)`

#### D-2：补充命名策略

建议规则：

1. 若用户未指定模板名，默认 `"{当前图名} - 模板"`
2. 不要覆盖原 Runtime 图

#### D-3：刷新侧边栏和当前状态

完成标准：

1. 保存后模板列表可见
2. 不影响当前 Runtime 图继续存在

### 10.4 本阶段验收

1. Runtime 图可以保存为新模板
2. 新模板不可执行
3. 新模板不带运行态污染

---

## 11. 阶段 E：测试补齐

### 11.1 目标

用测试把统一链路锁死，防止后续入口重新走偏。

### 11.2 必增或必改测试

1. `tests/AgentOrchestrator.App.Tests/TaskGraphTemplateInstantiatorTests.cs`
2. `tests/AgentOrchestrator.App.Tests/JsonTaskGraphStoreInstantiatorIntegrationTests.cs`
3. `tests/AgentOrchestrator.App.Tests/ChatAutoOrchestrationTemplateTests.cs`
4. `tests/AgentOrchestrator.App.Tests/TaskGraphDocumentEditorViewModelTests.cs`
5. 如已有对应文件：新增 Workspace 创建链路测试

### 11.3 必测场景

#### E-1：Workspace 模板创建走实例化链路

断言：

1. 结果是 Runtime 文档
2. `BasedOnTemplateId` 正确
3. 节点 ID 已刷新

#### E-2：`{{user_input}}` 被替换

断言：

1. 模板节点 prompt 中的占位符已被实际输入替换

#### E-3：实例化时写入来源和会话信息

断言：

1. `OriginHint`
2. `ConversationSessionId`

能进入结果图。

#### E-4：Save As Template 清洗运行态

断言：

1. 图级状态清空
2. 节点级状态清空
3. `DocumentKind == Template`
4. `BasedOnTemplateId == null`

#### E-5：Chat 自动编排仍可工作

断言：

1. 自动编排主路径不被本次改造破坏

### 11.4 本阶段验收

1. 相关测试全部通过
2. 不依赖人工肉眼确认统一链路

---

## 12. 阶段 F：文档回填

### 12.1 目标

保证当前事实能在文档里被正确表达。

### 12.2 最低要求

1. 本工作文档随实际实现状态更新
2. 若用户可见行为变化，更新 `Docs/user/task-graph.md`
3. 若架构真相变化，更新 `Docs/developer/architecture.md` 或 `Docs/developer/project.md`

### 12.3 回填原则

1. 正式文档只写当前有效状态
2. 本工作文档保留实施步骤和待办
3. 不在正式文档里保留“以前曾经直接构图”这类历史兼容说明

---

## 13. 小模型执行规则

### 13.1 通用规则

1. 一次只做一个阶段。
2. 一个阶段内，优先先写测试再改实现；若做不到，至少先补失败测试草稿。
3. 每完成一个任务单元就重新编译。
4. 不允许跨文件大面积顺手重构。
5. 发现现有未提交修改时，不回退，不覆盖，先兼容。

### 13.2 每次执行的固定模板

小模型在开始一个任务单元前，先列出：

1. 要修改的文件
2. 本次只解决的一个问题
3. 完成后的验证命令

执行后必须回报：

1. 已完成什么
2. 哪些测试通过
3. 是否有遗留风险

### 13.3 禁止事项

1. 不要重写整个 `TaskGraphWorkspaceViewModel`
2. 不要把实例化逻辑复制到 ViewModel
3. 不要再新增一条“快捷直接构图”的正式业务路径
4. 不要把输入注入逻辑散落到 Chat/Workspace 两边分别实现
5. 不要把 Save As Template 做成“直接修改当前 Runtime 文档类型”

---

## 14. 建议提交粒度

推荐按以下粒度拆分提交：

1. `TaskGraph: expand template instantiation options and input injection`
2. `TaskGraph: route workspace template creation through store instantiation`
3. `TaskGraph: narrow template builder to template-focused APIs`
4. `TaskGraph: add runtime save-as-template flow`
5. `TaskGraph: add tests for unified template instantiation pipeline`

---

## 15. 最终验收清单

全部满足才算完成：

1. Workspace 模板创建统一走 `InstantiateTemplateAsync(...)`
2. Chat 自动编排与模板编排继续走 `InstantiateTemplateAsync(...)`
3. Builder 不再承担业务入口的 Runtime 直接生成
4. `TemplateInstantiationOptions` 能表达来源与会话信息
5. `{{user_input}}` 占位符有统一替换实现
6. Runtime 图可另存为模板
7. 新增或修改的测试全部通过
8. 相关文档已更新为当前真相

---

## 16. 推荐执行顺序

建议严格按以下顺序推进：

1. 阶段 A
2. 阶段 E 中与 A 对应的测试
3. 阶段 B
4. 阶段 E 中与 B 对应的测试
5. 阶段 C
6. 阶段 D
7. 阶段 E 余下测试
8. 阶段 F

如果中途卡住，优先保住“统一出口”这条主线，不要退回到新增旁路的做法。
