using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ContextGroupsKeepCommandsAndCapturedSelectionWithOneUndo()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.SetEditorTextForTesting("Selected text");
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.Select(0, 8);
            var menu = window.EditorContextMenuForTesting();
            var groups = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(new[] { "History", "Clipboard", "Inline formatting", "Paragraphs and headings", "Lists", "Insert" }, groups.Select(group => group.Header));
            var actions = groups.SelectMany(group => group.Items.OfType<MenuItem>()).ToArray();
            Assert.Equal(24, actions.Length);
            var bold = actions.Single(item => Equals(item.Tag, "editor.format.bold"));
            bold.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("**Selected** text", editor.Text);
            editor.Undo();
            Assert.Equal("Selected text", editor.Text);
            var image = actions.Single(item => Equals(item.Tag, "editor.pasteImage"));
            Assert.False(image.IsEnabled);
            Assert.Contains("Save", ToolTip.GetTip(image)?.ToString());
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task FieldInformationRemainsAvailableWhenMetadataCannotBeEdited()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.RefreshClassificationAsync();
            var metadata = window.FindControl<TabItem>("MetadataTab")!;
            var fields = metadata.GetLogicalDescendants().OfType<Button>().Where(button => button.Tag is string field && field is "Tags" or "Type" or "Aliases").ToArray();
            Assert.Equal(3, fields.Length);
            foreach (var info in fields)
            {
                Assert.True(info.IsEffectivelyEnabled);
                Assert.StartsWith("About", AutomationProperties.GetName(info));
                var label = Assert.IsType<StackPanel>(info.Parent).Children.OfType<TextBlock>().Single();
                Assert.StartsWith((string)info.Tag!, label.Text);
            }
            Assert.False(window.FindControl<TextBox>("MetadataTags")!.IsEnabled);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SourceColoursOnlyCSharpFencesAndRetainsTextUndoAndOtherLanguages()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            ((Panel)preview.Parent!).Children.Remove(preview);
            window.Show(); await window.StartupCompleted;
            const string source = "public prose\n\n```c#\npublic string Value = \"return\";\n/* comment\nreturn */\n```\n\n```text\nreturn\n```";
            window.SetEditorTextForTesting(source);
            await Task.Delay(500);
            var tokens = window.CSharpTokensForTesting;
            Assert.Equal(new[] { "public", "string" }, tokens.Where(token => token.Kind == "keyword").Select(token => source.Substring(token.Offset, token.Length)));
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.Document.Insert(editor.Text.Length, "\nTail");
            await Task.Delay(400);
            editor.Undo();
            Assert.Equal(source, editor.Text);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
