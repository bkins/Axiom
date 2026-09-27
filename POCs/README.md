# Axiom Markdown editor UI spikes

These are deliberately small, disposable experiments for choosing Axiom's Windows UI stack. They are not the beginning of the production architecture and intentionally do not introduce a document model, recovery, external-change detection, workspace services, or a shared UI abstraction.

Both spikes provide the same narrow slice:

- open an existing `.md` or `.markdown` file;
- edit it in a line-numbered text editor;
- render a live preview after a 250 ms debounce;
- save back to the same file with the Save button or `Ctrl+S`.

Raw HTML in Markdown is disabled for the preview. The source Markdown is not changed by opening it.

## Prerequisites

- Windows 10 or 11
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime (normally already present on supported Windows installations)

The Avalonia project includes a Windows compatibility manifest. `NativeWebView` is hosted as a native Win32 child control and Avalonia cannot create that host reliably without a supported-Windows declaration in the executable manifest.

## Build

From this directory:

```powershell
dotnet restore .\Axiom.MarkdownEditorSpikes.slnx
dotnet build .\Axiom.MarkdownEditorSpikes.slnx
```

Verified on Windows `10.0.26200` with .NET SDK `10.0.302`: Debug and Release builds completed with zero warnings and zero errors, both apps stayed running in startup smoke tests with `sample.md` loaded, and NuGet reported no known vulnerable direct or transitive packages from the configured sources.

## Run

Each app opens `sample.md` when no path is supplied. Pass any existing Markdown file to open it instead.

```powershell
dotnet run --project .\WpfMarkdownEditor -- .\sample.md
dotnet run --project .\AvaloniaMarkdownEditor -- .\sample.md
```

## Packages used

| Spike | UI | Editor | Preview | Markdown |
|---|---|---|---|---|
| WPF | WPF on .NET 10 | AvalonEdit 6.3.1.120 | Microsoft WebView2 1.0.4191.47 | Markdig 1.4.0 |
| Avalonia | Avalonia 12.1.0 | AvaloniaEdit 12.0.0 | Avalonia `NativeWebView` 12.1.0 | Markdig 1.4.0 |

## Practical tradeoffs observed

| Area | WPF + AvalonEdit + WebView2 | Avalonia + AvaloniaEdit + NativeWebView |
|---|---|---|
| Windows fit | Native Windows desktop stack with mature tooling and direct WebView2 integration. | Works on Windows while preserving a path to other desktop platforms. |
| Spike size | Fewer framework packages and very direct platform APIs. | Slightly more bootstrap and styling setup, though application code remains similar. |
| Editor | AvalonEdit is mature and widely used, but WPF-specific. | AvaloniaEdit is a port of AvalonEdit and keeps much of the same programming model. |
| Preview | WebView2 exposes the Windows browser control directly and has deep Windows-specific APIs. | `NativeWebView` gives a unified Avalonia API over platform-native browser engines; platform behavior and prerequisites remain relevant. |
| Portability | Windows-only by design. | UI and editor are cross-platform; this spike has only been built and exercised on Windows. |
| Risk to evaluate next | Whether Windows-only is acceptable for Axiom's expected lifetime. | Native control hosting/airspace behavior, platform parity, packaging, and the younger editor/webview ecosystem. |

Useful upstream references: [AvalonEdit](https://github.com/icsharpcode/AvalonEdit), [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit), [WebView2](https://learn.microsoft.com/microsoft-edge/webview2/), and [Avalonia NativeWebView](https://docs.avaloniaui.net/controls/web/nativewebview).

## Deliberate omissions

These spikes overwrite the opened file directly and have no Save As, dirty-close prompt, autosave/recovery, file watcher, conflict handling, syntax highlighting, relative-image resolution, synchronized scrolling, tests, or production separation of UI and application behavior. Those omissions keep the experiment focused; they are not recommendations for Axiom.
