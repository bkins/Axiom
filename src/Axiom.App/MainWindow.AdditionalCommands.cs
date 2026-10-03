using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private void InitializeAdditionalCommands()
    {
        InitializeExplorerCommands();
        RegisterCommand("document.cancelExport", "Cancel current document export", null, false, (_, _) =>
        {
            ExportCancelRequested?.Invoke();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Export cancellation requested; committed output is retained."));
        }, _ => new(ExportCancelRequested is not null, "No export is running."), isCancellationControl: true);
        RegisterCommand("workspace.cancelLoading", "Cancel workspace loading", null, false, (_, _) =>
        {
            _workspaceLoadCancellation?.Cancel();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Workspace loading cancellation requested."));
        }, _ => new(_workspaceLoadCancellation is not null, "No workspace load is running."), isCancellationControl: true);
        RegisterCommand("workspace.cancelBacklinks", "Cancel saved Markdown indexing", null, false, (_, _) =>
        {
            CancelBacklinks();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Index cancellation requested; previous results retained."));
        }, isCancellationControl: true);
        RegisterCommand("workspace.search", "Search workspace using search field", null, false,
            (_, _) => SearchWorkspaceAsync(WorkspaceSearchBox.Text ?? string.Empty), WorkspaceCommandAvailability);
        RegisterCommand("navigation.classificationResult", "Open selected classification result", null, false,
            (_, _) => OpenClassificationResultAsync(), _ => new(ClassificationResults.SelectedItem is ClassifiedDocument, "Select a classification result first."));
        RegisterCommand("navigation.pinnedDocument", "Open selected workspace pin", null, false, (_, _) => OpenSelectedPinAsync(),
            _ => new(_activeWorkspace is not null && SelectedWorkspacePin is WorkspacePin, "Select a workspace pin first."));
        RegisterCommand("navigation.searchResult", "Open selected workspace search match", null, false,
            (_, _) => OpenSearchResultAsync((WorkspaceSearchResult)WorkspaceSearchResults.SelectedItem!),
            _ => new(WorkspaceSearchResults.SelectedItem is WorkspaceSearchResult, "Select a workspace search match first."));
        RegisterCommand("navigation.outlineHeading", "Go to selected outline heading", null, false, (_, _) =>
        {
            var entry = (DocumentOutlineEntry)DocumentOutline.SelectedItem!;
            NavigateToOutlineEntry(entry);
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Outline heading selected."));
        }, _ => new(DocumentOutline.SelectedItem is DocumentOutlineEntry entry && entry.Offset >= 0 && entry.Offset <= Editor.Text.Length,
            "Select an available outline heading first."));
        RegisterCommand("navigation.relationship", "Follow selected relationship", null, false,
            (_, _) => FollowRelationshipAsync(((RelationshipView)Relationships.SelectedItem!).Relationship),
            _ => new(Relationships.SelectedItem is RelationshipView, "Select a relationship first."));
        RegisterCommand("navigation.backlink", "Open selected backlink source", null, false,
            (_, _) => OpenBacklinkAsync((WorkspaceLinkReference)Backlinks.SelectedItem!),
            _ => new(Backlinks.SelectedItem is WorkspaceLinkReference, "Select an incoming Markdown reference first."));
        RegisterCommand("workspace.rebuildBacklinks", "Rebuild saved Markdown reference index", null, false,
            (_, _) => RebuildBacklinksAsync(), WorkspaceCommandAvailability);
        RegisterCommand("application.settings", "Configure editor settings", null, false, (_, _) => ConfigureSettingsAsync());
        RegisterCommand("view.dismissStatus", "Dismiss persistent status message", null, false, (_, _) =>
        {
            DismissStatus();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Status dismissed; underlying operation unchanged."));
        });
        RegisterCommand("view.statusDetails", "Review operation status details", null, false, async (_, _) =>
        {
            await ChoiceDialog.ShowAsync(this, "Operation details", _persistentFailure ?? _latestInformation, "Close");
            return new(CommandOutcome.Completed, "Status details reviewed.");
        });
        RegisterCommand("navigation.nextTab", "Select next document tab", null, false, (_, _) => CycleDocumentTabAsync(1));
        RegisterCommand("navigation.previousTab", "Select previous document tab", null, false, (_, _) => CycleDocumentTabAsync(-1));
        RegisterCommand("application.installPortable", "Install verified portable update", null, true, (_, _) => InstallPortableVersionAsync(false),
            _ => new(OperatingSystem.IsWindows(), "Portable Windows updates require Windows."));
        RegisterCommand("application.switchPortable", "Switch compatible portable version", null, true, (_, _) => InstallPortableVersionAsync(true),
            _ => new(OperatingSystem.IsWindows(), "Portable Windows version switching requires Windows."));
        RegisterCommand("application.shortcuts", "Configure keyboard shortcuts", null, false, (_, _) => ConfigureShortcutsAsync());
        RegisterCommand("application.about", "About Axiom", null, false, (_, _) => ShowAboutAsync());
        RegisterCommand("application.diagnostics", "Inspect diagnostics", null, false, (_, _) => ShowDiagnosticsAsync());
        RegisterCommand("application.maintenance", "Inspect state and export verified backup", null, true, (_, _) => ShowMaintenanceAsync());
        RegisterCommand("workspace.open", "Open Markdown workspace", null, false, (_, _) => OpenWorkspacePickerAsync());
        RegisterCommand("workspace.catalog", "Choose or forget remembered workspace", null, true, (_, _) => ShowWorkspaceCatalogAsync());
        RegisterCommand("document.recent", "Open recent document", null, false, (_, _) => ShowRecentDocumentsAsync());
        RegisterCommand("document.removeSelectedPin", "Remove selected workspace pin", null, true, (_, _) => RemoveSelectedPinAsync(),
            _ => new(_activeWorkspace is not null && SelectedWorkspacePin is WorkspacePin, "Select a workspace pin first."));
        RegisterCommand("document.resolveConflict", "Resolve external file conflict", null, false, (_, _) => ResolveConflictAsync(),
            _ => new(_session.Conflict is not null, "No external file conflict to resolve."));
        RegisterCommand("document.enroll", "Make current buffer an Axiom document", null, false, (_, _) => Task.FromResult(EnrollCurrentBuffer()));
        RegisterCommand("document.pin", "Pin or unpin current workspace document", null, true, async (_, _) =>
        {
            await ToggleCurrentPinAsync();
            return new(CommandOutcome.Completed, "Workspace pin updated.");
        }, context => new(context.DocumentPath is not null && _activeWorkspace is not null && IsWithinFolder(context.DocumentPath, _activeWorkspace.RootPath), "Open a saved document inside the active workspace."));
        RegisterCommand("editor.insertImage", "Insert existing workspace image reference", null, false, (_, _) => InsertWorkspaceImageAsync(),
            _ => new(CanInsertImageReference(), CanInsertImageReference() ? null : "Save the current document inside the workspace first."));
        foreach (var preference in Enum.GetValues<AppearancePreference>())
            RegisterCommand("view.appearance." + preference.ToString().ToLowerInvariant(), "Use " + preference + " appearance", null, false, async (_, _) =>
            {
                await ApplyAppearanceAsync(preference);
                return new(CommandOutcome.Completed, "Appearance preference applied.");
            });
        RegisterCommand("workspace.bulkEnroll", "Bulk make Axiom documents", null, true, (_, _) => ShowBulkEnrollmentAsync(), WorkspaceCommandAvailability);
        RegisterCommand("workspace.createDocument", "Create workspace document from blank or template", null, true, (_, cancellation) => CreateFromTemplateAsync(cancellation), WorkspaceCommandAvailability);
        RegisterCommand("metadata.apply", "Apply pending document metadata", null, true, async (_, _) =>
            new(await ApplyMetadataAsync() ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Metadata Apply reviewed."),
            _ => new(_panelPending && _panelValue is not null, "Edit eligible document metadata first."));
        RegisterCommand("metadata.cancel", "Discard pending metadata panel changes", null, false, (_, _) =>
        {
            _panelPending = false;
            UpdateMetadataPanel();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Pending panel changes discarded."));
        });
        RegisterCommand("metadata.refresh", "Refresh workspace classification", null, false, async (_, _) =>
        {
            await RefreshClassificationAsync();
            return new(CommandOutcome.Completed, "Classification refreshed.");
        }, WorkspaceCommandAvailability);
        RegisterCommand("metadata.filter", "Apply classification filter fields", null, false, (_, _) =>
        {
            ApplyClassificationFilter();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Classification filter reviewed."));
        }, WorkspaceCommandAvailability);
        RegisterCommand("metadata.clearFilter", "Clear classification filters", null, false, (_, _) =>
        {
            ClearClassificationFilter();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Classification filters cleared."));
        });
        RegisterCommand("metadata.export", "Review and export workspace metadata", null, true, (_, cancellation) => ExportMetadataAsync(cancellation), WorkspaceCommandAvailability);
        RegisterCommand("metadata.import", "Review and import workspace metadata", null, true, (_, cancellation) => ImportMetadataAsync(cancellation), WorkspaceCommandAvailability);
        RegisterCommand("relationship.add", "Add directed document relationship", null, true, (_, _) => EditRelationshipAsync(null),
            _ => new(_session.DocumentId is { } id && _classificationCatalog?.Find(id) is not null, "Save a unique Axiom source identity first."));
        RegisterCommand("relationship.edit", "Edit selected outgoing relationship", null, true, (_, _) => EditSelectedRelationshipAsync(), OutgoingRelationshipAvailability);
        RegisterCommand("relationship.remove", "Remove selected outgoing relationship", null, true, (_, _) => RemoveSelectedRelationshipAsync(), OutgoingRelationshipAvailability);
        RegisterCommand("workspace.importMarkdown", "Copy Markdown into workspace", null, true, (_, cancellation) => ImportIntoWorkspaceAsync(false, cancellation), WorkspaceCommandAvailability);
        RegisterCommand("workspace.importImage", "Import image into workspace", null, true, (_, cancellation) => ImportIntoWorkspaceAsync(true, cancellation),
            _ => new(CanInsertImageReference(), CanInsertImageReference() ? null : "Save the current document inside the active workspace first."));
        RegisterCommand("view.scrollSync", "Toggle synchronized preview scrolling", null, false, (_, _) =>
        {
            ToggleScrollSynchronization();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Scroll synchronization changed."));
        });
        RegisterCommand("view.previewAtCaret", "Show preview at editor caret", null, false, async (_, _) =>
        {
            await ScrollPreviewToSourceAsync(Editor.CaretOffset);
            return new(CommandOutcome.Completed, "Preview position reviewed.");
        });
        foreach (var extension in new[] { "docx", "html", "txt", "pdf", "epub", "odt" })
            RegisterCommand("document.export." + extension, "Export current buffer to " + extension.ToUpperInvariant(), null, true,
                (_, cancellation) => ExportCurrentBufferAsync(extension, cancellation),
                _ => new(extension != "pdf" || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux(), "PDF export is unavailable on this platform."));
        foreach (var kind in new[] { DocumentMutationKind.CreateFolder, DocumentMutationKind.Rename, DocumentMutationKind.Move, DocumentMutationKind.Duplicate, DocumentMutationKind.Recycle })
            RegisterCommand("workspace.manage." + kind.ToString().ToLowerInvariant(), kind + " workspace document", null, true,
                (context, cancellation) => ManageDocumentAsync(kind, cancellation, ExplorerCommandNode(context), kind == DocumentMutationKind.Move ? _requestedMoveDestination : null),
                context => new(_activeWorkspace is not null && context.WorkspaceId == _activeWorkspace.WorkspaceId && (kind == DocumentMutationKind.CreateFolder ||
                    context.ExplorerIsFolder && kind is DocumentMutationKind.Move or DocumentMutationKind.Rename ||
                    !context.ExplorerIsFolder && (context.ExplorerRelativePath is not null || context.DocumentPath is not null)),
                    "Select a workspace document, or a folder for Rename/Move."));
        foreach (var kind in Enum.GetValues<MarkdownEditKind>())
            RegisterCommand("editor.format." + kind.ToString().ToLowerInvariant(), "Format " + GetMarkdownEditLabel(kind), null, false,
                (_, _) => Task.FromResult(new CommandResult(ApplyMarkdownEdit(kind) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Formatting reviewed.")));
        RegisterCommand("document.close", "Close document tab", null, false, async (context, _) =>
        {
            var tab = _tabs.FirstOrDefault(candidate => candidate.Session.DraftKey == context.DraftKey);
            if (tab is null) return new(CommandOutcome.Unavailable, "The requested document tab is no longer open.");
            return new(await CloseTabAsync(tab) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Close tab workflow finished.");
        });
        RegisterCommand("navigation.back", "Navigate back", null, false, async (_, _) =>
        {
            await NavigateHistoryAsync(false);
            return new(CommandOutcome.Completed, "Back navigation reviewed.");
        }, _ => new(_backLocations.Count > 0, _backLocations.Count > 0 ? null : "No previous document location."));
        RegisterCommand("navigation.forward", "Navigate forward", null, false, async (_, _) =>
        {
            await NavigateHistoryAsync(true);
            return new(CommandOutcome.Completed, "Forward navigation reviewed.");
        }, _ => new(_forwardLocations.Count > 0, _forwardLocations.Count > 0 ? null : "No forward document location."));
        RegisterCommand("navigation.followLink", "Follow Markdown link at caret", null, false, async (_, _) =>
        {
            await FollowEditorLinkAsync();
            return new(CommandOutcome.Completed, "Link navigation reviewed.");
        }, _ => new(GetCurrentStructure().FindLinkAt(Editor.CaretOffset) is not null, "Place the caret in a Markdown link."));
        RegisterCommand("workspace.refresh", "Refresh workspace", null, false, async (_, _) =>
        {
            await RefreshWorkspaceSafelyAsync();
            return new(CommandOutcome.Completed, "Workspace refresh reviewed.");
        }, WorkspaceCommandAvailability);
    }

    private CommandAvailability WorkspaceCommandAvailability(CommandContext context) =>
        new(context.WorkspaceId is not null, context.WorkspaceId is null ? "Open a workspace first." : null);

    private CommandAvailability OutgoingRelationshipAvailability(CommandContext context) =>
        new(Relationships.SelectedItem is RelationshipView selected && selected.Relationship.SourceId == _session.DocumentId,
            "Select an outgoing relationship from its source document first.");
}
