# Current Project State

## Application

- Desktop app only
- `net10.0`
- Avalonia 12.0.4
- CommunityToolkit.Mvvm 8.4.1
- DI via Microsoft.Extensions.DependencyInjection 10.0.9

## Code Layout

- `Program.cs` boots Avalonia
- `App.axaml.cs` creates the service provider
- `MainWindowViewModel` owns `Chat` and `Settings`
- `ChatWorkspaceViewModel` manages messages, attachments, permissions, and draft input
- `MainWindow.axaml` hosts the chat workspace
- `SettingsWindow.axaml` is a fixed-size settings dialog

## Binding And Styling

- Compiled bindings are enabled by default
- Every view and data template should set `x:DataType`
- Global style tokens live in `App.axaml`
- `TextBox` uses the chat input style
- `TextBlock.code-text` is the code style
- `TextBlock.assistant-text` and `TextBlock.thinking-body-text` are chat content styles
