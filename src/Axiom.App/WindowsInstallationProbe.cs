using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Axiom.Core;
using Axiom.Infrastructure;
using Microsoft.Win32;

namespace Axiom.App;

internal static class WindowsInstallationProbe
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3 || !OperatingSystem.IsWindows()) return 2;
        var root = Path.GetFullPath(args[2]);
        if (Directory.Exists(root)) return 2;
        Directory.CreateDirectory(root);
        try
        {
            ExecuteAsync(args[1], root).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(root, "error.txt"), exception.ToString()); return 1; }
    }

    private static async Task ExecuteAsync(string source, string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var paths = new AppDataPaths(Path.Combine(root, "isolated-state"));
        var defaultChoices = ReadDefaultChoices();
        var state = new SqliteStateStore(paths);
        await state.InitializeAsync();
        await state.SaveDraftAsync(new RecoveryDraft("installation-protected", null, "retained draft", DateTimeOffset.UtcNow, null, true));
        var destination = await new WindowsInstallationService(paths).InstallAsync(source, true, true);
        var manifest = await new PortablePackageService(paths).ValidateAsync(destination);
        var archive = Path.Combine(root, "verified-package.zip");
        ZipFile.CreateFromDirectory(source, archive);
        var offer = new VerifiedUpdateOffer(1, manifest.Version, manifest.CommitId, manifest.SchemaVersion, manifest.Channel ?? "candidate",
            manifest.ReleaseNotes ?? "Fixture only", "https://fixture.invalid/package.zip", WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(archive)));
        var offerBytes = JsonSerializer.SerializeToUtf8Bytes(offer);
        using var client = new HttpClient(new PackageUpdateFixtureHandler(offerBytes, archive));
        var updates = new VerifiedUpdateService(client);
        var verifiedOffer = await updates.CheckAsync(new Uri("https://fixture.invalid/offer.json"), WorkspaceFileOperations.Hash(offerBytes), "0.0.0", SqliteStateStore.CurrentSchemaVersion);
        var downloaded = await updates.DownloadPackageAsync(verifiedOffer, Path.Combine(root, "fixture-downloads"));
        Require((await new PortablePackageService(paths).ValidateAsync(downloaded)).InformationalVersion == manifest.InformationalVersion, "Downloaded payload identity changed.");
        var registration = "Axiom-" + WorkspaceFileOperations.Hash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(paths.Root)))[..16];
        var keyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + registration;
        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"))
            Require(registered?.GetValue(registration) as string == @"Software\Axiom\" + registration + @"\Capabilities", "Default Apps registration is missing.");
        using (var associations = Registry.CurrentUser.OpenSubKey(@"Software\Axiom\" + registration + @"\Capabilities\FileAssociations"))
            foreach (var extension in new[] { ".md", ".markdown" })
                Require(associations?.GetValue(extension) as string == registration + ".Markdown", "Default Apps association is missing.");
        Require(ReadDefaultChoices() == defaultChoices, "Registration changed the user's default editor.");
        Require(WindowsInstallationService.DefaultAppsUri(paths.Root).AbsoluteUri.EndsWith("registeredAppUser=" + registration, StringComparison.Ordinal), "Default Apps Settings is not scoped to this installation.");
        using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            Require(key?.GetValue("InstallLocation") as string == destination, "Per-user installation registration missing.");
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), registration + ".lnk");
        Require(File.Exists(link), "Actual Start menu shortcut missing.");
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.md\OpenWithProgids"))
            Require(key?.GetValueNames().Contains(registration + ".Markdown") == true, "Optional Open with registration missing.");
        var backup = Directory.GetFiles(paths.BackupDirectory, "pre-install-*.db").Single();
        Require(File.Exists(backup), "Verified pre-install state backup missing.");
        var uninstall = Path.Combine(paths.Root, "Installation", "Uninstall-" + registration + ".ps1");
        var payload = Path.Combine(destination, "Test-Axiom.cmd");
        var original = await File.ReadAllBytesAsync(payload);
        await File.AppendAllTextAsync(payload, "modified payload");
        Require(await UninstallAsync(uninstall) != 0 && Directory.Exists(destination), "Modified payload was removed.");
        await File.WriteAllBytesAsync(payload, original);
        Require(await UninstallAsync(uninstall) == 0, "Verified uninstall failed.");
        Require(!Directory.Exists(destination) && !File.Exists(link), "Uninstall left its payload or shortcut.");
        using (var key = Registry.CurrentUser.OpenSubKey(keyPath)) Require(key is null, "Uninstall registration remained.");
        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"))
            Require(registered?.GetValue(registration) is null, "Default Apps registration remained.");
        using (var capabilities = Registry.CurrentUser.OpenSubKey(@"Software\Axiom\" + registration))
            Require(capabilities is null, "Owned capabilities remained.");
        Require(ReadDefaultChoices() == defaultChoices, "Uninstall changed the user's default editor.");
        var drafts = await state.GetRecoverableDraftsAsync();
        Require(drafts.Single().Text == "retained draft" && File.Exists(backup), "Uninstall changed user state or backup.");
        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            manifest.InformationalVersion
          , ActualPerUserRegistration = true
          , ActualStartMenuShortcut = true
          , ExplicitOpenWithRegistration = true
          , DefaultAppsCapabilitiesAndScopedSettings = true
          , DefaultEditorChoicePreserved = true
          , DefaultAppsRegistrationRemoved = true
          , ModifiedPayloadUninstallBlocked = true
          , VerifiedPayloadAndIntegrationRemoved = true
          , DraftStateAndBackupRetained = true
          , TrustedOfferAndArchivePipelineWithPackagedFixture = true
          , LivePublisherNetworkVerified = false
          , HumanChecks = "Windows Installed apps display, physical input, trust/signing/distribution and human release acceptance remain pending."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ReadDefaultChoices()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var values = new List<string?>();
        foreach (var extension in new[] { ".md", ".markdown" })
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + extension + @"\UserChoice");
            values.Add(choice?.GetValue("ProgId") as string);
            values.Add(choice?.GetValue("Hash") as string);
        }
        return JsonSerializer.Serialize(values);
    }

    private static async Task<int> UninstallAsync(string script)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await process.WaitForExitAsync(timeout.Token);
        await File.WriteAllTextAsync(script + ".last-result.txt", await output + "\n" + await errors);
        return process.ExitCode;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
