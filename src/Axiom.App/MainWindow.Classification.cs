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
            var saved = _classificationCatalog?.Documents.FirstOrDefault(document => string.Equals(document.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
            var eligible = saved?.IsEligible == true && _session.DocumentId == saved.DocumentId && !_activeTab.IsMissing;
            _panelValue = eligible ? saved!.Classification : null;
            MetadataStatus.Text = $"ID: {_session.DocumentId?.ToString("D") ?? "None"}\n{relative ?? "Outside the active workspace or untitled"}\n{saved?.Status ?? "Refresh to inspect saved identity"}";
            if (saved is not null && _session.DocumentId != saved.DocumentId) MetadataStatus.Text += "\nUnsaved identity change; save before editing metadata.";
            MetadataTags.Text = string.Join(Environment.NewLine, _panelValue?.Tags ?? []);
            MetadataType.Text = _panelValue?.Type ?? "";
            MetadataAliases.Text = string.Join(Environment.NewLine, _panelValue?.Aliases ?? []);
            MetadataFields.IsEnabled = eligible && !_metadataApplying;
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
            if (tab.Session.DocumentId != id) throw new InvalidOperationException("The active identity changed. Save and refresh before applying.");
            var value = ClassificationPolicy.Normalize(new(id, PanelSet(MetadataTags.Text), MetadataType.Text, PanelSet(MetadataAliases.Text)));
            await Task.Run(() => _classificationService.ApplyAsync(_classificationCatalog, [new(value, _panelValue.Revision)]));
            _panelPending = false;
            MetadataStatus.Text = "Metadata saved; Markdown unchanged.";
            await RefreshClassificationAsync();
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

    private async void MetadataApply_OnClick(object? sender, RoutedEventArgs args) => await ApplyMetadataAsync();
    private void MetadataCancel_OnClick(object? sender, RoutedEventArgs args) { _panelPending = false; UpdateMetadataPanel(); }
    private async void ClassificationRefresh_OnClick(object? sender, RoutedEventArgs args) => await RefreshClassificationAsync();
    private void ClassificationFilter_OnClick(object? sender, RoutedEventArgs args) => ApplyClassificationFilter();
    private void ClassificationClear_OnClick(object? sender, RoutedEventArgs args)
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

    private async void ClassificationResults_OnDoubleTapped(object? sender, TappedEventArgs args) => await OpenClassificationResultAsync();
    private async void ClassificationResults_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        await OpenClassificationResultAsync();
    }
    private async Task OpenClassificationResultAsync()
    {
        if (_folder is null || ClassificationResults.SelectedItem is not ClassifiedDocument selected) return;
        var target = Path.Combine(_folder, selected.RelativePath);
        var read = await new MarkdownLinkTargetReader(_files).ReadAsync(_folder, target);
        if (read.Status != MarkdownLinkResolutionStatus.Resolved || DocumentIdCodec.Find(read.Text!) != selected.DocumentId)
        { ClassificationStatus.Text = "Result changed. Refresh before opening."; return; }
        await OpenDocumentAsync(target);
    }

    private async void MetadataExport_OnClick(object? sender, RoutedEventArgs args)
    {
        if (_folder is null || !await ResolvePendingMetadataAsync()) return;
        await RefreshClassificationAsync();
        if (_classificationCatalog is null) return;
        var transfer = MetadataTransferService.Export(_classificationCatalog, _relationships);
        if (await ChoiceDialog.ShowAsync(this, "Review metadata export", $"Export {transfer.Records.Length} unique saved identities and {transfer.Relationships?.Length ?? 0} relationships in format version 2. Skip {_classificationCatalog.Documents.Count - transfer.Records.Length} ordinary/ambiguous/incomplete documents. Unresolved relationship endpoints retain their IDs. Markdown and recovery text are excluded.", "Export", "Cancel") != "Export") return;
        var selected = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export metadata", SuggestedFileName = "axiom-metadata.json", DefaultExtension = "json" });
        if (selected?.TryGetLocalPath() is not { } path) return;
        if (File.Exists(path) && await ChoiceDialog.ShowAsync(this, "Replace metadata export", path, "Replace", "Cancel") != "Replace") return;
        try { await MetadataTransferService.WriteAsync(path, transfer); UpdateChrome($"Exported {transfer.Records.Length} metadata records."); }
        catch (Exception exception) { ReportError("metadata.export.failed", exception, path); }
    }

    private async void MetadataImport_OnClick(object? sender, RoutedEventArgs args)
    {
        if (_folder is null || _classificationService is null || !await ResolvePendingMetadataAsync()) return;
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import metadata", AllowMultiple = false });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            var transfer = await MetadataTransferService.ReadAsync(path);
            await RefreshClassificationAsync();
            if (_classificationCatalog is not { } evidence) return;
            var proposals = MetadataTransferService.Preview(transfer, evidence, _relationships);
            await new MetadataImportWindow(proposals, (changes, relationships) => Task.Run(() => _classificationService.ApplyConnectedAsync(evidence, changes, relationships))).ShowDialog(this);
            await RefreshClassificationAsync();
        }
        catch (Exception exception) { ReportError("metadata.import.failed", exception, path); }
    }

    internal Func<Task<string>>? MetadataChoiceForTesting { get; set; }
}
