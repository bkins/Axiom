namespace Axiom.Infrastructure;

public sealed class AppDataPaths
{
    public AppDataPaths(string? root = null)
    {
        Root = root ?? Environment.GetEnvironmentVariable("AXIOM_STATE_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom");
        DatabasePath = Path.Combine(Root, "State", "axiom.db");
        BackupDirectory = Path.Combine(Root, "State", "Backups");
        LogDirectory = Path.Combine(Root, "Logs");
    }

    public string Root { get; }
    public string DatabasePath { get; }
    public string BackupDirectory { get; }
    public string LogDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}

