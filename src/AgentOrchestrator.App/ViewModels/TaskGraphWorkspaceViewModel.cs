namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Placeholder workspace for the "任务编排" tab. Renders a fixed
/// "coming soon" page; the real TaskGraph implementation will replace it.
/// </summary>
public sealed partial class TaskGraphWorkspaceViewModel : ViewModelBase
{
    public string Title => "任务编排";
    public string Description => "用图形化的方式编排一组可执行任务,支持文档 / 任务列表 / 对话记录自动生成 DAG。";
    public string Hint => "v1 占位页:TaskGraph 实施后会接管此处。";
    public string ComingSoon => "Coming soon";
}
