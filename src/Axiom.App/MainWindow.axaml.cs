using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
    private readonly MarkdownEditService _markdownEditing = new();
    private readonly ApplicationBuildIdentity _buildIdentity = ApplicationBuildIdentity.FromAssembly(typeof(MainWindow).Assembly);
    private readonly IDocumentFileStore _files;
    private readonly IRecoveryStore _recovery;
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
    private string? _lastProtectedText;
    private bool _loading;
    private bool _previewReady;
    private bool _closingApproved;
    private IReadOnlyDictionary<Guid, IReadOnlyList<string>> _duplicates = new Dictionary<Guid, IReadOnlyList<string>>();
    private CancellationTokenSource? _workspaceSearchCancellation;
    private int _workspaceSearchGeneration;

    public MainWindow() : this(DesignComposition.Create()) { }

    private MainWindow(DesignComposition composition) : this(
        composition.Documents,
        composition.Workspaces,
        composition.Files,
        composition.State,
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
        MarkdownPreviewService preview,
        ExternalFileMonitor monitor,
        IAxiomLog log,
        Func<Task> markCleanExit)
    {
        _documents = documents;
        _workspaces = workspaces;
        _files = files;
        _workspaceSearch = new WorkspaceSearchService(files);
        _recovery = recovery;
        _preview = preview;
        _monitor = monitor;
        _log = log;
        _markCleanExit = markCleanExit;
        _session = documents.NewDocument();
        InitializeComponent();

        FileTree.ItemsSource = _workspaceTree;
        WorkspaceSearchResults.ItemsSource = _workspaceSearchResults;
        DocumentOutline.ItemsSource = _outlineEntries;
        PinnedDocuments.ItemsSource = _workspacePins;
        BuildIdentityText.Text = _buildIdentity.DisplayText;
        Editor.TextChanged += (_, _) => EditorTextChanged();
        Preview.AdapterCreated += (_, _) => { _previewReady = true; RenderPreview(); };
        Preview.NavigationStarted += (_, args) =>
        {
            if (args.Request is { } uri && uri.Scheme is not "about" and not "data") args.Cancel = true;
        };
        Preview.NewWindowRequested += (_, args) => args.Handled = true;
        _previewTimer.Tick += (_, _) => RenderPreview();
        _outlineTimer.Tick += (_, _) => RenderOutline();
        _recoveryTimer.Tick += async (_, _) => await ProtectIfNeededAsync();
        _recoveryTimer.Start();
        _monitor.Changed += (_, _) => Dispatcher.UIThread.Post(async () => await HandleExternalChangeAsync());
        _monitor.Renamed += (_, pair) => Dispatcher.UIThread.Post(() => HandleRename(pair.OldPath, pair.NewPath));
        Opened += async (_, _) => await RestoreDraftIfAvailableAsync();
        Closing += MainWindow_Closing;
        AddHandler(KeyDownEvent, Window_OnKeyDown, RoutingStrategies.Tunnel);
        LoadSession(_session, "New unsaved document");
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open a Markdown folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        try
        {
            await LoadFolderAsync(folder);
        }
        catch (Exception exception)
        {
            ReportError("workspace.open.failed", exception, folder);
        }
    }

    internal async Task LoadFolderAsync(string folder)
    {
        var normalizedFolder = WorkspaceService.NormalizeRootPath(folder);
        _workspaceSearchCancellation?.Cancel();
        _workspaceSearchResults.Clear();
        WorkspaceSearchStatus.Text = string.Empty;
        var discovery = await _files.ListMarkdownFilesAsync(normalizedFolder);
        var duplicates = await _documents.FindDuplicateIdentitiesAsync(normalizedFolder);
        var workspace = await _workspaces.OpenAsync(normalizedFolder);

        var expandedPaths = Traverse(_workspaceTree).Where(node => node.IsExpanded).Select(node => node.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _folder = workspace.RootPath;
        _activeWorkspace = workspace;
        _workspaceTree.Clear();
        foreach (var node in WorkspaceTreeBuilder.Build(_folder, discovery.Files))
        {
            ApplyExpansion(node, expandedPaths);
            _workspaceTree.Add(node);
        }
        _duplicates = duplicates;
        await RefreshPinsAsync();
        var warnings = new List<string>();
        if (_duplicates.Count != 0)
            warnings.Add($"Identity warning: {_duplicates.Count} copied document ID{(_duplicates.Count == 1 ? string.Empty : "s")} must be made distinct when opened.");
        if (discovery.SkippedDirectories.Count != 0)
            warnings.Add($"Skipped {discovery.SkippedDirectories.Count} inaccessible or linked director{(discovery.SkippedDirectories.Count == 1 ? "y" : "ies")}.");
        DuplicateWarning.Text = string.Join(Environment.NewLine, warnings);
        StatusText.Text = $"Opened workspace {workspace.DisplayName} with {discovery.Files.Count} Markdown file{(discovery.Files.Count == 1 ? string.Empty : "s")}.";
        UpdateDurableStatus();
    }

    private async void FileTree_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_folder is null || FileTree.SelectedItem is not WorkspaceTreeNode { IsDocument: true } selected) return;
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
        await OpenDocumentAsync(Path.Combine(_folder, selected.RelativePath));
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
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
        await OpenDocumentAsync(path);
    }

    private async Task RefreshPinsAsync()
    {
        _workspacePins.Clear();
        if (_activeWorkspace is not null)
            foreach (var pin in await _workspaces.GetPinsAsync(_activeWorkspace)) _workspacePins.Add(pin);
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
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
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
        try
        {
            var session = await _documents.OpenAsync(path);
            if (session.DocumentId is { } id && _duplicates.TryGetValue(id, out var duplicatePaths))
            {
                var choice = await ChoiceDialog.ShowAsync(this, "Duplicate Axiom document identity",
                    $"This document shares {id:D} with:\n\n{string.Join(Environment.NewLine, duplicatePaths)}\n\nAssign this copy a distinct identity before editing?",
                    "Assign new ID", "Cancel");
                if (choice == "Cancel") return;
                _documents.ReassignIdentity(session);
            }
            LoadSession(session, session.IsDirty ? "Opened with a new identity; save deliberately when ready." : "Opened without changing the file.");
        }
        catch (Exception exception)
        {
            ReportError("document.open.failed", exception, path);
        }
    }

    private async void New_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
        LoadSession(_documents.NewDocument(), "New unsaved document");
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs e) => await SaveDocumentAsync(false);
    private async void SaveAs_OnClick(object? sender, RoutedEventArgs e) => await SaveDocumentAsync(true);

    private async Task<bool> SaveDocumentAsync(bool forceSaveAs)
    {
        try
        {
            if (forceSaveAs || _session.Path is null)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Markdown document",
                    SuggestedFileName = _session.Path is null ? "Untitled.md" : Path.GetFileName(_session.Path),
                    DefaultExtension = "md",
                    FileTypeChoices = [new FilePickerFileType("Markdown files") { Patterns = ["*.md", "*.markdown"] }]
                });
                var path = file?.TryGetLocalPath();
                if (path is null) return false;
                await _documents.SaveAsAsync(_session, path);
            }
            else
            {
                await _documents.SaveAsync(_session);
            }

            _lastProtectedText = null;
            _monitor.Watch(_session.Path);
            UpdateChrome($"Saved {Path.GetFileName(_session.Path)} at {DateTime.Now:T}");
            if (_folder is not null && _session.Path is not null && IsWithinFolder(_session.Path, _folder)) await LoadFolderAsync(_folder);
            return true;
        }
        catch (Exception exception)
        {
            ConflictBanner.IsVisible = _session.Conflict is not null;
            ReportError("document.save.failed", exception, _session.Path);
            return false;
        }
    }

    private void Undo_OnClick(object? sender, RoutedEventArgs e) { if (Editor.CanUndo) Editor.Undo(); }
    private void Redo_OnClick(object? sender, RoutedEventArgs e) { if (Editor.CanRedo) Editor.Redo(); }

    private void Format_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string value } || !Enum.TryParse<MarkdownEditKind>(value, out var kind)) return;
        ApplyMarkdownEdit(kind);
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
        SchedulePreview();
        ScheduleOutline();
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

    private void RenderPreview()
    {
        _previewTimer.Stop();
        if (!_previewReady) return;
        Preview.NavigateToString(_preview.RenderDocument(Editor.Text, _session.Path));
    }

    private void RenderOutline()
    {
        _outlineTimer.Stop();
        _outlineEntries.Clear();
        foreach (var entry in _outline.Parse(Editor.Text)) _outlineEntries.Add(entry);
        OutlineStatus.Text = _outlineEntries.Count == 0
            ? "No headings"
            : $"{_outlineEntries.Count} heading{(_outlineEntries.Count == 1 ? string.Empty : "s")}";
    }

    private async Task ProtectIfNeededAsync()
    {
        if (string.Equals(_lastProtectedText, _session.Text, StringComparison.Ordinal)) return;
        if (!_session.IsDirty && _lastProtectedText is null) return;
        try
        {
            await _documents.ProtectAsync(_session);
            _lastProtectedText = _session.Text;
            RecoveryText.Text = _session.IsDirty ? $"Protected {DateTime.Now:T}" : "Recovery ready";
        }
        catch (Exception exception)
        {
            RecoveryText.Text = "Recovery protection failed";
            _log.Error("recovery.save.failed", exception, _session.Path);
        }
    }

    private async Task HandleExternalChangeAsync()
    {
        try
        {
            if (!await _documents.CheckExternalChangeAsync(_session)) return;
            if (_session.Conflict is not null)
            {
                ConflictBanner.IsVisible = true;
                await _documents.ProtectAsync(_session);
                UpdateChrome("External change conflict — both versions preserved");
            }
            else
            {
                SetEditorText(_session.Text);
                UpdateChrome("Reloaded an external file change");
            }
        }
        catch (IOException)
        {
            UpdateChrome("The open file was removed or is temporarily unavailable; your buffer is preserved.");
        }
        catch (Exception exception)
        {
            ReportError("external.change.failed", exception, _session.Path);
        }
    }

    private void HandleRename(string oldPath, string newPath)
    {
        if (_session.Path is null || !Path.GetFullPath(oldPath).Equals(Path.GetFullPath(_session.Path), StringComparison.OrdinalIgnoreCase)) return;
        _session.UpdatePath(newPath);
        _monitor.Watch(newPath);
        UpdateChrome($"Tracked renamed document: {Path.GetFileName(newPath)}");
        if (_folder is not null) _ = LoadFolderAsync(_folder);
    }

    private async Task RestoreDraftIfAvailableAsync()
    {
        var draft = (await _recovery.GetRecoverableDraftsAsync()).FirstOrDefault();
        if (draft is null) return;
        var choice = await ChoiceDialog.ShowAsync(this, "Recover protected work",
            $"Axiom protected a newer working draft from {draft.UpdatedUtc.LocalDateTime:g}. Restore it without overwriting the saved file?",
            "Restore", "Discard", "Later");
        if (choice == "Discard")
        {
            await _recovery.DeleteDraftAsync(draft.DraftKey);
            return;
        }
        if (choice != "Restore") return;
        var savedText = string.Empty;
        if (draft.FilePath is not null && await _files.ExistsAsync(draft.FilePath))
            savedText = (await _files.ReadAsync(draft.FilePath)).Text;
        LoadSession(DocumentSession.Recover(draft, savedText), "Recovered protected work. The saved file has not been overwritten.");
    }

    private async Task<bool> ConfirmSafeToReplaceSessionAsync()
    {
        if (!_session.IsDirty) return true;
        var choice = await ChoiceDialog.ShowAsync(this, "Unsaved changes", "Save changes before leaving this document?", "Save", "Discard", "Cancel");
        if (choice == "Cancel") return false;
        if (choice == "Save") return await SaveDocumentAsync(false);
        await _documents.DiscardAsync(_session);
        return true;
    }

    private void LoadSession(DocumentSession session, string status)
    {
        _session = session;
        _lastProtectedText = null;
        ConflictBanner.IsVisible = session.Conflict is not null;
        SetEditorText(session.Text);
        _monitor.Watch(session.Path);
        UpdateChrome(status);
    }

    private void SetEditorText(string text)
    {
        _loading = true;
        Editor.Text = text;
        Editor.Document.UndoStack.ClearAll();
        _loading = false;
        RenderPreview();
        RenderOutline();
    }

    private void UpdateChrome(string status)
    {
        var name = _session.Path is null ? "Untitled" : Path.GetFileName(_session.Path);
        Title = $"{name}{(_session.IsDirty ? " *" : string.Empty)} — Axiom";
        StatusText.Text = status;
        EnrollButton.IsVisible = _session.DocumentId is null;
        UpdatePinButton();
        UpdateDurableStatus();
    }

    private void UpdateDurableStatus()
    {
        WorkspaceContextText.Text = _activeWorkspace is null ? "No workspace" : $"Workspace: {_activeWorkspace.DisplayName}";
        PathText.Text = _session.Path ?? _folder ?? "Unsaved document";
        var identity = _session.DocumentId is null ? "Ordinary Markdown" : "Axiom document";
        var saveState = _session.Conflict is not null ? "Conflict" : _session.IsDirty ? "Unsaved" : _session.IsNew ? "New" : "Saved";
        DocumentStateText.Text = $"{identity} | {saveState}";
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
        StatusText.Text = exception.Message;
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingApproved) return;
        e.Cancel = true;
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
        _closingApproved = true;
        _recoveryTimer.Stop();
        _previewTimer.Stop();
        _outlineTimer.Stop();
        _monitor.Dispose();
        await _markCleanExit();
        Close();
    }

    private async void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (TryHandleMarkdownShortcut(e.Key, e.KeyModifiers, Editor.IsKeyboardFocusWithin))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.S)
        {
            await SaveDocumentAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            New_OnClick(sender, new RoutedEventArgs());
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
    internal bool EditorCanUndoForTesting => Editor.CanUndo;
    internal void UndoEditorForTesting() => Editor.Undo();

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
