namespace Axiom.Core;

public sealed record WorkspaceDocumentLocation(string RelativePath
                                            , int CaretOffset
                                            , int SelectionStart
                                            , int SelectionLength
                                            , double HorizontalOffset
                                            , double VerticalOffset
                                            , double PreviewScrollRatio);
