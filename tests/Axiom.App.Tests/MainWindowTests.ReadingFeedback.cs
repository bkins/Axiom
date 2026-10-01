using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task StatisticsAndPersistentFailuresDoNotChangeBuffersOrBlockEditing()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.SetEditorTextForTesting(DocumentIdCodec.Enroll("# Café\n\nTwo words", Guid.NewGuid()));
            window.SetEditorSelectionForTesting(window.EditorTextForTesting.Length, 0);
            window.RefreshStatisticsForTesting();
            var text = window.EditorTextForTesting;
            Assert.Contains("3 words", window.FindControl<TextBlock>("DocumentStatisticsText")!.Text);
            Assert.Contains("Mixed newlines", window.FindControl<TextBlock>("DocumentFormatText")!.Text);
            window.ShowFailureForTesting("Output folder denied");
            window.ShowInformationForTesting("Other action succeeded");
            Assert.Contains("Output folder denied", window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.True(window.FindControl<Button>("DismissStatusButton")!.IsVisible);
            Assert.Equal(text, window.EditorTextForTesting);
            Assert.True(window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.IsEnabled);
            window.DismissFailureForTesting();
            Assert.Equal("Other action succeeded", window.FindControl<TextBlock>("StatusText")!.Text);
            Assert.False(window.FindControl<Button>("DismissStatusButton")!.IsVisible);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
        await Task.CompletedTask;
    }
}
