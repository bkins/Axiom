using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceCreationServiceTests
{
    [Fact]
    public void Creates_Only_An_Empty_Named_Folder_And_Refuses_Existing_Content()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomCreation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var created = WorkspaceCreationService.Create(root, "My notes ' café");
            Assert.Equal(Path.Combine(root, "My notes ' café"), created);
            Assert.Empty(Directory.EnumerateFileSystemEntries(created));
            File.WriteAllText(Path.Combine(created, "keep.md"), "Keep these bytes");
            Assert.Throws<IOException>(() => WorkspaceCreationService.Create(root, "My notes ' café"));
            Assert.Equal("Keep these bytes", File.ReadAllText(Path.Combine(created, "keep.md")));
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/folder")]
    [InlineData("nested\\folder")]
    [InlineData("CON")]
    [InlineData("name.")]
    [InlineData("")]
    public void Invalid_Names_Do_Not_Create_Folders(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomCreation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<ArgumentException>(() => WorkspaceCreationService.Create(root, name));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root); }
    }
}
