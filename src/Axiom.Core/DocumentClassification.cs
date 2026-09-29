namespace Axiom.Core;

public sealed record DocumentClassification(Guid DocumentId, string[] Tags, string? Type, string[] Aliases, long Revision = 0)
{
    public static DocumentClassification Empty(Guid id) => new(id, [], null, []);
}
