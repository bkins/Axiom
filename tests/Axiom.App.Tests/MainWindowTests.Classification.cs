using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task MetadataApplyIsBoundToSavedIdentityAndPendingEditsCannotFollowAnotherTab()
    {
        var root = CreateTabWorkspace();
        try
        {
            var id = Guid.NewGuid();
            var one = Path.Combine(root, "one.md");
            var two = Path.Combine(root, "two.md");
            var original = DocumentIdCodec.Enroll("# One", id);
            await File.WriteAllTextAsync(one, original);
            await File.WriteAllTextAsync(two, "# Ordinary");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(one);
            window.FindControl<TextBox>("MetadataTags")!.Text = " First \nSECOND\nfirst";
            window.FindControl<TextBox>("MetadataAliases")!.Text = "Alternate; name";
            window.MetadataChoiceForTesting = () => Task.FromResult("Cancel");
            await window.OpenDocumentAsync(two);
            Assert.Equal(one, window.PathContextForTesting);
            Assert.True(await window.ApplyMetadataAsync());
            var store = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var metadata = Assert.Single(await store.GetClassificationsAsync());
            Assert.Equal(id, metadata.DocumentId);
            Assert.Equal(["First", "SECOND"], metadata.Tags);
            Assert.Equal("Alternate; name", Assert.Single(metadata.Aliases));
            Assert.Equal(original, await File.ReadAllTextAsync(one));
            window.SetEditorTextForTesting(original.Replace(" -->", "\n -->", StringComparison.Ordinal));
            Assert.False(window.FindControl<StackPanel>("MetadataFields")!.IsEnabled);
            Assert.False(await window.ApplyMetadataAsync());
            Assert.Equal(original, await File.ReadAllTextAsync(one));
            window.SetEditorTextForTesting(original);
            window.FindControl<TextBox>("MetadataType")!.Text = "Idea";
            window.MetadataChoiceForTesting = () => Task.FromResult("Discard");
            await window.OpenDocumentAsync(two);
            Assert.Equal(two, window.PathContextForTesting);
            Assert.False(window.FindControl<StackPanel>("MetadataFields")!.IsEnabled);
            Assert.Null(Assert.Single(await store.GetClassificationsAsync()).Type);
            Assert.Contains("ordinary Markdown", window.FindControl<TextBlock>("MetadataGuidance")!.Text);
            Assert.True(window.FindControl<Button>("MetadataEnroll")!.IsVisible);
            window.FindControl<Button>("MetadataEnroll")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("# Ordinary", await File.ReadAllTextAsync(two));
            Assert.True(window.FindControl<Button>("MetadataSave")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("MetadataFields")!.IsEnabled);
            Assert.Contains("Save", window.FindControl<TextBlock>("MetadataGuidance")!.Text);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
