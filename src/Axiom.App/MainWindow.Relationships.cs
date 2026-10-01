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

    private async void RelationshipAdd_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("relationship.add");
    private async void RelationshipEdit_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("relationship.edit");

    private async Task<CommandResult> EditSelectedRelationshipAsync()
    {
        if (Relationships.SelectedItem is RelationshipView selected && selected.Relationship.SourceId == _session.DocumentId)
            return await EditRelationshipAsync(selected.Relationship);
        RelationshipStatus.Text = "Select an outgoing relationship to edit from its source document.";
        return new(CommandOutcome.Unavailable, "Select an outgoing relationship first.");
    }

    private async Task<CommandResult> EditRelationshipAsync(DocumentRelationship? existing)
    {
        if (!await ResolvePendingMetadataAsync() || _classificationService is null) return new(CommandOutcome.Cancelled, "Relationship editing cancelled.");
        await RefreshClassificationAsync();
        if (_classificationCatalog is not { } evidence || _session.DocumentId is not { } id || evidence.Find(id) is null || _panelValue?.DocumentId != id) return new(CommandOutcome.Unavailable, "Save a unique source identity first.");
        var candidates = evidence.Documents.Where(document => document.IsEligible && document.DocumentId != id).ToArray();
        if (candidates.Length == 0) { RelationshipStatus.Text = "No unique saved target identities in this workspace."; return new(CommandOutcome.Unavailable, "No unique saved target identities."); }
        var dialog = new RelationshipEditorWindow(id, candidates, existing,
            change => Task.Run(() => _classificationService.ApplyConnectedAsync(evidence, [], [change])));
        await dialog.ShowDialog(this);
        await RefreshClassificationAsync();
        return new(dialog.Committed ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Relationship review closed.");
    }

    private async void RelationshipRemove_OnClick(object? sender, RoutedEventArgs args)
        => await ExecuteCommandAsync("relationship.remove");

    private async Task<CommandResult> RemoveSelectedRelationshipAsync()
    {
        if (Relationships.SelectedItem is not RelationshipView selected || selected.Relationship.SourceId != _session.DocumentId || _recovery is not IConnectedMetadataStore store) return new(CommandOutcome.Unavailable, "Select an outgoing relationship first.");
        if (await ChoiceDialog.ShowAsync(this, "Remove explicit relationship", selected.Display, "Remove", "Cancel") != "Remove") return new(CommandOutcome.Cancelled, "Relationship retained.");
        try
        {
            await store.ApplyConnectedMetadataAsync([], [new(selected.Relationship, selected.Relationship.Revision, true)]);
            await RefreshClassificationAsync();
            return new(CommandOutcome.Completed, "Relationship removed.");
        }
        catch (Exception exception) { RelationshipStatus.Text = exception.Message; return new(CommandOutcome.Failed, exception.Message, exception); }
    }

    private async void Relationships_OnDoubleTapped(object? sender, TappedEventArgs args)
    {
        await ExecuteCommandAsync("navigation.relationship");
    }
    private async void Relationships_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Relationships.SelectedItem is not RelationshipView selected) return;
        args.Handled = true;
        await ExecuteCommandAsync("navigation.relationship");
    }

    internal async Task<CommandResult> FollowRelationshipAsync(DocumentRelationship relationship)
    {
        if (_classificationService is null || _folder is null || !await ResolvePendingMetadataAsync()) return new(CommandOutcome.Cancelled, "Relationship navigation cancelled.");
        if (_sessionTransitionRunning || !await _linkNavigationGate.WaitAsync(0)) return new(CommandOutcome.Unavailable, "Another navigation is running.");
        _navigationRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            await RefreshClassificationAsync();
            if (!_relationships.Any(current => current.RelationshipId == relationship.RelationshipId && current.Revision == relationship.Revision))
            { RelationshipStatus.Text = "Relationship changed; select the refreshed record."; return new(CommandOutcome.Unavailable, RelationshipStatus.Text); }
            var id = _session.DocumentId;
            if (_panelValue?.DocumentId != id) { RelationshipStatus.Text = "Save a unique canonical identity before following identity-bound relationships."; return new(CommandOutcome.Unavailable, RelationshipStatus.Text); }
            var targetId = relationship.SourceId == id ? relationship.TargetId : relationship.TargetId == id ? relationship.SourceId : (Guid?)null;
            var target = targetId is { } endpoint ? _classificationCatalog?.Find(endpoint) : null;
            if (target is null) { RelationshipStatus.Text = "The endpoint is unresolved, ambiguous, or unavailable. No path was guessed."; return new(CommandOutcome.Unavailable, RelationshipStatus.Text); }
            var previous = CurrentNavigationLocation();
            var read = await new MarkdownLinkTargetReader(_files).ReadAsync(_folder, Path.Combine(_folder, target.RelativePath));
            if (read.Status != MarkdownLinkResolutionStatus.Resolved || DocumentIdentityPolicy.FindCanonical(read.Text!) != targetId)
            { RelationshipStatus.Text = "Endpoint changed. Refresh before following."; return new(CommandOutcome.Unavailable, RelationshipStatus.Text); }
            await OpenDocumentAsync(Path.Combine(_folder, target.RelativePath));
            if (_session.DocumentId == targetId && previous is not null)
            {
                _backLocations.Add(previous);
                _forwardLocations.Clear();
                UpdateNavigationCommands();
            }
            return new(_session.DocumentId == targetId ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Relationship navigation reviewed.");
        }
        catch (Exception exception) { RelationshipStatus.Text = exception.Message; return new(CommandOutcome.Failed, exception.Message, exception); }
        finally
        {
            _navigationRunning = false;
            SetSessionCommandsEnabled(true);
            _linkNavigationGate.Release();
        }
    }
}
