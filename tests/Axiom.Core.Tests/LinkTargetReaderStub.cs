using Axiom.Core;

namespace Axiom.Core.Tests;

internal sealed class LinkTargetReaderStub : IMarkdownLinkTargetReader
{
    public string? LastPath { get; private set; }
    public int Reads { get; private set; }
    public MarkdownLinkTarget Result { get; set; } = new(MarkdownLinkResolutionStatus.Resolved, "# Summary\n## Café\n## Summary");

    public Task<MarkdownLinkTarget> ReadAsync(string workspaceRoot, string targetPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        LastPath = targetPath;
        return Task.FromResult(Result);
    }
}
