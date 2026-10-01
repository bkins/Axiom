using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow : Window
{
    private readonly DocumentService _documents;
    private readonly WorkspaceService _workspaces;
    private readonly WorkspaceSearchService _workspaceSearch;
    private readonly DocumentOutlineService _outline = new();
    private readonly MarkdownStructureService _structureParser = new();
    private DocumentStructure? _structure;
    private readonly MarkdownEditService _markdownEditing = new();
    private readonly MarkdownListEditService _markdownListEditing = new();
    private readonly MarkdownImageReferenceService _markdownImages = new();
    private readonly ApplicationBuildIdentity _buildIdentity = ApplicationBuildIdentity.FromAssembly(typeof(MainWindow).Assembly);
    private readonly IDocumentFileStore _files;
    private readonly IRecoveryStore _recovery;
    private readonly IAppearanceStore _appearanceStore;
    private readonly MarkdownPreviewService _preview;
    private readonly ExternalFileMonitor _monitor;
    private readonly IAxiomLog _log;
    private readonly Func<Task> _markCleanExit;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _outlineTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly ObservableCollection<WorkspaceTreeNode> _workspaceTree = [];
    private readonly ObservableCollection<WorkspaceSearchResult> _workspaceSearchResults = [];
    private readonly ObservableCollection<DocumentOutlineEntry> _outlineEntries = [];
    private readonly ObservableCollection<WorkspacePin> _workspacePins = [];
    private DocumentSession _session;
    private string? _folder;
    private WorkspaceRecord? _activeWorkspace;
    private bool _loading;
    private bool _previewReady;
    private double? _pendingPreviewScrollRatio;
    private int _previewRenderGeneration;
    private bool _closingApproved;
    private IReadOnlyDictionary<Guid, IReadOnlyList<string>> _duplicates = new Dictionary<Guid, IReadOnlyList<string>>();
    private CancellationTokenSource? _workspaceSearchCancellation;
    private int _workspaceSearchGeneration;
    private AppearancePreference _appearance;

    public MainWindow() : this(DesignComposition.Create()) { }

    private MainWindow(DesignComposition composition) : this(
        composition.Documents,
        composition.Workspaces,
        composition.Files,
        composition.State,
        composition.State,
        AppearancePreference.System,
        new MarkdownPreviewService(),
        new ExternalFileMonitor(),
        composition.Log,
        () => Task.CompletedTask)
    { }

    public MainWindow(
        DocumentService documents,
        WorkspaceService workspaces,
        IDocumentFileStore files,
        IRecoveryStore recovery,
        IAppearanceStore appearanceStore,
        AppearancePreference appearance,
        MarkdownPreviewService preview,
        ExternalFileMonitor monitor,
        IAxiomLog log,
        Func<Task> markCleanExit,
        bool readingMode = false)
    {
        _documents = documents;
        _workspaces = workspaces;
        _files = files;
        _workspaceSearch = new WorkspaceSearchService(files);
        _recovery = recovery;
        _operationalPaths = (recovery as SqliteStateStore)?.Paths ?? new AppDataPaths();
        _appearanceStore = appearanceStore;
        _appearance = Enum.IsDefined(appearance) ? appearance : AppearancePreference.System;
        _preview = preview;
        _monitor = monitor;
        _log = log;
        _markCleanExit = markCleanExit;
        _session = documents.NewDocument();
        InitializeComponent();
        InitializeCommands();
        _shortcutBindings = _shortcutConfiguration.Resolve(_commands.Commands);
        InitializeReadingFeedback();
        InitializePreviewSynchronization();
        InitializeFileDrop();
        SizeChanged += (_, _) => ClampHelpPanelWidth();
        InitializeClassification();
        InitializeRelationships();
        InitializeBacklinks();
        InitializeWorkspaceRefresh();
        AddHandler(KeyUpEvent, Window_OnKeyUp, RoutingStrategies.Tunnel);

        DocumentTabs.ItemsSource = _tabs;
        FileTree.ItemsSource = _workspaceTree;
        WorkspaceSearchResults.ItemsSource = _workspaceSearchResults;
        DocumentOutline.ItemsSource = _outlineEntries;
        PinnedDocuments.ItemsSource = _workspacePins;
        BuildIdentityText.Text = _buildIdentity.DisplayText;
        SetRequestedAppearance(_appearance);
        ActualThemeVariantChanged += (_, _) => ApplyAppearanceVisuals();
        Editor.TextChanged += (_, _) => EditorTextChanged();
        Preview.EnvironmentRequested += (_, args) =>
        {
            if (args is WindowsWebView2EnvironmentRequestedEventArgs windows)
                windows.UserDataFolder = Path.Combine(_operationalPaths.Root, "PreviewCache", _buildIdentity.InformationalVersion);
        };
        Preview.AdapterCreated += (_, _) => { _previewReady = true; RenderPreview(); };
        Preview.NavigationCompleted += async (_, args) => await RestorePreviewScrollAsync(args.IsSuccess);
        Preview.NavigationStarted += (_, args) =>
        {
            if (args.Request is { Scheme: "axiom-source" } source)
            {
                args.Cancel = true;
                Dispatcher.UIThread.Post(async () => await HandlePreviewSourceAsync(source));
                return;
            }
            if (args.Request is { Scheme: "axiom-link" } link)
            {
                args.Cancel = true;
                Dispatcher.UIThread.Post(async () => await HandlePreviewLinkAsync(link));
                return;
            }
            if (args.Request is { } uri && uri.Scheme is not "about" and not "data") args.Cancel = true;
        };
        Preview.NewWindowRequested += (_, args) => args.Handled = true;
        _previewTimer.Tick += (_, _) => RenderPreview(preservePosition: true);
        _outlineTimer.Tick += (_, _) => RenderOutline();
        _recoveryTimer.Tick += async (_, _) => await ProtectIfNeededAsync();
        _recoveryTimer.Start();
        _monitor.Changed += (_, path) => Dispatcher.UIThread.Post(async () => await HandleExternalChangeAsync(path));
        _monitor.Renamed += (_, pair) => Dispatcher.UIThread.Post(() => HandleRename(pair.OldPath, pair.NewPath));
        Opened += async (_, _) =>
        {
            try { await InitializeEditorPreferencesAsync(); await InitializeShortcutsAsync(); await InitializeManagementAsync(); await RestoreDraftIfAvailableAsync(); }
            finally { _startupCompleted.TrySetResult(); }
        };
        Closing += MainWindow_Closing;
        AddHandler(KeyDownEvent, Window_OnKeyDown, RoutingStrategies.Tunnel);
        ApplyAppearanceVisuals();
        ActivateTab(AddTab(_session), "New unsaved document");
        ApplyReadingMode(readingMode);
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select a folder for the Markdown workspace", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        try
        {
            await SwitchWorkspaceAsync(folder);
        }
        catch (Exception exception)
        {
            ReportError("workspace.open.failed", exception, folder);
        }
    }

    internal async Task LoadFolderAsync(string folder)
    {
        var snapshot = await PrepareWorkspaceAsync(folder);
        if (snapshot is null) throw new OperationCanceledException("Workspace loading was cancelled or failed; current workspace retained.");
        await AdoptWorkspaceAsync(snapshot);
    }
    private async void FileTree_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_folder is null || FileTree.SelectedItem is not WorkspaceTreeNode { IsDocument: true } selected) return;
        await OpenExplorerPathAsync(Path.Combine(_folder, selected.RelativePath));
    }

    private async void Pin_OnClick(object? sender, RoutedEventArgs e) => await ToggleCurrentPinAsync();

    private async Task ToggleCurrentPinAsync()
    {
        if (_activeWorkspace is null || _session.Path is null || !IsWithinFolder(_session.Path, _activeWorkspace.RootPath)) return;
        var relativePath = WorkspaceService.GetRelativeDocumentPath(_activeWorkspace.RootPath, _session.Path);
        if (_workspacePins.Any(pin => pin.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
            await _workspaces.UnpinAsync(_activeWorkspace, _session.Path);
        else
            await _workspaces.PinAsync(_activeWorkspace, _session.Path);
        await RefreshPinsAsync();
        UpdateChrome(_workspacePins.Any(pin => pin.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            ? $"Pinned {relativePath}."
            : $"Unpinned {relativePath}.");
    }

    private async void RemovePin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_activeWorkspace is null || PinnedDocuments.SelectedItem is not WorkspacePin pin) return;
        await _workspaces.RemovePinAsync(_activeWorkspace, pin);
        await RefreshPinsAsync();
        UpdateChrome($"Removed pin {pin.RelativePath}.");
    }

    private async void PinnedDocuments_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_activeWorkspace is null || PinnedDocuments.SelectedItem is not WorkspacePin pin) return;
        var path = Path.Combine(_activeWorkspace.RootPath, pin.RelativePath);
        if (!await _files.ExistsAsync(path))
        {
            UpdateChrome($"Pinned document is missing: {pin.RelativePath}. Remove the pin when it is no longer needed.");
            return;
        }
        await OpenDocumentAsync(path);
    }

    private async Task RefreshPinsAsync()
    {
        _workspacePins.Clear();
        if (_activeWorkspace is not null)
            foreach (var pin in await _workspaces.GetPinsAsync(_activeWorkspace)) _workspacePins.Add(pin);
        UpdateEmptyStates();
        UpdatePinButton();
    }

    private async void WorkspaceSearch_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchWorkspaceAsync(WorkspaceSearchBox.Text ?? string.Empty);

    private async Task SearchWorkspaceAsync(string query)
    {
        _workspaceSearchCancellation?.Cancel();
        _workspaceSearchCancellation?.Dispose();
        _workspaceSearchCancellation = new CancellationTokenSource();
        var cancellationToken = _workspaceSearchCancellation.Token;
        var generation = ++_workspaceSearchGeneration;
        _workspaceSearchResults.Clear();
        if (_folder is null)
        {
            WorkspaceSearchStatus.Text = "Open a workspace before searching.";
            return;
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            WorkspaceSearchStatus.Text = "Enter text to search the workspace.";
            return;
        }

        WorkspaceSearchStatus.Text = "Searching...";
        try
        {
            var response = await _workspaceSearch.SearchAsync(_folder, query, cancellationToken);
            if (generation != _workspaceSearchGeneration) return;
            foreach (var result in response.Results) _workspaceSearchResults.Add(result);
            var skipped = response.SkippedPaths.Count == 0 ? string.Empty : $" Skipped {response.SkippedPaths.Count} unreadable path{(response.SkippedPaths.Count == 1 ? string.Empty : "s")}.";
            WorkspaceSearchStatus.Text = response.Results.Count == 0
                ? "No matches found." + skipped
                : $"Found {response.Results.Count} match{(response.Results.Count == 1 ? string.Empty : "es")}." + skipped;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer query or workspace selection owns the visible result state.
        }
        catch (Exception exception)
        {
            if (generation != _workspaceSearchGeneration) return;
            WorkspaceSearchStatus.Text = "Workspace search failed.";
            ReportError("workspace.search.failed", exception, _folder);
        }
    }

    private async void WorkspaceSearchResults_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (WorkspaceSearchResults.SelectedItem is WorkspaceSearchResult result)
            await OpenSearchResultAsync(result);
    }

    private async Task OpenSearchResultAsync(WorkspaceSearchResult result)
    {
        await OpenDocumentAsync(result.FullPath);
        if (result.MatchOffset + result.MatchedText.Length <= Editor.Text.Length &&
            Editor.Text.AsSpan(result.MatchOffset, result.MatchedText.Length).Equals(result.MatchedText, StringComparison.OrdinalIgnoreCase))
        {
            Editor.Select(result.MatchOffset, result.MatchedText.Length);
            Editor.ScrollToLine(result.LineNumber);
            Editor.Focus();
            UpdateChrome($"Opened search match at line {result.LineNumber}.");
        }
        else
        {
            UpdateChrome("Opened search result, but the matching text has changed.");
        }
    }

    private void DocumentOutline_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DocumentOutline.SelectedItem is DocumentOutlineEntry entry) NavigateToOutlineEntry(entry);
    }

    private void NavigateToOutlineEntry(DocumentOutlineEntry entry)
    {
        if (entry.Offset < 0 || entry.Offset > Editor.Text.Length) return;
        Editor.TextArea.Caret.Offset = entry.Offset;
        Editor.ScrollToLine(entry.LineNumber);
        Editor.Focus();
        UpdateChrome($"Moved to {entry.Text} at line {entry.LineNumber}.");
    }

    internal async Task OpenDocumentAsync(string path)
    {
        if (!await ResolvePendingMetadataAsync()) return;
        try
        {
            path = Path.GetFullPath(path);
            if (FindTab(path) is { } existing)
            {
                await SelectTabAsync(existing);
                await RecordRecentOpenAsync(path);
                return;
            }
            var session = await _documents.OpenAsync(path);
            if (session.DocumentId is { } id && _duplicates.TryGetValue(id, out var duplicatePaths))
            {
                var choice = await ChoiceDialog.ShowAsync(this, "Duplicate Axiom document identity",
                    $"This document shares {id:D} with:\n\n{string.Join(Environment.NewLine, duplicatePaths)}\n\nAssign this copy a distinct identity before editing?",
                    "Assign new ID", "Cancel");
                if (choice == "Cancel") return;
                _documents.ReassignIdentity(session);
            }
            if (FindTab(path) is { } openedWhileReading)
            {
                await SelectTabAsync(openedWhileReading);
                return;
            }
            if (_initialPlaceholder && _tabs.Count == 1 && !_session.IsDirty && _session.IsNew)
            {
                _selectingTab = true;
                _tabs.Clear();
                _selectingTab = false;
            }
            _initialPlaceholder = false;
            await LoadSessionAsync(session, session.IsDirty ? "Opened with a new identity; save deliberately when ready." : "Opened without changing the file.");
            await RecordRecentOpenAsync(path);
        }
        catch (Exception exception)
        {
            ReportError("document.open.failed", exception, path);
        }
    }

    private async void New_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync("document.new");
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs e) => await ExecuteCommandAsync("document.save");
    private async void SaveAs_OnClick(object? sender, RoutedEventArgs e) => await ExecuteCommandAsync("document.saveAs");

    private async Task<bool> SaveDocumentAsync(bool forceSaveAs, DocumentTab? target = null)
    {
        if (_mutationBlocked) { UpdateChrome("Resolve pending file operations before saving; your buffer remains protected."); return false; }
        var tab = target ?? _activeTab;
        var session = tab.Session;
        await _recoveryGate.WaitAsync();
        var editorEnabled = Editor.IsEnabled;
        var tabsEnabled = DocumentTabs.IsEnabled;
        Editor.IsEnabled = false;
        DocumentTabs.IsEnabled = false;
        try
        {
            if (forceSaveAs || session.Path is null)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Markdown document",
                    SuggestedFileName = session.Path is null ? "Untitled.md" : Path.GetFileName(session.Path),
                    DefaultExtension = "md",
                    FileTypeChoices = [new FilePickerFileType("Markdown files") { Patterns = ["*.md", "*.markdown"] }]
                });
                var path = file?.TryGetLocalPath();
                if (path is null) return false;
                if (FindTab(path) is { } owner && owner != tab)
                {
                    UpdateChrome("That file is already open in another tab. Choose another Save As path.");
                    return false;
                }
                await _documents.SaveAsAsync(session, path);
            }
            else
            {
                await _documents.SaveAsync(session);
            }

            tab.LastProtectedText = null;
            tab.RecoveryStatus = "Recovery ready";
            tab.IsMissing = false;
            RefreshDocumentMonitors();
            UpdateChrome($"Saved {Path.GetFileName(session.Path)} at {DateTime.Now:T}");
            if (session.Path is not null)
            {
                if (_folder is not null && IsWithinFolder(session.Path, _folder))
                    await LoadFolderAsync(_folder);
                else if (_activeWorkspace is null && Path.GetDirectoryName(session.Path) is { } savedFolder)
                    await LoadFolderAsync(savedFolder);
            }
            return true;
        }
        catch (Exception exception)
        {
            ConflictBanner.IsVisible = _session.Conflict is not null;
            ReportError("document.save.failed", exception, session.Path);
            return false;
        }
        finally
        {
            Editor.IsEnabled = editorEnabled;
            DocumentTabs.IsEnabled = tabsEnabled;
            _recoveryGate.Release();
        }
    }

    private async void Undo_OnClick(object? sender, RoutedEventArgs e) => await ExecuteCommandAsync("editor.undo");
    private async void Redo_OnClick(object? sender, RoutedEventArgs e) => await ExecuteCommandAsync("editor.redo");

    private void Format_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string value } || !Enum.TryParse<MarkdownEditKind>(value, out var kind)) return;
        ApplyMarkdownEdit(kind);
    }

    private async void InsertImage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanInsertImageReference())
        {
            UpdateChrome("Save the document inside the active workspace before inserting an image.");
            return;
        }

        var workspaceFolder = await StorageProvider.TryGetFolderFromPathAsync(_activeWorkspace!.RootPath);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert an existing workspace image",
            AllowMultiple = false,
            SuggestedStartLocation = workspaceFolder,
            FileTypeFilter =
            [
                new FilePickerFileType("Supported images")
                {
                    Patterns = [.. MarkdownImageReferenceService.FilePickerPatterns]
                }
            ]
        });
        var selectedFile = files.FirstOrDefault();
        if (selectedFile is null) return;
        if (selectedFile.TryGetLocalPath() is not { } imagePath)
        {
            const string message = "The selected image is not available as a local workspace file.";
            UpdateChrome(message);
            await ChoiceDialog.ShowAsync(this, "Image not inserted", message, "OK");
            return;
        }
        if (!InsertImageReference(imagePath))
            await ChoiceDialog.ShowAsync(this, "Image not inserted", StatusText.Text ?? "The image reference could not be inserted.", "OK");
    }

    private async void Appearance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string value } ||
            !Enum.TryParse<AppearancePreference>(value, ignoreCase: true, out var preference))
            return;
        await ApplyAppearanceAsync(preference);
    }

    private async Task ApplyAppearanceAsync(AppearancePreference preference)
    {
        if (!Enum.IsDefined(preference)) preference = AppearancePreference.System;
        _appearance = preference;
        SetRequestedAppearance(preference);
        ApplyAppearanceVisuals();
        RenderPreview(preservePosition: true);
        try
        {
            await _appearanceStore.SetAppearanceAsync(preference);
            UpdateChrome($"Using {preference} appearance.");
        }
        catch (Exception exception)
        {
            ReportError("appearance.save.failed", exception, null);
        }
    }

    private static void SetRequestedAppearance(AppearancePreference preference)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = preference switch
        {
            AppearancePreference.Light => ThemeVariant.Light,
            AppearancePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void ApplyAppearanceVisuals()
    {
        _persistentHelp?.SetAppearance(_appearance);
        var dark = _appearance == AppearancePreference.Dark ||
            (_appearance == AppearancePreference.System && ActualThemeVariant == ThemeVariant.Dark);
        var page = MakeBrush(dark ? "#111827" : "#FFFFFF");
        var surface = MakeBrush(dark ? "#1F2937" : "#F3F4F6");
        var border = MakeBrush(dark ? "#374151" : "#CBD5E1");
        var text = MakeBrush(dark ? "#E5E7EB" : "#1F2937");
        var warningPage = MakeBrush(dark ? "#451A03" : "#FEF3C7");
        var warningText = MakeBrush(dark ? "#FDE68A" : "#78350F");

        Background = page;
        Foreground = text;
        TopCommandBar.Background = surface;
        TopCommandBar.BorderBrush = border;
        BottomStatusBar.Background = page;
        BottomStatusBar.BorderBrush = border;
        WorkspaceSidebar.BorderBrush = border;
        WorkspaceSplitter.Background = border;
        EditorPreviewSplitter.Background = border;
        Editor.Background = page;
        Editor.Foreground = text;
        ConflictBanner.Background = warningPage;
        ConflictBanner.BorderBrush = MakeBrush(dark ? "#D97706" : "#F59E0B");
        ConflictText.Foreground = warningText;
        AppearanceButton.Content = $"Theme: {_appearance}";
    }

    private static SolidColorBrush MakeBrush(string value) => new(Color.Parse(value));

    private bool InsertImageReference(string imagePath)
    {
        if (_folder is null || _session.Path is null)
        {
            UpdateChrome("Save the document inside the active workspace before inserting an image.");
            return false;
        }

        var result = _markdownImages.CreateInsertion(
            Editor.Text,
            Editor.SelectionStart,
            Editor.SelectionLength,
            _folder,
            _session.Path,
            imagePath);
        if (result.Edit is not { } edit)
        {
            UpdateChrome(result.Error ?? "The image reference could not be inserted.");
            return false;
        }

        Editor.Document.UndoStack.StartUndoGroup();
        try
        {
            Editor.Document.Replace(0, Editor.Document.TextLength, edit.Text);
        }
        finally
        {
            Editor.Document.UndoStack.EndUndoGroup();
        }
        Editor.Select(edit.SelectionStart, edit.SelectionLength);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
        UpdateChrome($"Inserted reference to {Path.GetFileName(imagePath)}. Save deliberately to change the Markdown file.");
        return true;
    }

    private bool ApplyMarkdownEdit(MarkdownEditKind kind)
    {
        var result = _markdownEditing.Apply(Editor.Text, Editor.SelectionStart, Editor.SelectionLength, kind);
        if (!result.Changed)
        {
            UpdateChrome(SelectionTouchesIdentityLine(Editor.Text, Editor.SelectionStart, Editor.SelectionLength)
                ? "The Axiom document identity line cannot be formatted."
                : "No formatting change was needed.");
            return false;
        }

        Editor.Document.UndoStack.StartUndoGroup();
        try
        {
            Editor.Document.Replace(0, Editor.Document.TextLength, result.Text);
        }
        finally
        {
            Editor.Document.UndoStack.EndUndoGroup();
        }
        Editor.Select(result.SelectionStart, result.SelectionLength);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
        UpdateChrome($"Applied {GetMarkdownEditLabel(kind)} formatting.");
        return true;
    }

    private static bool SelectionTouchesIdentityLine(string text, int start, int length)
    {
        if (DocumentIdCodec.Find(text) is null) return false;
        var newlineIndex = text.IndexOf('\n');
        var protectedLength = newlineIndex < 0 ? text.Length : newlineIndex + 1;
        return length == 0 ? start < protectedLength : start < protectedLength && start + length > 0;
    }

    private bool TryHandleMarkdownShortcut(Key key, KeyModifiers modifiers, bool editorHasFocus)
    {
        if (!editorHasFocus || !modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt)) return false;
        var kind = key switch
        {
            Key.B => MarkdownEditKind.Bold,
            Key.I => MarkdownEditKind.Italic,
            Key.K => MarkdownEditKind.Link,
            _ => (MarkdownEditKind?)null
        };
        return kind is not null && ApplyMarkdownEdit(kind.Value);
    }

    private bool IsEditorCommandTarget()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return IsEditorCommandTarget(Editor.IsKeyboardFocusWithin, focused is TextBox);
    }

    internal static bool IsEditorCommandTarget(bool editorContainsFocus, bool focusedElementIsTextBox) =>
        editorContainsFocus && !focusedElementIsTextBox;

    private bool TryHandleMarkdownListKey(Key key, KeyModifiers modifiers, bool editorHasFocus)
    {
        if (!editorHasFocus || modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt)) return false;
        var kind = key switch
        {
            Key.Enter when modifiers == KeyModifiers.None => MarkdownListEditKind.Continue,
            Key.Tab when modifiers == KeyModifiers.None => MarkdownListEditKind.Indent,
            Key.Tab when modifiers == KeyModifiers.Shift => MarkdownListEditKind.Outdent,
            _ => (MarkdownListEditKind?)null
        };
        return kind is not null && ApplyMarkdownListEdit(kind.Value);
    }

    private bool ApplyMarkdownListEdit(MarkdownListEditKind kind)
    {
        var result = _markdownListEditing.Apply(Editor.Text, Editor.SelectionStart, Editor.SelectionLength, kind);
        if (!result.Changed) return false;

        Editor.Document.UndoStack.StartUndoGroup();
        try
        {
            Editor.Document.Replace(0, Editor.Document.TextLength, result.Text);
        }
        finally
        {
            Editor.Document.UndoStack.EndUndoGroup();
        }
        Editor.Select(result.SelectionStart, result.SelectionLength);
        Editor.TextArea.Caret.BringCaretToView();
        UpdateChrome(kind == MarkdownListEditKind.Continue
            ? "Continued Markdown list."
            : $"{(kind == MarkdownListEditKind.Indent ? "Indented" : "Outdented")} Markdown list.");
        return true;
    }

    private static string GetMarkdownEditLabel(MarkdownEditKind kind) => kind switch
    {
        MarkdownEditKind.Heading1 => "heading 1",
        MarkdownEditKind.Heading2 => "heading 2",
        MarkdownEditKind.Heading3 => "heading 3",
        MarkdownEditKind.UnorderedList => "bulleted list",
        MarkdownEditKind.OrderedList => "numbered list",
        MarkdownEditKind.TaskList => "task list",
        MarkdownEditKind.FencedCode => "fenced code block",
        MarkdownEditKind.InlineCode => "inline code",
        _ => kind.ToString().ToLowerInvariant()
    };

    private void Enroll_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_session.DocumentId is not null) return;
        var id = _documents.Enroll(_session);
        _loading = true;
        Editor.Text = _session.Text;
        _loading = false;
        SchedulePreview();
        UpdateChrome($"Assigned document ID {id:D}. Save to write it to Markdown.");
    }

    private async void ResolveConflict_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_session.Conflict is not { } conflict) return;
        var dialog = new ConflictWindow(conflict.LocalText, conflict.ExternalText);
        var result = await dialog.ShowDialog<string>(this);
        if (result == "external")
        {
            _session.Reload(new DocumentFile(_session.Path!, conflict.ExternalText, conflict.ExternalWriteUtc, conflict.ExternalText.Length));
            SetEditorText(_session.Text);
            await _recovery.DeleteDraftAsync(_session.DraftKey);
        }
        else if (result == "local")
        {
            _session.KeepLocalAfterConflict();
        }
        ConflictBanner.IsVisible = _session.Conflict is not null;
        UpdateChrome(result == "cancel" ? "Conflict remains unresolved; both versions are preserved." : "Conflict choice applied. Save deliberately to change the file.");
    }

    private void EditorTextChanged()
    {
        if (_loading) return;
        _session.UpdateText(Editor.Text);
        RefreshIncomingReferences();
        UpdateEmptyStates();
        SchedulePreview();
        ScheduleOutline();
        ScheduleDocumentStatistics();
        UpdateChrome(_session.IsDirty ? "Unsaved changes — recovery protection active" : "No unsaved changes");
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ScheduleOutline()
    {
        _outlineTimer.Stop();
        _outlineTimer.Start();
    }

    private async void RenderPreview(bool preservePosition = false)
    {
        _previewTimer.Stop();
        var generation = ++_previewRenderGeneration;
        if (!_previewReady) return;

        double? scrollRatio = null;
        if (preservePosition)
        {
            try
            {
                var scriptResult = await Preview.InvokeScript("(() => { const root = document.scrollingElement || document.documentElement; const range = Math.max(0, root.scrollHeight - window.innerHeight); return range === 0 ? 0 : root.scrollTop / range; })()");
                if (TryParsePreviewScrollRatio(scriptResult, out var parsed)) scrollRatio = parsed;
                await CapturePreviewAnchorAsync();
            }
            catch (Exception exception)
            {
                _log.Error("preview.scroll.capture.failed", exception, _session.Path);
            }
        }

        if (generation != _previewRenderGeneration) return;
        if (scrollRatio is { } captured) _activeTab.PreviewScrollRatio = captured;
        _pendingPreviewScrollRatio = scrollRatio ?? _activeTab.PreviewScrollRatio;
        _previewLinkToken = Guid.NewGuid().ToString("N");
        ResetPreviewSynchronization();
        _previewLinkStructure = GetCurrentStructure();
        Preview.NavigateToString(_preview.RenderDocument(_previewLinkStructure, _session.Path, _appearance, _activeWorkspace?.RootPath, _previewLinkToken));
    }

    private async Task RestorePreviewScrollAsync(bool navigationSucceeded)
    {
        if (!navigationSucceeded)
        {
            _pendingPreviewScrollRatio = null;
            return;
        }
        if (_pendingHeadingFragment is { Length: > 0 })
        {
            _pendingPreviewScrollRatio = null;
            await RestoreHeadingFragmentAsync();
            return;
        }
        if (_pendingPreviewScrollRatio is not { } ratio) return;
        _pendingPreviewScrollRatio = null;

        try
        {
            if (await RestorePreviewAnchorAsync()) return;
            await Preview.InvokeScript(BuildPreviewScrollRestoreScript(ratio));
        }
        catch (Exception exception)
        {
            _log.Error("preview.scroll.restore.failed", exception, _session.Path);
        }
    }

    private static bool TryParsePreviewScrollRatio(string? scriptResult, out double ratio)
    {
        if (scriptResult is null)
        {
            ratio = 0;
            return false;
        }
        var value = scriptResult.Trim().Trim('"');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio) || !double.IsFinite(ratio))
        {
            ratio = 0;
            return false;
        }
        ratio = Math.Clamp(ratio, 0, 1);
        return true;
    }

    private static string BuildPreviewScrollRestoreScript(double ratio)
    {
        var normalized = Math.Clamp(ratio, 0, 1).ToString("R", CultureInfo.InvariantCulture);
        return $"(() => {{ const root = document.scrollingElement || document.documentElement; const range = Math.max(0, root.scrollHeight - window.innerHeight); root.scrollTop = {normalized} * range; return range === 0 ? 0 : root.scrollTop / range; }})()";
    }

    private void RenderOutline()
    {
        _outlineTimer.Stop();
        _outlineEntries.Clear();
        foreach (var entry in _outline.Parse(GetCurrentStructure())) _outlineEntries.Add(entry);
        OutlineStatus.Text = _outlineEntries.Count == 0
            ? "No headings"
            : $"{_outlineEntries.Count} heading{(_outlineEntries.Count == 1 ? string.Empty : "s")}";
    }

    private async Task ProtectIfNeededAsync()
    {
        await _recoveryGate.WaitAsync();
        try
        {
            if (_recovery is IBatchRecoveryStore batch)
            {
                var snapshots = _tabs.Where(tab =>
                        !string.Equals(tab.LastProtectedText, tab.Session.Text, StringComparison.Ordinal)
                        && (tab.Session.IsDirty || tab.LastProtectedText is not null))
                    .Select(tab => new RecoveryProtectionSnapshot(tab, tab.Session.Text,
                        tab.Session.IsDirty ? new RecoveryDraft(tab.Session.DraftKey, tab.Session.Path,
                            tab.Session.Text, DateTimeOffset.UtcNow, tab.Session.LastKnownWriteUtc, tab.Session.IsNew) : null))
                    .ToArray();
                if (snapshots.Length == 0) return;
                try
                {
                    await batch.ApplyDraftsAsync(snapshots.Where(item => item.Draft is not null).Select(item => item.Draft!).ToArray(),
                        snapshots.Where(item => item.Draft is null).Select(item => item.Tab.Session.DraftKey).ToArray());
                    foreach (var snapshot in snapshots)
                    {
                        snapshot.Tab.LastProtectedText = snapshot.Text;
                        snapshot.Tab.RecoveryStatus = snapshot.Draft is not null ? $"Protected {DateTime.Now:T}" : "Recovery ready";
                    }
                }
                catch (Exception exception)
                {
                    foreach (var snapshot in snapshots) snapshot.Tab.RecoveryStatus = "Recovery protection failed";
                    _log.Error("recovery.save.failed", exception);
                    ShowPersistentFailure("Recovery protection failed: " + exception.Message + " Keep the buffer open and save to an available destination.");
                }
                RecoveryText.Text = _activeTab.RecoveryStatus;
                return;
            }
            foreach (var tab in _tabs.ToArray())
            {
                var session = tab.Session;
                var text = session.Text;
                if (string.Equals(tab.LastProtectedText, text, StringComparison.Ordinal)) continue;
                if (!session.IsDirty && tab.LastProtectedText is null) continue;
                try
                {
                    await _documents.ProtectAsync(session);
                    tab.LastProtectedText = text;
                    tab.RecoveryStatus = session.IsDirty ? $"Protected {DateTime.Now:T}" : "Recovery ready";
                }
                catch (Exception exception)
                {
                    tab.RecoveryStatus = "Recovery protection failed";
                    _log.Error("recovery.save.failed", exception, session.Path);
                    ShowPersistentFailure("Recovery protection failed: " + exception.Message + " Keep the buffer open and save to an available destination.");
                }
            }
            RecoveryText.Text = _activeTab.RecoveryStatus;
        }
        finally { _recoveryGate.Release(); }
    }

    private async Task HandleExternalChangeAsync(string path)
    {
        if (_closingApproved || FindTab(path) is not { } tab) return;
        var session = tab.Session;
        try
        {
            if (!await _files.ExistsAsync(path))
            {
                tab.IsMissing = true;
                RefreshTabLabels();
                if (tab == _activeTab) UpdateChrome("The open file is missing; your buffer is preserved.");
                return;
            }
            tab.IsMissing = false;
            if (!await _documents.CheckExternalChangeAsync(session)) return;
            if (session.Conflict is null)
            {
                tab.Document.Text = session.Text;
                tab.Document.UndoStack.ClearAll();
                tab.Structure = null;
                if (tab == _activeTab)
                {
                    _structure = null;
                    RenderOutline();
                    RenderPreview();
                    UpdateChrome("Reloaded an external file change");
                }
            }
            else
            {
                await ProtectIfNeededAsync();
                if (tab == _activeTab) UpdateChrome("External change conflict — both versions preserved");
            }
            ConflictBanner.IsVisible = _session.Conflict is not null;
            RefreshTabLabels();
        }
        catch (IOException)
        {
            tab.IsMissing = true;
            RefreshTabLabels();
            if (tab == _activeTab) UpdateChrome("The open file was removed or is temporarily unavailable; your buffer is preserved.");
        }
        catch (Exception exception) { ReportError("external.change.failed", exception, path); }
    }

    private void HandleRename(string oldPath, string newPath)
    {
        if (FindTab(oldPath) is not { } tab) return;
        if (FindTab(newPath) is { } collision && collision != tab)
        {
            tab.IsMissing = true;
            RefreshTabLabels();
            UpdateChrome("A renamed path already belongs to another open tab. Both buffers remain open.");
            return;
        }
        tab.Session.UpdatePath(newPath);
        tab.IsMissing = false;
        RefreshDocumentMonitors();
        RefreshTabLabels();
        if (tab == _activeTab) UpdateChrome($"Tracked renamed document: {Path.GetFileName(newPath)}");
        if (_folder is not null) _ = RefreshWorkspaceSafelyAsync();
    }

    private async Task RestoreDraftIfAvailableAsync()
    {
        foreach (var draft in await _recovery.GetRecoverableDraftsAsync())
        {
            var choice = RecoveryChoiceForTesting is { } choose
                ? await choose(draft)
                : await ChoiceDialog.ShowAsync(this, "Recover protected work",
                    $"Axiom protected {draft.FilePath ?? "an untitled document"} from {draft.UpdatedUtc.LocalDateTime:g}. Restore it without overwriting the saved file?",
                    "Restore", "Discard", "Later");
            if (choice == "Discard")
            {
                await _recoveryGate.WaitAsync();
                try { await _recovery.DeleteDraftAsync(draft.DraftKey); }
                finally { _recoveryGate.Release(); }
                continue;
            }
            if (choice != "Restore") continue;
            var savedText = string.Empty;
            try
            {
                if (draft.FilePath is not null && await _files.ExistsAsync(draft.FilePath))
                    savedText = (await _files.ReadAsync(draft.FilePath)).Text;
            }
            catch (IOException exception) { _log.Error("recovery.saved.file.unavailable", exception, draft.FilePath); }
            if (_initialPlaceholder && _tabs.Count == 1 && !_session.IsDirty && _session.IsNew)
            {
                _selectingTab = true;
                _tabs.Clear();
                _selectingTab = false;
            }
            _initialPlaceholder = false;
            await LoadRecoveredSessionAsync(DocumentSession.Recover(draft, savedText));
        }
    }

    private async Task<bool> ConfirmSafeToReplaceSessionAsync()
    {
        var tab = _activeTab;
        if (!tab.Session.IsDirty) return true;
        var choice = UnsavedChoiceForTesting is { } choose
            ? await choose(tab.Session)
            : await ChoiceDialog.ShowAsync(this, "Unsaved changes", $"Save changes to {tab.Label} before continuing?", "Save", "Discard", "Cancel");
        if (choice is not "Save" and not "Discard") return false;
        if (choice == "Save") return await SaveDocumentAsync(false, tab);
        await _recoveryGate.WaitAsync();
        try
        {
            await _documents.DiscardAsync(tab.Session);
            tab.LastProtectedText = null;
            tab.RecoveryStatus = "Recovery ready";
            tab.Structure = null;
            if (tab == _activeTab) SetEditorText(tab.Session.Text);
            else
            {
                tab.Document.Text = tab.Session.Text;
                tab.Document.UndoStack.ClearAll();
            }
            UpdateChrome("Discarded changes in the selected document.");
        }
        finally { _recoveryGate.Release(); }
        return true;
    }

    private DocumentStructure GetCurrentStructure()
    {
        if (_structure is null || !string.Equals(_structure.SourceText, Editor.Text, StringComparison.Ordinal))
            _structure = _structureParser.Parse(Editor.Text, _structure);
        return _structure;
    }

    private void SetEditorText(string text)
    {
        _loading = true;
        Editor.Text = text;
        Editor.Document.UndoStack.ClearAll();
        _loading = false;
        UpdateEmptyStates();
        RenderPreview();
        RenderOutline();
    }

    private void UpdateChrome(string status)
    {
        RefreshTabLabels();
        var name = _session.Path is null ? "Untitled" : Path.GetFileName(_session.Path);
        Title = $"{name}{(_session.IsDirty ? " *" : string.Empty)} — Axiom";
        ShowInformation(status);
        EnrollButton.IsVisible = _session.DocumentId is null;
        UpdatePinButton();
        UpdateDurableStatus();
        ScheduleDocumentStatistics();
        UpdateMetadataPanel();
    }

    private void UpdateDurableStatus()
    {
        WorkspaceContextText.Text = _activeWorkspace is null ? "No workspace" : $"Workspace: {_activeWorkspace.DisplayName}";
        PathText.Text = _session.Path ?? _folder ?? "Unsaved document";
        var identity = _session.DocumentId is null ? "Ordinary Markdown" : "Axiom document";
        var saveState = _session.Conflict is not null ? "Conflict" : _activeTab.IsMissing ? "Missing — buffer retained" : _session.IsDirty ? "Unsaved" : _session.IsNew ? "New" : "Saved";
        DocumentStateText.Text = $"{identity} | {saveState}";
        UpdateImageButton();
    }

    private bool CanInsertImageReference() =>
        _activeWorkspace is not null &&
        _session.Path is not null &&
        File.Exists(_session.Path) &&
        IsWithinFolder(_session.Path, _activeWorkspace.RootPath) &&
        IsMarkdownPath(_session.Path);

    private void UpdateImageButton()
    {
        InsertImageButton.IsEnabled = CanInsertImageReference();
        ToolTip.SetTip(InsertImageCommandHost, InsertImageButton.IsEnabled
            ? "Insert a reference to an existing image inside this workspace"
            : "Save a document inside the active workspace before inserting an image");
    }

    private void UpdateEmptyStates()
    {
        EditorEmptyState.IsVisible = string.IsNullOrEmpty(Editor.Text);
        PinnedEmptyState.IsVisible = _workspacePins.Count == 0;
        WorkspaceEmptyState.IsVisible = _workspaceTree.Count == 0;
        WorkspaceEmptyState.Text = _folder is null
            ? "Open a workspace to browse Markdown documents."
            : "This workspace contains no .md or .markdown documents.";
    }

    private void UpdatePinButton()
    {
        var canPin = _activeWorkspace is not null && _session.Path is not null && IsWithinFolder(_session.Path, _activeWorkspace.RootPath) && IsMarkdownPath(_session.Path);
        PinButton.IsVisible = canPin;
        if (!canPin) return;
        var relativePath = WorkspaceService.GetRelativeDocumentPath(_activeWorkspace!.RootPath, _session.Path!);
        PinButton.Content = _workspacePins.Any(pin => pin.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)) ? "Unpin" : "Pin";
    }

    private void ReportError(string eventName, Exception exception, string? path)
    {
        _log.Error(eventName, exception, path);
        ShowPersistentFailure(exception.Message);
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_closingApproved) return;
        eventArgs.Cancel = true;
        await RunSessionTransitionAsync(async () =>
        {
            await _markCleanExit();
            _closingApproved = true;
            _recoveryTimer.Stop();
            _previewTimer.Stop();
            _outlineTimer.Stop();
            _monitor.Dispose();
            _workspaceRefreshTimer.Stop();
            _workspaceChanges.Dispose();
            _classificationCancellation?.Cancel();
            ++_classificationGeneration;
            _workspaceLoadCancellation?.Cancel();
            ++_workspaceLoadGeneration;
            Close();
        });
    }

    private async void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_sessionTransitionRunning || _navigationRunning || _metadataApplying) return;
        if (e.Key == Key.P && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            e.Handled = true;
            await ShowCommandPaletteAsync();
            return;
        }
        if (await TryExecuteShortcutAsync(e)) return;
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
        {
            e.Handled = true;
            await NavigateHistoryAsync(e.Key == Key.Right);
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Enter && IsEditorCommandTarget())
        {
            e.Handled = true;
            await FollowEditorLinkAsync();
            return;
        }
        if (TryHandleCommandMenuKey(e)) return;
        var editorIsCommandTarget = IsEditorCommandTarget();
        if (TryHandleMarkdownListKey(e.Key, e.KeyModifiers, editorIsCommandTarget))
        {
            e.Handled = true;
            return;
        }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (TryHandleMarkdownShortcut(e.Key, e.KeyModifiers, editorIsCommandTarget))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control)
        {
            await CloseTabAsync(_activeTab);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
        {
            var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            var index = (_tabs.IndexOf(_activeTab) + direction + _tabs.Count) % _tabs.Count;
            await SelectTabAsync(_tabs[index]);
            e.Handled = true;
        }
    }

    private static bool IsWithinFolder(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsMarkdownPath(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    // These members are intentionally internal: the headless UI suite exercises the
    // same controls and application paths without making a testing API public.
    internal IReadOnlyList<string> FolderItemsForTesting => Traverse(_workspaceTree)
        .Where(node => node.IsDocument)
        .Select(node => node.RelativePath)
        .ToArray();
    internal IReadOnlyList<WorkspaceTreeNode> WorkspaceTreeForTesting => _workspaceTree;
    internal string EditorTextForTesting => Editor.Text;
    internal bool IsEnrollAvailableForTesting => EnrollButton.IsVisible;

    internal string? ActiveWorkspaceRootForTesting => _folder;
    internal IReadOnlyList<WorkspaceSearchResult> SearchResultsForTesting => _workspaceSearchResults;
    internal string EditorSelectedTextForTesting => Editor.SelectedText;
    internal int EditorCaretOffsetForTesting => Editor.TextArea.Caret.Offset;
    internal void SetEditorTextForTesting(string text)
    {
        Editor.Text = text;
        RenderOutline();
    }
    internal Task SearchWorkspaceForTestingAsync(string query) => SearchWorkspaceAsync(query);
    internal Task OpenSearchResultForTestingAsync(WorkspaceSearchResult result) => OpenSearchResultAsync(result);
    internal IReadOnlyList<DocumentOutlineEntry> OutlineEntriesForTesting => _outlineEntries;
    internal void NavigateToOutlineEntryForTesting(DocumentOutlineEntry entry) => NavigateToOutlineEntry(entry);
    internal ApplicationBuildIdentity BuildIdentityForTesting => _buildIdentity;
    internal IReadOnlyList<WorkspacePin> WorkspacePinsForTesting => _workspacePins;
    internal Task ToggleCurrentPinForTestingAsync() => ToggleCurrentPinAsync();
    internal string PinButtonTextForTesting => PinButton.Content?.ToString() ?? string.Empty;
    internal string WorkspaceContextForTesting => WorkspaceContextText.Text ?? string.Empty;
    internal string DocumentStateForTesting => DocumentStateText.Text ?? string.Empty;
    internal string PathContextForTesting => PathText.Text ?? string.Empty;
    internal string RecoveryContextForTesting => RecoveryText.Text ?? string.Empty;
    internal Task ProtectIfNeededForTestingAsync() => ProtectIfNeededAsync();
    internal void SetEditorSelectionForTesting(int start, int length) => Editor.Select(start, length);
    internal bool ApplyMarkdownEditForTesting(MarkdownEditKind kind) => ApplyMarkdownEdit(kind);
    internal bool HandleMarkdownShortcutForTesting(Key key, KeyModifiers modifiers, bool editorHasFocus) =>
        TryHandleMarkdownShortcut(key, modifiers, editorHasFocus);
    internal bool HandleMarkdownListKeyForTesting(Key key, KeyModifiers modifiers, bool editorHasFocus) =>
        TryHandleMarkdownListKey(key, modifiers, editorHasFocus);
    internal bool EditorCanUndoForTesting => Editor.CanUndo;
    internal void UndoEditorForTesting() => Editor.Undo();
    internal static bool TryParsePreviewScrollRatioForTesting(string result, out double ratio) =>
        TryParsePreviewScrollRatio(result, out ratio);
    internal static string BuildPreviewScrollRestoreScriptForTesting(double ratio) =>
        BuildPreviewScrollRestoreScript(ratio);
    internal bool InsertImageReferenceForTesting(string imagePath) => InsertImageReference(imagePath);
    internal bool CanInsertImageReferenceForTesting => CanInsertImageReference();
    internal Task ApplyAppearanceForTestingAsync(AppearancePreference preference) => ApplyAppearanceAsync(preference);
    internal AppearancePreference AppearanceForTesting => _appearance;
    internal bool PrimaryCommandsHaveAccessibleHelpForTesting => CommandPanel.Children
        .OfType<Button>()
        .All(button => !string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)) &&
            ToolTip.GetTip(button) is string { Length: > 0 });
    internal Size MeasureTopCommandBarForTesting(double width)
    {
        TopCommandBar.Measure(new Size(width, double.PositiveInfinity));
        TopCommandBar.Arrange(new Rect(0, 0, width, TopCommandBar.DesiredSize.Height));
        return TopCommandBar.Bounds.Size;
    }
    internal bool EditorEmptyStateForTesting => EditorEmptyState.IsVisible;
    internal bool WorkspaceEmptyStateForTesting => WorkspaceEmptyState.IsVisible;
    internal bool PinnedEmptyStateForTesting => PinnedEmptyState.IsVisible;

    private static IEnumerable<WorkspaceTreeNode> Traverse(IEnumerable<WorkspaceTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Traverse(node.Children)) yield return child;
        }
    }

    private static void ApplyExpansion(WorkspaceTreeNode node, IReadOnlySet<string> expandedPaths)
    {
        node.IsExpanded = expandedPaths.Contains(node.RelativePath);
        foreach (var child in node.Children) ApplyExpansion(child, expandedPaths);
    }

    private sealed record DesignComposition(DocumentService Documents, WorkspaceService Workspaces, FileDocumentStore Files, SqliteStateStore State, IAxiomLog Log)
    {
        public static DesignComposition Create()
        {
            var paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), "Axiom-Designer"));
            var log = new NullAxiomLog();
            var state = new SqliteStateStore(paths, log);
            state.InitializeAsync().GetAwaiter().GetResult();
            var files = new FileDocumentStore(log);
            return new DesignComposition(new DocumentService(files, state, state, log), new WorkspaceService(state), files, state, log);
        }
    }
}
