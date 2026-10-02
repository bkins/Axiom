using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private ClassificationService? _classificationService;
    private ClassificationCatalog? _classificationCatalog;
    private readonly ObservableCollection<ClassifiedDocument> _classificationResults = [];
    private CancellationTokenSource? _classificationCancellation;
    private int _classificationGeneration;
    private DocumentClassification? _panelValue;
    private bool _panelLoading;
    private bool _panelPending;
    private bool _metadataApplying;

    private void InitializeClassification()
    {
        if (_recovery is IClassificationStore store && _recovery is IDocumentMetadataStore identities)
            _classificationService = new(_files, identities, store);
        ClassificationResults.ItemsSource = _classificationResults;
        foreach (var field in new[] { MetadataTags, MetadataType, MetadataAliases })
            field.TextChanged += (_, _) =>
            {
                if (!_panelLoading && _panelValue is { } value)
                    _panelPending = (MetadataTags.Text ?? "") != string.Join(Environment.NewLine, value.Tags) ||
                        (MetadataType.Text ?? "") != (value.Type ?? "") ||
                        (MetadataAliases.Text ?? "") != string.Join(Environment.NewLine, value.Aliases);
            };
    }

    private void BeginClassificationWorkspace()
    {
        _classificationCancellation?.Cancel();
        ++_classificationGeneration;
        _classificationCatalog = null;
        _relationships = [];
        _classificationResults.Clear();
    }

    internal async Task RefreshClassificationAsync()
    {
        if (_folder is null || _classificationService is null) { UpdateMetadataPanel(); return; }
        _classificationCancellation?.Cancel();
        _classificationCancellation?.Dispose();
        _classificationCancellation = new();
        var token = _classificationCancellation.Token;
        var generation = ++_classificationGeneration;
        var root = _folder;
        ClassificationStatus.Text = "Loading saved identities and metadata…";
        try
        {
            var catalog = await Task.Run(() => _classificationService.DiscoverAsync(root, token), token);
            var relationships = _recovery is IConnectedMetadataStore connected ? await connected.GetRelationshipsAsync(token) : [];
            if (generation != _classificationGeneration || root != _folder) return;
            _classificationCatalog = catalog;
            _relationships = relationships;
            ApplyClassificationFilter();
            UpdateMetadataPanel();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (generation != _classificationGeneration) return;
            _classificationCatalog = null;
            ClassificationStatus.Text = "Discovery failed: " + exception.Message;
            UpdateMetadataPanel();
        }
    }

    private static string[] PanelSet(string? text) => ClassificationPolicy.NormalizeSet((text ?? "").Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private void UpdateMetadataPanel()
    {
        if (_panelPending || _metadataApplying) return;
        _panelLoading = true;
        try
        {
            var relative = _session.Path is { } path && _folder is not null && IsWithinFolder(path, _folder) ? Path.GetRelativePath(_folder, path) : null;
            var saved = _classificationCatalog?.Documents.FirstOrDefault(document => string.Equals(document.RelativePath, relative, WorkspacePathBoundary.Comparison));
            var liveId = DocumentIdentityPolicy.FindCanonical(_session.Text);
            var eligible = saved?.IsEligible == true && liveId == saved.DocumentId && !_activeTab.IsMissing;
            _panelValue = eligible ? saved!.Classification : null;
            MetadataStatus.Text = $"ID: {_session.DocumentId?.ToString("D") ?? "None"}\n{relative ?? "Outside the active workspace or untitled"}\n{saved?.Status ?? "Refresh to inspect saved identity"}";
            if (saved is not null && liveId != saved.DocumentId) MetadataStatus.Text += "\nUnsaved identity change; save before editing metadata.";
            MetadataTags.Text = string.Join(Environment.NewLine, _panelValue?.Tags ?? []);
            MetadataType.Text = _panelValue?.Type ?? "";
            MetadataAliases.Text = string.Join(Environment.NewLine, _panelValue?.Aliases ?? []);
            MetadataFields.IsEnabled = eligible && !_metadataApplying;
            MetadataEnroll.IsVisible = !eligible && _session.DocumentId is null;
            MetadataSave.IsVisible = !eligible && liveId is not null && liveId != saved?.DocumentId;
            MetadataGuidance.Text = eligible
                ? "Tags group documents (for example: Project, Decision). Type describes this document (for example: Note). Aliases are alternate names used by Classify, not filenames. Enter one tag or alias per line, then Apply. Metadata does not change Markdown."
                : _folder is null || relative is null
                    ? "Open a workspace and a saved Markdown document within it. Metadata belongs to a document's saved Axiom ID."
                    : liveId is null && _session.DocumentId is null
                        ? "This is ordinary Markdown. Click Make Axiom document to add an ID to the editor, then Save document. This deliberately changes the Markdown file; opening it alone does not. After saving, tags, type and aliases become editable."
                        : liveId != saved?.DocumentId
                            ? "Save the document's Axiom ID, then Refresh saved identities. Metadata is disabled while its saved and editor IDs differ."
                            : saved?.Status == "Ambiguous identity"
                                ? "Another accessible file has the same Axiom ID. Resolve the copied identity, then Refresh saved identities; metadata cannot be assigned to an ambiguous document."
                                : _classificationCatalog?.Errors.Count > 0
                                    ? "Identity discovery is incomplete. Resolve the inaccessible or excluded locations, then Refresh saved identities. Details: " + string.Join("; ", _classificationCatalog.Errors.Take(3))
                                    : "Refresh saved identities to inspect this document. Metadata requires a complete canonical Axiom ID on the first line of its saved file; malformed IDs must be corrected before editing metadata.";
        }
        finally { _panelLoading = false; }
        RefreshRelationshipViews();
    }

    private async Task<bool> ResolvePendingMetadataAsync()
    {
        if (_metadataApplying) return false;
        if (!_panelPending) return true;
        var choice = MetadataChoiceForTesting is { } choose ? await choose()
            : await ChoiceDialog.ShowAsync(this, "Pending metadata changes", "Apply or discard the metadata edits before leaving this document?", "Apply", "Discard", "Cancel");
        if (choice == "Apply") return await ApplyMetadataAsync();
        if (choice != "Discard") return false;
        _panelPending = false;
        UpdateMetadataPanel();
        return true;
    }

    internal async Task<bool> ApplyMetadataAsync()
    {
        if (_panelValue is null || _classificationCatalog is null || _classificationService is null || _metadataApplying) return false;
        var tab = _activeTab;
        var id = _panelValue.DocumentId;
        _metadataApplying = true;
        SetSessionCommandsEnabled(false);
        MetadataFields.IsEnabled = false;
        try
        {
            if (DocumentIdentityPolicy.FindCanonical(tab.Session.Text) != id) throw new InvalidOperationException("The active identity changed. Save and refresh before applying.");
            var value = ClassificationPolicy.Normalize(new(id, PanelSet(MetadataTags.Text), MetadataType.Text, PanelSet(MetadataAliases.Text)));
            await Task.Run(() => _classificationService.ApplyAsync(_classificationCatalog, [new(value, _panelValue.Revision)]));
            _panelPending = false;
            MetadataStatus.Text = "Metadata saved; Markdown unchanged.";
            await RefreshClassificationAsync();
            ScheduleBacklinkRebuild();
            return true;
        }
        catch (Exception exception) { MetadataStatus.Text = exception.Message; return false; }
        finally
        {
            _metadataApplying = false;
            SetSessionCommandsEnabled(!_sessionTransitionRunning && !_navigationRunning);
            if (!_panelPending) UpdateMetadataPanel(); else MetadataFields.IsEnabled = true;
        }
    }

    private async void MetadataApply_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("metadata.apply");
    private async void MetadataCancel_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("metadata.cancel");
    private async void ClassificationRefresh_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("metadata.refresh");
    private async void ClassificationFilter_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("metadata.filter");
    private async void ClassificationClear_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("metadata.clearFilter");

    private void ClearClassificationFilter()
    {
        FilterTags.Text = FilterType.Text = FilterText.Text = "";
        ApplyClassificationFilter();
    }

    private void ApplyClassificationFilter()
    {
        _classificationResults.Clear();
        if (_classificationCatalog is null) return;
        try
        {
            var tags = PanelSet(FilterTags.Text);
            foreach (var document in _classificationCatalog.Documents.Where(document => document.IsEligible &&
                ClassificationPolicy.Matches(document.RelativePath, document.Classification!, tags, FilterType.Text, FilterText.Text)))
                _classificationResults.Add(document);
            ClassificationStatus.Text = $"{_classificationResults.Count} matches; {_classificationCatalog.Documents.Count} documents inspected. {_classificationCatalog.Errors.Count} discovery warnings. Double click or Enter to open.";
        }
        catch (Exception exception) { ClassificationStatus.Text = exception.Message; }
    }

    private async void ClassificationResults_OnDoubleTapped(object? sender, TappedEventArgs args) => await ExecuteCommandAsync("navigation.classificationResult");
    private async void ClassificationResults_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        await ExecuteCommandAsync("navigation.classificationResult");
    }
    private async Task<CommandResult> OpenClassificationResultAsync()
    {
        if (_folder is null || ClassificationResults.SelectedItem is not ClassifiedDocument selected) return new(CommandOutcome.Unavailable, "Select a classification result first.");
        var target = Path.Combine(_folder, selected.RelativePath);
        var read = await new MarkdownLinkTargetReader(_files).ReadAsync(_folder, target);
        if (read.Status != MarkdownLinkResolutionStatus.Resolved || DocumentIdentityPolicy.FindCanonical(read.Text!) != selected.DocumentId)
        { ClassificationStatus.Text = "Result changed. Refresh before opening."; return new(CommandOutcome.Unavailable, ClassificationStatus.Text); }
        await OpenDocumentAsync(target);
        return new(_session.Path == target ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Classification result navigation reviewed.");
    }

    private async void MetadataExport_OnClick(object? sender, RoutedEventArgs args)
        => await ExecuteCommandAsync("metadata.export");

    private async Task<CommandResult> ExportMetadataAsync(CancellationToken cancellationToken)
    {
        if (_folder is null || !await ResolvePendingMetadataAsync()) return new(CommandOutcome.Cancelled, "Metadata export cancelled.");
        await RefreshClassificationAsync();
        if (_classificationCatalog is null) return new(CommandOutcome.Unavailable, "Classification catalog unavailable.");
        var transfer = MetadataTransferService.Export(_classificationCatalog, _relationships);
        if (await ChoiceDialog.ShowAsync(this, "Review metadata export", $"Export {transfer.Records.Length} unique saved identities and {transfer.Relationships?.Length ?? 0} relationships in format version 2. Skip {_classificationCatalog.Documents.Count - transfer.Records.Length} ordinary/ambiguous/incomplete documents. Unresolved relationship endpoints retain their IDs. Markdown and recovery text are excluded.", "Export", "Cancel") != "Export") return new(CommandOutcome.Cancelled, "Metadata export review cancelled.");
        var selected = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export metadata", SuggestedFileName = "axiom-metadata.json", DefaultExtension = "json" });
        if (selected?.TryGetLocalPath() is not { } path) return new(CommandOutcome.Cancelled, "Metadata export destination cancelled.");
        if (File.Exists(path) && await ChoiceDialog.ShowAsync(this, "Replace metadata export", path, "Replace", "Cancel") != "Replace") return new(CommandOutcome.Cancelled, "Existing metadata export retained.");
        try { await MetadataTransferService.WriteAsync(path, transfer, cancellationToken); UpdateChrome($"Exported {transfer.Records.Length} metadata records."); return new(CommandOutcome.Completed, "Metadata exported."); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(CommandOutcome.Cancelled, "Metadata export cancelled."); }
        catch (Exception exception) { ReportError("metadata.export.failed", exception, path); return new(CommandOutcome.Failed, exception.Message, exception); }
    }

    private async void MetadataImport_OnClick(object? sender, RoutedEventArgs args)
        => await ExecuteCommandAsync("metadata.import");

    private async Task<CommandResult> ImportMetadataAsync(CancellationToken cancellationToken)
    {
        if (_folder is null || _classificationService is null || !await ResolvePendingMetadataAsync()) return new(CommandOutcome.Cancelled, "Metadata import cancelled.");
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import metadata", AllowMultiple = false });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is not { } path) return new(CommandOutcome.Cancelled, "Metadata import file selection cancelled.");
        try
        {
            var transfer = await Task.Run(() => MetadataTransferService.ReadAsync(path, cancellationToken), cancellationToken);
            await RefreshClassificationAsync();
            if (_classificationCatalog is not { } evidence) return new(CommandOutcome.Unavailable, "Classification catalog unavailable.");
            var proposals = MetadataTransferService.Preview(transfer, evidence, _relationships);
            var dialog = new MetadataImportWindow(proposals, (changes, relationships) => Task.Run(() => _classificationService.ApplyConnectedAsync(evidence, changes, relationships)));
            await dialog.ShowDialog(this);
            await RefreshClassificationAsync();
            if (dialog.Committed) ScheduleBacklinkRebuild();
            UpdateChrome(dialog.Committed ? $"Imported {dialog.CommittedRecords} metadata/relationship replacements; Markdown unchanged." : "Metadata import cancelled; no changes applied.");
            return new(dialog.Committed ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Metadata import review finished.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(CommandOutcome.Cancelled, "Metadata import cancelled."); }
        catch (Exception exception) { ReportError("metadata.import.failed", exception, path); return new(CommandOutcome.Failed, exception.Message, exception); }
    }

    internal Func<Task<string>>? MetadataChoiceForTesting { get; set; }
}
