using System.Text.Json;
using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure.Tests;

public sealed class OperationalServicesTests
{
    [Fact]
    public async Task WorkspaceSelectionExplainsPackageRequirementWithoutChangingDocuments()
    {
        using var fixture = new MutationTestWorkspace();
        var document = Path.Combine(fixture.Root, "notes.md");
        await File.WriteAllTextAsync(document, "protected Markdown");
        var error = await Assert.ThrowsAsync<IOException>(() => new PortablePackageService(fixture.State.Paths).ValidateAsync(fixture.Root));
        Assert.Contains("not a published Axiom application package", error.Message);
        Assert.Contains("payload-manifest.json", error.Message);
        Assert.Contains("Markdown workspace", error.Message);
        Assert.Equal("protected Markdown", await File.ReadAllTextAsync(document));
        Assert.False(Directory.Exists(Path.Combine(fixture.State.Paths.Root, "Versions")));
        Assert.Throws<IOException>(() => PortablePackageService.GetPackageDirectory(document));
        Assert.Equal(fixture.Root, PortablePackageService.GetPackageDirectory(Path.Combine(fixture.Root, PortablePackageService.ManifestName)));
    }

    [Fact]
    public async Task OnlineBackupIncludesLiveWalContentAndNeverOverwritesDestination()
    {
        using var fixture = new MutationTestWorkspace();
        var paths = fixture.State.Paths;
        await using var live = new SqliteConnection($"Data Source={paths.DatabasePath};Pooling=False");
        await live.OpenAsync();
        await using var command = live.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
        await command.ExecuteNonQueryAsync();
        await fixture.State.SaveDraftAsync(new RecoveryDraft("wal-draft", null, "protected text", DateTimeOffset.UtcNow, null, true));
        var maintenance = new StateMaintenanceService(paths);
        var report = await maintenance.InspectAsync();
        Assert.Equal("ok", report.Integrity);
        Assert.Equal(1, report.Drafts);
        Assert.Equal(SqliteStateStore.CurrentSchemaVersion, report.SchemaVersion);
        var destination = Path.Combine(fixture.Root, "backup.db");
        await maintenance.ExportBackupAsync(destination);
        await using var snapshot = new SqliteConnection($"Data Source={destination};Mode=ReadOnly;Pooling=False");
        await snapshot.OpenAsync();
        await using var read = snapshot.CreateCommand();
        read.CommandText = "SELECT TextContent FROM RecoveryDrafts WHERE DraftKey='wal-draft';";
        Assert.Equal("protected text", await read.ExecuteScalarAsync());
        await Assert.ThrowsAsync<IOException>(() => maintenance.ExportBackupAsync(destination));
        await Assert.ThrowsAsync<IOException>(() => maintenance.ExportBackupAsync(paths.DatabasePath));
    }

    [Fact]
    public async Task InitializationSnapshotPreservesLiveWalDraftAndSchemaFive()
    {
        using var fixture = new MutationTestWorkspace();
        await using var live = new SqliteConnection($"Data Source={fixture.State.Paths.DatabasePath};Pooling=False");
        await live.OpenAsync();
        await using var setup = live.CreateCommand();
        setup.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
        await setup.ExecuteNonQueryAsync();
        await fixture.State.SaveDraftAsync(new RecoveryDraft("retained", null, "snapshot content", DateTimeOffset.UtcNow, null, true));
        await fixture.State.InitializeAsync();
        var backup = Directory.EnumerateFiles(fixture.State.Paths.BackupDirectory, "*.db").Single();
        await using var snapshot = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        await snapshot.OpenAsync();
        await using var query = snapshot.CreateCommand();
        query.CommandText = "SELECT TextContent FROM RecoveryDrafts WHERE DraftKey='retained';";
        Assert.Equal("snapshot content", await query.ExecuteScalarAsync());
    }

    [Fact]
    public void DiagnosticTailIsBoundedAndDoesNotReadWorkspaceDocuments()
    {
        using var fixture = new MutationTestWorkspace();
        var log = Path.Combine(fixture.State.Paths.LogDirectory, "axiom.log");
        File.WriteAllLines(log, Enumerable.Range(0, 1000).Select(index => "event " + index));
        File.WriteAllText(Path.Combine(fixture.Root, "private.md"), "secret Markdown");
        var text = new DiagnosticsReader(fixture.State.Paths).ReadTail(4, 100);
        Assert.Contains("event 999", text);
        Assert.DoesNotContain("event 995", text);
        Assert.DoesNotContain("secret Markdown", text);
        Assert.True(text.Length <= 100);
        File.WriteAllText(log, new string('x', 500_000));
        Assert.True(new DiagnosticsReader(fixture.State.Paths).ReadTail(4, 100).Length <= 100);
    }

