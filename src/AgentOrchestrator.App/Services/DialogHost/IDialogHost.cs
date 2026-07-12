using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace AgentOrchestrator.App.Services.DialogHost;

public interface IDialogHost
{
    async Task ShowMessageAsync(Window? owner, string title, string message)
    {
        _ = await ConfirmAsync(owner, title, message);
    }
    Task<bool> ConfirmAsync(Window? owner, string title, string message);
    Task<string?> InputAsync(Window? owner, string title, string label, string initial);
    Task<string?> SelectAsync(Window? owner, string title, string label, IReadOnlyList<string> options, string? selectedOption = null);
}
