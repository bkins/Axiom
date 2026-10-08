using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly EnglishSpellingService _spelling = new();
    private readonly CSharpSourceColorizer _csharpColorizer = new();
    private readonly EndOfLineElementGenerator _endOfLineMarkers = new();
    private readonly SpellingUnderlineRenderer _spellingRenderer = new();
    private readonly HashSet<string> _ignoredSpelling = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _spellingCancellation;

    private void InitializeAuthoring()
    {
        RegisterCommand("editor.copy", "Copy selection", null, false, (_, _) => { Editor.Copy(); return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Copied selection.")); }, _ => new(Editor.CanCopy, "Select editor text first."));
        RegisterCommand("editor.cut", "Cut selection", null, false, (_, _) => { Editor.Cut(); return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Cut selection.")); }, _ => new(Editor.CanCut && !SelectionTouchesIdentityLine(Editor.Text, Editor.SelectionStart, Editor.SelectionLength), "Select text outside the identity line."));
        RegisterCommand("editor.paste", "Paste text or image", null, false, (_, cancellation) => PasteEditorClipboardAsync(cancellation), _ => new(!SelectionTouchesIdentityLine(Editor.Text, Editor.SelectionStart, Editor.SelectionLength), "Choose a position outside the identity line."));
        RegisterCommand("view.nonPrinting", "Toggle non-printing characters", "Ctrl+Shift+N", false, async (_, _) =>
        {
            var value = _editorPreferences with { ShowNonPrintingCharacters = !_editorPreferences.ShowNonPrintingCharacters };
            if (ManagementState is null) return new(CommandOutcome.Unavailable, "Settings storage is unavailable.");
            await ManagementState.SetEditorPreferencesAsync(value, _appearance);
            ApplyEditorPreferences(value);
            return new(CommandOutcome.Completed, "Non-printing characters toggled.");
        });
        RegisterCommand("editor.pasteImage", "Paste clipboard image into workspace assets", null, true, (_, cancellation) => PasteClipboardImageAsync(cancellation),
            _ => new(CanInsertImageReference(), "Save the current document inside the active workspace first."));
        RegisterCommand("editor.insertSymbol", "Insert symbol or emoji", null, false, async (_, _) =>
        {
            var source = Editor.Text; var start = Editor.SelectionStart; var length = Editor.SelectionLength;
            var symbol = await new UnicodePickerWindow().ShowDialog<string?>(this);
            if (symbol is null) return new(CommandOutcome.Cancelled, "Symbol insertion cancelled.");
            if (source != Editor.Text || SelectionTouchesIdentityLine(source, start, length)) return new(CommandOutcome.Cancelled, "Buffer changed or identity line selected.");
            Editor.Document.Replace(start, length, symbol);
            Editor.Select(start + symbol.Length, 0); Editor.TextArea.Focus();
            return new(CommandOutcome.Completed, "Inserted Unicode symbol.");
        });
        Editor.TextArea.TextView.BackgroundRenderers.Add(_spellingRenderer);
        Editor.TextArea.TextView.LineTransformers.Add(_csharpColorizer);
        Editor.TextArea.TextView.ElementGenerators.Add(_endOfLineMarkers);
        Editor.ContextRequested += Editor_OnContextRequested;
        Closed += (_, _) => _spellingCancellation?.Cancel();
    }

    private async void ScheduleSpelling()
    {
        _spellingCancellation?.Cancel();
        _spellingCancellation?.Dispose();
        var cancellation = _spellingCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _spellingRenderer.Issues = [];
        _gitView?.RefreshAvailability();
        Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        if (!_editorPreferences.SpellCheck) return;
        var source = Editor.Text;
        var document = Editor.Document;
        var ignored = _editorPreferences.PersonalWords.Concat(_ignoredSpelling).ToArray();
        try
        {
            await Task.Delay(450, token);
            var issues = await Task.Run(() => _spelling.FindIssues(source, ignored, token), token);
            if (cancellation.IsCancellationRequested || document != Editor.Document || source != Editor.Text) return;
            _spellingRenderer.Issues = issues;
            Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _log.Error("editor.spelling.failed", exception, _session.Path); }
    }

    private void Editor_OnContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(Editor.TextArea.TextView, out var point) && Editor.TextArea.TextView.GetPosition(point + Editor.TextArea.TextView.ScrollOffset) is { } position)
        {
            var offset = Editor.Document.GetOffset(position.Line, position.Column);
            if (offset < Editor.SelectionStart || offset > Editor.SelectionStart + Editor.SelectionLength) Editor.Select(offset, 0);
        }
        var context = CaptureCommandContext() with { EditorIsTarget = true };
        var menu = BuildEditorContextMenu(context);
        _editorContextMenu?.Close();
        _editorContextMenu = menu;
        var source = Editor.Text;
        var issue = _spellingRenderer.Issues.FirstOrDefault(item => Editor.CaretOffset >= item.Offset && Editor.CaretOffset <= item.Offset + item.Length);
        if (issue is not null)
        {
            var suggestions = new MenuItem { Header = "Spelling: " + issue.Word };
            foreach (var suggestion in _spelling.Suggest(issue.Word))
            {
                var item = new MenuItem { Header = suggestion };
                item.Click += (_, _) =>
                {
                    if (source != Editor.Text || _sessionTransitionRunning || _metadataApplying || _navigationRunning) return;
                    Editor.Document.Replace(issue.Offset, issue.Length, suggestion);
                    Editor.TextArea.Focus();
                };
                suggestions.Items.Add(item);
            }
            var ignore = new MenuItem { Header = "Ignore for this session" };
            ignore.Click += (_, _) => { _ignoredSpelling.Add(issue.Word); ScheduleSpelling(); };
            suggestions.Items.Add(ignore);
            var add = new MenuItem { Header = "Add to personal dictionary", IsEnabled = ManagementState is not null && _editorPreferences.PersonalWords.Length < 1000 };
            add.Click += async (_, _) =>
            {
                try
                {
                    var preferences = _editorPreferences with { PersonalDictionary = string.Join('\n', _editorPreferences.PersonalWords.Append(issue.Word).Distinct(StringComparer.OrdinalIgnoreCase)) };
                    preferences.Validate();
                    await ManagementState!.SetEditorPreferencesAsync(preferences, _appearance);
                    _editorPreferences = preferences;
                    ScheduleSpelling();
                }
                catch (Exception exception) { ReportError("editor.dictionary.save.failed", exception, null); }
            };
            suggestions.Items.Add(add);
            menu.Items.Add(suggestions);
        }
        menu.Open(Editor);
        args.Handled = true;
    }
    internal IReadOnlyList<SpellingIssue> SpellingIssuesForTesting => _spellingRenderer.Issues;
    internal IReadOnlyList<CSharpToken> CSharpTokensForTesting => _csharpColorizer.Tokens;

    private ContextMenu BuildEditorContextMenu(CommandContext context)
    {
        var menu = new ContextMenu();
        MenuItem Action(string id)
        {
            var command = _commands.Commands.Single(command => command.Id == id);
            var availability = _commands.GetAvailability(id, context);
            var item = new MenuItem
            {
                Header = command.Title
              , IsEnabled = availability.Enabled
              , Tag = id
            };
            if (!availability.Enabled) ToolTip.SetTip(item, availability.Reason);
            item.Click += async (_, _) => await ExecuteCommandInContextAsync(id, context);
            return item;
        }
        void Group(string title, params string[] ids)
        {
            var group = new MenuItem { Header = title };
            foreach (var id in ids) group.Items.Add(Action(id));
            menu.Items.Add(group);
        }
        Group("History", "editor.undo", "editor.redo");
        Group("Clipboard", "editor.cut", "editor.copy", "editor.paste");
        Group("Inline formatting", "editor.format.bold", "editor.format.italic", "editor.format.inlinecode", "editor.format.link");
        Group("Paragraphs and headings", "editor.format.paragraph", "editor.format.heading1", "editor.format.heading2", "editor.format.heading3");
        Group("Lists", "editor.format.unorderedlist", "editor.format.orderedlist", "editor.format.tasklist");
        Group("Insert", "editor.format.fencedcode", "editor.format.table", "editor.format.horizontalrule", "editor.format.callout", "editor.format.hardbreak", "editor.insertSymbol", "editor.insertImage", "editor.pasteImage");
        return menu;
    }

    internal ContextMenu EditorContextMenuForTesting() => BuildEditorContextMenu(CaptureCommandContext() with { EditorIsTarget = true });
    private ContextMenu? _editorContextMenu;
    internal ContextMenu? OpenEditorContextMenuForTesting => _editorContextMenu;
}
