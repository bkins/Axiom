using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task Every_Registered_Command_Has_Current_Feature_Help_And_Semantic_Command_Selects_The_Exercise()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            var commands = window.RegisteredCommandsForTesting;
            var text = OfflineHelp.Read(commands);
            var guide = new MarkdownStructureService().Parse(text);
            foreach (var command in commands)
            {
                Assert.Contains(guide.Headings, heading => heading.Text == OfflineHelp.CommandTopic(command.Id));
                Assert.Contains(command.Title, text);
            }
            Assert.DoesNotContain(guide.Headings, heading => System.Text.RegularExpressions.Regex.IsMatch(heading.Text, @"\b1\.[012]\.\d|candidate|implementation in progress", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            string? context = null;
            window.HelpRequestedForTesting = topic => { context = topic; return Task.CompletedTask; };
            await window.ExecuteCommandAsync("help.semantic");
            Assert.Equal("Semantic search: step-by-step test", context);
            Assert.Contains("Build reviewed semantic index", text);
            Assert.Contains("Wind.md", text);
            Assert.Contains("stale", text, StringComparison.OrdinalIgnoreCase);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
