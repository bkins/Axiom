using System.Text;
using Axiom.Core;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class ReviewedWorkspaceMoveTests
{
    [Fact]
    public async Task ReviewedMoveRepairsInboundAndOutboundWithoutTouchingCodeOrBom()
    {
        using var fixture = new Fixture();
        var root = fixture.Root;
        var source = Path.Combine(root, "a.md");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(root, "image.png"), "asset");
        var original = "# A\r\n![x](image.png)\r\n`[code](a.md)`\r\n";
        await File.WriteAllTextAsync(source, original, new UTF8Encoding(true));
        await File.WriteAllTextAsync(Path.Combine(root, "b.md"), "[A](a.md#part)\n[x][ref]\n\n[ref]: a.md \"Title\"\n");
        var store = await fixture.Store();
        var workspace = await new WorkspaceService(store).OpenAsync(root);
        var service = new ReviewedWorkspaceMove(store);
        var plan = await service.ReviewAsync(workspace, source, Path.Combine(root, "nested", "a.md"));
        Assert.Equal(3, plan.Files.Sum(file => file.Edits.Count));
        await service.ApplyAsync(plan, true);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "nested", "a.md"));
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Equal(original.Replace("(image.png)", "(../image.png)"), MarkdownIdentityBytes.Decode(bytes));
        Assert.Contains("(nested/a.md#part)", await File.ReadAllTextAsync(Path.Combine(root, "b.md")));
        Assert.Contains("[ref]: nested/a.md", await File.ReadAllTextAsync(Path.Combine(root, "b.md")));
        Assert.False(service.Pending);
    }

    [Fact]
    public async Task StaleReviewAndCollisionLeaveAllFilesIntact()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Root, "a.md");
        await File.WriteAllTextAsync(source, "# A");
        var store = await fixture.Store();
        var workspace = await new WorkspaceService(store).OpenAsync(fixture.Root);
        var service = new ReviewedWorkspaceMove(store);
        var target = Path.Combine(fixture.Root, "b.md");
        var plan = await service.ReviewAsync(workspace, source, target);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "new.md"), "[A](a.md)");
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan, true));
        Assert.True(File.Exists(source)); Assert.False(File.Exists(target));
        await File.WriteAllTextAsync(target, "keep");
        await Assert.ThrowsAsync<IOException>(() => service.ReviewAsync(workspace, source, target));
        Assert.Equal("keep", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ExternalMovePreservesIdentityAndRebindsState()
    {
        using var fixture = new Fixture();
        var external = Path.Combine(fixture.Home, "outside.md");
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(external, DocumentIdCodec.Enroll("# Outside", id));
        var store = await fixture.Store();
        await store.RecordIdentityAsync(id, external);
        var workspace = await new WorkspaceService(store).OpenAsync(fixture.Root);
        var service = new ReviewedWorkspaceMove(store);
        var target = Path.Combine(fixture.Root, "outside.md");
        var plan = await service.ReviewAsync(workspace, external, target);
        await service.ApplyAsync(plan, false);
        Assert.False(File.Exists(external));
        Assert.Equal(id, DocumentIdCodec.Find(await File.ReadAllTextAsync(target)));
        Assert.DoesNotContain(await store.GetIdentitiesAsync(), item => item.Path == external);
        Assert.Contains(await store.GetIdentitiesAsync(), item => item.Path == target);
    }

    [Fact]
    public async Task InterruptedRepairFinishesOnlyReviewedBytesAndRefusesExternalChanges()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Root, "a.md");
        var inbound = Path.Combine(fixture.Root, "b.md");
        await File.WriteAllTextAsync(source, "# A");
        await File.WriteAllTextAsync(inbound, "[A](a.md)");
        var store = await fixture.Store();
        var workspace = await new WorkspaceService(store).OpenAsync(fixture.Root);
        var service = new ReviewedWorkspaceMove(store);
        var target = Path.Combine(fixture.Root, "renamed.md");
        var plan = await service.ReviewAsync(workspace, source, target);
        var receipt = Path.Combine(Path.GetDirectoryName(store.Paths.DatabasePath)!, "reviewed-move.json");
        await File.WriteAllTextAsync(receipt, System.Text.Json.JsonSerializer.Serialize(plan));
        await new WorkspaceFileOperations(store).ExecuteAsync(workspace, DocumentMutationKind.Move, "a.md", "renamed.md", plan.ReviewedHash);
        await File.WriteAllTextAsync(inbound, "external edit");
        await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync());
        Assert.True(service.Pending);
        Assert.Equal("external edit", await File.ReadAllTextAsync(inbound));
        await File.WriteAllTextAsync(inbound, "[A](a.md)");
        await service.RecoverAsync();
        Assert.Equal("[A](renamed.md)", await File.ReadAllTextAsync(inbound));
        Assert.False(service.Pending);
        await service.RecoverAsync();
    }

    [Fact]
    public async Task FolderMoveMapsAssetsInsideSubtreeAndRepairsOutsideReferences()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "old"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "target"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "old", "a.md"), "![image](x.png)\n[missing](absent.md)");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "old", "x.png"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "ref.md"), "[A](old/a.md)");
        var store = await fixture.Store();
        var workspace = await new WorkspaceService(store).OpenAsync(fixture.Root);
        var service = new ReviewedWorkspaceMove(store);
        var plan = await service.ReviewAsync(workspace, Path.Combine(fixture.Root, "old"), Path.Combine(fixture.Root, "target", "new"));
        Assert.Contains(plan.Warnings, warning => warning.Contains("Unresolved"));
        await service.ApplyAsync(plan, true);
        Assert.Equal("[A](target/new/a.md)", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "ref.md")));
        Assert.Equal("![image](x.png)\n[missing](absent.md)", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "target", "new", "a.md")));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "target", "new", "x.png")));
    }

    private sealed class Fixture : IDisposable
    {
        public string Home { get; } = Path.Combine(Path.GetTempPath(), "AxiomMove-" + Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Home, "workspace");
        public Fixture() => Directory.CreateDirectory(Root);
        public async Task<SqliteStateStore> Store()
        {
            var store = new SqliteStateStore(new AppDataPaths(Path.Combine(Home, "state")));
            await store.InitializeAsync(); return store;
        }
        public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(Home, true); }
    }
}
