using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

internal sealed class MutationTestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "AxiomMutations-" + Guid.NewGuid().ToString("N"));
    public SqliteStateStore State { get; }
    public WorkspaceRecord Workspace { get; }

    public MutationTestWorkspace()
    {
        Directory.CreateDirectory(Root);
        State = new SqliteStateStore(new AppDataPaths(Path.Combine(Root, "state")));
        State.InitializeAsync().GetAwaiter().GetResult();
        Workspace = State.OpenWorkspaceAsync(Root, "Tests", DateTimeOffset.UtcNow).GetAwaiter().GetResult();
    }

    public void Dispose() => Directory.Delete(Root, true);
}
