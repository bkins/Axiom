using Axiom.Core;

namespace Axiom.App;

internal sealed class InlineIndexProgress(Action<WorkspaceIndexProgress> report) : IProgress<WorkspaceIndexProgress>
{
    public void Report(WorkspaceIndexProgress value) => report(value);
}
