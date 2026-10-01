namespace Axiom.Core;

public sealed record GitChangedFile(string RelativePath
                                 , string? WorkspaceRelativePath
                                 , string IndexStatus
                                 , string WorkingStatus
                                 , string? OriginalPath
                                 , bool CanManage
                                 , string? Reason);
