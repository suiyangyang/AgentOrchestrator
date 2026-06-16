# AgentOrchestrator

Cross-platform desktop app built with Avalonia UI 12, .NET 10, and MVVM with CommunityToolkit.Mvvm.

## Current Stack

- Target: `net10.0`
- UI: Avalonia 12.0.4 + FluentTheme + Inter font
- MVVM: CommunityToolkit.Mvvm 8.4.1
- DI: Microsoft.Extensions.DependencyInjection 10.0.9

## Current App Shape

- Entry: `src/AgentOrchestrator.App/Program.cs`
- App setup: `src/AgentOrchestrator.App/App.axaml.cs`
- Main window: `src/AgentOrchestrator.App/Views/MainWindow.axaml`
- Main window VM owns `Chat` and `Settings`
- Chat workspace is the active product surface
- Settings window is present as a separate dialog

## Non-Negotiables

- Use MVVM and CommunityToolkit source generators for new view models.
- Keep compiled bindings valid by setting `x:DataType` on every view and data template.
- Register view models as singleton and views as transient unless a local pattern says otherwise.
- Keep code-behind minimal; put behavior in view models unless it is pure UI plumbing.
- Put global styles in `App.axaml`.
- Keep `Services/` for cross-cutting services and hide platform-specific behavior behind interfaces.

## Build And Run

```bash
dotnet restore
dotnet build -c Debug
dotnet run --project src/AgentOrchestrator.App
```

## Documentation

- `docs/agent/` for agent-facing context
- `docs/developer/` for current technical truth
- `docs/user/` for user guidance
- `docs/working/` for temporary plans and migration notes
