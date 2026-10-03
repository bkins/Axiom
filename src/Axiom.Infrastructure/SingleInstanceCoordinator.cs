using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Axiom.Infrastructure;

/// <summary>Owns one interactive process per application-state root, before any state/log writer opens.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _ownership;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private bool _disposed;

    public SingleInstanceCoordinator(string stateRoot)
    {
        var canonical = Path.GetFullPath(stateRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + canonical)));
        _pipeName = "Axiom-" + identity;
        _ownership = new Mutex(false, _pipeName);
        try { IsPrimary = _ownership.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public bool IsPrimary { get; }

    public void Listen(Action<string?> activate)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("Only the owning instance can listen once.");
        _listener = ListenAsync(activate);
    }

    private async Task ListenAsync(Action<string?> activate)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var lengthBytes = new byte[4];
                await pipe.ReadExactlyAsync(lengthBytes, timeout.Token);
                var length = BitConverter.ToInt32(lengthBytes);
                if (length is < 1 or > 32_768) continue;
                var bytes = new byte[length];
                await pipe.ReadExactlyAsync(bytes, timeout.Token);
                var path = JsonSerializer.Deserialize<string>(bytes);
                if (path is not null && (!Path.IsPathFullyQualified(path) ||
                    Path.GetExtension(path).ToLowerInvariant() is not (".md" or ".markdown"))) continue;
                activate(path);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    public async Task<bool> ForwardAsync(string? document, CancellationToken cancellationToken = default)
    {
        if (IsPrimary) throw new InvalidOperationException("The primary instance cannot forward to itself.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
            if (bytes.Length > 32_768) return false;
            await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), timeout.Token);
            await pipe.WriteAsync(bytes, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            var acknowledgement = new byte[1];
            await pipe.ReadExactlyAsync(acknowledgement, timeout.Token);
            return acknowledgement[0] == 1;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (IOException) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        if (IsPrimary) _ownership.ReleaseMutex();
        _ownership.Dispose();
        // Listener cancellation completes asynchronously; do not block the UI shutdown thread.
    }
}
