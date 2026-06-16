# Agent Orchestrator

A cross-platform desktop application built with Avalonia UI, .NET 10, and the MVVM pattern using CommunityToolkit.Mvvm.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.301 or later)
- An IDE or editor of your choice: Visual Studio 2022, JetBrains Rider, VS Code with C# Dev Kit

## Getting Started

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build -c Debug

# Run the application
dotnet run --project src/AgentOrchestrator.App
```

## Project Structure

```
AgentOrchestrator.slnx
src/
  AgentOrchestrator.App/
    Program.cs                  # Application entry point
    App.axaml / App.axaml.cs    # Application class, DI/IoC setup
    ViewModels/
      ViewModelBase.cs          # Abstract base class for all ViewModels
      MainWindowViewModel.cs    # Main window ViewModel (MVVM demo)
    Views/
      MainWindow.axaml          # Main window UI (XAML)
      MainWindow.axaml.cs       # Main window code-behind
    Converters/                 # Value converters (empty, ready for use)
    Services/                   # Application services (empty, ready for use)
    Models/                     # Data models (empty, ready for use)
    Assets/                     # Icons and static resources
    app.manifest                # Windows application manifest
```

## MVVM Architecture

This project follows the **Model-View-ViewModel (MVVM)** pattern using Microsoft's recommended toolkit:

- **[CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)** — Provides source generators for `[ObservableProperty]` and `[RelayCommand]`, eliminating boilerplate INotifyPropertyChanged code.
- **`ViewModelBase : ObservableObject`** — All ViewModels inherit from this base class, which provides INotifyPropertyChanged implementation.
- **Compiled Bindings** — Avalonia's compiled binding system (`x:DataType`) gives compile-time safety and runtime performance.
- **`ViewLocator`** — Automatically resolves Views from ViewModels by convention (replaces "ViewModel" with "View" in the type name).

### Dependency Injection

The application uses `Microsoft.Extensions.DependencyInjection` for IoC:

- ViewModels are registered as **Singleton** (they live for the lifetime of the app).
- Views are registered as **Transient** (each resolve creates a new instance).
- `MainWindow` receives its `MainWindowViewModel` via constructor injection, resolved by the DI container in `App.axaml.cs`.

## Demo: Main Window

The main window demonstrates the MVVM pattern in action:

- A `TextBox` bound (TwoWay) to the `Name` property in the ViewModel.
- A `Button` bound to the `GreetCommand` relay command.
- A `TextBlock` that displays the `GreetingMessage` — updated when the button is clicked.

Try it: type your name, click "Greet", and see the personalized message appear.

## Cross-Platform Support

This project targets **Desktop only** (Windows, macOS, Linux) via the single `net10.0` target framework. No platform-specific code or RuntimeIdentifier gymnastics are needed.

Future extensions (not included):
- Mobile (iOS/Android) via Avalonia `avalonia.xplat` template
- Browser (WASM) via `Avalonia.Browser`
