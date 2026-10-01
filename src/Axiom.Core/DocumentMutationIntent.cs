namespace Axiom.Core;

public sealed record DocumentMutationIntent(Guid OperationId
                                         , Guid WorkspaceId
                                         , string RootPath
                                         , DocumentMutationKind Kind
                                         , string? SourcePath
                                         , string? DestinationPath
                                         , string ExpectedHash
                                         , Guid? DocumentId)
{
    public Guid? RecoveryOperationId { get; init; }
}
