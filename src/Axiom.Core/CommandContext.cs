namespace Axiom.Core;

public sealed record CommandContext(string DraftKey
                                  , Guid? WorkspaceId
                                  , string? DocumentPath
                                  , bool IsBusy
                                  , bool EditorIsTarget
                                  , bool CanUndo
                                  , bool CanRedo
                                  , bool MutationBlocked
                                  , bool UserInitiated)
{
    public string? ExplorerRelativePath { get; init; }
    public bool ExplorerIsFolder { get; init; }
}
