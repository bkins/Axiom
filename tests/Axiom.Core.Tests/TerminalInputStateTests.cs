namespace Axiom.Core.Tests;

public sealed class TerminalInputStateTests
{
    [Fact]
    public void OnlyAnEmptyPromptAtTheOriginalRootCanBePrepared()
    {
        var state = new TerminalInputState("test");
        Assert.False(state.TryPrepare("git add ."));
        state.ObserveOutput("\u001b]777;test;root\aPS root> ");
        Assert.True(state.TryPrepare("git add ."));
        state.ObserveOutput("echoed characters");
        Assert.False(state.CanPrepare);
        Assert.False(state.TryPrepare("git push"));
        state.ObserveInput("\r"); Assert.False(state.CanPrepare);
        state.ObserveOutput("\u001b]777;test;other\a"); Assert.False(state.CanPrepare);
        state.ObserveOutput("\u001b]777;test;root\a"); Assert.True(state.CanPrepare);
        Assert.False(state.TryPrepare("git add .\r"));
    }
    [Fact]
    public void SplitPromptMarkerIsRecognizedOnceAndHistoryIsConservative()
    {
        var state = new TerminalInputState("test");
        state.ObserveOutput("\u001b]777;te"); state.ObserveOutput("st;roo"); state.ObserveOutput("t\a");
        Assert.True(state.CanPrepare);
        state.ObserveInput("text"); state.ObserveOutput("more output"); Assert.False(state.CanPrepare);
        state.ObserveInput("\u0003"); state.ObserveOutput("\u001b]777;test;root\a");
        state.ObserveInput("\u001b[A"); Assert.False(state.CanPrepare);
    }
    [Fact]
    public void OrdinaryTypingAndBackspaceDoNotOverwriteAnExistingCommand()
    {
        var state = new TerminalInputState("test"); state.ObserveOutput("\u001b]777;test;root\a");
        state.ObserveInput("abc"); Assert.False(state.CanPrepare);
        state.ObserveInput("\u007f\u007f\u007f"); Assert.True(state.CanPrepare);
        state.ObserveInput("\t"); Assert.False(state.CanPrepare);
    }
}
