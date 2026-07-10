using System.Collections.Generic;
using System.Threading.Tasks;
using AgentOrchestrator.App.Services.DialogHost;
using Avalonia.Controls;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Thin backward-compatibility shim for View code-behind that still
/// references <c>DialogHost.ConfirmAsync(...)</c> directly. New code
/// should inject <see cref="IDialogHost"/> instead.
/// </summary>
internal static class DialogHost
{
    private static readonly AvaloniaDialogHost _impl = new();

    public static Task<bool> ConfirmAsync(Window? owner, string title, string message) =>
        _impl.ConfirmAsync(owner, title, message);

    public static Task<string?> InputAsync(Window? owner, string title, string label, string initial) =>
        _impl.InputAsync(owner, title, label, initial);

    public static Task<string?> SelectAsync(Window? owner, string title, string label, IReadOnlyList<string> options, string? selectedOption = null) =>
        _impl.SelectAsync(owner, title, label, options, selectedOption);
}