    [Fact]
    public async Task VerifiedPackageInstallsIntoNewFolderAndRejectsExistingVersion()
    {
        using var fixture = new MutationTestWorkspace();
        var source = await CreatePackageAsync(fixture.Root);
        var service = new PortablePackageService(fixture.State.Paths, _ => "1.0.4+1234567");
        var report = await service.ValidateAsync(source);
        Assert.Equal("1.0.4", report.Version);
        var destination = await service.InstallAsync(source, 5);
        Assert.StartsWith(Path.Combine(fixture.State.Paths.Root, "Versions"), destination);
        Assert.Equal(report.InformationalVersion, (await service.ValidateAsync(destination)).InformationalVersion);
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(source, 5));
        Assert.True(File.Exists(Path.Combine(source, "Axiom.App.exe")));
        Assert.Single(await fixture.State.ListWorkspacesAsync());
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("escape")]
    [InlineData("duplicate")]
    [InlineData("identity")]
    [InlineData("schema")]
    public async Task PackageValidationRejectsUnverifiedOrContradictoryPayload(string failure)
    {
        using var fixture = new MutationTestWorkspace();
        var source = await CreatePackageAsync(fixture.Root);
        var manifestPath = Path.Combine(source, PortablePackageService.ManifestName);
        var manifest = JsonSerializer.Deserialize<PortablePackageManifest>(await File.ReadAllTextAsync(manifestPath))!;
        switch (failure)
        {
            case "tamper": await File.AppendAllTextAsync(Path.Combine(source, "Axiom.App.exe"), "changed"); break;
            case "extra": await File.WriteAllTextAsync(Path.Combine(source, "extra.dll"), "unexpected"); break;
            case "missing": File.Delete(Path.Combine(source, "Axiom.App.exe")); break;
            case "escape": manifest = manifest with { Files = [manifest.Files[0] with { RelativePath = "../escape.exe" }] }; break;
            case "duplicate": manifest = manifest with { Files = [.. manifest.Files, manifest.Files[0]] }; break;
            case "identity": manifest = manifest with { InformationalVersion = "1.0.4+abcdef0" }; break;
            case "schema": manifest = manifest with { SchemaVersion = 4 }; break;
        }
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
        await Assert.ThrowsAsync<IOException>(() => new PortablePackageService(fixture.State.Paths, _ => "1.0.4+1234567").ValidateAsync(source));
    }

    [Fact]
    public void DowngradeGuardAndRestartScriptRetainStateAndQuoteLiteralPaths()
    {
        Assert.Throws<IOException>(() => PortablePackageService.EnsureSchemaCompatible(4, 5));
        PortablePackageService.EnsureSchemaCompatible(5, 5);
        var script = PortableUpdateLauncher.BuildRestartScript(Path.Combine(Path.GetTempPath(), "Ben's version"), Path.Combine(Path.GetTempPath(), "Ben's state"), 12345);
        Assert.Contains("Wait-Process -Id 12345", script);
        Assert.Contains("if (Get-Process -Id 12345", script);
        Assert.Contains("Ben''s state", script);
        Assert.Contains("Ben''s version", script);
        Assert.DoesNotContain("Remove-Item", script);
    }

    private static async Task<string> CreatePackageAsync(string root)
    {
        var source = Path.Combine(root, "package");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "Axiom.App.exe"), "fixture executable bytes");
        await File.WriteAllTextAsync(Path.Combine(source, "Axiom.App.dll"), "fixture assembly bytes");
        File.Copy(typeof(SqliteStateStore).Assembly.Location, Path.Combine(source, "Axiom.Infrastructure.dll"));
        var files = new List<PortablePackageFile>();
        foreach (var path in Directory.EnumerateFiles(source))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            files.Add(new PortablePackageFile(Path.GetFileName(path), bytes.Length, WorkspaceFileOperations.Hash(bytes)));
        }
        var manifest = new PortablePackageManifest(1, "1.0.4", "1.0.4+1234567", "1234567", "benny/test", DateTimeOffset.UtcNow, SqliteStateStore.CurrentSchemaVersion, files);
        await File.WriteAllTextAsync(Path.Combine(source, PortablePackageService.ManifestName), JsonSerializer.Serialize(manifest));
        return source;
    }
}
