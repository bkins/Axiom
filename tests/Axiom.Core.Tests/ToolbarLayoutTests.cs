using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class ToolbarLayoutTests
{
    [Fact]
    public void LayoutAllowsHiddenCommandsAndReorderButRejectsDuplicatesAndUnknownIds()
    {
        Assert.Equal(new[] { "document.save", "document.new" }, ToolbarLayout.Read("document.save,document.new"));
        Assert.Empty(ToolbarLayout.Read(""));
        Assert.Throws<ArgumentException>(() => ToolbarLayout.Read("document.save,document.save"));
        Assert.Throws<ArgumentException>(() => ToolbarLayout.Read("menu"));
        Assert.Throws<ArgumentException>(() => new EditorPreferences { ToolbarCommands = "unknown" }.Validate());
    }
}
