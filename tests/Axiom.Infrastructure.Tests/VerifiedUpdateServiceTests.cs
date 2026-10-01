using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class VerifiedUpdateServiceTests
{
    [Fact]
    public async Task OfferRequiresExplicitHttpsTrustedHashNewerVersionAndCompatibleSchema()
    {
        var offer = new VerifiedUpdateOffer(1, "1.0.19", "abcdef012345", 8, "candidate", "Human acceptance pending", "https://example.invalid/update.zip", new string('A', 64));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(offer);
        using var handler = new UpdateResponseHandler(bytes);
        using var client = new HttpClient(handler);
        var service = new VerifiedUpdateService(client);
        var url = new Uri("https://example.invalid/offer.json");
        Assert.Equal(offer, await service.CheckAsync(url, WorkspaceFileOperations.Hash(bytes), "1.0.18", 8));
        await Assert.ThrowsAsync<IOException>(() => service.CheckAsync(url, new string('0', 64), "1.0.18", 8));
        await Assert.ThrowsAsync<IOException>(() => service.CheckAsync(url, WorkspaceFileOperations.Hash(bytes), "1.0.19", 8));
        await Assert.ThrowsAsync<IOException>(() => service.CheckAsync(url, WorkspaceFileOperations.Hash(bytes), "1.0.18", 9));
        var calls = handler.Calls;
        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync(new Uri("http://example.invalid/offer"), WorkspaceFileOperations.Hash(bytes), "1.0.18", 8));
        Assert.Equal(calls, handler.Calls);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("C:/outside.md")]
    public async Task TrustedArchiveStillRejectsEscapingPaths(string entryName)
    {
        using var fixture = new MutationTestWorkspace();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write("must remain isolated");
        var bytes = buffer.ToArray();
        using var client = new HttpClient(new UpdateResponseHandler(bytes));
        var offer = new VerifiedUpdateOffer(1, "1.0.19", "abcdef012345", 8, "candidate", "", "https://example.invalid/update.zip", WorkspaceFileOperations.Hash(bytes));
        await Assert.ThrowsAsync<IOException>(() => new VerifiedUpdateService(client).DownloadPackageAsync(offer, Path.Combine(fixture.Root, "downloads")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "outside.md")));
        Assert.False(Directory.Exists(Path.Combine(fixture.State.Paths.Root, "Versions")));
    }

    [Fact]
    public async Task DownloadHashFailureAndCancellationNeverInstall()
    {
        using var fixture = new MutationTestWorkspace();
        using var client = new HttpClient(new UpdateResponseHandler([1, 2, 3]));
        var offer = new VerifiedUpdateOffer(1, "1.0.19", "abcdef012345", 8, "candidate", "", "https://example.invalid/update.zip", new string('0', 64));
        var service = new VerifiedUpdateService(client);
        await Assert.ThrowsAsync<IOException>(() => service.DownloadPackageAsync(offer, Path.Combine(fixture.Root, "downloads")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadPackageAsync(offer, Path.Combine(fixture.Root, "downloads"), new CancellationToken(true)));
        Assert.False(Directory.Exists(Path.Combine(fixture.State.Paths.Root, "Versions")));
    }
}
