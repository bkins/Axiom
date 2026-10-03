using Axiom.Core;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class EverydayUseStateTests
{
    [Fact]
    public async Task PreferencesAndAppearancePersistTogetherWithoutChangingDrafts()
    {
        using var fixture = new MutationTestWorkspace();
        var settings = new EditorPreferences("Consolas", 22, false, 8, MenuStyle.Rounded) { CompactTitleBar = false, HighlightCurrentLine = false, ShowNonPrintingCharacters = true, ShowPreviewLineNumbers = true, SpellCheck = false, PersonalDictionary = "Axiom\nWorkbench", ToolbarCommands = "document.save,document.new", CompactWorkbench = true };
        await fixture.State.SetEditorPreferencesAsync(settings, AppearancePreference.Dark);
        Assert.Equal(settings, await fixture.State.GetEditorPreferencesAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.State.SetEditorPreferencesAsync(settings with { MenuStyle = (MenuStyle)99 }, AppearancePreference.Light));
        Assert.Equal(AppearancePreference.Dark, await fixture.State.GetAppearanceAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.State.SetEditorPreferencesAsync(settings with { FontSize = 41 }, AppearancePreference.Light));
        Assert.Equal(settings, await fixture.State.GetEditorPreferencesAsync());
        Assert.Equal(AppearancePreference.Dark, await fixture.State.GetAppearanceAsync());
    }

    [Fact]
    public async Task RecentHistoryIsBoundedDeduplicatedAndRemovalNeverDeletesFiles()
    {
        using var fixture = new MutationTestWorkspace();
        for (var index = 0; index < 25; index++) await fixture.State.RecordRecentDocumentAsync(Path.Combine(fixture.Root, index + ".md"), fixture.Root);
        var path = Path.Combine(fixture.Root, "7.md");
        await File.WriteAllTextAsync(path, "keep");
        await fixture.State.RecordRecentDocumentAsync(path, fixture.Root);
        var values = await fixture.State.GetRecentDocumentsAsync();
        Assert.Equal(20, values.Count);
        Assert.Equal(path, values[0].Path);
        Assert.Single(values, value => value.Path == path);
        await fixture.State.RemoveRecentDocumentAsync(path);
        Assert.Equal(19, (await fixture.State.GetRecentDocumentsAsync()).Count);
        await fixture.State.RemoveRecentDocumentAsync(null);
        Assert.Empty(await fixture.State.GetRecentDocumentsAsync());
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
    }
}
