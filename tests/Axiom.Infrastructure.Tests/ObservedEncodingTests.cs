using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class ObservedEncodingTests
{
    [Fact]
    public async Task ReadReportsObservedBomEncodingAndSaveRetainsExistingUtf8Policy()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomEncoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "sample.md");
            var store = new FileDocumentStore();
            await File.WriteAllTextAsync(path, "Café 日本語\r\n", Encoding.Unicode);
            var file = await store.ReadAsync(path);
            Assert.Contains("UTF-16", file.ObservedEncoding);
            Assert.Contains("BOM", file.ObservedEncoding);
            var session = DocumentSession.Open(file);
            Assert.Equal(file.ObservedEncoding, session.ObservedEncoding);
            await store.WriteAsync(path, session.Text);
            var saved = await store.ReadAsync(path);
            Assert.Equal(file.Text, saved.Text);
            Assert.Equal("UTF-8 (assumed; no BOM)", saved.ObservedEncoding);
            Assert.False((await File.ReadAllBytesAsync(path)).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
        }
        finally { Directory.Delete(root, true); }
    }
}
