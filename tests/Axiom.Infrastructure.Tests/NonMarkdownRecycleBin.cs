namespace Axiom.Infrastructure.Tests;

internal sealed class NonMarkdownRecycleBin(string root) : IRecycleBin
{
    public void Recycle(string path) => File.Move(path, Path.Combine(root, Guid.NewGuid().ToString("N") + ".recycled"));
}
