using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task RelationshipNavigationReusesDirtyTabsAndMissingEndpointsStayUnresolved()
    {
        var root = CreateTabWorkspace();
        try
        {
            var sourceId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var source = Path.Combine(root, "source.md");
            var target = Path.Combine(root, "target.md");
            var sourceText = DocumentIdCodec.Enroll("Source", sourceId);
            await File.WriteAllTextAsync(source, sourceText);
            await File.WriteAllTextAsync(target, DocumentIdCodec.Enroll("Target", targetId));
            var window = CreateWindow(root);
            var store = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var relation = new DocumentRelationship(Guid.NewGuid(), sourceId, targetId, "Related", "note");
            await store.ApplyConnectedMetadataAsync([], [new(relation, 0)]);
            relation = Assert.Single(await store.GetRelationshipsAsync());
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(source);
            window.SetEditorTextForTesting(sourceText + " dirty");
            await window.FollowRelationshipAsync(relation);
            Assert.Equal(target, window.PathContextForTesting);
            Assert.True(window.TabsForTesting[0].Session.IsDirty);
            await window.FollowRelationshipAsync(relation);
            Assert.Equal(source, window.PathContextForTesting);
            Assert.Equal(2, window.TabsForTesting.Count);
            File.Delete(target);
            await window.FollowRelationshipAsync(relation);
            Assert.Equal(source, window.PathContextForTesting);
            Assert.Single(await store.GetRelationshipsAsync());
            Assert.Equal(sourceText, await File.ReadAllTextAsync(source));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
