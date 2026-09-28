namespace Axiom.Core;

public interface IMarkdownLinkTargetReader
{
    Task<MarkdownLinkTarget> ReadAsync(string workspaceRoot, string targetPath, CancellationToken cancellationToken = default);
}
