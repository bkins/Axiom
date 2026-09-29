using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Interactivity;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private IReadOnlyList<DocumentRelationship> _relationships = [];
    private readonly ObservableCollection<RelationshipView> _relationshipViews = [];

    private void InitializeRelationships() => Relationships.ItemsSource = _relationshipViews;

    private void RefreshRelationshipViews()
    {
        _relationshipViews.Clear();
        var id = _session.DocumentId;
        var saved = id is { } activeId ? _classificationCatalog?.Find(activeId) : null;
        RelationshipAdd.IsEnabled = saved is not null && _panelValue?.DocumentId == id;
        foreach (var relationship in _relationships.Where(relationship => relationship.SourceId == id || relationship.TargetId == id))
        {
            var outgoing = relationship.SourceId == id;
            var endpointId = outgoing ? relationship.TargetId : relationship.SourceId;
            var endpoint = _classificationCatalog?.Find(endpointId);
            var status = endpoint is not null ? "Unique saved identity" : _classificationCatalog?.Documents.Any(document => document.DocumentId == endpointId) == true ? "Ambiguous or unavailable" : "Unresolved endpoint";
            _relationshipViews.Add(new(relationship, outgoing ? "Outgoing" : "Incoming", endpoint?.RelativePath ?? endpointId.ToString("D"), status));
        }
        RelationshipStatus.Text = id is null ? "Save a unique Axiom identity before adding relationships." : $"{_relationshipViews.Count} incoming/outgoing relationships. Missing endpoints remain stored. Double click or Enter to follow.";
    }

    private async void RelationshipAdd_OnClick(object? sender, RoutedEventArgs args) => await EditRelationshipAsync(null);
    private async void RelationshipEdit_OnClick(object? sender, RoutedEventArgs args)
    {
        if (Relationships.SelectedItem is RelationshipView selected && selected.Relationship.SourceId == _session.DocumentId)
            await EditRelationshipAsync(selected.Relationship);
        else RelationshipStatus.Text = "Select an outgoing relationship to edit from its source document.";
    }

    private async Task EditRelationshipAsync(DocumentRelationship? existing)
    {
        if (!await ResolvePendingMetadataAsync() || _classificationService is null) return;
        await RefreshClassificationAsync();
        if (_classificationCatalog is not { } evidence || _session.DocumentId is not { } id || evidence.Find(id) is null || _panelValue?.DocumentId != id) return;
        var candidates = evidence.Documents.Where(document => document.IsEligible && document.DocumentId != id).ToArray();
        if (candidates.Length == 0) { RelationshipStatus.Text = "No unique saved target identities in this workspace."; return; }
        await new RelationshipEditorWindow(id, candidates, existing,
            change => Task.Run(() => _classificationService.ApplyConnectedAsync(evidence, [], [change]))).ShowDialog(this);
        await RefreshClassificationAsync();
    }

    private async void RelationshipRemove_OnClick(object? sender, RoutedEventArgs args)
    {
        if (Relationships.SelectedItem is not RelationshipView selected || selected.Relationship.SourceId != _session.DocumentId || _recovery is not IConnectedMetadataStore store) return;
        if (await ChoiceDialog.ShowAsync(this, "Remove explicit relationship", selected.Display, "Remove", "Cancel") != "Remove") return;
        try
        {
            await store.ApplyConnectedMetadataAsync([], [new(selected.Relationship, selected.Relationship.Revision, true)]);
            await RefreshClassificationAsync();
        }
        catch (Exception exception) { RelationshipStatus.Text = exception.Message; }
    }

    private async void Relationships_OnDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (Relationships.SelectedItem is RelationshipView selected) await FollowRelationshipAsync(selected.Relationship);
    }
    private async void Relationships_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Relationships.SelectedItem is not RelationshipView selected) return;
        args.Handled = true;
        await FollowRelationshipAsync(selected.Relationship);
    }

    internal async Task FollowRelationshipAsync(DocumentRelationship relationship)
    {
        if (_classificationService is null || _folder is null || !await ResolvePendingMetadataAsync()) return;
        if (_sessionTransitionRunning || !await _linkNavigationGate.WaitAsync(0)) return;
        _navigationRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            await RefreshClassificationAsync();
            if (!_relationships.Any(current => current.RelationshipId == relationship.RelationshipId && current.Revision == relationship.Revision))
            { RelationshipStatus.Text = "Relationship changed; select the refreshed record."; return; }
            var id = _session.DocumentId;
            if (_panelValue?.DocumentId != id) { RelationshipStatus.Text = "Save a unique canonical identity before following identity-bound relationships."; return; }
            var targetId = relationship.SourceId == id ? relationship.TargetId : relationship.TargetId == id ? relationship.SourceId : (Guid?)null;
            var target = targetId is { } endpoint ? _classificationCatalog?.Find(endpoint) : null;
            if (target is null) { RelationshipStatus.Text = "The endpoint is unresolved, ambiguous, or unavailable. No path was guessed."; return; }
            var previous = CurrentNavigationLocation();
            var read = await new MarkdownLinkTargetReader(_files).ReadAsync(_folder, Path.Combine(_folder, target.RelativePath));
            if (read.Status != MarkdownLinkResolutionStatus.Resolved || DocumentIdentityPolicy.FindCanonical(read.Text!) != targetId)
            { RelationshipStatus.Text = "Endpoint changed. Refresh before following."; return; }
            await OpenDocumentAsync(Path.Combine(_folder, target.RelativePath));
            if (_session.DocumentId == targetId && previous is not null)
            {
                _backLocations.Add(previous);
                _forwardLocations.Clear();
                UpdateNavigationCommands();
            }
        }
        catch (Exception exception) { RelationshipStatus.Text = exception.Message; }
        finally
        {
            _navigationRunning = false;
            SetSessionCommandsEnabled(true);
            _linkNavigationGate.Release();
        }
    }
}
