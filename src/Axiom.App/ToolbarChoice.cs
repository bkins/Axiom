using Axiom.Core;

namespace Axiom.App;

internal sealed class ToolbarChoice(ToolbarCommandDefinition command, bool visible)
{
    internal ToolbarCommandDefinition Command { get; } = command;
    internal bool Visible { get; set; } = visible;
}
