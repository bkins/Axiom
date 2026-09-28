using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

internal sealed class RetainingRecycleBin(string restoreFolder, bool fail = false) : IRecycleBin
{
    public void Recycle(string path)
    {
        if (fail) throw new IOException("Injected recycle failure.");
        Directory.CreateDirectory(restoreFolder);
        File.Move(path, Path.Combine(restoreFolder, Path.GetFileName(path)), false);
    }
}
