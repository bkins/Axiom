using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed record LocalCpProfile(string Endpoint, string CredentialPath, string SetupScript, string SetupSha256, string BackendPath, string BackendSha256, string Model);

internal static class LocalCpConnection
{
    internal static async Task<CpDocumentAnalysisClient> ConnectAsync(Action<string> progress, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Automatic local CP setup requires Windows. Use Advanced connection for another service.");
        var profileFile = Path.Combine(AppContext.BaseDirectory, "cp-local-profile.json");
        if (!File.Exists(profileFile)) throw new InvalidOperationException("Local CP is not configured for this build. Use Advanced connection or the configured review package.");
        var profile = JsonSerializer.Deserialize<LocalCpProfile>(await File.ReadAllTextAsync(profileFile, token), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The local CP profile is invalid.");
        var endpoint = new Uri(profile.Endpoint);
        if (!endpoint.IsLoopback || endpoint.Scheme != "http") throw new InvalidOperationException("Automatic startup requires a configured loopback service.");
        progress("Connecting to local CP…");
        var credential = ReadCredential(profile.CredentialPath);
        var client = new CpDocumentAnalysisClient(endpoint, credential);
        try
        {
            using var quick = CancellationTokenSource.CreateLinkedTokenSource(token);
            quick.CancelAfter(TimeSpan.FromSeconds(3));
            try { Validate(await client.GetCapabilitiesAsync(quick.Token), profile.Model); return client; }
            catch (Exception exception) when (!token.IsCancellationRequested && exception is OperationCanceledException or HttpRequestException or InvalidOperationException) { }
            progress("Starting local CP and checking models…");
            var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, profile.SetupScript));
            if (!script.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The local setup path leaves this package.");
            Verify(script, profile.SetupSha256);
            Verify(Path.GetFullPath(profile.BackendPath), profile.BackendSha256);
            var resultFile = Path.Combine(Path.GetTempPath(), "Axiom-cp-setup-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-BackendPath", profile.BackendPath, "-ResultPath", resultFile, "-CredentialPath", profile.CredentialPath, "-SkipClipboard" }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Local CP setup could not start.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); token.ThrowIfCancellationRequested(); throw new InvalidOperationException("Local CP setup timed out. Retry when the service is ready."); }
                if (process.ExitCode != 0)
                {
                    var reason = "startup failed";
                    if (File.Exists(resultFile))
                    {
                        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, token));
                        if (result.RootElement.TryGetProperty("FailureCode", out var code)) reason = code.GetString() switch { "credential_rejected" => "the saved credential was rejected", "unrelated_port_owner" => "another service owns the configured port", "wrong_model" => "the service uses another model", _ => "the service did not become ready" };
                    }
                    throw new InvalidOperationException("Local CP could not connect: " + reason + ". No document was submitted. Use Advanced connection for details.");
                }
                Validate(await client.GetCapabilitiesAsync(token), profile.Model);
                return client;
            }
            finally { if (File.Exists(resultFile)) File.Delete(resultFile); }
        }
        catch { client.Dispose(); throw; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static string ReadCredential(string path)
    {
        var encrypted = File.ReadAllText(path).Trim();
        if (encrypted.Length > 8192) throw new InvalidOperationException("The saved CP credential is invalid.");
        var bytes = ProtectedData.Unprotect(Convert.FromHexString(encrypted), null, DataProtectionScope.CurrentUser);
        try { return Encoding.Unicode.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void Validate(Axiom.Core.AnalysisCapabilities capabilities, string model)
    {
        if (capabilities.Simulated || !capabilities.InsightsConfigured || !capabilities.EmbeddingsConfigured || capabilities.InsightsModel != model)
            throw new InvalidOperationException("The configured CP models are not ready. No document was submitted.");
    }
    private static void Verify(string path, string expected)
    {
        using var file = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The configured CP setup or backend changed. Restore the verified profile before connecting.");
    }
}
