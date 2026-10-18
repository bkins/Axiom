namespace Axiom.Core;

public sealed record AxiomCommand(string Id
                                , string Title
                                , string? DefaultShortcut
                                , bool MutatesFiles
                                , Func<CommandContext, CommandAvailability> Availability
                                , Func<CommandContext, CancellationToken, Task<CommandResult>> Execute
                                , bool IsCancellationControl = false
                                , bool HiddenFromPalette = false);
