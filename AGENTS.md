# AgentOrchestrator

Cross-platform desktop application built with **Avalonia UI 12**, **.NET 10**, and the **MVVM** pattern using **CommunityToolkit.Mvvm**.

> Project name: `AgentOrchestrator` (no historical typo retained).

---

## Tech Stack

| Layer            | Choice                                       | Version  |
|------------------|----------------------------------------------|----------|
| Runtime          | .NET                                        | 10.0     |
| UI framework     | Avalonia (FluentTheme + Inter font)          | 12.0.4   |
| MVVM toolkit     | CommunityToolkit.Mvvm                        | 8.4.1    |
| DI container     | Microsoft.Extensions.DependencyInjection    | 10.0.9   |

Single target framework: `net10.0`. Desktop only (Windows / macOS / Linux). No platform-specific code or `RuntimeIdentifier` gymnastics required.

---

## Build & Run

```bash
# Restore dependencies
dotnet restore

# Build (Debug)
dotnet build -c Debug

# Run the application
dotnet run --project src/AgentOrchestrator.App

# Publish a single-file executable (Windows x64 example)
dotnet publish src/AgentOrchestrator.App -c Release -r win-x64 --self-contained
```

Solution file: `AgentOrchestrator.slnx` (new XML solution format).

---

## Project Structure

```
AgentOrchestrator.slnx
src/
  AgentOrchestrator.App/
    Program.cs                            # Entry point; AppBuilder + lifetime
    App.axaml / App.axaml.cs              # Application class, DI registration, global styles
    ViewLocator.cs                        # IDataTemplate: VM -> View by name convention
    app.manifest                          # Windows application manifest
    Assets/                               # Icons and static resources
    Controls/                             # Reusable UserControls (e.g. ChatWorkspaceControl)
    Converters/                           # IValueConverter implementations (empty, ready)
    Models/Chat/                          # Chat domain models (ChatMessage, ChatBlock, ...)
    Services/                             # Application services (empty, ready)
    ViewModels/                           # All ViewModels (inherit ViewModelBase)
    Views/                                # Top-level Windows (MainWindow)
```

---

## Architecture

### MVVM with CommunityToolkit.Mvvm

- **`ViewModelBase : ObservableObject`** — abstract base for every VM. Provides `INotifyPropertyChanged`.
- **`[ObservableProperty]`** — source-generated properties; emits backing field + `OnPropertyChanged`.
- **`[RelayCommand]`** — source-generated `ICommand` properties from methods.
- **VMs that use source generators must be declared `partial class`**.

```csharp
public partial class MyViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [RelayCommand]
    private void Greet() => /* ... */;
}
```

### Compiled Bindings

`<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>` is set in the `.csproj`. This means:

- **Every** binding (`{Binding X}`) is compile-checked against its `x:DataType`.
- **Always** set `x:DataType="vm:SomeViewModel"` on the root of a View or DataTemplate.
- **No runtime reflection** for binding paths — fast and safe.
- An incorrect binding path becomes a **build error**, not a silent runtime null.

### ViewLocator

`App.axaml.cs` registers `ViewLocator` as the Application's `IDataTemplate`. Convention:

- `MainWindowViewModel` → `Views/MainWindow.axaml` (replace `ViewModel` with `View` in full type name)
- Currently the only window is resolved explicitly through DI, but `ViewLocator` is in place for future VM-only DataTemplate scenarios.

### Dependency Injection

Configured in `App.axaml.cs : ConfigureServices`. Lifetime conventions:

- **ViewModels → Singleton** (one per app)
- **Views → Transient** (new instance per resolve)

When adding a new VM:

```csharp
services.AddSingleton<MyNewViewModel>();
services.AddTransient<MyView>();
```

### Streaming Architecture (Chat)

`ChatWorkspaceViewModel` is the prototype for streaming patterns in this codebase:

- `IChatStreamChunk` (in `Models/Chat/`) is the abstraction for streamed responses.
- `ChatBlockKind` enumerates block types: `Text`, `Thought`, etc.
- The View uses `Expander` for `Thought` blocks and plain `TextBlock` for `Text` blocks.

When wiring a real LLM transport, replace the `SeedConversation()` seed with a streaming source that pushes `ChatBlockViewModel` updates incrementally.

---

## UI Design Tokens

Global typography is defined in `App.axaml : Application.Styles`. **Do not set `FontSize` inline** unless intentionally overriding these tokens.

