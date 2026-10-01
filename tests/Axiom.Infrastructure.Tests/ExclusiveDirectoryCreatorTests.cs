using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class ExclusiveDirectoryCreatorTests
{
    [Fact]
    public async Task ConcurrentCreationHasOneWinnerAndRetainsExistingUnicodeDirectoryContents()
    {
        using var fixture = new MutationTestWorkspace();
        var target = Path.Combine(fixture.Root, "Café ☕");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(attempt => Task.Run(() =>
        {
            try { ExclusiveDirectoryCreator.Create(target); return true; }
            catch (IOException) { return false; }
        })));
        Assert.Equal(1, outcomes.Count(created => created));
        var document = Path.Combine(target, "notes.md");
        await File.WriteAllTextAsync(document, "Preserved");
        Assert.Throws<IOException>(() => ExclusiveDirectoryCreator.Create(target));
        Assert.Equal("Preserved", await File.ReadAllTextAsync(document));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(target) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
    }

    [Fact]
    public void MissingParentAndInvalidPathsDoNotCreateImplicitAncestors()
    {
        using var fixture = new MutationTestWorkspace();
        var parent = Path.Combine(fixture.Root, "missing");
        Assert.Throws<IOException>(() => ExclusiveDirectoryCreator.Create(Path.Combine(parent, "child")));
        Assert.False(Directory.Exists(parent));
        Assert.Throws<ArgumentException>(() => ExclusiveDirectoryCreator.Create("relative-directory"));
        Assert.Throws<ArgumentException>(() => ExclusiveDirectoryCreator.Create(Path.Combine(fixture.Root, "null\0suffix")));
    }
}
