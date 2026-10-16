using Axiom.Core;

namespace Axiom.App;

internal static class OfflineHelp
{
    internal static Avalonia.Controls.Button TopicButton(Avalonia.Controls.Window owner, string context, AppearancePreference appearance = AppearancePreference.System)
    {
        var button = new Avalonia.Controls.Button { Name = "ContextualHelp", Content = "Help" };
        async Task ShowAsync() => await new HelpWindow(context,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom"), appearance).ShowDialog(owner);
        button.Click += async (_, _) => await ShowAsync();
        owner.KeyDown += async (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.F1) return;
            args.Handled = true;
            await ShowAsync();
        };
        return button;
    }

    public static string Read(IEnumerable<AxiomCommand>? commands = null)
    {
        using var stream = typeof(OfflineHelp).Assembly.GetManifestResourceStream("Axiom.App.Help.UserGuide.md")
            ?? throw new InvalidOperationException("The generated offline User Guide resource is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        text = text.StartsWith("<!-- Generated", StringComparison.Ordinal) ? text[(text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..] : text;
        if (commands is null) return text;
        var guide = new MarkdownStructureService().Parse(text);
        var reference = new System.Text.StringBuilder("\n\n## All commands and default shortcuts\n\nFind these actions in the Command Palette (Ctrl+Shift+P). Unavailable actions explain why they cannot run. These are default bindings; Keyboard Shortcuts shows your current overrides. Follow the feature topic for instructions and limits.\n\n| Command | Default shortcut | Feature Help |\n| --- | --- | --- |\n");
        foreach (var command in commands.OrderBy(command => command.Title, StringComparer.OrdinalIgnoreCase))
        {
            var context = CommandTopic(command.Id);
            var heading = guide.Headings.FirstOrDefault(heading => heading.Text.Equals(context, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Feature Help topic is missing: " + context);
            reference.AppendLine($"| {command.Title.Replace("|", "\\|")} | {command.DefaultShortcut ?? "—"} | [{heading.Text}](#{heading.Fragment}) |");
        }
        return text + reference;
    }

    internal static string CommandTopic(string id) => id switch
    {
        "document.cpAnalysis" or "document.cpEdits" or "document.cpConnection" or "help.cp" => "CP connection and reviewed analysis"
      , "workspace.cpAnalysis" or "help.semantic" => "Semantic search: step-by-step test"
      , "workspace.git" => "Discovery and embedded Git terminal"
      , "document.history" or "document.checkpoint" or "workspace.historySetup" => "Document history, comparison and restore"
      , "document.resolveConflict" => "External changes"
      , "workspace.deletedDocuments" => "Deleted documents and recorded folder structure"
      , "document.enroll" or "workspace.bulkEnroll" => "Axiom identity"
      , "document.pin" or "document.removeSelectedPin" or "workspace.pinItem" or "navigation.pinnedDocument" => "Pin important documents"
      , "document.recent" => "Recent documents"
      , "document.structure" or "navigation.outlineHeading" or "view.outline" => "Navigate by document outline"
      , "workspace.search" or "navigation.searchResult" or "view.search" or "view.discoveryPlacement" or "navigation.returnToDiscovery" => "Discovery and embedded Git terminal"
      , "workspace.rebuildBacklinks" or "workspace.cancelBacklinks" or "navigation.backlink" or "view.backlinks" => "Incoming Markdown references"
      , "navigation.relationship" or "view.relationships" or "help.relationships" => "Explicit relationships"
      , "navigation.classificationResult" or "view.metadata" or "view.classification" or "help.metadata" => "Document metadata and classification"
      , "navigation.nextTab" or "navigation.previousTab" => "Work with document tabs"
      , "workspace.open" or "workspace.catalog" or "workspace.cancelLoading" or "workspace.refresh" or "help.workspace" => "Workspace loading, reading mode, and Help"
      , "workspace.createDocument" => "Templates and named creation"
      , "workspace.create" or "document.new" => "Create documents and workspaces"
      , "workspace.importMarkdown" or "workspace.importImage" or "editor.pasteImage" or "editor.insertImage" => "Drop files and import assets"
      , "workspace.filter" or "workspace.copyFile" or "workspace.pasteFile" or "workspace.copyPath" or "workspace.showInExplorer" or "workspace.showFolderInExplorer" => "Workspace navigation and safe moves"
      , "application.about" => "Identify the running build"
      , "application.settings" or "view.nonPrinting" => "Settings and editor preferences"
      , "application.shortcuts" => "Keyboard shortcut settings"
      , "application.toolbar" or "view.resetLayout" => "Toolbar and viewing modes"
      , "application.diagnostics" or "application.maintenance" or "application.restoreState" or "application.installPortable" or "application.switchPortable" => "Diagnostics, backups, and versions"
      , "application.install" or "application.checkUpdate" => "Per-user installation and trusted updates"
      , "view.statusDetails" or "view.dismissStatus" => "Document statistics and operation feedback"
      , "view.scrollSync" or "view.previewAtCaret" => "Preview navigation and synchronized scrolling"
      , "view.reading" => "Workspace loading, reading mode, and Help"
      , "editor.insertSymbol" => "Authoring assistance: spelling, images, symbols and callouts"
      , _ when id.StartsWith("document.export.", StringComparison.Ordinal) || id == "document.cancelExport" => id.EndsWith("pdf", StringComparison.Ordinal) ? "PDF export" : id.EndsWith("txt", StringComparison.Ordinal) ? "Plain-text export" : id.EndsWith("epub", StringComparison.Ordinal) || id.EndsWith("odt", StringComparison.Ordinal) ? "EPUB and OpenDocument Text export" : "Export to Word and HTML"
      , _ when id.StartsWith("metadata.", StringComparison.Ordinal) => "Document metadata and classification"
      , _ when id.StartsWith("relationship.", StringComparison.Ordinal) => "Explicit relationships"
      , _ when id.StartsWith("workspace.manage.", StringComparison.Ordinal) => "Manage documents and collections"
      , _ when id.StartsWith("workspace.batch.", StringComparison.Ordinal) => "Workspace batch rename, folder copy and export"
      , _ when id.StartsWith("view.mode.", StringComparison.Ordinal) || id.StartsWith("view.appearance.", StringComparison.Ordinal) => "Toolbar and viewing modes"
      , _ when id.StartsWith("document.moveTab", StringComparison.Ordinal) => "Daily authoring: tab order, column selection and document details"
      , _ when id.StartsWith("document.", StringComparison.Ordinal) || id.StartsWith("editor.", StringComparison.Ordinal) => "Open and edit"
      , _ when id.StartsWith("navigation.", StringComparison.Ordinal) => "Follow Markdown links"
      , _ when id.StartsWith("help.", StringComparison.Ordinal) => "Searchable Help while working"
      , _ => throw new InvalidOperationException("Command needs a feature Help mapping: " + id)
    };

    public static DocumentHeading? FindTopic(DocumentStructure guide, string? context) => string.IsNullOrWhiteSpace(context) ? guide.Headings.FirstOrDefault() :
        guide.Headings.FirstOrDefault(heading => heading.Text.Contains(context, StringComparison.OrdinalIgnoreCase)) ?? guide.Headings.FirstOrDefault();
}