| Token              | Selector                              | FontSize | FontFamily                                      | Use for                                                                |
|--------------------|---------------------------------------|---------:|-------------------------------------------------|------------------------------------------------------------------------|
| **UI default**     | `TextBlock` (no class)                | **14px** | Inter (theme default)                           | Labels, headers, chrome, popup items, anything without a specific style |
| **Code**           | `TextBlock.code-text`                 | **12px** | Cascadia Code → Consolas → Menlo → monospace    | Code blocks, monospace snippets, technical text                        |
| **Chat content**   | `TextBlock.assistant-text`, `TextBlock.thinking-body-text` | 12px | Inter                  | Streaming assistant responses, thinking blocks — content, not chrome   |
| **Chat input**     | `TextBox` (default style)             | 18px     | Inter                                           | The composer/draft input — large for ergonomic typing                  |

Apply the **code** style:

```xml
<TextBlock Classes="code-text"
           Text="await Task.Delay(100);"
           TextWrapping="Wrap" />
```

Rules and caveats:

- Existing class-based styles (`.assistant-text`, `.permission-pill-text`, `.model-text`, etc.) set `FontSize` explicitly and **override** the global 14px default for elements that carry those classes. This is intentional.
- `TextBox` is intentionally **not** affected by the 14px default — its dedicated style overrides to 18px to keep the chat composer ergonomic.
- For color tokens, see the hardcoded hex values in `App.axaml` (e.g. `#1F2328`, `#FF6A00`). They are not yet extracted into resources — feel free to extract them when more than two or three places use the same color.

---

## Conventions

### File / folder layout

- One ViewModel per file; filename equals class name (`MyViewModel.cs`).
- Top-level windows live under `Views/`; reusable UserControls under `Controls/`.
- Models go under `Models/<Feature>/` (e.g. `Models/Chat/`).
- Plain POCOs / records in `Models/` — **no Avalonia references** in this layer.

### XAML style conventions

- **Selector naming**: `<Type>.<purpose>` (e.g. `Border.composer-shell`, `Button.toolbar-icon-button`).
- **Place all global styles in `App.axaml`**; only put inline styles on a specific View when it is genuinely local.
- **Prefer class selectors (`.foo`) over inline `Style=...`** so styles are reusable.
- When adding a new global style, append it to `App.axaml : Application.Styles`.

### Code-behind

- Keep code-behind minimal. Pure UI plumbing (popup open/close, key routing) is fine; **logic belongs in the ViewModel**.
- For popup toggles, follow the existing pattern in `ChatWorkspaceControl.axaml.cs : OnPermissionClick` — toggle `IsOpen` and close sibling popups.

### Services

- `Services/` is for cross-cutting app services (transport, persistence, settings).
- Wire services through DI; isolate platform-specific behavior behind an interface.

---

## Common Tasks

### Add a new View + ViewModel

1. Create `ViewModels/MyViewModel.cs : ViewModelBase` with `partial class` and `[ObservableProperty]` / `[RelayCommand]` as needed.
2. Create `Views/MyView.axaml` with `x:Class="AgentOrchestrator.App.Views.MyView"` and `x:DataType="vm:MyViewModel"`.
3. Register in `App.axaml.cs : ConfigureServices`:
   ```csharp
   services.AddSingleton<MyViewModel>();
   services.AddTransient<MyView>();
   ```
4. Navigate to it from `MainWindow` (or another VM) by injecting / resolving.

### Add a new global style

Append a new `<Style Selector="...">` block to `App.axaml : Application.Styles`. Prefer class selectors over type selectors when overriding.

### Add a new design token (color / spacing)

Extract a hardcoded value into `Application.Resources`:

```xml
<Application.Resources>
    <SolidColorBrush x:Key="Brush.Text.Primary">#1F2328</SolidColorBrush>
</Application.Resources>
```

Reference with `{StaticResource Brush.Text.Primary}`.

### Add a code block to chat content

Use the `code-text` style class on a `TextBlock` inside the chat View's DataTemplate. See "UI Design Tokens" above.

---

## Cross-platform Notes

- Single TFM (`net10.0`) — no `#if WINDOWS` blocks needed for the basic app.
- Inter font is registered via `Program.cs : WithInterFont()` — **do not re-register** it.
- For platform-specific behavior (file pickers, system tray, notifications), isolate behind an interface in `Services/` and inject the right implementation per platform.

---

## Testing

No test projects exist yet. When adding tests:

- Use **xUnit** (or NUnit / MSTest) for unit tests.
- Use **Avalonia.Headless** for view-level assertions.
- Place test projects under a top-level `tests/` directory (not yet created).
