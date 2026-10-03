namespace Axiom.Core;

public sealed record WorkbenchLayout(double WorkspaceWidth = 280
                                   , double ToolWidth = 320
                                   , int ToolIndex = 1
                                   , bool ToolVisible = false)
{
    public bool IsValid => double.IsFinite(WorkspaceWidth) && WorkspaceWidth is >= 180 and <= 480
                        && double.IsFinite(ToolWidth) && ToolWidth is >= 260 and <= 480
                        && ToolIndex is >= 0 and <= 6;
}
