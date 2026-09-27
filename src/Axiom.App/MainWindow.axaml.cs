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
    private readonly IDocumentFileStore _files;
    private readonly IRecoveryStore _recovery;
    private readonly MarkdownPreviewService _preview;
    private readonly ExternalFileMonitor _monitor;
    private readonly IAxiomLog _log;
    private readonly Func<Task> _markCleanExit;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly ObservableCollection<WorkspaceTreeNode> _workspaceTree = [];
    private DocumentSession _session;
    private string? _folder;
    private string? _lastProtectedText;
    private bool _loading;
    private bool _previewReady;
    private bool _closingApproved;
    private IReadOnlyDictionary<Guid, IReadOnlyList<string>> _duplicates = new Dictionary<Guid, IReadOnlyList<string>>();

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
        _recovery = recovery;
        _preview = preview;
        _monitor = monitor;
        _log = log;
        _markCleanExit = markCleanExit;
        _session = documents.NewDocument();
        InitializeComponent();

        FileTree.ItemsSource = _workspaceTree;
        Editor.TextChanged += (_, _) => EditorTextChanged();
        Preview.AdapterCreated += (_, _) => { _previewReady = true; RenderPreview(); };
        Preview.NavigationStarted += (_, args) =>
        {
            if (args.Request is { } uri && uri.Scheme is not "about" and not "data") args.Cancel = true;
        };
        Preview.NewWindowRequested += (_, args) => args.Handled = true;
        _previewTimer.Tick += (_, _) => RenderPreview();
        _recoveryTimer.Tick += async (_, _) => await ProtectIfNeededAsync();
        _recoveryTimer.Start();
        _monitor.Changed += (_, _) => Dispatcher.UIThread.Post(async () => await HandleExternalChangeAsync());
        _monitor.Renamed += (_, pair) => Dispatcher.UIThread.Post(() => HandleRename(pair.OldPath, pair.NewPath));
        Opened += async (_, _) => await RestoreDraftIfAvailableAsync();
        Closing += MainWindow_Closing;
        KeyDown += Window_OnKeyDown;
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
        var discovery = await _files.ListMarkdownFilesAsync(normalizedFolder);
        var duplicates = await _documents.FindDuplicateIdentitiesAsync(normalizedFolder);
        var workspace = await _workspaces.OpenAsync(normalizedFolder);

        var expandedPaths = Traverse(_workspaceTree).Where(node => node.IsExpanded).Select(node => node.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _folder = workspace.RootPath;
        _workspaceTree.Clear();
        foreach (var node in WorkspaceTreeBuilder.Build(_folder, discovery.Files))
        {
            ApplyExpansion(node, expandedPaths);
            _workspaceTree.Add(node);
        }
        _duplicates = duplicates;
        var warnings = new List<string>();
        if (_duplicates.Count != 0)
            warnings.Add($"Identity warning: {_duplicates.Count} copied document ID{(_duplicates.Count == 1 ? string.Empty : "s")} must be made distinct when opened.");
        if (discovery.SkippedDirectories.Count != 0)
            warnings.Add($"Skipped {discovery.SkippedDirectories.Count} inaccessible or linked director{(discovery.SkippedDirectories.Count == 1 ? "y" : "ies")}.");
        DuplicateWarning.Text = string.Join(Environment.NewLine, warnings);
        PathText.Text = _folder;
        StatusText.Text = $"Opened workspace {workspace.DisplayName} with {discovery.Files.Count} Markdown file{(discovery.Files.Count == 1 ? string.Empty : "s")}.";
    }

    private async void FileTree_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_folder is null || FileTree.SelectedItem is not WorkspaceTreeNode { IsDocument: true } selected) return;
        if (!await ConfirmSafeToReplaceSessionAsync()) return;
        await OpenDocumentAsync(Path.Combine(_folder, selected.RelativePath));
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
        UpdateChrome(_session.IsDirty ? "Unsaved changes — recovery protection active" : "No unsaved changes");
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RenderPreview()
    {
        _previewTimer.Stop();
        if (!_previewReady) return;
        Preview.NavigateToString(_preview.RenderDocument(Editor.Text, _session.Path));
    }

    private async Task ProtectIfNeededAsync()
    {
        if (!_session.IsDirty || string.Equals(_lastProtectedText, _session.Text, StringComparison.Ordinal)) return;
        try
        {
            await _documents.ProtectAsync(_session);
            _lastProtectedText = _session.Text;
            RecoveryText.Text = $"Protected {DateTime.Now:T}";
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
    }

    private void UpdateChrome(string status)
    {
        var name = _session.Path is null ? "Untitled" : Path.GetFileName(_session.Path);
        Title = $"{name}{(_session.IsDirty ? " *" : string.Empty)} — Axiom";
        StatusText.Text = status;
        PathText.Text = _session.Path ?? _folder ?? "Unsaved document";
        EnrollButton.IsVisible = _session.DocumentId is null;
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
        _monitor.Dispose();
        await _markCleanExit();
        Close();
    }

    private async void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
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
