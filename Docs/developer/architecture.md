# Architecture

## MVVM

- View models inherit from `ViewModelBase`
- Source-generated properties and commands use CommunityToolkit.Mvvm
- View models are partial when they use source generators

## Dependency Injection

- View models are registered as singleton
- Views are registered as transient
- Services are injected through interfaces

## UI

- `MainWindow` is the shell
- `ChatWorkspaceControl` is the main active workspace
- `SettingsWindow` is a separate dialog
- Global styles stay in `App.axaml`

## Current Services

- `MarkdownRenderer` handles Markdown rendering for chat content
