using System.Diagnostics;
using System.Net.NetworkInformation;
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
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(listener => listener.Port == endpoint.Port))
                throw new InvalidOperationException("A service already owns the configured CP port but did not authenticate correctly. It was not replaced. Use Advanced connection to inspect it.");
            await EnsureOllamaAsync(progress, token);
            var start = new ProcessStartInfo(profile.BackendPath) { WorkingDirectory = Path.GetDirectoryName(profile.BackendPath)!, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            var settings = new Dictionary<string,string>
            {
                ["ASPNETCORE_ENVIRONMENT"]="Development", ["ASPNETCORE_URLS"]=profile.Endpoint,
                ["DocumentAnalysis__Enabled"]="true", ["DocumentAnalysis__ClientKeySha256"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential))),
                ["DocumentAnalysis__TimeoutSeconds"]="120", ["LlmClient__Provider"]="Ollama", ["LlmClient__Endpoint"]="http://localhost:11434",
                ["LlmClient__DefaultModel"]=profile.Model, ["LlmClient__Timeout"]="120", ["Embedding__OllamaBaseUrl"]="http://localhost:11434", ["Embedding__EmbeddingModel"]="nomic-embed-text:latest"
            };
            foreach(var setting in settings) start.Environment[setting.Key]=setting.Value;
            start.Environment.TryGetValue("NO_PROXY",out var bypass);
            start.Environment["NO_PROXY"]=(bypass??"")+",localhost,127.0.0.1";
            start.Environment["no_proxy"]=start.Environment["NO_PROXY"];
            using var process=Process.Start(start)??throw new InvalidOperationException("Local CP could not start.");
            var deadline=DateTime.UtcNow.AddMinutes(2);
            while(DateTime.UtcNow<deadline)
            {
                token.ThrowIfCancellationRequested();
                if(process.HasExited)throw new InvalidOperationException("Local CP exited during initialization. No document was submitted.");
                using var attempt=CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(TimeSpan.FromSeconds(3));
                try{Validate(await client.GetCapabilitiesAsync(attempt.Token),profile.Model);return client;}
                catch(Exception error) when(!token.IsCancellationRequested && error is InvalidOperationException or OperationCanceledException or HttpRequestException){}
                await Task.Delay(250,token);
            }
            throw new InvalidOperationException("Local CP did not become ready. Retry; source is unchanged.");
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task EnsureOllamaAsync(Action<string> progress, CancellationToken token)
    {
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(3)};
        async Task<bool> Ready(){try{using var response=await http.GetAsync("http://127.0.0.1:11434/api/tags",token);return response.IsSuccessStatusCode;}catch(HttpRequestException){return false;}catch(OperationCanceledException)when(!token.IsCancellationRequested){return false;}}
        if(await Ready())return;
        if(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(listener=>listener.Port==11434))throw new InvalidOperationException("The local model service is unavailable; its port was left unchanged.");
        var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Ollama","ollama.exe");
        if(!File.Exists(executable))throw new InvalidOperationException("Install/configure Ollama before using local CP. No model was downloaded.");
        progress("Starting the installed local model service…");
        using var process=Process.Start(new ProcessStartInfo(executable,"serve"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        for(var attempt=0;attempt<40;attempt++){if(await Ready())return;await Task.Delay(250,token);}
        throw new InvalidOperationException("The local model service did not become ready. Retry; source is unchanged.");
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
        if (capabilities.Simulated || !capabilities.InsightsConfigured || !capabilities.EmbeddingsConfigured || capabilities.InsightsModel != model || capabilities.Availability != "catalog_model_present")
            throw new InvalidOperationException("The configured CP models are not ready. No document was submitted.");
    }
    private static void Verify(string path, string expected)
    {
        using var file = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The configured CP setup or backend changed. Restore the verified profile before connecting.");
    }
}
