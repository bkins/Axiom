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
            window.FindControl<TextBox>("MetadataType")!.Text = "Idea";
            window.MetadataChoiceForTesting = () => Task.FromResult("Discard");
            await window.OpenDocumentAsync(two);
            Assert.Equal(two, window.PathContextForTesting);
            Assert.False(window.FindControl<StackPanel>("MetadataFields")!.IsEnabled);
            Assert.Null(Assert.Single(await store.GetClassificationsAsync()).Type);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
