namespace Axiom.Core;

public interface IWorkbenchLayoutStore
{
    Task<WorkbenchLayout> GetWorkbenchLayoutAsync(CancellationToken cancellationToken = default);
    Task SetWorkbenchLayoutAsync(WorkbenchLayout layout, CancellationToken cancellationToken = default);
}
