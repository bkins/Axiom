using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task InFlightBatchKeepsImmutableRevisionsAndProtectsNewerInactiveEditsOnNextPass()
    {
        var root = CreateTabWorkspace();
        try
        {
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            await state.InitializeAsync();
            var delayed = new DelayedBatchRecoveryStore(state);
            var files = new FileDocumentStore();
            var window = new MainWindow(new DocumentService(files, delayed, state), new WorkspaceService(state),
                files, delayed, state, AppearancePreference.System, new MarkdownPreviewService(),
                new ExternalFileMonitor(), new NullAxiomLog(), () => Task.CompletedTask);
            window.SetEditorTextForTesting("first revision");
            var first = window.TabsForTesting[0];
            await window.NewTabForTestingAsync();
            window.SetEditorTextForTesting("second revision");
            var protecting = window.ProtectIfNeededForTestingAsync();
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first.Document.Text = "newer inactive revision";
            delayed.Release.TrySetResult();
            await protecting;
            Assert.Equal("first revision", (await state.GetRecoverableDraftsAsync()).Single(item => item.DraftKey == first.Session.DraftKey).Text);
            Assert.Equal("newer inactive revision", first.Session.Text);
            await window.ProtectIfNeededForTestingAsync();
            Assert.Equal("newer inactive revision", (await state.GetRecoverableDraftsAsync()).Single(item => item.DraftKey == first.Session.DraftKey).Text);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            Assert.Empty(await state.GetRecoverableDraftsAsync());
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
