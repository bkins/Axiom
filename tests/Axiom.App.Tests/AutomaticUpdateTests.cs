using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed class AutomaticUpdateTests
{
    [Fact]
    public async Task PortableBuildNeverContactsUpdateSource()
    {
        var client = new FakeClient { IsInstalled = false };
        var service = new AutomaticUpdateService(_ => client);
        Assert.Contains("installer", await service.CheckAsync(new(), TestContext.Current.CancellationToken));
        Assert.Equal(0, client.Downloads);
        Assert.Throws<InvalidOperationException>(() => service.ApplyAndRestart(new()));
    }

    [Fact]
    public async Task DownloadNeverInstallsUntilExplicitApplyAndChannelChangeBlocksIt()
    {
        var client = new FakeClient();
        var service = new AutomaticUpdateService(_ => client);
        await service.CheckAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal("1.8.0", service.ReadyVersion);
        Assert.Equal(0, client.Applies);
        Assert.Throws<InvalidOperationException>(() => service.ApplyAndRestart(new(Channel: "preview")));
        service.ApplyAndRestart(new());
        Assert.Equal(1, client.Applies);
    }

    [Fact]
    public async Task FailedRecheckCannotApplyAnEarlierDownload()
    {
        var client = new FakeClient();
        var service = new AutomaticUpdateService(_ => client);
        await service.CheckAsync(new(), TestContext.Current.CancellationToken);
        client.Failure = new IOException("checksum mismatch");
        await Assert.ThrowsAsync<IOException>(() => service.CheckAsync(new(), TestContext.Current.CancellationToken));
        Assert.Null(service.ReadyVersion);
        Assert.Throws<InvalidOperationException>(() => service.ApplyAndRestart(new()));
    }

    [Fact]
    public async Task CancellationDoesNotArmAnUpdate()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new AutomaticUpdateService(_ => new FakeClient { AfterDownload = cancellation.Cancel });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(new(), cancellation.Token));
        Assert.Null(service.ReadyVersion);
    }

    [Fact]
    public async Task ConcurrentChecksAreSerialized()
    {
        var release = new TaskCompletionSource();
        var client = new FakeClient { Wait = release.Task };
        var service = new AutomaticUpdateService(_ => client);
        var first = service.CheckAsync(new(), TestContext.Current.CancellationToken);
        Assert.Contains("already running", await service.CheckAsync(new(), TestContext.Current.CancellationToken));
        release.SetResult();
        await first;
        Assert.Equal(1, client.Downloads);
    }

    [Fact]
    public void PreferencesPersistChannelAndOptOutWithoutTouchingDocuments()
    {
        var root = Path.Combine(Path.GetTempPath(), "axiom-updates-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AutomaticUpdatePreferencesStore(new AppDataPaths(root), "preview");
            Assert.Equal("preview", store.Load().Channel);
            store.Save(new(false, "stable"));
            Assert.Equal(new(false, "stable"), store.Load());
            Assert.Throws<ArgumentException>(() => store.Save(new(Channel: "unknown")));
            Assert.Equal(new(false, "stable"), store.Load());
            Assert.Single(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FakeClient : IAutomaticUpdateClient
    {
        public bool IsInstalled { get; init; } = true;
        public int Downloads { get; private set; }
        public int Applies { get; private set; }
        public Exception? Failure { get; set; }
        public Action? AfterDownload { get; init; }
        public Task? Wait { get; init; }
        public async Task<string?> DownloadLatestAsync(CancellationToken cancellationToken, IProgress<AutomaticUpdateProgress>? progress = null)
        {
            Downloads++;
            if (Wait is not null) await Wait.WaitAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            AfterDownload?.Invoke();
            return "1.8.0";
        }
        public void ApplyAndRestart() => Applies++;
    }
}

